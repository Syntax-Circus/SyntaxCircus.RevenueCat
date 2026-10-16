namespace SyntaxCircus.RevenueCat;

public static class RevenueCatServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="RevenueCatOptions"/> (bound from the "RevenueCat" section) and typed
    /// <see cref="HttpClient"/>s for <see cref="IRevenueCatTransactionService"/>,
    /// <see cref="IRevenueCatSubscriberAliasClient"/>, <see cref="IRevenueCatPurchaseVerifier"/>,
    /// <see cref="IRevenueCatProductCatalogService"/>, and <see cref="IRevenueCatSubscriberDeletionClient"/>.
    /// Does not register
    /// <see cref="RevenueCatWebhookReader"/> (it's static) or wire up a webhook endpoint â€”
    /// that's your controller/minimal-API's job.
    /// </summary>
    public static IServiceCollection AddRevenueCat(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<RevenueCatOptions>(configuration.GetSection(RevenueCatOptions.SectionName));

        services.AddHttpClient<IRevenueCatTransactionService, RevenueCatTransactionService>(ConfigureApiClient).ConfigurePrimaryHttpMessageHandler(ConfigureCustomerHandler);
        services.AddHttpClient<IRevenueCatSubscriberAliasClient, RevenueCatSubscriberAliasClient>(ConfigureApiClient);
        services.AddHttpClient<IRevenueCatPurchaseVerifier, RevenueCatPurchaseVerifier>(ConfigureApiClient).ConfigurePrimaryHttpMessageHandler(ConfigureCustomerHandler);
        services.AddHttpClient<IRevenueCatProductCatalogService, RevenueCatProductCatalogService>(ConfigureProductSyncClient);
        services.AddHttpClient<IRevenueCatSubscriberDeletionClient, RevenueCatSubscriberDeletionClient>(ConfigureApiClient).ConfigurePrimaryHttpMessageHandler(ConfigureCustomerHandler);

        return services;
    }

    private static HttpMessageHandler ConfigureCustomerHandler(IServiceProvider provider)
        => new HttpClientHandler
        {
            AllowAutoRedirect = provider.GetRequiredService<IOptions<RevenueCatOptions>>().Value.CustomerApiVersion != RevenueCatCustomerApiVersion.V2,
        };

    private static void ConfigureApiClient(IServiceProvider serviceProvider, HttpClient client)
        => ConfigureClient(serviceProvider, client, options => options.ApiBaseUrl);

    private static void ConfigureProductSyncClient(IServiceProvider serviceProvider, HttpClient client)
        => ConfigureClient(serviceProvider, client, options => options.ProductSyncApiBaseUrl);

    private static void ConfigureClient(IServiceProvider serviceProvider, HttpClient client, Func<RevenueCatOptions, string> selectBaseUrl)
    {
        var options = serviceProvider.GetRequiredService<IOptions<RevenueCatOptions>>().Value;
        if (Uri.TryCreate(selectBaseUrl(options), UriKind.Absolute, out var baseAddress))
        {
            client.BaseAddress = baseAddress;
        }

        client.Timeout = TimeSpan.FromSeconds(30);
    }
}
