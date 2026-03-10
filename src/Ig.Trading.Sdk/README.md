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
