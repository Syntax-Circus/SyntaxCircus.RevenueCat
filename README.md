# SyntaxCircus.RevenueCat

[![Build](https://github.com/Syntax-Circus/SyntaxCircus.RevenueCat/actions/workflows/build.yml/badge.svg)](https://github.com/Syntax-Circus/SyntaxCircus.RevenueCat/actions/workflows/build.yml)
[![NuGet](https://img.shields.io/nuget/v/SyntaxCircus.RevenueCat.svg)](https://www.nuget.org/packages/SyntaxCircus.RevenueCat)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE.txt)

Backend-side RevenueCat integration: HMAC webhook signature verification, a strict-by-default webhook reader, and typed REST clients for subscriber verification, transaction reconciliation, product publishing, and anonymous-user aliasing. No third-party dependency — everything is plain `HttpClient` + `System.Text.Json` + `System.Security.Cryptography` against RevenueCat's REST API.

For client-side (MAUI) RevenueCat integration, see [SyntaxCircus.RevenueCat.Maui](https://github.com/Syntax-Circus/SyntaxCircus.RevenueCat.Maui).

> **No support guaranteed.** Published as-is and maintained on a best-effort basis. Issues and PRs are welcome, but there's no SLA — fork it or vendor what you need if that's not enough.

## Setup

```csharp
builder.Services.AddRevenueCat(builder.Configuration); // binds "RevenueCat", registers all 5 typed clients
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

- **`IRevenueCatPurchaseVerifier.VerifyAsync(new RevenueCatPurchaseVerificationRequest(appUserId, productId, transactionId))`** — in default V1 mode, confirms a purchase against the subscriber's `non_subscriptions`, falling back to the transactions API if the subscriber record hasn't caught up yet.
- **`IRevenueCatTransactionService.GetTransactionsForCandidatesAsync(candidateAppUserIds, startDate, endDate)`** — reconciles transactions for a caller-supplied set of candidate app_user_ids by querying `GET v1/subscribers/{app_user_id}` per id and aggregating `non_subscriptions`. This is the production-realistic reconciliation path: RevenueCat's REST API has no bulk "list transactions in a date range" endpoint. `GetTransactionsAsync(startDate, endDate)` still exists for consumers fronting RevenueCat with their own aggregation proxy at `RevenueCatOptions.TransactionsEndpoint`, but that endpoint doesn't exist on RevenueCat's own API.
- **`IRevenueCatProductCatalogService.PublishOneTimeProductAsync(...)`** — creates or updates a one-time product across one or more RevenueCat apps (v2 API).
- **`IRevenueCatSubscriberAliasClient.CreateAliasAsync(canonicalAppUserId, anonymousAppUserId)`** — aliases an anonymous purchaser to an identified user after login.
- **`IRevenueCatSubscriberDeletionClient.DeleteSubscriberAsync(appUserId)`** — permanently deletes a RevenueCat subscriber (e.g. when a user closes their account). In default V1 mode, calls `DELETE v1/subscribers/{app_user_id}` and **requires `RevenueCat:ApiKey` (a v1 secret key)** — `PublicApiKey` cannot delete subscribers, and `ResolveSecretApiKeyOrThrow` throws `InvalidOperationException` if only that's configured. Returns `RevenueCatSubscriberDeletionResult.NotFound` (not an exception) for an already-gone subscriber; any other non-success response throws `HttpRequestException` with `StatusCode` set so callers can retry on 429/5xx.

## Opt-in V2 customer operations

Existing consumers keep V1 behavior by default. To use V2 for purchase verification, transaction reconciliation and customer deletion, configure:

```json
{
  "RevenueCat": {
    "CustomerApiVersion": "V2",
    "ApiKey": "<v2-secret-key>",
    "ProjectId": "proj_...",
    "CustomerAppIds": ["app_..."],
    "CustomerApiBaseUrl": "https://api.revenuecat.com/v2/",
    "ExpectedTransactionEnvironment": "Production"
  }
}
```

`CustomerApiVersion` defaults to `V1`. Selection is explicit: key prefixes do not reliably distinguish V1 from V2 keys, and authentication failures never trigger an automatic switch between API versions. V2 requires a server secret `ApiKey` and `ProjectId`; `PublicApiKey` is not a substitute. Leave the base URL at its default for RevenueCat itself.

V2 permissions:

| Operation | Required permissions |
| --- | --- |
| Read purchases for known customers / verify a one-time purchase | `customer_information:purchases:read`, `project_configuration:products:read` |
| Scan project customers for reconciliation | The above plus `customer_information:customers:read` |
| Delete a customer | `customer_information:customers:read_write` |

`CustomerAppIds` restricts V2 purchases to the configured RevenueCat apps. Set it when a project contains multiple apps, especially if store product identifiers overlap. An empty list allows the whole project.

The existing interfaces select their implementation from this configuration:

- `GetTransactionsForCandidatesAsync` reads every purchase page for the supplied customer IDs and filters by the requested time window.
- `GetTransactionsAsync` scans customers and their purchases through the real V2 API. It can be substantially more expensive than candidate reconciliation; prefer candidates when the application already knows the relevant customers. In V1 it retains the existing custom aggregation-proxy behavior.
- `VerifyAsync` verifies one-time purchases against the customer purchase list, resolves RevenueCat product IDs to store product identifiers, and retains the transaction environment guard. An owned purchase can verify; a refunded purchase cannot. Subscription verification is outside this client’s scope.
- `DeleteSubscriberAsync` calls V2 customer deletion when selected. A missing customer remains an idempotent `NotFound`; authentication, rate-limit and server failures remain failures.

V2 purchase prices are gross USD from `revenue_in_usd.gross`, paired with currency `USD`. Store transaction identifiers are kept distinct from RevenueCat purchase IDs. Reconciliation includes purchase status; it is not a complete financial refund ledger.

V2 follows paginated responses with cycle/page limits and rejects pagination outside the expected origin and collection before sending credentials. Failed or malformed pages do not become successful empty or partial reconciliation results. The DI-registered V2 clients disable HTTP redirects; consumers constructing clients with their own `HttpClient` must also disable automatic redirects on its handler.

Aliasing remains a V1 operation. If using it alongside a V2 secret key, configure an appropriate V1-compatible `PublicApiKey` separately. Product publishing already uses V2 through its independent `ProductSyncApiKey` settings and is unaffected. Webhook handling is also unchanged.

Contracts checked against RevenueCat’s official [customer API](https://www.revenuecat.com/docs/api-v2/customer), [customer resources](https://www.revenuecat.com/docs/api-v2/customer/resources), [purchase API](https://www.revenuecat.com/docs/api-v2/purchase), and [product API](https://www.revenuecat.com/docs/api-v2/product).

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

`RevenueCatApiKeyResolver` returns configured V1 credential candidates in preference order (`PublicApiKey`, then `ApiKey`); it does not infer API compatibility from key prefixes. The subscriber/purchase/alias clients use these candidates for V1 calls. `ResolveSecretApiKeyOrThrow` is the exception — it's used only by `IRevenueCatSubscriberDeletionClient`, which RevenueCat restricts to `ApiKey` specifically and never accepts `PublicApiKey` for.

## Contributing

Issues and pull requests are welcome:
- Keep changes focused, with a clear description of the behavior change.
- Match the existing code style (see `.editorconfig`).
- Call out any breaking changes to the public API in your PR description.

## License

MIT — see [LICENSE.txt](LICENSE.txt).
