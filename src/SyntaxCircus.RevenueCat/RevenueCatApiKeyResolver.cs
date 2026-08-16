namespace SyntaxCircus.RevenueCat;

public static class RevenueCatApiKeyResolver
{
    private const string SecretApiKeyPrefix = "sk_";
    private const string OAuthTokenPrefix = "atk_";

    /// <summary>Returns configured v1-compatible credentials in preference order (PublicApiKey first, then ApiKey unless it's v2-only).</summary>
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

        if (LooksLikeV2OnlyCredential(options.ApiKey))
        {
            throw new InvalidOperationException(
                $"{operationName} requires a RevenueCat API v1-compatible app-specific key. Configure RevenueCat:PublicApiKey with the RevenueCat app's public/API key; RevenueCat:ApiKey may remain your project secret key for other backend uses.");
        }

        throw new InvalidOperationException(
            $"{operationName} requires RevenueCat:PublicApiKey or a non-secret RevenueCat:ApiKey to be configured.");
    }

    private static void AddCredential(List<(string Source, string ApiKey)> credentials, string source, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var trimmed = apiKey.Trim();
        if (LooksLikeV2OnlyCredential(trimmed) ||
            credentials.Any(existing => string.Equals(existing.ApiKey, trimmed, StringComparison.Ordinal)))
        {
            return;
        }

        credentials.Add((source, trimmed));
    }

    private static bool LooksLikeV2OnlyCredential(string? apiKey)
        => !string.IsNullOrWhiteSpace(apiKey) &&
           (apiKey.Trim().StartsWith(SecretApiKeyPrefix, StringComparison.Ordinal) ||
            apiKey.Trim().StartsWith(OAuthTokenPrefix, StringComparison.Ordinal));
}
