namespace LlmQuality.Api.Services;

/// <summary>Estimated USD price per 1M tokens (input, output). Estimates only — keep in sync with provider price pages.</summary>
public static class Pricing
{
    private static readonly Dictionary<string, (decimal In, decimal Out)> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gpt-4o"] = (2.50m, 10.00m),
        ["gpt-4o-mini"] = (0.15m, 0.60m),
        ["gpt-4.1"] = (2.00m, 8.00m),
        ["claude-sonnet-4-5"] = (3.00m, 15.00m),
        ["gemini-2.5-pro"] = (1.25m, 10.00m),
    };

    public static decimal Estimate(string model, int promptTokens, int completionTokens)
    {
        var key = Table.Keys.OrderByDescending(k => k.Length).FirstOrDefault(k => model.StartsWith(k, StringComparison.OrdinalIgnoreCase));
        if (key is null) return 0m;
        var (pin, pout) = Table[key];
        return (promptTokens * pin + completionTokens * pout) / 1_000_000m;
    }
}
