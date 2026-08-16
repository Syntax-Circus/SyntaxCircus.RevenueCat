namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatServiceCollectionExtensionsTests
{
    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values)
        => new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void AddRevenueCat_NullServices_ThrowsArgumentNullException()
        => Should.Throw<ArgumentNullException>(() =>
            RevenueCatServiceCollectionExtensions.AddRevenueCat(null!, BuildConfiguration([])));

    [Fact]
    public void AddRevenueCat_NullConfiguration_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();

        Should.Throw<ArgumentNullException>(() => services.AddRevenueCat(null!));
    }

    [Fact]
    public void AddRevenueCat_ResolvesAllPublicServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRevenueCat(BuildConfiguration(new Dictionary<string, string?>
        {
            ["RevenueCat:PublicApiKey"] = "public_key",
        }));

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IRevenueCatTransactionService>().ShouldBeOfType<RevenueCatTransactionService>();
        provider.GetRequiredService<IRevenueCatSubscriberAliasClient>().ShouldBeOfType<RevenueCatSubscriberAliasClient>();
        provider.GetRequiredService<IRevenueCatPurchaseVerifier>().ShouldBeOfType<RevenueCatPurchaseVerifier>();
        provider.GetRequiredService<IRevenueCatProductCatalogService>().ShouldBeOfType<RevenueCatProductCatalogService>();
    }

    [Fact]
    public void AddRevenueCat_BindsOptionsFromConfiguration()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRevenueCat(BuildConfiguration(new Dictionary<string, string?>
        {
            ["RevenueCat:PublicApiKey"] = "public_key",
            ["RevenueCat:WebhookSecret"] = "whsec_test",
        }));

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<RevenueCatOptions>>().Value;

        options.PublicApiKey.ShouldBe("public_key");
        options.WebhookSecret.ShouldBe("whsec_test");
    }

    [Fact]
    public void AddRevenueCat_ApiClientBaseAddress_MatchesConfiguredApiBaseUrl()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRevenueCat(BuildConfiguration(new Dictionary<string, string?>
        {
            ["RevenueCat:PublicApiKey"] = "public_key",
            ["RevenueCat:ApiBaseUrl"] = "https://custom.revenuecat.test/",
        }));

        using var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = httpClientFactory.CreateClient(nameof(IRevenueCatSubscriberAliasClient));

        client.BaseAddress.ShouldBe(new Uri("https://custom.revenuecat.test/"));
    }

    [Fact]
    public void AddRevenueCat_ProductSyncClientBaseAddress_MatchesConfiguredProductSyncApiBaseUrl()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRevenueCat(BuildConfiguration(new Dictionary<string, string?>
        {
            ["RevenueCat:PublicApiKey"] = "public_key",
            ["RevenueCat:ProductSyncApiBaseUrl"] = "https://sync.revenuecat.test/",
        }));

        using var provider = services.BuildServiceProvider();
        var httpClientFactory = provider.GetRequiredService<IHttpClientFactory>();
        using var client = httpClientFactory.CreateClient(nameof(IRevenueCatProductCatalogService));

        client.BaseAddress.ShouldBe(new Uri("https://sync.revenuecat.test/"));
    }
}
