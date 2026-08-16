using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace SyntaxCircus.RevenueCat;

public sealed class RevenueCatSubscriberAliasClient(
    HttpClient httpClient,
    IOptions<RevenueCatOptions> revenueCatOptions) : IRevenueCatSubscriberAliasClient
{
    public async Task CreateAliasAsync(string canonicalAppUserId, string anonymousAppUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalAppUserId);
        ArgumentException.ThrowIfNullOrWhiteSpace(anonymousAppUserId);

        var apiKey = RevenueCatApiKeyResolver.ResolvePrimaryV1CompatibleApiKeyOrThrow(revenueCatOptions.Value, "RevenueCat aliasing");

        using var request = new HttpRequestMessage(HttpMethod.Post, $"v1/subscribers/{Uri.EscapeDataString(anonymousAppUserId)}/alias");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = JsonContent.Create(new { new_app_user_id = canonicalAppUserId });

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.Forbidden && message.Contains("\"code\":7723", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "RevenueCat aliasing failed because the configured key is not compatible with RevenueCat API v1. Configure RevenueCat:PublicApiKey with the RevenueCat app's public/API key.");
            }

            throw new InvalidOperationException($"RevenueCat alias request failed with HTTP {(int)response.StatusCode}: {message}");
        }
    }
}
