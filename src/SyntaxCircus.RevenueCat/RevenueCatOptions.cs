namespace SyntaxCircus.RevenueCat;

/// <summary>Configuration for the RevenueCat integration. Bind from the "RevenueCat" configuration section.</summary>
public sealed class RevenueCatOptions
{
    public const string SectionName = "RevenueCat";

    /// <summary>Customer operations API version. Defaults to V1 for compatibility; aliasing always uses V1.</summary>
    public RevenueCatCustomerApiVersion CustomerApiVersion { get; init; } = RevenueCatCustomerApiVersion.V1;

    /// <summary>V2 customer operations base URL, independent of product publication.</summary>
    public string CustomerApiBaseUrl { get; init; } = "https://api.revenuecat.com/v2/";

    /// <summary>Optional V2 purchase app allowlist. Empty accepts purchases across the project.</summary>
    public string[] CustomerAppIds { get; init; } = [];


    /// <summary>
    /// Server credential for the selected customer API version. V2 requires a secret key with
    /// customer purchase/customer permissions plus product read permission. V1 may use a legacy
    /// compatible key. Pair with <see cref="PublicApiKey"/> for aliasing when this key is V2-only.
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

    /// <summary>RevenueCat project identifier required for V2 customer operations and product publication.</summary>
    public string? ProjectId { get; init; }

    /// <summary>RevenueCat app identifier products are published to. Fallback for single-app setups when <see cref="ProductSyncAppIds"/> is empty.</summary>
    public string? ProductSyncAppId { get; init; }

    /// <summary>RevenueCat app identifiers a single internal product should be published to (multi-platform/test-plus-live setups).</summary>
    public string[] ProductSyncAppIds { get; init; } = [];

    /// <summary>
    /// The store transaction environment this deployment expects to see (e.g. a UAT deployment sets
    /// <see cref="RevenueCatTransactionEnvironment.Sandbox"/>, Production sets
    /// <see cref="RevenueCatTransactionEnvironment.Production"/>). Opt-in and unset by default, so
    /// existing single-environment consumers are unaffected. When set,
    /// <see cref="RevenueCatTransactionEnvironmentMatcher"/> flags a mismatch between this and a
    /// webhook event's/verified purchase's real environment - independent of what any client believes
    /// its selected backend to be, since a mobile client's app-environment choice isn't proof of the
    /// underlying store transaction's real environment.
    /// </summary>
    public RevenueCatTransactionEnvironment? ExpectedTransactionEnvironment { get; init; }

    /// <summary>
    /// Relative endpoint path used by <see cref="IRevenueCatTransactionService.GetTransactionsAsync"/>
    /// to retrieve transactions for reconciliation. RevenueCat's own REST API has no bulk
    /// "list transactions in a date range" endpoint â€” this default only resolves against a custom
    /// aggregation proxy you control. Most consumers should instead call
    /// <see cref="IRevenueCatTransactionService.GetTransactionsForCandidatesAsync"/>, which queries
    /// RevenueCat's real per-subscriber endpoint (<c>GET v1/subscribers/{app_user_id}</c>) for a
    /// caller-supplied set of candidate app_user_ids.
    /// </summary>
    public string TransactionsEndpoint { get; init; } = "v1/transactions";
}
