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
    public void Deserialize_WebBillingPurchase_MapsTaxFields()
    {
        // A Web Billing (RC_BILLING) sandbox event: tax added on top of the 0.99 list price.
        const string body = """
            {
              "api_version": "1.0",
              "event": {
                "id": "evt_web",
                "type": "NON_RENEWING_PURCHASE",
                "app_user_id": "$RCAnonymousID:abc",
                "product_id": "absolution_single",
                "price": 1.06,
                "price_in_purchased_currency": 1.06,
                "currency": "USD",
                "store": "RC_BILLING",
                "takehome_percentage": null,
                "tax_amount": 0.07,
                "tax_amount_in_purchased_currency": 0.07,
                "tax_inclusive": false,
                "tax_provider": "stripe"
              }
            }
            """;

        var evt = JsonSerializer.Deserialize<RevenueCatWebhookPayload>(body)!.Event;

        evt.Price.ShouldBe(1.06m);
        evt.TaxAmount.ShouldBe(0.07m);
        evt.TaxInclusive.ShouldBe(false);
        evt.TakehomePercentage.ShouldBeNull();
        evt.TaxPercentage.ShouldBeNull();
        evt.CommissionPercentage.ShouldBeNull();
    }

    [Fact]
    public void Deserialize_PlayStorePurchase_MapsTakehomeAndLeavesTaxFieldsEmpty()
    {
        const string body = """
            {
              "api_version": "1.0",
              "event": {
                "id": "evt_play",
                "type": "NON_RENEWING_PURCHASE",
                "app_user_id": "3f2ec695",
                "product_id": "accusation_single",
                "price": 0.99,
                "currency": "USD",
                "store": "PLAY_STORE",
                "takehome_percentage": 0.85
              }
            }
            """;

        var evt = JsonSerializer.Deserialize<RevenueCatWebhookPayload>(body)!.Event;

        evt.TakehomePercentage.ShouldBe(0.85m);
        evt.TaxAmount.ShouldBeNull();
        evt.TaxInclusive.ShouldBeNull();
    }

    [Fact]
    public void Deserialize_StoreEventWithEstimatedPercentages_MapsThem()
    {
        const string body = """
            {
              "api_version": "1.0",
              "event": {
                "id": "evt_store",
                "type": "INITIAL_PURCHASE",
                "app_user_id": "u",
                "product_id": "p",
                "price": 10.0,
                "currency": "USD",
                "store": "APP_STORE",
                "tax_percentage": 0.2,
                "commission_percentage": 0.15
              }
            }
            """;

        var evt = JsonSerializer.Deserialize<RevenueCatWebhookPayload>(body)!.Event;

        evt.TaxPercentage.ShouldBe(0.2m);
        evt.CommissionPercentage.ShouldBe(0.15m);
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
