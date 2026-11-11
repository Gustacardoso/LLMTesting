using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace LlmQuality.Api.Services.Llm;

public interface IEmbedder
{
    Task<double[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}

public static class VectorMath
{
    public static double Cosine(double[] a, double[] b)
    {
        if (a.Length == 0 || a.Length != b.Length) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>Mean pairwise cosine similarity — used as the consistency score.</summary>
    public static double MeanPairwise(IReadOnlyList<double[]> v)
    {
        if (v.Count < 2) return 1;
        double sum = 0; var n = 0;
        for (var i = 0; i < v.Count; i++)
            for (var j = i + 1; j < v.Count; j++) { sum += Cosine(v[i], v[j]); n++; }
        return Math.Clamp(sum / n, 0, 1);
    }
}

/// <summary>OpenAI embeddings when a key exists; otherwise a deterministic hashed bag-of-words embedding (offline/dev/tests).</summary>
public class Embedder(IHttpClientFactory factory, IOptions<LlmOptions> opt) : IEmbedder
{
    private const int HashDims = 256;

    public async Task<double[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];
        if (string.IsNullOrWhiteSpace(opt.Value.OpenAiKey)) return texts.Select(HashEmbed).ToArray();

        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", opt.Value.OpenAiKey);
        var result = new List<double[]>();
        foreach (var batch in texts.Chunk(64))
        {
            var body = new JsonObject
            {
                ["model"] = opt.Value.EmbeddingModel,
                ["input"] = new JsonArray(batch.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray())
            };
            using var resp = await http.PostAsJsonAsync("https://api.openai.com/v1/embeddings", body, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonNode>(ct) ?? throw new InvalidOperationException("Empty embeddings response");
            result.AddRange(json["data"]!.AsArray().Select(d => d!["embedding"]!.AsArray().Select(x => x!.GetValue<double>()).ToArray()));
        }
        return result.ToArray();
    }

    public static double[] HashEmbed(string text)
    {
        var v = new double[HashDims];
        foreach (Match m in Regex.Matches(text.ToLowerInvariant(), @"\p{L}{2,}"))
        {
            var h = (uint)StableHash(m.Value);
            v[h % HashDims] += 1;
        }
        return v;
    }

    private static int StableHash(string s)
    {
        unchecked { var h = 23; foreach (var c in s) h = h * 31 + c; return h & 0x7fffffff; }
    }
}
