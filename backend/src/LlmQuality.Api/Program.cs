using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using LlmQuality.Api.Data;
using LlmQuality.Api.Endpoints;
using LlmQuality.Api.Services.Evaluation;
using LlmQuality.Api.Services.Llm;
using LlmQuality.Api.Services.Rag;
using LlmQuality.Api.Services.Reports;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDb>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddHttpClient();
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddOpenApi();

// Providers: first match wins; MockProvider is the offline fallback and must be registered last.
builder.Services.AddSingleton<ILlmProvider, OpenAiProvider>();
builder.Services.AddSingleton<ILlmProvider, AnthropicProvider>();
builder.Services.AddSingleton<ILlmProvider, GeminiProvider>();
builder.Services.AddSingleton<ILlmProvider, MockProvider>();
builder.Services.AddSingleton<LlmGateway>();
builder.Services.AddSingleton<IEmbedder, Embedder>();
builder.Services.AddScoped<IJudge, JudgeService>();
builder.Services.AddScoped<RunService>();
builder.Services.AddScoped<RagService>();
builder.Services.AddScoped<ReportService>();

var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:3000"];
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

// OWASP: throttle expensive endpoints (LLM calls cost money) — API4 Unrestricted Resource Consumption.
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "anon",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = builder.Configuration.GetValue("RateLimit:PerMinute", 300), Window = TimeSpan.FromMinutes(1) }));
});
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o => o.MultipartBodyLengthLimit = 10 * 1024 * 1024);

var app = builder.Build();

// OWASP: secure headers
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    ctx.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";
    await next();
});
app.UseRateLimiter();
app.UseCors();

if (app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("OpenApi:Enabled")) app.MapOpenApi();

app.MapApi();

// Schema bootstrap with retry (DB container may still be starting). Replace with EF migrations for production.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    for (var attempt = 1; ; attempt++)
    {
        try { await db.Database.EnsureCreatedAsync(); break; }
        catch when (attempt < 15) { await Task.Delay(2000); }
    }
}

app.Run();

public partial class Program;
