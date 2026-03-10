using Ig.Trading.Sdk.Models;
using Trading.Abstractions;

namespace Trading.IG;

internal static class IgTradingConversions
{
    public static string ToIgDirection(TradeDirection direction)
        => direction switch
        {
            TradeDirection.Buy => "BUY",
            TradeDirection.Sell => "SELL",
            _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, "Unsupported trade direction."),
        };

    public static string ToIgWorkingOrderType(WorkingOrderType type)
        => type switch
        {
            WorkingOrderType.Limit => "LIMIT",
            WorkingOrderType.Stop => "STOP",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported working order type."),
        };

    public static WorkingOrderType ParseWorkingOrderType(string type)
        => string.Equals(type, "STOP", StringComparison.OrdinalIgnoreCase)
            ? WorkingOrderType.Stop
            : WorkingOrderType.Limit;

    public static string ToIgTimeInForce(WorkingOrderTimeInForce timeInForce)
        => timeInForce switch
        {
            WorkingOrderTimeInForce.GoodTillCancelled => "GOOD_TILL_CANCELLED",
            WorkingOrderTimeInForce.GoodTillDate => "GOOD_TILL_DATE",
            _ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, "Unsupported time-in-force."),
        };

    public static WorkingOrderTimeInForce ParseTimeInForce(string timeInForce)
        => string.Equals(timeInForce, "GOOD_TILL_DATE", StringComparison.OrdinalIgnoreCase)
            ? WorkingOrderTimeInForce.GoodTillDate
            : WorkingOrderTimeInForce.GoodTillCancelled;

    public static TradeDirection ParseDirection(string direction)
        => string.Equals(direction, "SELL", StringComparison.OrdinalIgnoreCase)
            ? TradeDirection.Sell
            : TradeDirection.Buy;

    public static string ToOppositeDirection(string direction)
        => string.Equals(direction, "BUY", StringComparison.OrdinalIgnoreCase)
            ? "SELL"
            : "BUY";

    public static DateTimeOffset ParseDate(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : DateTimeOffset.UtcNow;

    public static DateTimeOffset? ParseNullableDate(string? value)
        => DateTimeOffset.TryParse(value, out var parsed) ? parsed : null;

    public static string CreateDealReference(string prefix)
    {
        var normalizedPrefix = new string(prefix.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        var suffix = Guid.NewGuid().ToString("N")[..20].ToUpperInvariant();
        return $"{normalizedPrefix}{suffix}";
    }

    public static string? ToIgGoodTillDate(DateTimeOffset? value)
        => value?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss");

    public static string ResolveCurrencyCode(MarketDetailsResponse market)
    {
        var currency = market.Instrument.Currencies?.FirstOrDefault(x => x.IsDefault)
                       ?? market.Instrument.Currencies?.FirstOrDefault();

        if (currency is null || string.IsNullOrWhiteSpace(currency.Code))
        {
            throw new TradingGatewayException(TradingErrorCode.InvalidRequest, "Unable to determine default market currency.");
        }

        return currency.Code;
    }

    public static void EnsureMarketIsTradable(MarketDetailsResponse market)
    {
        if (!string.Equals(market.Snapshot.MarketStatus, "TRADEABLE", StringComparison.OrdinalIgnoreCase))
        {
            throw new TradingGatewayException(TradingErrorCode.MarketClosed, $"Market is not tradeable. Status: {market.Snapshot.MarketStatus}.");
        }
    }
}
