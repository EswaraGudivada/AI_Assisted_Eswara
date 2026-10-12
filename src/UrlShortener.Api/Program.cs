using UrlShortener.Api.Endpoints;
using UrlShortener.Api.Infrastructure;
using UrlShortener.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddUrlShortener(builder.Configuration);
builder.Services.AddSingleton<ShortUrlBuilder>();
builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

await app.Services.InitializeStorageAsync();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    await next(context);
});
app.UseRateLimiter();

app.MapOpenApi();
app.MapHealthChecks("/health");
app.MapShortLinkEndpoints();

await app.RunAsync();

public partial class Program;
