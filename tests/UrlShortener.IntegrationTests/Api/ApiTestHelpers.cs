using System.Net.Http.Json;
using UrlShortener.Api.Contracts;

namespace UrlShortener.IntegrationTests.Api;

internal static class ApiTestHelpers
{
    public static async Task<ShortLinkResponse> CreateLinkAsync(this HttpClient client, string url, string? alias = null, DateTimeOffset? expiresAt = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/links", new CreateShortLinkRequest(url, alias, expiresAt));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ShortLinkResponse>())!;
    }

    public static async Task<T> EventuallyAsync<T>(Func<Task<T>> action, Func<T, bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var value = await action();
            if (condition(value))
            {
                return value;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Condition was not met in time.");
            }

            await Task.Delay(50);
        }
    }
}
