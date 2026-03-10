# AI Trader IG Spike

Small .NET 10 solution proving IG dealing behind a broker-neutral abstraction.

## Projects

- `src/Trading.Abstractions` broker-neutral contracts and domain models.
- `src/Ig.Trading.Sdk` isolated IG SDK (Refit-based) intended to be extractable to its own OSS repo.
- `src/Trading.IG` adapter implementing `ITradingGateway` using the SDK.
- `src/Trading.Cli` tiny manual CLI.
- `tests/*` fast unit tests plus optional integration tests.

## Configuration

Set via environment variables or user secrets:

- `IG__BaseUrl`
- `IG__ApiKey`
- `IG__Identifier`
- `IG__Password`
- `IG__AccountId` (optional)
- `IG__UseDemo`

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

Never commit real credentials.
