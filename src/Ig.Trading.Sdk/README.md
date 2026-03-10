# Ig.Trading.Sdk

Composable IG REST SDK for .NET 10.

## Goals

- Small, readable client surface.
- Refit endpoint contracts with explicit request/response models.
- Session/token plumbing isolated from consumers.

## Register in DI

```csharp
services.AddIgTradingSdk(configuration);
```

## Primary facade

`IIgTradingApi` exposes high-value dealing operations while endpoint contracts remain available for advanced use.

## Endpoint inventory

### Implemented

| Endpoint | Version | Purpose | Exposed via `IIgTradingApi` | Notes |
| --- | --- | --- | --- | --- |
| `POST /session` | `2` | Authenticate and create session tokens | Yes | Primary login flow |
| `PUT /session` | `1` | Switch active account | Yes | Used when `IG:AccountId` is set |
| `GET /accounts` | `1` | Account summary and balances | Yes | Observability endpoint |
| `GET /markets/{epic}` | `3` | Resolve market metadata for trading | Yes | Used before market and working orders |
| `GET /positions` | `2` | List open positions | Yes | Maps to broker-neutral positions |
| `GET /positions/{dealId}` | `2` | Inspect a single position | Yes | Used for observability/status support |
| `POST /positions/otc` | `2` | Create OTC market position | Yes | Used for market buys/sells |
| `POST /positions/otc` + `_method: DELETE` | `1` | Close OTC position | Yes | IG-documented fallback for delete-with-body issues |
| `GET /confirms/{dealReference}` | `1` | Immediate deal confirmation lookup | Yes | First status lookup path |
| `GET /history/activity` | `3` | Recent detailed account activity | Yes | Used for close-status correlation |
| `GET /history/transactions` | `2` | Transaction history | Yes | REST fallback for closed outcomes |
| `GET /workingorders` | `2` | List working orders | Yes | Live demo path uses no hyphen |
| `POST /workingorders/otc` | `2` | Create working order | Yes | Supports limit/stop entry orders |
| `PUT /workingorders/otc/{dealId}` | `2` | Update working order | Yes | Supports working-order amendments |
| `DELETE /workingorders/otc/{dealId}` | `2` | Cancel working order | Yes | Supports working-order cancellation |

### Not implemented

| Endpoint | Purpose | Planned? | Notes |
| --- | --- | --- | --- |
| `PUT /positions/otc/{dealId}` | Amend stops/limits on an open position | Yes | Natural next step for position risk management |
| `GET /workingorders/{dealId}` | Inspect one working order directly | Maybe | Current SDK can list and filter, but not fetch a single order |
| `POST /workingorders/otc` with advanced stop/limit attachments | Richer entry order placement | Yes | Current support is intentionally narrow |
| `GET /accounts/preferences` | Account preference details | Maybe | Lower priority observability endpoint |
| `GET /repeat-dealing-window` | Repeat dealing state | Maybe | Relevant for higher-frequency execution flows |
| `GET /session/encryptionKey` | Encrypted password login support | Yes | Useful if plaintext login is no longer acceptable |
| `GET /markets` search endpoints | Market discovery | Yes | Useful once the SDK grows beyond direct EPIC usage |
| `GET /marketnavigation` and related navigation endpoints | Browse market hierarchy | Maybe | Helpful for richer discovery tooling |
| `GET /prices/{epic}` | Historical pricing | Maybe | Not required for the execution spike |
| `GET /clientsentiment/{marketId}` | Client sentiment | No | Outside the current execution scope |
| `GET /watchlists` and watchlist mutations | Saved market lists | No | Not needed for trading execution |
| `GET /indicativecostsandcharges/*` | Cost disclosure estimates | Maybe | Useful if the SDK grows toward pre-trade analysis |
| `TRADE:{accountId}` streaming (`CONFIRMS`, `OPU`, `WOU`) | Real-time confirmations and position/order updates | Yes | Recommended future upgrade beyond REST polling |

### Notes

- This SDK currently targets the dealing flow: authenticate, inspect markets, place or close positions, inspect order state, and manage working orders.
- The live IG demo API currently responds on `workingorders` paths without a hyphen. The SDK uses those live paths.
- The SDK intentionally keeps the public facade smaller than the full IG API surface. Advanced consumers can still compose directly against the lower-level Refit contracts if needed.

## Live endpoint coverage

The live integration suite uses a hybrid approach:

- one auth smoke test for quick credential validation
- one full demo-run test that drives the main flow through `ITradingGateway` and uses `IIgTradingApi` only for implemented endpoints intentionally hidden by the gateway

| Endpoint | Live coverage |
| --- | --- |
| `POST /session` | Auth smoke and full demo run |
| `PUT /session` | Full demo run when `IG__AccountId` is configured |
| `GET /accounts` | Full demo run |
| `GET /markets/{epic}` | Full demo run via gateway order placement |
| `GET /positions` | Full demo run via gateway |
| `GET /positions/{dealId}` | Full demo run via SDK |
| `POST /positions/otc` | Full demo run via gateway |
| `POST /positions/otc` + `_method: DELETE` | Full demo run via gateway |
| `GET /confirms/{dealReference}` | Full demo run via gateway status checks |
| `GET /history/activity` | Full demo run via gateway order history |
| `GET /history/transactions` | Full demo run via SDK |
| `GET /workingorders` | Full demo run via gateway |
| `POST /workingorders/otc` | Full demo run via gateway |
| `PUT /workingorders/otc/{dealId}` | Full demo run via gateway |
| `DELETE /workingorders/otc/{dealId}` | Full demo run via gateway |
