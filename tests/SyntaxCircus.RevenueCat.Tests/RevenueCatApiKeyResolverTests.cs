namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatApiKeyResolverTests
{
    [Fact]
    public void ResolveV1CompatibleCredentials_NullOptions_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() => RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(null!));

    [Fact]
    public void ResolveV1CompatibleCredentials_BothConfigured_PublicApiKeyFirst()
    {
        var options = new RevenueCatOptions { PublicApiKey = "public_key", ApiKey = "legacy_key" };

        var credentials = RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(options);

        credentials.ShouldBe([("RevenueCat:PublicApiKey", "public_key"), ("RevenueCat:ApiKey", "legacy_key")]);
    }

    [Fact]
    public void ResolveV1CompatibleCredentials_ApiKeyIsSecretKey_Included()
    {
        // RevenueCat issues both v1- and v2-designated secret keys with the same "sk_" prefix, so the
        // resolver can no longer tell them apart from the string alone — both candidates are returned.
        var options = new RevenueCatOptions { PublicApiKey = "public_key", ApiKey = "sk_secret_key" };

        var credentials = RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(options);

        credentials.ShouldBe([("RevenueCat:PublicApiKey", "public_key"), ("RevenueCat:ApiKey", "sk_secret_key")]);
    }

    [Fact]
    public void ResolveV1CompatibleCredentials_ApiKeyIsOAuthToken_Included()
    {
        var options = new RevenueCatOptions { ApiKey = "atk_oauth_token" };

        var credentials = RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(options);

        credentials.ShouldBe([("RevenueCat:ApiKey", "atk_oauth_token")]);
    }

    [Fact]
    public void ResolveV1CompatibleCredentials_DuplicateValues_Deduplicated()
    {
        var options = new RevenueCatOptions { PublicApiKey = "same_key", ApiKey = "same_key" };

        var credentials = RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(options);

        credentials.Count.ShouldBe(1);
    }

    [Fact]
    public void ResolveV1CompatibleCredentials_WhitespaceTrimmed()
    {
        var options = new RevenueCatOptions { PublicApiKey = "  padded_key  " };

        var credentials = RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(options);

        credentials[0].ApiKey.ShouldBe("padded_key");
    }

    [Fact]
    public void ResolveV1CompatibleCredentials_NoneConfigured_ReturnsEmpty()
    {
        var credentials = RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(new RevenueCatOptions());

        credentials.ShouldBeEmpty();
    }

    [Fact]
    public void ResolvePrimaryV1CompatibleApiKeyOrThrow_CredentialAvailable_ReturnsFirst()
    {
        var options = new RevenueCatOptions { PublicApiKey = "public_key" };

        var key = RevenueCatApiKeyResolver.ResolvePrimaryV1CompatibleApiKeyOrThrow(options, "test operation");

        key.ShouldBe("public_key");
    }

    [Fact]
    public void ResolvePrimaryV1CompatibleApiKeyOrThrow_OnlySecretKeyConfigured_ReturnsIt()
    {
        var options = new RevenueCatOptions { ApiKey = "sk_secret_only" };

        var key = RevenueCatApiKeyResolver.ResolvePrimaryV1CompatibleApiKeyOrThrow(options, "test operation");

        key.ShouldBe("sk_secret_only");
    }

    [Fact]
    public void ResolvePrimaryV1CompatibleApiKeyOrThrow_NothingConfigured_ThrowsGenericGuidance()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            RevenueCatApiKeyResolver.ResolvePrimaryV1CompatibleApiKeyOrThrow(new RevenueCatOptions(), "test operation"));

        exception.Message.ShouldContain("test operation");
    }
}
