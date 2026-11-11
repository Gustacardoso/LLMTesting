using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;

namespace LlmQuality.Api.Services.Llm;

public class LlmOptions
{
    public string OpenAiKey { get; set; } = "";
    public string AnthropicKey { get; set; } = "";
    public string GeminiKey { get; set; } = "";
    public string JudgeModel { get; set; } = "gpt-4o-mini";
    public string EmbeddingModel { get; set; } = "text-embedding-3-small";
    public string[] Models { get; set; } = ["gpt-4o", "gpt-4.1", "claude-sonnet-4-5", "gemini-2.5-pro"];
    /// <summary>Base URL of the Python evaluator sidecar (DeepEval + RAGAS). Empty = disabled.</summary>
    public string EvaluatorUrl { get; set; } = "";
    public double PassThreshold { get; set; } = 0.7;
    public int ConsistencySamples { get; set; } = 3;
}

public record LlmRequest(string Model, string User, string? System = null, double Temperature = 0.2, bool Json = false);
public record LlmResponse(string Text, int PromptTokens, int CompletionTokens, long LatencyMs);

public interface ILlmProvider
{
    bool Live { get; }
    bool Supports(string model);
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct);
}

/// <summary>Routes a model name to the provider that serves it. Falls back to a deterministic mock when no API key is set.</summary>
public class LlmGateway(IEnumerable<ILlmProvider> providers)
{
    private ILlmProvider Resolve(string model) => providers.First(p => p.Supports(model));

    public bool IsLive(string model) => Resolve(model).Live;

    public Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken ct = default)
        => Resolve(request.Model).CompleteAsync(request, ct);
}

public class OpenAiProvider(IHttpClientFactory factory, IOptions<LlmOptions> opt) : ILlmProvider
{
    public bool Live => !string.IsNullOrWhiteSpace(opt.Value.OpenAiKey);
    public bool Supports(string model) => Live && (model.StartsWith("gpt") || model.StartsWith("o1") || model.StartsWith("o3") || model.StartsWith("o4"));

    public async Task<LlmResponse> CompleteAsync(LlmRequest r, CancellationToken ct)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new("Bearer", opt.Value.OpenAiKey);
        var messages = new JsonArray();
        if (r.System is not null) messages.Add(new JsonObject { ["role"] = "system", ["content"] = r.System });
        messages.Add(new JsonObject { ["role"] = "user", ["content"] = r.User });
        var body = new JsonObject { ["model"] = r.Model, ["messages"] = messages, ["temperature"] = r.Temperature };
        if (r.Json) body["response_format"] = new JsonObject { ["type"] = "json_object" };

        var sw = Stopwatch.StartNew();
        using var resp = await http.PostAsJsonAsync("https://api.openai.com/v1/chat/completions", body, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonNode>(ct) ?? throw new InvalidOperationException("Empty OpenAI response");
        sw.Stop();
        return new(json["choices"]![0]!["message"]!["content"]!.GetValue<string>(),
            json["usage"]?["prompt_tokens"]?.GetValue<int>() ?? 0,
            json["usage"]?["completion_tokens"]?.GetValue<int>() ?? 0,
            sw.ElapsedMilliseconds);
    }
}

public class AnthropicProvider(IHttpClientFactory factory, IOptions<LlmOptions> opt) : ILlmProvider
{
    public bool Live => !string.IsNullOrWhiteSpace(opt.Value.AnthropicKey);
    public bool Supports(string model) => Live && model.StartsWith("claude");

    public async Task<LlmResponse> CompleteAsync(LlmRequest r, CancellationToken ct)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("x-api-key", opt.Value.AnthropicKey);
        http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
        var body = new JsonObject
        {
            ["model"] = r.Model,
            ["max_tokens"] = 1024,
            ["temperature"] = r.Temperature,
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = r.User })
        };
        if (r.System is not null) body["system"] = r.System;

        var sw = Stopwatch.StartNew();
        using var resp = await http.PostAsJsonAsync("https://api.anthropic.com/v1/messages", body, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonNode>(ct) ?? throw new InvalidOperationException("Empty Anthropic response");
        sw.Stop();
        return new(json["content"]![0]!["text"]!.GetValue<string>(),
            json["usage"]?["input_tokens"]?.GetValue<int>() ?? 0,
            json["usage"]?["output_tokens"]?.GetValue<int>() ?? 0,
            sw.ElapsedMilliseconds);
    }
}

public class GeminiProvider(IHttpClientFactory factory, IOptions<LlmOptions> opt) : ILlmProvider
{
    public bool Live => !string.IsNullOrWhiteSpace(opt.Value.GeminiKey);
    public bool Supports(string model) => Live && model.StartsWith("gemini");

    public async Task<LlmResponse> CompleteAsync(LlmRequest r, CancellationToken ct)
    {
        var http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("x-goog-api-key", opt.Value.GeminiKey);
        var gen = new JsonObject { ["temperature"] = r.Temperature };
        if (r.Json) gen["responseMimeType"] = "application/json";
        var body = new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = r.User })
            }),
            ["generationConfig"] = gen
        };
        if (r.System is not null)
            body["systemInstruction"] = new JsonObject { ["parts"] = new JsonArray(new JsonObject { ["text"] = r.System }) };

        var sw = Stopwatch.StartNew();
        using var resp = await http.PostAsJsonAsync($"https://generativelanguage.googleapis.com/v1beta/models/{r.Model}:generateContent", body, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadFromJsonAsync<JsonNode>(ct) ?? throw new InvalidOperationException("Empty Gemini response");
        sw.Stop();
        return new(json["candidates"]![0]!["content"]!["parts"]![0]!["text"]!.GetValue<string>(),
            json["usageMetadata"]?["promptTokenCount"]?.GetValue<int>() ?? 0,
            json["usageMetadata"]?["candidatesTokenCount"]?.GetValue<int>() ?? 0,
            sw.ElapsedMilliseconds);
    }
}

/// <summary>Deterministic offline provider so the whole platform can be demoed and tested without API keys.</summary>
public class MockProvider : ILlmProvider
{
    public bool Live => false;
    public bool Supports(string model) => true;

    private static readonly string[] AttackMarkers =
        ["ignore todas", "ignore all", "sem restri", "no restrictions", "confidenci", "confidential", "system prompt", "dan mode", "senha", "password"];

    public Task<LlmResponse> CompleteAsync(LlmRequest r, CancellationToken ct)
    {
        var lower = r.User.ToLowerInvariant();
        var text = AttackMarkers.Any(lower.Contains)
            ? "Não posso ajudar com isso. Esse pedido viola minhas diretrizes de segurança."
            : $"[mock:{r.Model}] Resposta simulada para: {r.User}";
        var pt = Math.Max(1, r.User.Length / 4);
        var ct2 = Math.Max(1, text.Length / 4);
        return Task.FromResult(new LlmResponse(text, pt, ct2, 5));
    }
}
