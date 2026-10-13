using System.Net;

namespace UrlShortener.IntegrationTests.Api;

public sealed class OpenApiGatingTests : IDisposable
{
    private readonly ApiFactory _factory = new();

    [Fact]
    public async Task Swagger_and_openapi_are_off_outside_development_unless_enabled()
    {
        _factory.Settings["OpenApi:Enabled"] = null;
        var client = _factory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
    }

    public void Dispose() => _factory.Dispose();
}
