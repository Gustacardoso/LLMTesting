using System.Net;
using System.Net.Http.Json;
using System.Text;
using LlmQuality.Api.Services.Metrics;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace LlmQuality.Tests.Integration;

/// <summary>Spins up a real PostgreSQL via Testcontainers and exercises the API end-to-end with the offline mock LLM.</summary>
public class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public HttpClient Client { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _pg.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _pg.GetConnectionString(),
                ["Llm:OpenAiKey"] = "", ["Llm:AnthropicKey"] = "", ["Llm:GeminiKey"] = "", ["Llm:EvaluatorUrl"] = "",
                ["Llm:ConsistencySamples"] = "2",
            })));
        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await _pg.DisposeAsync();
    }
}

[CollectionDefinition("api")]
public class ApiCollection : ICollectionFixture<ApiFixture>;

[Collection("api")]
public class ApiIntegrationTests(ApiFixture fx)
{
    private HttpClient Http => fx.Client;

    private async Task<Guid> CreateCase(string name = "Scrum")
    {
        var resp = await Http.PostAsJsonAsync("/api/testcases", new
        {
            name, category = "Agile", prompt = "Explique o que é Scrum",
            expectedAnswer = "Framework ágil para gestão de projetos.", acceptanceCriteria = "Menciona framework ágil"
        });
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        return (await resp.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>())!["id"]!.GetValue<Guid>();
    }

    [Fact]
    public async Task Health_ReturnsOk() => Assert.Equal(HttpStatusCode.OK, (await Http.GetAsync("/api/health")).StatusCode);

    [Fact]
    public async Task Responses_CarrySecurityHeaders()
    {
        var r = await Http.GetAsync("/api/health");
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
    }

    [Fact]
    public async Task CreateTestCase_WithoutPrompt_ReturnsValidationProblem()
    {
        var r = await Http.PostAsJsonAsync("/api/testcases", new { name = "x", category = "c", prompt = "", expectedAnswer = "e" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task RunTestCase_PersistsExecutionMetadataAndScores()
    {
        var id = await CreateCase("Run metadata");
        var resp = await Http.PostAsJsonAsync($"/api/testcases/{id}/run", new { model = "gpt-4o" });
        resp.EnsureSuccessStatusCode();
        var run = (await resp.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>())!;
        Assert.Equal("gpt-4o", run["model"]!.GetValue<string>());
        Assert.True(run["promptTokens"]!.GetValue<int>() > 0);
        Assert.NotNull(run["consistency"]);
        Assert.InRange(run["score"]!.GetValue<double>(), 0, 1);
    }

    [Fact]
    public async Task SecuritySuite_SeedsAndRuns_MockRefusesAttacks()
    {
        (await Http.PostAsync("/api/security/seed", null)).EnsureSuccessStatusCode();
        var resp = await Http.PostAsJsonAsync("/api/security/run", new { models = new[] { "gpt-4o" } });
        resp.EnsureSuccessStatusCode();
        var results = (await resp.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonArray>())!;
        Assert.True(results.Count >= 12);
    }

    [Fact]
    public async Task Rag_UploadAndAsk_ReturnsContextsAndScores()
    {
        using var form = new MultipartFormDataContent();
        var text = "O Scrum é um framework ágil. As sprints duram até quatro semanas. O Product Owner prioriza o backlog.";
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(text)), "file", "scrum.txt");
        var up = await Http.PostAsync("/api/rag/documents", form);
        Assert.Equal(HttpStatusCode.OK, up.StatusCode);

        var ask = await Http.PostAsJsonAsync("/api/rag/ask", new { question = "Quanto dura uma sprint?", groundTruth = "Até quatro semanas.", model = "gpt-4o" });
        ask.EnsureSuccessStatusCode();
        var json = (await ask.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonNode>())!;
        Assert.NotEmpty(json["contexts"]!.AsArray());
        Assert.NotNull(json["scores"]!["contextRecall"]);
    }

    [Fact]
    public async Task Rag_Upload_RejectsDisallowedExtension()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "file", "malware.exe");
        Assert.Equal(HttpStatusCode.BadRequest, (await Http.PostAsync("/api/rag/documents", form)).StatusCode);
    }

    [Fact]
    public async Task Dashboard_Leaderboard_Metrics_AndReport_Work()
    {
        var id = await CreateCase("Dash");
        (await Http.PostAsJsonAsync($"/api/testcases/{id}/run", new { model = "gpt-4.1" })).EnsureSuccessStatusCode();

        var dash = await Http.GetFromJsonAsync<DashboardDto>("/api/dashboard");
        Assert.True(dash!.Kpis.TotalRuns > 0);

        var board = await Http.GetFromJsonAsync<List<LeaderboardRow>>("/api/leaderboard");
        Assert.Contains(board!, r => r.Model == "gpt-4.1");

        Assert.NotNull(await Http.GetFromJsonAsync<QaMetrics>("/api/metrics"));

        var pdf = await Http.GetAsync("/api/reports/pdf");
        Assert.Equal("application/pdf", pdf.Content.Headers.ContentType!.MediaType);
        var bytes = await pdf.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
