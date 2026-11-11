using LlmQuality.Api.Data;
using LlmQuality.Api.Domain;
using LlmQuality.Api.Services.Metrics;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace LlmQuality.Api.Services.Reports;

public static class RecommendationEngine
{
    public static List<string> Build(Kpis k, QaMetrics qa, IReadOnlyCollection<TestRun> runs)
    {
        var rec = new List<string>();
        if (k.TotalRuns == 0) return ["Execute test cases to generate recommendations."];
        if (k.PassRate < 0.8) rec.Add("Pass rate below 80%: review prompts and acceptance criteria, consider a stronger model or few-shot examples.");
        if (qa.HallucinationRate is > 0.1) rec.Add("Hallucination rate above 10%: ground answers with RAG, lower temperature and require citations.");
        if (qa.ToxicityRate is > 0) rec.Add("Toxic output detected: add output moderation (guardrails) before responses reach users.");
        if (runs.Any(r => r.FailureType == "Security")) rec.Add("Security failures (injection/jailbreak/leakage): harden the system prompt, isolate untrusted input and add input/output filters.");
        if (runs.Any(r => r.Consistency is < 0.7)) rec.Add("Low consistency across samples: reduce temperature and pin model versions.");
        if (qa.FalsePositiveRate is > 0.1 || qa.FalseNegativeRate is > 0.1) rec.Add("Automated judge disagrees with human QA: recalibrate the judge prompt and expand the human-labeled set.");
        if (rec.Count == 0) rec.Add("No critical issues detected. Keep regression suites running on every model or prompt change.");
        return rec;
    }

    public static List<string> Risks(QaMetrics qa, IReadOnlyCollection<TestRun> runs)
    {
        var risks = new List<string>();
        var secFail = runs.Count(r => r.FailureType == "Security");
        if (secFail > 0) risks.Add($"{secFail} security test(s) failed (prompt injection / jailbreak / data leakage).");
        if (qa.ToxicityRate is > 0) risks.Add($"Toxicity rate: {qa.ToxicityRate:P1}.");
        if (qa.HallucinationRate is > 0.05) risks.Add($"Hallucination rate: {qa.HallucinationRate:P1}.");
        var errors = runs.Count(r => r.FailureType == "Error");
        if (errors > 0) risks.Add($"{errors} execution error(s) (provider/API failures).");
        if (risks.Count == 0) risks.Add("No significant risks identified in the evaluated sample.");
        return risks;
    }
}

public class ReportService(AppDb db)
{
    public async Task<byte[]> GenerateAsync(CancellationToken ct = default)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var runs = await db.TestRuns.AsNoTracking().Include(r => r.TestCase).ToListAsync(ct);
        var rag = await db.RagRuns.AsNoTracking().ToListAsync(ct);
        var kpis = MetricsCalculator.ComputeKpis(runs);
        var qa = MetricsCalculator.ComputeQa(runs, rag);
        var board = MetricsCalculator.ComputeLeaderboard(runs);
        var passed = runs.Where(r => r.Passed).OrderByDescending(r => r.Score).Take(25).ToList();
        var failed = runs.Where(r => !r.Passed).OrderBy(r => r.Score).Take(25).ToList();
        var risks = RecommendationEngine.Risks(qa, runs);
        var recs = RecommendationEngine.Build(kpis, qa, runs);
        string P(double? v) => v.HasValue ? $"{v.Value:P1}" : "n/a";

        return QuestPDF.Fluent.Document.Create(doc => doc.Page(page =>
        {
            page.Margin(36);
            page.DefaultTextStyle(t => t.FontSize(10));
            page.Header().Text("LLM Quality Evaluator — Report").FontSize(20).Bold();
            page.Footer().AlignCenter().Text(t => { t.Span($"Generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC · page "); t.CurrentPageNumber(); });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Spacing(8);
                col.Item().Text("1. Executive summary").FontSize(14).Bold();
                col.Item().Text($"{kpis.TotalRuns} executions · pass rate {kpis.PassRate:P1} · fail rate {kpis.FailRate:P1} · avg score {kpis.AvgScore:0.00} · " +
                                $"avg latency {kpis.AvgLatencyMs:0} ms · avg cost ${kpis.AvgCostUsd:0.0000}/run.");
                col.Item().Text($"Hallucination {P(qa.HallucinationRate)} · Prompt success {P(qa.PromptSuccessRate)} · Toxicity {P(qa.ToxicityRate)} · " +
                                $"Groundedness {P(qa.GroundednessScore)} · Answer relevancy {P(qa.AnswerRelevancyScore)} · " +
                                $"False positive {P(qa.FalsePositiveRate)} · False negative {P(qa.FalseNegativeRate)}.");

                if (board.Count > 0)
                {
                    col.Item().Text("Model comparison").Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); c.RelativeColumn(); });
                        foreach (var h in new[] { "Model", "Runs", "Score", "Pass", "Latency ms" }) t.Cell().Background(Colors.Grey.Lighten3).Padding(3).Text(h).Bold();
                        foreach (var b in board)
                        {
                            t.Cell().Padding(3).Text(b.Model); t.Cell().Padding(3).Text(b.Runs.ToString());
                            t.Cell().Padding(3).Text($"{b.AvgScore:0.00}"); t.Cell().Padding(3).Text($"{b.PassRate:P0}");
                            t.Cell().Padding(3).Text($"{b.AvgLatencyMs:0}");
                        }
                    });
                }

                col.Item().PaddingTop(6).Text("2. Approved cases").FontSize(14).Bold();
                CaseList(col, passed);
                col.Item().PaddingTop(6).Text("3. Failed cases").FontSize(14).Bold();
                CaseList(col, failed);

                col.Item().PaddingTop(6).Text("4. Main risks identified").FontSize(14).Bold();
                foreach (var r in risks) col.Item().Text($"• {r}");
                col.Item().PaddingTop(6).Text("5. Improvement recommendations").FontSize(14).Bold();
                foreach (var r in recs) col.Item().Text($"• {r}");
            });
        })).GeneratePdf();
    }

    private static void CaseList(ColumnDescriptor col, List<TestRun> runs)
    {
        if (runs.Count == 0) { col.Item().Text("None.").Italic(); return; }
        foreach (var r in runs)
            col.Item().Text($"• {r.TestCase?.Name ?? "?"} [{r.Model}] score {r.Score:0.00}" +
                            (r.FailureType is null ? "" : $" — {r.FailureType}") +
                            (string.IsNullOrWhiteSpace(r.JudgeReason) ? "" : $": {r.JudgeReason}"));
    }
}
