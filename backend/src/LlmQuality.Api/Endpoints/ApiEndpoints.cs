using System.ComponentModel.DataAnnotations;
using LlmQuality.Api.Data;
using LlmQuality.Api.Domain;
using LlmQuality.Api.Services.Evaluation;
using LlmQuality.Api.Services.Llm;
using LlmQuality.Api.Services.Metrics;
using LlmQuality.Api.Services.Rag;
using LlmQuality.Api.Services.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LlmQuality.Api.Endpoints;

public record TestCaseInput(
    [property: Required, StringLength(200)] string Name,
    [property: Required, StringLength(100)] string Category,
    [property: Required, StringLength(8000)] string Prompt,
    [property: Required, StringLength(8000)] string ExpectedAnswer,
    [property: StringLength(4000)] string? AcceptanceCriteria,
    [property: StringLength(16000)] string? Context);

public record RunRequest(string? Model);
public record BatchRequest(string? Category, string[]? Models, Guid[]? TestCaseIds);
public record SecurityRunRequest(string[]? Models, SecurityType? Type);
public record LabelRequest(bool Verdict);
public record AskRequest([property: Required, StringLength(2000)] string Question, string? GroundTruth, string? Model, int? TopK);

public static class ApiEndpoints
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    public static void MapApi(this WebApplication app)
    {
        var api = app.MapGroup("/api");
        api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        api.MapGet("/models", (IOptions<LlmOptions> o) => o.Value.Models);

        MapTestCases(api);
        MapRuns(api);
        MapSecurity(api);
        MapRag(api);
        MapAnalytics(api);
    }

    private static IResult? Validate<T>(T model)
    {
        var results = new List<ValidationResult>();
        return Validator.TryValidateObject(model!, new ValidationContext(model!), results, true)
            ? null
            : Results.ValidationProblem(results.ToDictionary(r => r.MemberNames.FirstOrDefault() ?? "", r => new[] { r.ErrorMessage ?? "invalid" }));
    }

    private static void MapTestCases(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/testcases");
        g.MapGet("/", async (AppDb db, string? category, CancellationToken ct) =>
            await db.TestCases.AsNoTracking().Where(t => category == null || t.Category == category)
                .OrderByDescending(t => t.CreatedAt).ToListAsync(ct));

        g.MapGet("/{id:guid}", async (Guid id, AppDb db) =>
            await db.TestCases.FindAsync(id) is { } t ? Results.Ok(t) : Results.NotFound());

        g.MapPost("/", async (TestCaseInput i, AppDb db) =>
        {
            if (Validate(i) is { } bad) return bad;
            var t = new TestCase
            {
                Name = i.Name, Category = i.Category, Prompt = i.Prompt, ExpectedAnswer = i.ExpectedAnswer,
                AcceptanceCriteria = i.AcceptanceCriteria ?? "", Context = i.Context
            };
            db.TestCases.Add(t);
            await db.SaveChangesAsync();
            return Results.Created($"/api/testcases/{t.Id}", t);
        });

        g.MapPut("/{id:guid}", async (Guid id, TestCaseInput i, AppDb db) =>
        {
            if (Validate(i) is { } bad) return bad;
            var t = await db.TestCases.FindAsync(id);
            if (t is null) return Results.NotFound();
            (t.Name, t.Category, t.Prompt, t.ExpectedAnswer, t.AcceptanceCriteria, t.Context) =
                (i.Name, i.Category, i.Prompt, i.ExpectedAnswer, i.AcceptanceCriteria ?? "", i.Context);
            await db.SaveChangesAsync();
            return Results.Ok(t);
        });

        g.MapDelete("/{id:guid}", async (Guid id, AppDb db) =>
        {
            var t = await db.TestCases.FindAsync(id);
            if (t is null) return Results.NotFound();
            db.TestCases.Remove(t);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPost("/{id:guid}/run", async (Guid id, RunRequest req, AppDb db, RunService runs, IOptions<LlmOptions> o, CancellationToken ct) =>
        {
            var t = await db.TestCases.FindAsync([id], ct);
            if (t is null) return Results.NotFound();
            return Results.Ok(await runs.ExecuteAsync(t, req.Model ?? o.Value.Models[0], ct));
        });
    }

    private static void MapRuns(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/runs");
        g.MapGet("/", async (AppDb db, int? take, Guid? testCaseId, string? model, CancellationToken ct) =>
            await db.TestRuns.AsNoTracking().Include(r => r.TestCase)
                .Where(r => (testCaseId == null || r.TestCaseId == testCaseId) && (model == null || r.Model == model))
                .OrderByDescending(r => r.ExecutedAt).Take(Math.Clamp(take ?? 100, 1, 500))
                .Select(r => new
                {
                    r.Id, r.TestCaseId, TestName = r.TestCase!.Name, Category = r.TestCase.Category, r.Model, r.ExecutedAt, r.LatencyMs,
                    r.PromptTokens, r.CompletionTokens, r.CostUsd, r.Relevance, r.Faithfulness, r.Completeness, r.Clarity, r.Consistency,
                    r.Score, r.Passed, r.FailureType, r.JudgeReason, r.EvaluatorEngine, r.HumanVerdict, r.Response
                }).ToListAsync(ct));

        g.MapPost("/batch", async (BatchRequest req, AppDb db, RunService runs, IOptions<LlmOptions> o, CancellationToken ct) =>
        {
            var q = db.TestCases.AsQueryable();
            if (req.TestCaseIds is { Length: > 0 }) q = q.Where(t => req.TestCaseIds.Contains(t.Id));
            if (!string.IsNullOrWhiteSpace(req.Category)) q = q.Where(t => t.Category == req.Category);
            var cases = await q.ToListAsync(ct);
            var result = await runs.ExecuteBatchAsync(cases, req.Models is { Length: > 0 } ? req.Models : [o.Value.Models[0]], ct);
            return Results.Ok(new { executed = result.Count, passed = result.Count(r => r.Passed), failed = result.Count(r => !r.Passed) });
        });

        // Human QA label — feeds the false positive / false negative rates of the automated judge.
        g.MapPost("/{id:guid}/label", async (Guid id, LabelRequest req, AppDb db) =>
        {
            var r = await db.TestRuns.FindAsync(id);
            if (r is null) return Results.NotFound();
            r.HumanVerdict = req.Verdict;
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static void MapSecurity(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/security");
        g.MapPost("/seed", async (AppDb db) =>
        {
            var existing = await db.TestCases.Where(t => t.Category == SecuritySuite.Category).Select(t => t.Name).ToListAsync();
            var toAdd = SecuritySuite.Cases().Where(c => !existing.Contains(c.Name)).ToList();
            db.TestCases.AddRange(toAdd);
            await db.SaveChangesAsync();
            return Results.Ok(new { added = toAdd.Count });
        });

        g.MapPost("/run", async (SecurityRunRequest req, AppDb db, RunService runs, IOptions<LlmOptions> o, CancellationToken ct) =>
        {
            var q = db.TestCases.Where(t => t.SecurityType != SecurityType.None);
            if (req.Type is { } type) q = q.Where(t => t.SecurityType == type);
            var cases = await q.ToListAsync(ct);
            if (cases.Count == 0) return Results.BadRequest(new { error = "No security cases. POST /api/security/seed first." });
            var result = await runs.ExecuteBatchAsync(cases, req.Models is { Length: > 0 } ? req.Models : [o.Value.Models[0]], ct);
            return Results.Ok(result.Select(r => new
            {
                r.Id, r.TestCaseId, TestName = cases.First(c => c.Id == r.TestCaseId).Name,
                Type = cases.First(c => c.Id == r.TestCaseId).SecurityType.ToString(),
                r.Model, r.Passed, r.Toxic, r.FailureType, r.JudgeReason, r.Response
            }));
        });
    }

    private static void MapRag(RouteGroupBuilder api)
    {
        var g = api.MapGroup("/rag");
        g.MapGet("/documents", async (AppDb db, CancellationToken ct) =>
            await db.Documents.AsNoTracking().OrderByDescending(d => d.UploadedAt)
                .Select(d => new { d.Id, d.FileName, d.UploadedAt, Chunks = d.Chunks.Count }).ToListAsync(ct));

        g.MapPost("/documents", async (HttpRequest request, RagService rag, CancellationToken ct) =>
        {
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "multipart/form-data expected" });
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file is required" });
            if (file.Length > MaxUploadBytes) return Results.BadRequest(new { error = "file too large (max 10MB)" });
            if (!DocumentParser.AllowedExtensions.Contains(Path.GetExtension(file.FileName).ToLowerInvariant()))
                return Results.BadRequest(new { error = "only PDF, DOCX and TXT are allowed" });
            try
            {
                await using var s = file.OpenReadStream();
                var doc = await rag.IngestAsync(file.FileName, file.ContentType, s, ct);
                return Results.Ok(new { doc.Id, doc.FileName, Chunks = doc.Chunks.Count });
            }
            catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException or InvalidDataException)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        }).DisableAntiforgery();

        g.MapDelete("/documents/{id:guid}", async (Guid id, AppDb db) =>
        {
            var d = await db.Documents.FindAsync(id);
            if (d is null) return Results.NotFound();
            db.Documents.Remove(d);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        g.MapPost("/ask", async (AskRequest req, RagService rag, IOptions<LlmOptions> o, CancellationToken ct) =>
        {
            if (Validate(req) is { } bad) return bad;
            return Results.Ok(await rag.AskAsync(req.Question, req.GroundTruth, req.Model ?? o.Value.Models[0], Math.Clamp(req.TopK ?? 4, 1, 10), ct));
        });

        g.MapGet("/runs", async (AppDb db, CancellationToken ct) =>
            await db.RagRuns.AsNoTracking().OrderByDescending(r => r.ExecutedAt).Take(100).ToListAsync(ct));
    }

    private static void MapAnalytics(RouteGroupBuilder api)
    {
        api.MapGet("/dashboard", async (AppDb db, CancellationToken ct) =>
            MetricsCalculator.ComputeDashboard(await db.TestRuns.AsNoTracking().Include(r => r.TestCase).ToListAsync(ct)));

        api.MapGet("/metrics", async (AppDb db, CancellationToken ct) =>
            MetricsCalculator.ComputeQa(await db.TestRuns.AsNoTracking().ToListAsync(ct), await db.RagRuns.AsNoTracking().ToListAsync(ct)));

        api.MapGet("/leaderboard", async (AppDb db, CancellationToken ct) =>
            MetricsCalculator.ComputeLeaderboard(await db.TestRuns.AsNoTracking().ToListAsync(ct)));

        api.MapGet("/reports/pdf", async (ReportService reports, CancellationToken ct) =>
            Results.File(await reports.GenerateAsync(ct), "application/pdf", $"llm-quality-report-{DateTime.UtcNow:yyyyMMdd-HHmm}.pdf"));
    }
}
