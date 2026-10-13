using Microsoft.OpenApi;

namespace UrlShortener.Api.Infrastructure;

internal static class OpenApiConfiguration
{
    /// <summary>On in Development; elsewhere only with <c>OpenApi:Enabled=true</c>, so production does not advertise its API.</summary>
    public static bool OpenApiEnabled(this WebApplication app) =>
        app.Configuration.GetValue("OpenApi:Enabled", app.Environment.IsDevelopment());

    public static IServiceCollection AddShortenerOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "URL Shortener API",
                Version = "v1",
                Description = "Create short links, look them up (200 with the original URL), and read click statistics.",
            };
            return Task.CompletedTask;
        }));

    public static WebApplication MapShortenerOpenApi(this WebApplication app)
    {
        if (!app.OpenApiEnabled())
        {
            return app;
        }

        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "URL Shortener API v1");
            options.RoutePrefix = "swagger";
            options.DocumentTitle = "URL Shortener API";
            options.DocExpansion(Swashbuckle.AspNetCore.SwaggerUI.DocExpansion.List);
            options.EnableTryItOutByDefault();
            options.DisplayRequestDuration();
        });
        return app;
    }
}
