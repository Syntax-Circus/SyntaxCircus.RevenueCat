using System.Net;
using System.Net.Http.Headers;

namespace SyntaxCircus.RevenueCat;

public sealed class RevenueCatSubscriberDeletionClient(
    HttpClient httpClient,
    IOptions<RevenueCatOptions> revenueCatOptions) : IRevenueCatSubscriberDeletionClient
{
    public async Task<RevenueCatSubscriberDeletionResult> DeleteSubscriberAsync(string appUserId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserId);

        var apiKey = RevenueCatApiKeyResolver.ResolveSecretApiKeyOrThrow(revenueCatOptions.Value, "RevenueCat subscriber deletion");

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"v1/subscribers/{Uri.EscapeDataString(appUserId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return RevenueCatSubscriberDeletionResult.NotFound;
        }

        if (!response.IsSuccessStatusCode)
        {
            var message = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            throw new HttpRequestException(
                $"RevenueCat subscriber deletion request failed with HTTP {(int)response.StatusCode}: {message}",
                inner: null,
                statusCode: response.StatusCode);
        }

        return RevenueCatSubscriberDeletionResult.Deleted;
    }
}
