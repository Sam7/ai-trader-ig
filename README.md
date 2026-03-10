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

Recommended local file workflow:

- copy `appsettings.example.json` to `appsettings.json` or `appsettings.local.json`
- keep real credentials only in the local ignored file
- never commit live credentials

Optional integration test values:

- `RUN_IG_INTEGRATION=true`
- `IG__TestEpic`
- `IG__TestSize`

## CLI

```powershell
dotnet run --project src/Trading.Cli -- auth
dotnet run --project src/Trading.Cli -- buy --instrument IX.D.SPTRD.DAILY.IP --size 1
dotnet run --project src/Trading.Cli -- positions
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

The full demo run is the main end-to-end proof. It performs one realistic broker journey and touches the implemented endpoint surface roughly once:

- authenticate
- accounts lookup
- market metadata lookup
- create/list/update/cancel a working order
- place a market order
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

`PUT /session` is covered automatically when `IG__AccountId` is configured.

Never commit real credentials.
