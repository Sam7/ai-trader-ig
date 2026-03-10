# AI Trader IG Spike

Small .NET 10 solution proving IG dealing behind a broker-neutral abstraction.

## Projects

- `src/Trading.Abstractions` broker-neutral contracts and domain models.
- `src/Ig.Trading.Sdk` isolated IG SDK (Refit-based) intended to be extractable to its own OSS repo.
- `src/Trading.IG` adapter implementing `ITradingGateway` using the SDK.
- `src/Trading.Cli` tiny manual CLI.
- `tests/*` fast unit tests plus optional integration tests.

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
dotnet run --project src/Trading.Cli -- buy --instrument IX.D.SPTRD.DAILY.IP --size 1
dotnet run --project src/Trading.Cli -- markets-search --query VIX
dotnet run --project src/Trading.Cli -- markets-browse
dotnet run --project src/Trading.Cli -- prices --instrument CC.D.VIX.UMA.IP
dotnet run --project src/Trading.Cli -- positions
dotnet run --project src/Trading.Cli -- position-update --deal-id DIAAAAAAA --stop-level 1 --limit-level 100
dotnet run --project src/Trading.Cli -- close --deal-id DIAAAAAAA
dotnet run --project src/Trading.Cli -- status --deal-reference spike-...
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
