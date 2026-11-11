using System.Text.Json.Serialization;

namespace LlmQuality.Api.Domain;

public enum SecurityType { None = 0, PromptInjection, Jailbreak, DataLeakage, Toxicity }

public class TestCase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Category { get; set; } = "General";
    public string Prompt { get; set; } = "";
    public string ExpectedAnswer { get; set; } = "";
    public string AcceptanceCriteria { get; set; } = "";
    /// <summary>Optional grounding context used for faithfulness.</summary>
    public string? Context { get; set; }
    public SecurityType SecurityType { get; set; } = SecurityType.None;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public List<TestRun> Runs { get; set; } = [];
}

public class TestRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TestCaseId { get; set; }
    [JsonIgnore] public TestCase? TestCase { get; set; }

    // Execution metadata
    public string Model { get; set; } = "";
    public string Response { get; set; } = "";
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public long LatencyMs { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal CostUsd { get; set; }

    // Quality metrics (0..1)
    public double? Relevance { get; set; }
    public double? Faithfulness { get; set; }
    public double? Completeness { get; set; }
    public double? Clarity { get; set; }
    public double? Consistency { get; set; }

    // Safety
    public bool Safe { get; set; } = true;
    public bool Toxic { get; set; }

    // Verdict
    public double Score { get; set; }
    public bool Passed { get; set; }
    /// <summary>Relevance | Faithfulness | Completeness | Clarity | Consistency | Security | Toxicity | Error</summary>
    public string? FailureType { get; set; }
    public string? JudgeReason { get; set; }
    public string EvaluatorEngine { get; set; } = "llm-judge";

    /// <summary>Human QA label used to compute false positive / false negative rates of the automated judge.</summary>
    public bool? HumanVerdict { get; set; }
}

public class Document
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    [JsonIgnore] public List<DocumentChunk> Chunks { get; set; } = [];
}

public class DocumentChunk
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid DocumentId { get; set; }
    [JsonIgnore] public Document? Document { get; set; }
    public int Index { get; set; }
    public string Text { get; set; } = "";
    public double[] Embedding { get; set; } = [];
}

public class RagRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Question { get; set; } = "";
    public string? GroundTruth { get; set; }
    public string Answer { get; set; } = "";
    public string Model { get; set; } = "";
    public string ContextsJson { get; set; } = "[]";
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
    public long LatencyMs { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public decimal CostUsd { get; set; }

    public double? ContextPrecision { get; set; }
    public double? AnswerRelevancy { get; set; }
    public double? Faithfulness { get; set; }
    public double? ContextRecall { get; set; }
    public string Engine { get; set; } = "llm-judge";
}
