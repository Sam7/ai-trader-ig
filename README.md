# AI Trader IG Spike

Small .NET 10 solution proving IG dealing behind a broker-neutral abstraction.

## Projects

- `src/Trading.Abstractions` broker-neutral contracts and domain models.
- `src/Ig.Trading.Sdk` isolated IG SDK (Refit-based) intended to be extractable to its own OSS repo.
- `src/Trading.IG` adapter implementing `ITradingGateway` using the SDK.
- `src/Trading.Cli` small Spectre.Console CLI for manual flows and local verification.
- `tests/*` fast unit tests plus optional integration tests.

## Architecture

The solution is intentionally split into four layers so the broker-neutral model stays readable and stable:

- `Trading.Cli` is the outermost shell. It loads configuration, wires DI, and exposes manual commands. It should stay thin and avoid business logic.
- `Trading.Abstractions` defines the domain language the rest of the solution talks in: `ITradingGateway`, requests, results, and enums.
- `Trading.IG` is the broker adapter. It maps abstraction requests into IG calls, translates IG failures into `TradingGatewayException`, and keeps order-status orchestration in one place.
- `Ig.Trading.Sdk` owns IG-specific concerns: auth, session handling, request/response DTOs, Refit contracts, headers, and endpoint quirks.

Typical flow:

1. The CLI calls `ITradingGateway`.
2. `Trading.IG` validates and maps the broker-neutral request.
3. `Ig.Trading.Sdk` executes the HTTP call against IG and manages auth/session details.
4. `Trading.IG` maps the IG response back into broker-neutral models.

This keeps the CLI and abstractions free from IG transport details, while keeping the IG SDK usable on its own.

## Testing approach

- `tests/Trading.Abstractions.Tests` covers request and model behavior.
- `tests/Ig.Trading.Sdk.Tests` covers IG SDK concerns in isolation.
- `tests/Trading.IG.Tests` covers adapter behavior plus opt-in live integration tests.
- `tests/Trading.Cli.Tests` covers command routing, exit codes, and rendered output without calling the live broker.

## Configuration

Set via environment variables, user secrets, or a local ignored config file:

- `IG__BaseUrl`
- `IG__ApiKey`
- `IG__Identifier`
- `IG__Password`
- `IG__AccountId` (optional)
- `IG__UseEncryptedPassword` (optional, default `false`)

Recommended local file workflow:

- copy `appsettings.example.json` to `appsettings.json` or `appsettings.local.json`
- keep real credentials only in the local ignored file
- never commit live credentials

Optional integration test values:

- `RUN_IG_INTEGRATION=true`
- `IG__TestEpic`
- `IG__TestSize`
- `IG__MarketSearchTerm`

## CLI

```powershell
dotnet run --project src/Trading.Cli -- auth
dotnet run --project src/Trading.Cli -- trades buy --instrument IX.D.SPTRD.DAILY.IP --size 1
dotnet run --project src/Trading.Cli -- markets search --query VIX
dotnet run --project src/Trading.Cli -- markets browse
dotnet run --project src/Trading.Cli -- markets prices --instrument CC.D.VIX.UMA.IP --resolution hour --max 10
dotnet run --project src/Trading.Cli -- positions list
dotnet run --project src/Trading.Cli -- positions update --deal-id DIAAAAAAA --stop-level 1 --limit-level 100
dotnet run --project src/Trading.Cli -- positions close --deal-id DIAAAAAAA
dotnet run --project src/Trading.Cli -- orders status --deal-reference spike-...
```

## Tests

```powershell
dotnet test
$env:RUN_IG_INTEGRATION='true'; dotnet test --filter Category=Integration
```

## Live demo suite

The live integration suite is intentionally small:

- `AuthenticateAsync_WithValidDemoCredentials_ShouldReturnSession`
- `FullDemoRun_WithValidDemoCredentials_ShouldExerciseImplementedEndpoints`
- `AuthenticateAsync_WithEncryptedPasswordEnabled_ShouldReturnSession` (optional)
- `BrowseMarketsAsync_WhenDemoSupportsNavigation_ShouldReturnRootAndChildPage` (optional)
- `GetPricesAsync_WhenDemoAccountHasEntitlement_ShouldReturnPrices` (optional)

The full demo run is the main end-to-end proof. It performs one realistic broker journey and touches the implemented endpoint surface roughly once:

- authenticate
- accounts lookup
- market metadata lookup
- market search
- create/list/update/cancel a working order
- place a market order
- amend an open position with stop/limit protection
- confirm order state
- list open positions
- fetch one position by deal id
- query recent activity/orders
- query transaction history
- close the opened position

Recommended live settings:

- `RUN_IG_INTEGRATION=true`
- `IG__TestEpic`
- `IG__TestSize`
- `IG__WorkingOrderTestLevel`
- `IG__MarketSearchTerm`

`PUT /session` is covered automatically when `IG__AccountId` is configured.

Optional live coverage flags:

- `RUN_IG_ENCRYPTED_INTEGRATION=true` to try encrypted login against demo
- `RUN_IG_NAVIGATION_INTEGRATION=true` to run market-navigation coverage
- `RUN_IG_PRICES_INTEGRATION=true` to run price retrieval coverage

Notes:

- Some IG demo accounts reject encrypted login even when `/session/encryptionKey` is available.
- Some demo accounts return `500` on `marketnavigation`.
- Some demo accounts are not entitled to price-history endpoints and return `unauthorised.access.to.equity.exception`.

Never commit real credentials.
