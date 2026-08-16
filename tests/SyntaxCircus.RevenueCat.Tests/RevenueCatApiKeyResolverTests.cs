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
    public void ResolveV1CompatibleCredentials_ApiKeyIsSecretKey_Excluded()
    {
        var options = new RevenueCatOptions { PublicApiKey = "public_key", ApiKey = "sk_secret_key" };

        var credentials = RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(options);

        credentials.ShouldBe([("RevenueCat:PublicApiKey", "public_key")]);
    }

    [Fact]
    public void ResolveV1CompatibleCredentials_ApiKeyIsOAuthToken_Excluded()
    {
        var options = new RevenueCatOptions { ApiKey = "atk_oauth_token" };

        var credentials = RevenueCatApiKeyResolver.ResolveV1CompatibleCredentials(options);

        credentials.ShouldBeEmpty();
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
    public void ResolvePrimaryV1CompatibleApiKeyOrThrow_OnlySecretKeyConfigured_ThrowsWithV2Guidance()
    {
        var options = new RevenueCatOptions { ApiKey = "sk_secret_only" };

        var exception = Should.Throw<InvalidOperationException>(() =>
            RevenueCatApiKeyResolver.ResolvePrimaryV1CompatibleApiKeyOrThrow(options, "test operation"));

        exception.Message.ShouldContain("PublicApiKey");
    }

    [Fact]
    public void ResolvePrimaryV1CompatibleApiKeyOrThrow_NothingConfigured_ThrowsGenericGuidance()
    {
        var exception = Should.Throw<InvalidOperationException>(() =>
            RevenueCatApiKeyResolver.ResolvePrimaryV1CompatibleApiKeyOrThrow(new RevenueCatOptions(), "test operation"));

        exception.Message.ShouldContain("test operation");
    }
}
