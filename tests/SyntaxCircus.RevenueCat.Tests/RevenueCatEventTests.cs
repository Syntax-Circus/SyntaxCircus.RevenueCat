namespace SyntaxCircus.RevenueCat.Tests;

public class RevenueCatEventTests
{
    [Fact]
    public void Deserialize_InitialPurchasePayload_MapsAllFields()
    {
        const string body = """
            {
              "api_version": "1.0",
              "event": {
                "id": "evt_1",
                "type": "INITIAL_PURCHASE",
                "app_user_id": "user_1",
                "product_id": "com.subscription.weekly",
                "entitlement_ids": ["pro"],
                "event_timestamp_ms": 1658726378679,
                "expiration_at_ms": 1659331174000,
                "price": 4.99,
                "currency": "USD",
                "store": "APP_STORE"
              }
            }
            """;

        var payload = JsonSerializer.Deserialize<RevenueCatWebhookPayload>(body)!;

        payload.Event.Id.ShouldBe("evt_1");
        payload.Event.Type.ShouldBe(RevenueCatEventTypes.InitialPurchase);
        payload.Event.EventTimestampMs.ShouldBe(1658726378679L);
        payload.Event.ExpirationAtMs.ShouldBe(1659331174000L);
        payload.Event.Price.ShouldBe(4.99m);
        payload.Event.Currency.ShouldBe("USD");
        payload.Event.EntitlementIds.ShouldBe(["pro"]);
    }

    [Fact]
    public void Deserialize_BillingIssuePayload_HasNoExpirationOrPrice()
    {
        const string body = """
            {
              "api_version": "1.0",
              "event": {
                "id": "evt_2",
                "type": "BILLING_ISSUE",
                "event_timestamp_ms": 1601337601013,
                "store": "APP_STORE"
              }
            }
            """;

        var payload = JsonSerializer.Deserialize<RevenueCatWebhookPayload>(body)!;

        payload.Event.Type.ShouldBe(RevenueCatEventTypes.BillingIssue);
        payload.Event.ExpirationAtMs.ShouldBeNull();
        payload.Event.Price.ShouldBeNull();
    }
}
