# SyntaxCircus.RevenueCat

[![Build](https://github.com/Syntax-Circus/SyntaxCircus.RevenueCat/actions/workflows/build.yml/badge.svg)](https://github.com/Syntax-Circus/SyntaxCircus.RevenueCat/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)

Backend-side RevenueCat integration: HMAC webhook signature verification, a strict-by-default webhook reader, and typed REST clients for subscriber verification, transaction reconciliation, product publishing, and anonymous-user aliasing. No third-party dependency — everything is plain `HttpClient` + `System.Text.Json` + `System.Security.Cryptography` against RevenueCat's REST API.

For client-side (MAUI) RevenueCat integration, see [SyntaxCircus.RevenueCat.Maui](https://github.com/Syntax-Circus/SyntaxCircus.RevenueCat.Maui).

> **No support guaranteed.** Published as-is and maintained on a best-effort basis. Issues and PRs are welcome, but there's no SLA — fork it or vendor what you need if that's not enough.

## Setup

```csharp
builder.Services.AddRevenueCat(builder.Configuration); // binds "RevenueCat", registers all 4 typed clients
```

```json
{
  "RevenueCat": {
    "ApiKey": "sk_...",
    "PublicApiKey": "...",
    "WebhookSecret": "...",
    "ProjectId": "...",
    "ProductSyncApiKey": "...",
    "ProductSyncAppIds": ["app_..."]
  }
}
```

## Webhook endpoint

```csharp
app.MapPost("/webhooks/revenuecat", async (HttpRequest request, IOptions<RevenueCatOptions> options, CancellationToken ct) =>
{
    var result = await RevenueCatWebhookReader.ReadAndVerifyAsync(request, options.Value, ct);

    return result.Status switch
    {
        RevenueCatWebhookStatus.Unauthorized => Results.Unauthorized(),
        RevenueCatWebhookStatus.Malformed => Results.BadRequest(),
        _ => HandleVerifiedEvent(result.Payload!, result.RawBody!), // your idempotency store + processing
    };
});
```

**By default, `RevenueCatOptions.RequireWebhookSecret` is `true`** — if `WebhookSecret` isn't configured, the reader rejects every request outright rather than silently accepting unverified ones. Only set `RequireWebhookSecret` to `false` for local development. This is the package's whole reason for existing: a hand-rolled webhook auth check (a static header string compare, no HMAC, no constant-time comparison) is an easy mistake to make and a real vulnerability — this reader closes that gap by construction.

`ReadAndVerifyAsync` buffers the raw request body (so it can be HMAC-verified and JSON-deserialized without double-consuming the stream), verifies `X-RevenueCat-Signature` via HMAC-SHA256 with a constant-time comparison, and deserializes the envelope — checking for a present `event.id` (use it as your idempotency key; this package doesn't own storage or dispatch, that's yours).

## REST clients

- **`IRevenueCatPurchaseVerifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest(appUserId, productId, transactionId))`** — confirms a purchase against the subscriber's `non_subscriptions`, falling back to the transactions API if the subscriber record hasn't caught up yet.
- **`IRevenueCatTransactionService.GetTransactionsAsync(startDate, endDate)`** — fetches transactions for reconciliation (e.g. detecting "ghost" purchases the webhook never delivered).
- **`IRevenueCatProductCatalogService.PublishOneTimeProductAsync(...)`** — creates or updates a one-time product across one or more RevenueCat apps (v2 API).
- **`IRevenueCatSubscriberAliasClient.CreateAliasAsync(canonicalAppUserId, anonymousAppUserId)`** — aliases an anonymous purchaser to an identified user after login.

`RevenueCatApiKeyResolver` distinguishes v1-compatible keys (`PublicApiKey`, or a non-`sk_`/`atk_`-prefixed `ApiKey`) from v2-only project secret keys, and is what the subscriber/purchase/alias clients use internally to pick a working credential.

## Contributing

Issues and pull requests are welcome:
- Keep changes focused, with a clear description of the behavior change.
- Match the existing code style (see `.editorconfig`).
- Call out any breaking changes to the public API in your PR description.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
