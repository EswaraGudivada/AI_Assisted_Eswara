using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using UrlShortener.Api.Endpoints;
using UrlShortener.Api.Http;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddUrlShortener(builder.Configuration);
builder.Services.AddSingleton<ShortUrlBuilder>();
builder.Services.AddApiRateLimiting();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions.TryAdd("correlationId", CorrelationIdMiddleware.Get(context.HttpContext)));
builder.Services.AddShortenerOpenApi();

var app = builder.Build();

await app.Services.InitializeStorageAsync(app.Configuration);

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next(context);
});
app.UseRateLimiter();

app.MapShortenerOpenApi();
app.MapHealthChecks("/health", new HealthCheckOptions { ResponseWriter = WriteHealthAsync });
app.MapShortLinkEndpoints();

await app.RunAsync();

static Task WriteHealthAsync(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
    context.Response.ContentType = "application/json";
    return context.Response.WriteAsync(JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        durationMs = report.TotalDuration.TotalMilliseconds,
        checks = report.Entries.ToDictionary(
            e => e.Key,
            e => new { status = e.Value.Status.ToString(), description = e.Value.Description, data = e.Value.Data }),
    }));
}

public partial class Program;
