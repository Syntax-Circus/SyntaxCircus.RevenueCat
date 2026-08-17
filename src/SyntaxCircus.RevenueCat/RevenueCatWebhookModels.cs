using System.Text.Json;
using System.Text.Json.Serialization;

namespace SyntaxCircus.RevenueCat;

/// <summary>Top-level RevenueCat webhook payload envelope.</summary>
public sealed class RevenueCatWebhookPayload
{
    [JsonPropertyName("event")]
    public RevenueCatEvent Event { get; init; } = new();

    [JsonPropertyName("api_version")]
    public string ApiVersion { get; init; } = string.Empty;
}

/// <summary>The <c>event</c> object nested inside a RevenueCat webhook payload. Unmapped fields are ignored.</summary>
public sealed class RevenueCatEvent
{
    /// <summary>Unique event ID — use as the idempotency key.</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Event type string (e.g. INITIAL_PURCHASE, REFUND) — see <see cref="RevenueCatEventTypes"/>.</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("app_id")]
    public string? AppId { get; init; }

    /// <summary>RevenueCat app user ID.</summary>
    [JsonPropertyName("app_user_id")]
    public string? AppUserId { get; init; }

    [JsonPropertyName("original_app_user_id")]
    public string? OriginalAppUserId { get; init; }

    /// <summary>Store-specific transaction ID.</summary>
    [JsonPropertyName("transaction_id")]
    public string? TransactionId { get; init; }

    [JsonPropertyName("original_transaction_id")]
    public string? OriginalTransactionId { get; init; }

    /// <summary>Product identifier.</summary>
    [JsonPropertyName("product_id")]
    public string? ProductId { get; init; }

    /// <summary>Entitlement IDs granted by this purchase.</summary>
    [JsonPropertyName("entitlement_ids")]
    public IReadOnlyList<string> EntitlementIds { get; init; } = [];

    /// <summary>Purchase price in the transaction currency.</summary>
    [JsonPropertyName("price")]
    public decimal? Price { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }

    /// <summary>Store identifier: APP_STORE, PLAY_STORE, STRIPE, RC_BILLING, etc. — see <see cref="RevenueCatStores"/>.</summary>
    [JsonPropertyName("store")]
    public string? Store { get; init; }

    [JsonPropertyName("environment")]
    public string? Environment { get; init; }

    /// <summary>Event timestamp in milliseconds since Unix epoch.</summary>
    [JsonPropertyName("event_timestamp_ms")]
    public long? EventTimestampMs { get; init; }

    /// <summary>Subscription expiration timestamp in milliseconds since Unix epoch — distinct from <see cref="EventTimestampMs"/> (when the event fired). Present on purchase/renewal-family events.</summary>
    [JsonPropertyName("expiration_at_ms")]
    public long? ExpirationAtMs { get; init; }

    [JsonPropertyName("country_code")]
    public string? CountryCode { get; init; }

    /// <summary>App User ID(s) transactions and entitlements are taken from. Only populated on TRANSFER events.</summary>
    [JsonPropertyName("transferred_from")]
    public IReadOnlyList<string> TransferredFrom { get; init; } = [];

    /// <summary>App User ID(s) receiving the transactions and entitlements. Only populated on TRANSFER events.</summary>
    [JsonPropertyName("transferred_to")]
    public IReadOnlyList<string> TransferredTo { get; init; } = [];

    [JsonPropertyName("subscriber_attributes")]
    public IReadOnlyDictionary<string, RevenueCatSubscriberValue> SubscriberAttributes { get; init; }
        = new Dictionary<string, RevenueCatSubscriberValue>();

    [JsonPropertyName("metadata")]
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; }
        = new Dictionary<string, JsonElement>();
}

public sealed class RevenueCatSubscriberValue
{
    [JsonPropertyName("value")]
    public string? Value { get; init; }

    [JsonPropertyName("updated_at_ms")]
    public long? UpdatedAtMs { get; init; }
}

/// <summary>
/// Known RevenueCat event type constants. Verified against
/// https://www.revenuecat.com/docs/integrations/webhooks/event-types-and-fields
/// and https://www.revenuecat.com/docs/integrations/webhooks/sample-events on 2026-08-17.
/// </summary>
public static class RevenueCatEventTypes
{
    public const string InitialPurchase = "INITIAL_PURCHASE";
    public const string NonRenewingPurchase = "NON_RENEWING_PURCHASE";
    public const string Renewal = "RENEWAL";
    public const string Cancellation = "CANCELLATION";
    public const string Uncancellation = "UNCANCELLATION";
    public const string Refund = "REFUND";
    public const string RefundReversed = "REFUND_REVERSED";
    public const string ProductChange = "PRODUCT_CHANGE";
    public const string BillingIssue = "BILLING_ISSUE";
    public const string SubscriberAlias = "SUBSCRIBER_ALIAS";
    public const string Transfer = "TRANSFER";
    public const string Expiration = "EXPIRATION";
    public const string SubscriptionPaused = "SUBSCRIPTION_PAUSED";
    public const string SubscriptionExtended = "SUBSCRIPTION_EXTENDED";
    public const string InvoiceIssuance = "INVOICE_ISSUANCE";
    public const string TemporaryEntitlementGrant = "TEMPORARY_ENTITLEMENT_GRANT";
    public const string VirtualCurrencyTransaction = "VIRTUAL_CURRENCY_TRANSACTION";
    public const string ExperimentEnrollment = "EXPERIMENT_ENROLLMENT";
    public const string PurchaseRedeemed = "PURCHASE_REDEEMED";

    // Paywall UI events
    public const string PaywallImpression = "PAYWALL_IMPRESSION";
    public const string PaywallClose = "PAYWALL_CLOSE";
    public const string PaywallCancel = "PAYWALL_CANCEL";
    public const string PaywallExitOffer = "PAYWALL_EXIT_OFFER";
    public const string PaywallComponentInteracted = "PAYWALL_COMPONENT_INTERACTED";

    public const string PriceIncreaseConsentRequired = "PRICE_INCREASE_CONSENT_REQUIRED";
    public const string PriceIncreaseConsentApproved = "PRICE_INCREASE_CONSENT_APPROVED";

    /// <summary>Test event sent from the RevenueCat dashboard's webhook configuration page.</summary>
    public const string Test = "TEST";
}

/// <summary>Known RevenueCat store identifiers and their mapping to platform strings.</summary>
public static class RevenueCatStores
{
    public const string AppStore = "APP_STORE";
    public const string PlayStore = "PLAY_STORE";

    /// <summary>RevenueCat's Stripe Billing integration (Stripe as merchant of record).</summary>
    public const string Stripe = "STRIPE";

    /// <summary>RevenueCat's Web Billing surface (RevenueCat as merchant of record).</summary>
    public const string RcBilling = "RC_BILLING";

    public static string ToPlatform(string? store) => store switch
    {
        AppStore => "ios",
        PlayStore => "android",
        Stripe or RcBilling => "web",
        _ => "unknown",
    };
}
