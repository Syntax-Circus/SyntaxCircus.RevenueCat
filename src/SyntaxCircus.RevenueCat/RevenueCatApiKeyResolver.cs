namespace SyntaxCircus.RevenueCat;

public static class RevenueCatApiKeyResolver
{
    /// <summary>
    /// Returns configured v1-compatible credentials in preference order (PublicApiKey first, then ApiKey).
    /// RevenueCat issues all secret keys with the same "sk_"/"atk_" prefix regardless of the API version
    /// selected for them in the dashboard, so the key string itself cannot distinguish a v1-compatible key
    /// from a v2-only one — both configured values are returned as candidates and left to the caller's HTTP
    /// call to succeed or fail against the v1 endpoint.
    /// </summary>
    public static List<(string Source, string ApiKey)> ResolveV1CompatibleCredentials(RevenueCatOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var credentials = new List<(string Source, string ApiKey)>();
        AddCredential(credentials, "RevenueCat:PublicApiKey", options.PublicApiKey);
        AddCredential(credentials, "RevenueCat:ApiKey", options.ApiKey);
        return credentials;
    }

    public static string ResolvePrimaryV1CompatibleApiKeyOrThrow(RevenueCatOptions options, string operationName)
    {
        var credentials = ResolveV1CompatibleCredentials(options);
        if (credentials.Count > 0)
        {
            return credentials[0].ApiKey;
        }

        throw new InvalidOperationException(
            $"{operationName} requires RevenueCat:PublicApiKey or RevenueCat:ApiKey to be configured.");
    }

    private static void AddCredential(List<(string Source, string ApiKey)> credentials, string source, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var trimmed = apiKey.Trim();
        if (credentials.Any(existing => string.Equals(existing.ApiKey, trimmed, StringComparison.Ordinal)))
        {
            return;
        }

        credentials.Add((source, trimmed));
    }
}
