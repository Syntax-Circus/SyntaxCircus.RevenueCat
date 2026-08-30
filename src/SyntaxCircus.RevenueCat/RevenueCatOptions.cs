namespace SyntaxCircus.RevenueCat;

/// <summary>Configuration for the RevenueCat integration. Bind from the "RevenueCat" configuration section.</summary>
public sealed class RevenueCatOptions
{
    public const string SectionName = "RevenueCat";

    /// <summary>
    /// Project secret key (e.g. <c>sk_...</c>) or a legacy v1-compatible key used for
    /// server-to-server calls. Pair with <see cref="PublicApiKey"/> for subscriber/alias
    /// endpoints when this is a v2-only secret key.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>App-specific RevenueCat key used for API v1 subscriber endpoints (purchase confirmation, aliasing).</summary>
    public string? PublicApiKey { get; init; }

    /// <summary>RevenueCat webhook signing secret from the RevenueCat dashboard. Required unless <see cref="RequireWebhookSecret"/> is false.</summary>
    public string? WebhookSecret { get; init; }

    /// <summary>
    /// When true (the default), <see cref="RevenueCatWebhookReader"/> rejects webhook requests
    /// outright if <see cref="WebhookSecret"/> isn't configured, rather than silently accepting
    /// unverified requests. Only disable this for local development.
    /// </summary>
    public bool RequireWebhookSecret { get; init; } = true;

    /// <summary>Maximum permitted clock skew for webhook signature timestamps (replay-window tolerance).</summary>
    public int WebhookSignatureToleranceSeconds { get; init; } = 300;

    /// <summary>Maximum accepted webhook request body size in bytes, checked before any parsing or verification work.</summary>
    public int WebhookMaxBodyBytes { get; init; } = 262_144;

    /// <summary>RevenueCat REST API v1 base URL (subscribers, transactions).</summary>
    public string ApiBaseUrl { get; init; } = "https://api.revenuecat.com/";

    /// <summary>RevenueCat REST API v2 base URL (product publication).</summary>
    public string ProductSyncApiBaseUrl { get; init; } = "https://api.revenuecat.com/v2/";

    /// <summary>Secret API key used for product publication (v2 API).</summary>
    public string? ProductSyncApiKey { get; init; }

    /// <summary>RevenueCat project identifier used for product publication.</summary>
    public string? ProjectId { get; init; }

    /// <summary>RevenueCat app identifier products are published to. Fallback for single-app setups when <see cref="ProductSyncAppIds"/> is empty.</summary>
    public string? ProductSyncAppId { get; init; }

    /// <summary>RevenueCat app identifiers a single internal product should be published to (multi-platform/test-plus-live setups).</summary>
    public string[] ProductSyncAppIds { get; init; } = [];

    /// <summary>
    /// Relative endpoint path used by <see cref="IRevenueCatTransactionService.GetTransactionsAsync"/>
    /// to retrieve transactions for reconciliation. RevenueCat's own REST API has no bulk
    /// "list transactions in a date range" endpoint — this default only resolves against a custom
    /// aggregation proxy you control. Most consumers should instead call
    /// <see cref="IRevenueCatTransactionService.GetTransactionsForCandidatesAsync"/>, which queries
    /// RevenueCat's real per-subscriber endpoint (<c>GET v1/subscribers/{app_user_id}</c>) for a
    /// caller-supplied set of candidate app_user_ids.
    /// </summary>
    public string TransactionsEndpoint { get; init; } = "v1/transactions";
}
