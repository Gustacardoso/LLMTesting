using LlmQuality.Api.Domain;

namespace LlmQuality.Api.Services.Metrics;

public record Kpis(int TotalRuns, double PassRate, double FailRate, double AvgScore, decimal AvgCostUsd, double AvgLatencyMs);
public record DayCount(string Day, int Total, int Passed, int Failed);
public record DayScore(string Day, double AvgScore);
public record NameCount(string Name, int Count);
public record DashboardDto(Kpis Kpis, IReadOnlyList<DayCount> RunsPerDay, IReadOnlyList<DayScore> QualityTrend,
    IReadOnlyList<NameCount> ByCategory, IReadOnlyList<NameCount> FailuresByType);

/// <summary>AI-specific QA metrics. Rates are 0..1; null means "not enough data".</summary>
public record QaMetrics(
    double? HallucinationRate, double? PromptSuccessRate, double? FalsePositiveRate, double? FalseNegativeRate,
    double? ToxicityRate, double? GroundednessScore, double? AnswerRelevancyScore, int HumanLabeledRuns);

public record LeaderboardRow(string Model, int Runs, double AvgScore, double AvgLatencyMs, decimal AvgCostUsd, double PassRate);

/// <summary>Pure functions over in-memory runs → trivially unit-testable.</summary>
public static class MetricsCalculator
{
    public const double HallucinationThreshold = 0.5;

    public static Kpis ComputeKpis(IReadOnlyCollection<TestRun> runs)
    {
        if (runs.Count == 0) return new(0, 0, 0, 0, 0, 0);
        var passed = runs.Count(r => r.Passed);
        return new(runs.Count, Rate(passed, runs.Count), Rate(runs.Count - passed, runs.Count),
            Math.Round(runs.Average(r => r.Score), 4), Math.Round(runs.Average(r => r.CostUsd), 6),
            Math.Round(runs.Average(r => (double)r.LatencyMs), 1));
    }

    public static DashboardDto ComputeDashboard(IReadOnlyCollection<TestRun> runs)
    {
        var perDay = runs.GroupBy(r => r.ExecutedAt.ToString("yyyy-MM-dd")).OrderBy(g => g.Key)
            .Select(g => new DayCount(g.Key, g.Count(), g.Count(r => r.Passed), g.Count(r => !r.Passed))).ToList();
        var trend = runs.GroupBy(r => r.ExecutedAt.ToString("yyyy-MM-dd")).OrderBy(g => g.Key)
            .Select(g => new DayScore(g.Key, Math.Round(g.Average(r => r.Score), 4))).ToList();
        var byCat = runs.GroupBy(r => r.TestCase?.Category ?? "Unknown").Select(g => new NameCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count).ToList();
        var failures = runs.Where(r => !r.Passed).GroupBy(r => r.FailureType ?? "Unknown")
            .Select(g => new NameCount(g.Key, g.Count())).OrderByDescending(x => x.Count).ToList();
        return new(ComputeKpis(runs), perDay, trend, byCat, failures);
    }

    public static QaMetrics ComputeQa(IReadOnlyCollection<TestRun> runs, IReadOnlyCollection<RagRun> ragRuns)
    {
        var withFaith = runs.Where(r => r.Faithfulness.HasValue).Select(r => r.Faithfulness!.Value)
            .Concat(ragRuns.Where(r => r.Faithfulness.HasValue).Select(r => r.Faithfulness!.Value)).ToList();
        var withRel = runs.Where(r => r.Relevance.HasValue).Select(r => r.Relevance!.Value)
            .Concat(ragRuns.Where(r => r.AnswerRelevancy.HasValue).Select(r => r.AnswerRelevancy!.Value)).ToList();

        // Judge accuracy vs human QA label. Positive class = "the answer is acceptable (passed)".
        var labeled = runs.Where(r => r.HumanVerdict.HasValue).ToList();
        var humanFail = labeled.Count(r => r.HumanVerdict == false);
        var humanPass = labeled.Count(r => r.HumanVerdict == true);
        var fp = labeled.Count(r => r.Passed && r.HumanVerdict == false); // judge approved something humans rejected
        var fn = labeled.Count(r => !r.Passed && r.HumanVerdict == true); // judge rejected something humans accepted

        return new(
            HallucinationRate: withFaith.Count == 0 ? null : Rate(withFaith.Count(f => f < HallucinationThreshold), withFaith.Count),
            PromptSuccessRate: runs.Count == 0 ? null : Rate(runs.Count(r => r.Passed), runs.Count),
            FalsePositiveRate: humanFail == 0 ? null : Rate(fp, humanFail),
            FalseNegativeRate: humanPass == 0 ? null : Rate(fn, humanPass),
            ToxicityRate: runs.Count == 0 ? null : Rate(runs.Count(r => r.Toxic), runs.Count),
            GroundednessScore: withFaith.Count == 0 ? null : Math.Round(withFaith.Average(), 4),
            AnswerRelevancyScore: withRel.Count == 0 ? null : Math.Round(withRel.Average(), 4),
            HumanLabeledRuns: labeled.Count);
    }

    public static List<LeaderboardRow> ComputeLeaderboard(IReadOnlyCollection<TestRun> runs)
        => runs.GroupBy(r => r.Model).Select(g => new LeaderboardRow(g.Key, g.Count(), Math.Round(g.Average(r => r.Score), 4),
                Math.Round(g.Average(r => (double)r.LatencyMs), 1), Math.Round(g.Average(r => r.CostUsd), 6), Rate(g.Count(r => r.Passed), g.Count())))
            .OrderByDescending(r => r.AvgScore).ToList();

    private static double Rate(int part, int total) => total == 0 ? 0 : Math.Round((double)part / total, 4);
}
