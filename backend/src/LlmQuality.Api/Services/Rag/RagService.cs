using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml.Packaging;
using LlmQuality.Api.Data;
using LlmQuality.Api.Domain;
using LlmQuality.Api.Services.Evaluation;
using LlmQuality.Api.Services.Llm;
using Microsoft.EntityFrameworkCore;
using UglyToad.PdfPig;

namespace LlmQuality.Api.Services.Rag;

public static class Chunker
{
    /// <summary>Fixed-size sliding window with overlap, preferring to break on whitespace.</summary>
    public static List<string> Split(string text, int size = 800, int overlap = 100)
    {
        if (size <= overlap) throw new ArgumentException("size must be greater than overlap");
        var clean = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        var chunks = new List<string>();
        var start = 0;
        while (start < clean.Length)
        {
            var end = Math.Min(start + size, clean.Length);
            if (end < clean.Length)
            {
                var ws = clean.LastIndexOf(' ', end - 1, Math.Min(size / 4, end - start));
                if (ws > start) end = ws;
            }
            chunks.Add(clean[start..end].Trim());
            if (end >= clean.Length) break;
            start = Math.Max(end - overlap, start + 1);
        }
        return chunks.Where(c => c.Length > 0).ToList();
    }
}

public static class DocumentParser
{
    public static readonly string[] AllowedExtensions = [".pdf", ".docx", ".txt"];

    public static string ExtractText(string fileName, Stream stream)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".txt" => new StreamReader(stream, Encoding.UTF8).ReadToEnd(),
            ".pdf" => ReadPdf(stream),
            ".docx" => ReadDocx(stream),
            _ => throw new NotSupportedException($"Unsupported file type '{ext}'. Allowed: PDF, DOCX, TXT.")
        };
    }

    private static string ReadPdf(Stream s)
    {
        using var pdf = PdfDocument.Open(s);
        return string.Join("\n", pdf.GetPages().Select(p => p.Text));
    }

    private static string ReadDocx(Stream s)
    {
        using var doc = WordprocessingDocument.Open(s, false);
        return doc.MainDocumentPart?.Document.Body?.InnerText ?? "";
    }
}

public record RagAnswer(Guid RunId, string Answer, IReadOnlyList<string> Contexts, RagScores Scores, long LatencyMs, decimal CostUsd);

public class RagService(AppDb db, IEmbedder embedder, LlmGateway llm, IJudge judge)
{
    public async Task<Document> IngestAsync(string fileName, string contentType, Stream content, CancellationToken ct = default)
    {
        var text = DocumentParser.ExtractText(fileName, content);
        var pieces = Chunker.Split(text);
        if (pieces.Count == 0) throw new InvalidOperationException("Document has no extractable text.");
        var vectors = await embedder.EmbedAsync(pieces, ct);

        var doc = new Document { FileName = Path.GetFileName(fileName), ContentType = contentType };
        for (var i = 0; i < pieces.Count; i++)
            doc.Chunks.Add(new DocumentChunk { Index = i, Text = pieces[i], Embedding = vectors[i] });
        db.Documents.Add(doc);
        await db.SaveChangesAsync(ct);
        return doc;
    }

    public async Task<List<string>> RetrieveAsync(string question, int topK, CancellationToken ct = default)
    {
        var q = (await embedder.EmbedAsync([question], ct))[0];
        // Demo-scale brute-force search. For large corpora migrate to pgvector (see docs/ARCHITECTURE.md).
        var all = await db.Chunks.AsNoTracking().Select(c => new { c.Text, c.Embedding }).ToListAsync(ct);
        return all.OrderByDescending(c => VectorMath.Cosine(q, c.Embedding)).Take(topK).Select(c => c.Text).ToList();
    }

    public async Task<RagAnswer> AskAsync(string question, string? groundTruth, string model, int topK, CancellationToken ct = default)
    {
        var contexts = await RetrieveAsync(question, topK, ct);
        var ctx = string.Join("\n---\n", contexts);
        var prompt = $"""
            Answer the question using ONLY the context below. If the answer is not in the context, say you don't know.

            CONTEXT:
            {ctx}

            QUESTION: {question}
            """;
        var res = await llm.CompleteAsync(new(model, prompt), ct);
        var scores = await judge.EvaluateRagAsync(question, res.Text, contexts, groundTruth, ct);
        var cost = Pricing.Estimate(model, res.PromptTokens, res.CompletionTokens);

        var run = new RagRun
        {
            Question = question, GroundTruth = groundTruth, Answer = res.Text, Model = model,
            ContextsJson = JsonSerializer.Serialize(contexts), LatencyMs = res.LatencyMs,
            PromptTokens = res.PromptTokens, CompletionTokens = res.CompletionTokens, CostUsd = cost,
            ContextPrecision = scores.ContextPrecision, AnswerRelevancy = scores.AnswerRelevancy,
            Faithfulness = scores.Faithfulness, ContextRecall = scores.ContextRecall, Engine = scores.Engine
        };
        db.RagRuns.Add(run);
        await db.SaveChangesAsync(ct);
        return new(run.Id, res.Text, contexts, scores, res.LatencyMs, cost);
    }
}
