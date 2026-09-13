# SyntaxCircus.RevenueCat

[![Build](https://github.com/Syntax-Circus/SyntaxCircus.RevenueCat/actions/workflows/build.yml/badge.svg)](https://github.com/Syntax-Circus/SyntaxCircus.RevenueCat/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/SyntaxCircus.RevenueCat.svg)](https://www.nuget.org/packages/SyntaxCircus.RevenueCat)
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
    "WebhookSignatureToleranceSeconds": 300,
    "WebhookMaxBodyBytes": 262144,
    "ProjectId": "...",
    "ProductSyncApiKey": "...",
    "ProductSyncAppIds": ["app_..."],
    "ExpectedTransactionEnvironment": "Production"
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
        RevenueCatWebhookStatus.TooLarge => Results.StatusCode(StatusCodes.Status413PayloadTooLarge),
        _ => HandleVerifiedEvent(result.Payload!, result.RawBody!), // your idempotency store + processing
    };
});
```

**By default, `RevenueCatOptions.RequireWebhookSecret` is `true`** — if `WebhookSecret` isn't configured, the reader rejects every request outright rather than silently accepting unverified ones. Only set `RequireWebhookSecret` to `false` for local development. This is the package's whole reason for existing: a hand-rolled webhook auth check is an easy mistake to make and a real vulnerability — this reader closes that gap by construction.

`ReadAndVerifyAsync` enforces `RevenueCatOptions.WebhookMaxBodyBytes` before doing any parsing/verification work, buffers the raw request body as bytes (so it can be HMAC-verified and JSON-deserialized without double-consuming the stream), verifies the `X-RevenueCat-Webhook-Signature` header (`t=<unix_timestamp>,v1=<hmac_sha256_hex>`, HMAC-SHA256 over `"{timestamp}.{rawBody}"`, constant-time compared, with `WebhookSignatureToleranceSeconds` — default 300s — as a replay-window tolerance against the timestamp, per RevenueCat's documented webhook signing scheme), and deserializes the envelope — checking for a present `event.id` (use it as your idempotency key; this package doesn't own storage or dispatch, that's yours).

## REST clients

- **`IRevenueCatPurchaseVerifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest(appUserId, productId, transactionId))`** — confirms a purchase against the subscriber's `non_subscriptions`, falling back to the transactions API if the subscriber record hasn't caught up yet.
- **`IRevenueCatTransactionService.GetTransactionsForCandidatesAsync(candidateAppUserIds, startDate, endDate)`** — reconciles transactions for a caller-supplied set of candidate app_user_ids by querying `GET v1/subscribers/{app_user_id}` per id and aggregating `non_subscriptions`. This is the production-realistic reconciliation path: RevenueCat's REST API has no bulk "list transactions in a date range" endpoint. `GetTransactionsAsync(startDate, endDate)` still exists for consumers fronting RevenueCat with their own aggregation proxy at `RevenueCatOptions.TransactionsEndpoint`, but that endpoint doesn't exist on RevenueCat's own API.
- **`IRevenueCatProductCatalogService.PublishOneTimeProductAsync(...)`** — creates or updates a one-time product across one or more RevenueCat apps (v2 API).
- **`IRevenueCatSubscriberAliasClient.CreateAliasAsync(canonicalAppUserId, anonymousAppUserId)`** — aliases an anonymous purchaser to an identified user after login.

## Transaction-environment guard

If your app has more than one deployed environment (e.g. UAT and Production) sharing one RevenueCat project, set `ExpectedTransactionEnvironment` (`Sandbox` or `Production`) on the deployment whose expectation you want enforced — left unset (the default), nothing here changes behavior:

```csharp
var verified = await verifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest(appUserId, productId, transactionId));
if (verified.Status == "environment_mismatch")
{
    // verified.Purchase is null - the transaction's real store environment (RevenueCatVerifiedPurchase.IsSandbox,
    // parsed from the subscriber endpoint's non_subscriptions.*.is_sandbox) didn't match ExpectedTransactionEnvironment.
}
```

This is independent of what any client believes its selected backend to be — a mobile client choosing "UAT" or "Production" locally is not proof of the underlying store transaction's real environment, so a UAT deployment could otherwise record a genuine production purchase (or vice versa) if a client's local selection doesn't match reality. `RevenueCatTransactionEnvironmentMatcher.MatchesWebhookEnvironment(expected, revenueCatEvent.Environment)` applies the identical check to the webhook path, since `RevenueCatEvent.Environment` already carries RevenueCat's own `SANDBOX`/`PRODUCTION` value — call it yourself before acting on a webhook event, the same way `IRevenueCatPurchaseVerifier` already does internally for verification. Both checks pass through unknown/unverifiable values (a `null` `IsSandbox` or a missing webhook `Environment`) rather than rejecting them, since an inability to verify isn't evidence of a mismatch.

`RevenueCatApiKeyResolver` distinguishes v1-compatible keys (`PublicApiKey`, or a non-`sk_`/`atk_`-prefixed `ApiKey`) from v2-only project secret keys, and is what the subscriber/purchase/alias clients use internally to pick a working credential.

## Contributing

Issues and pull requests are welcome:
- Keep changes focused, with a clear description of the behavior change.
- Match the existing code style (see `.editorconfig`).
- Call out any breaking changes to the public API in your PR description.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
