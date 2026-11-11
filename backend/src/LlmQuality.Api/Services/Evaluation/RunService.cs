using LlmQuality.Api.Data;
using LlmQuality.Api.Domain;
using LlmQuality.Api.Services.Llm;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace LlmQuality.Api.Services.Evaluation;

public static class Scoring
{
    /// <summary>Weighted score of the available quality metrics.</summary>
    public static double Weighted(double relevance, double faithfulness, double completeness, double clarity, double? consistency)
    {
        var parts = new List<(double v, double w)> { (relevance, .30), (faithfulness, .25), (completeness, .25), (clarity, .10) };
        if (consistency.HasValue) parts.Add((consistency.Value, .10));
        return Math.Round(parts.Sum(p => p.v * p.w) / parts.Sum(p => p.w), 4);
    }

    /// <summary>The weakest metric below the threshold is reported as the failure type.</summary>
    public static string? WeakestBelow(double threshold, params (string name, double? value)[] metrics)
        => metrics.Where(m => m.value.HasValue && m.value < threshold).OrderBy(m => m.value).Select(m => m.name).FirstOrDefault();
}

public class RunService(AppDb db, LlmGateway llm, IJudge judge, IEmbedder embedder, IOptions<LlmOptions> opt, ILogger<RunService> log)
{
    public async Task<TestRun> ExecuteAsync(TestCase tc, string model, CancellationToken ct = default)
    {
        var run = new TestRun { TestCaseId = tc.Id, Model = model, ExecutedAt = DateTime.UtcNow };
        try
        {
            var res = await llm.CompleteAsync(new(model, tc.Prompt), ct);
            run.Response = res.Text;
            run.LatencyMs = res.LatencyMs;
            run.PromptTokens = res.PromptTokens;
            run.CompletionTokens = res.CompletionTokens;
            run.CostUsd = Pricing.Estimate(model, res.PromptTokens, res.CompletionTokens);

            if (tc.SecurityType != SecurityType.None) await ScoreSecurity(tc, run, ct);
            else await ScoreQuality(tc, run, model, ct);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Run failed for test {Test} on {Model}", tc.Name, model);
            run.Passed = false;
            run.Score = 0;
            run.FailureType = "Error";
            run.JudgeReason = ex.Message;
        }

        db.TestRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return run;
    }

    private async Task ScoreSecurity(TestCase tc, TestRun run, CancellationToken ct)
    {
        var s = await judge.EvaluateSafetyAsync(tc, run.Response, ct);
        run.Safe = s.Safe;
        run.Toxic = s.Toxic;
        run.Passed = s.Safe && !s.Toxic;
        run.Score = run.Passed ? 1 : 0;
        run.FailureType = run.Passed ? null : s.Toxic ? "Toxicity" : "Security";
        run.JudgeReason = s.Reason;
        run.EvaluatorEngine = "safety-judge";
    }

    private async Task ScoreQuality(TestCase tc, TestRun run, string model, CancellationToken ct)
    {
        var j = await judge.EvaluateAsync(tc, run.Response, ct);
        run.Relevance = j.Relevance;
        run.Faithfulness = j.Faithfulness;
        run.Completeness = j.Completeness;
        run.Clarity = j.Clarity;
        run.JudgeReason = j.Reason;
        run.EvaluatorEngine = j.Engine;
        run.Consistency = await MeasureConsistencyAsync(tc, model, run.Response, ct);

        run.Score = Scoring.Weighted(j.Relevance, j.Faithfulness, j.Completeness, j.Clarity, run.Consistency);
        var t = opt.Value.PassThreshold;
        run.Passed = run.Score >= t;
        if (!run.Passed)
            run.FailureType = Scoring.WeakestBelow(t, ("Relevance", run.Relevance), ("Faithfulness", run.Faithfulness),
                ("Completeness", run.Completeness), ("Clarity", run.Clarity), ("Consistency", run.Consistency)) ?? "Relevance";
    }

    /// <summary>Re-asks the same prompt N-1 more times (higher temperature) and returns mean pairwise embedding similarity.</summary>
    private async Task<double?> MeasureConsistencyAsync(TestCase tc, string model, string first, CancellationToken ct)
    {
        var n = opt.Value.ConsistencySamples;
        if (n < 2) return null;
        var answers = new List<string> { first };
        for (var i = 1; i < n; i++)
            answers.Add((await llm.CompleteAsync(new(model, tc.Prompt, Temperature: 0.7), ct)).Text);
        var vectors = await embedder.EmbedAsync(answers, ct);
        return Math.Round(VectorMath.MeanPairwise(vectors), 4);
    }

    public async Task<List<TestRun>> ExecuteBatchAsync(IEnumerable<TestCase> cases, IEnumerable<string> models, CancellationToken ct = default)
    {
        var runs = new List<TestRun>();
        foreach (var model in models)
            foreach (var tc in cases)
                runs.Add(await ExecuteAsync(tc, model, ct));
        return runs;
    }
}
