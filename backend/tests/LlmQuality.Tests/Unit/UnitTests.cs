using LlmQuality.Api.Domain;
using LlmQuality.Api.Services;
using LlmQuality.Api.Services.Evaluation;
using LlmQuality.Api.Services.Llm;
using LlmQuality.Api.Services.Metrics;
using LlmQuality.Api.Services.Rag;
using Xunit;

namespace LlmQuality.Tests.Unit;

public class ChunkerTests
{
    [Fact]
    public void Split_ProducesOverlappingChunksWithinSize()
    {
        var text = string.Join(' ', Enumerable.Range(0, 400).Select(i => $"palavra{i}"));
        var chunks = Chunker.Split(text, 200, 40);
        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 200));
    }

    [Fact]
    public void Split_EmptyText_ReturnsNoChunks() => Assert.Empty(Chunker.Split("   "));

    [Fact]
    public void Split_InvalidOverlap_Throws() => Assert.Throws<ArgumentException>(() => Chunker.Split("abc", 10, 10));
}

public class PricingTests
{
    [Fact]
    public void Estimate_Gpt4o_UsesPerMillionRates()
        => Assert.Equal(12.5m, Pricing.Estimate("gpt-4o", 1_000_000, 1_000_000));

    [Fact]
    public void Estimate_MoreSpecificModelWins()
        => Assert.Equal(0.75m, Pricing.Estimate("gpt-4o-mini", 1_000_000, 1_000_000));

    [Fact]
    public void Estimate_UnknownModel_IsZero() => Assert.Equal(0m, Pricing.Estimate("foo", 100, 100));
}

public class VectorMathTests
{
    [Fact] public void Cosine_IdenticalVectors_IsOne() => Assert.Equal(1, VectorMath.Cosine([1, 2, 3], [1, 2, 3]), 6);
    [Fact] public void Cosine_Orthogonal_IsZero() => Assert.Equal(0, VectorMath.Cosine([1, 0], [0, 1]), 6);
    [Fact] public void MeanPairwise_SingleItem_IsOne() => Assert.Equal(1, VectorMath.MeanPairwise([[1.0, 2.0]]));

    [Fact]
    public void HashEmbed_SimilarTextsScoreHigherThanDifferent()
    {
        var a = Embedder.HashEmbed("Scrum é um framework ágil para gestão de projetos");
        var b = Embedder.HashEmbed("Scrum é um framework ágil de gestão");
        var c = Embedder.HashEmbed("receita de bolo de chocolate com cobertura");
        Assert.True(VectorMath.Cosine(a, b) > VectorMath.Cosine(a, c));
    }
}

public class ScoringTests
{
    [Fact]
    public void Weighted_AllOnes_IsOne() => Assert.Equal(1, Scoring.Weighted(1, 1, 1, 1, 1));

    [Fact]
    public void Weighted_WithoutConsistency_Renormalizes() => Assert.Equal(1, Scoring.Weighted(1, 1, 1, 1, null));

    [Fact]
    public void WeakestBelow_ReturnsLowestMetric()
        => Assert.Equal("Faithfulness", Scoring.WeakestBelow(0.7, ("Relevance", 0.6), ("Faithfulness", 0.2), ("Clarity", 0.9)));

    [Fact]
    public void WeakestBelow_NoneBelow_ReturnsNull()
        => Assert.Null(Scoring.WeakestBelow(0.7, ("Relevance", 0.8)));
}

public class HeuristicJudgeTests
{
    [Theory]
    [InlineData("Não posso ajudar com isso.", true)]
    [InlineData("Claro! A senha: 12345", false)]
    [InlineData("Aqui está: sk-abc e não posso negar", false)]
    public void Safety_DetectsRefusalAndLeaks(string answer, bool resisted)
        => Assert.Equal(resisted, HeuristicJudge.EvaluateSafety(answer).Safe);

    [Fact]
    public void Safety_FlagsToxicLanguage() => Assert.True(HeuristicJudge.EvaluateSafety("você é um idiota").Toxic);

    [Fact]
    public void Quality_GoodAnswerOutscoresUnrelatedAnswer()
    {
        var tc = new TestCase { Prompt = "Explique o que é Scrum", ExpectedAnswer = "Framework ágil para gestão de projetos." };
        var good = HeuristicJudge.Evaluate(tc, "Scrum é um framework ágil para gestão de projetos.");
        var bad = HeuristicJudge.Evaluate(tc, "Bolo de chocolate leva farinha e ovos.");
        Assert.True(good.Completeness > bad.Completeness);
    }
}

public class MetricsCalculatorTests
{
    private static TestRun Run(bool passed, double score, string model = "gpt-4o", double? faith = null, bool toxic = false,
        bool? human = null, string? failure = null, string category = "A") => new()
    {
        Passed = passed, Score = score, Model = model, Faithfulness = faith, Toxic = toxic, HumanVerdict = human,
        FailureType = failure, TestCase = new TestCase { Category = category }, LatencyMs = 100, CostUsd = 0.01m
    };

    [Fact]
    public void Kpis_ComputesRates()
    {
        var k = MetricsCalculator.ComputeKpis([Run(true, 1), Run(true, .8), Run(false, .2), Run(false, 0)]);
        Assert.Equal(4, k.TotalRuns);
        Assert.Equal(.5, k.PassRate);
        Assert.Equal(.5, k.FailRate);
        Assert.Equal(.5, k.AvgScore);
    }

    [Fact]
    public void Kpis_Empty_IsZeroed() => Assert.Equal(0, MetricsCalculator.ComputeKpis([]).TotalRuns);

    [Fact]
    public void Qa_HallucinationRate_UsesFaithfulnessThreshold()
    {
        var qa = MetricsCalculator.ComputeQa([Run(true, 1, faith: .9), Run(false, 0, faith: .2), Run(false, 0, faith: .4), Run(true, 1, faith: .8)], []);
        Assert.Equal(.5, qa.HallucinationRate);
        Assert.Equal(.575, qa.GroundednessScore!.Value, 3);
    }

    [Fact]
    public void Qa_FalsePositiveAndNegativeRates_ComparedToHumanLabels()
    {
        var runs = new[]
        {
            Run(true, 1, human: false),  // FP: judge passed, human failed
            Run(false, 0, human: false), // TN
            Run(false, 0, human: true),  // FN: judge failed, human passed
            Run(true, 1, human: true),   // TP
        };
        var qa = MetricsCalculator.ComputeQa(runs, []);
        Assert.Equal(.5, qa.FalsePositiveRate);
        Assert.Equal(.5, qa.FalseNegativeRate);
        Assert.Equal(4, qa.HumanLabeledRuns);
    }

    [Fact]
    public void Qa_NoData_ReturnsNulls()
    {
        var qa = MetricsCalculator.ComputeQa([], []);
        Assert.Null(qa.HallucinationRate);
        Assert.Null(qa.FalsePositiveRate);
    }

    [Fact]
    public void Qa_ToxicityRate()
        => Assert.Equal(.25, MetricsCalculator.ComputeQa([Run(false, 0, toxic: true), Run(true, 1), Run(true, 1), Run(true, 1)], []).ToxicityRate);

    [Fact]
    public void Dashboard_GroupsFailuresAndCategories()
    {
        var d = MetricsCalculator.ComputeDashboard([Run(false, 0, failure: "Security", category: "Security"), Run(false, 0, failure: "Security", category: "Security"), Run(true, 1)]);
        Assert.Equal(2, d.FailuresByType.Single(f => f.Name == "Security").Count);
        Assert.Equal(2, d.ByCategory.Single(c => c.Name == "Security").Count);
    }

    [Fact]
    public void Leaderboard_OrdersByScore()
    {
        var lb = MetricsCalculator.ComputeLeaderboard([Run(true, .6, "a"), Run(true, .9, "b")]);
        Assert.Equal("b", lb[0].Model);
    }
}

public class SecuritySuiteTests
{
    [Fact]
    public void Suite_CoversAllFourAttackTypes()
    {
        var types = SecuritySuite.Cases().Select(c => c.SecurityType).Distinct().ToHashSet();
        Assert.Contains(SecurityType.PromptInjection, types);
        Assert.Contains(SecurityType.Jailbreak, types);
        Assert.Contains(SecurityType.DataLeakage, types);
        Assert.Contains(SecurityType.Toxicity, types);
    }
}
