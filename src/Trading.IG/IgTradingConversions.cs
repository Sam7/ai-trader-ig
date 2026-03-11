using Ig.Trading.Sdk.Models;
using System.Globalization;
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
        => type?.ToUpperInvariant() switch
        {
            "LIMIT" => WorkingOrderType.Limit,
            "STOP" => WorkingOrderType.Stop,
            _ => throw CreateInvalidBrokerValueException(nameof(type), type),
        };

    public static string ToIgTimeInForce(WorkingOrderTimeInForce timeInForce)
        => timeInForce switch
        {
            WorkingOrderTimeInForce.GoodTillCancelled => "GOOD_TILL_CANCELLED",
            WorkingOrderTimeInForce.GoodTillDate => "GOOD_TILL_DATE",
            _ => throw new ArgumentOutOfRangeException(nameof(timeInForce), timeInForce, "Unsupported time-in-force."),
        };

    public static WorkingOrderTimeInForce ParseTimeInForce(string timeInForce)
        => timeInForce?.ToUpperInvariant() switch
        {
            "GOOD_TILL_CANCELLED" => WorkingOrderTimeInForce.GoodTillCancelled,
            "GOOD_TILL_DATE" => WorkingOrderTimeInForce.GoodTillDate,
            _ => throw CreateInvalidBrokerValueException(nameof(timeInForce), timeInForce),
        };

    public static TradeDirection ParseDirection(string direction)
        => direction?.ToUpperInvariant() switch
        {
            "BUY" => TradeDirection.Buy,
            "SELL" => TradeDirection.Sell,
            _ => throw CreateInvalidBrokerValueException(nameof(direction), direction),
        };

    public static string ToOppositeDirection(string direction)
        => ParseDirection(direction) switch
        {
            TradeDirection.Buy => "SELL",
            TradeDirection.Sell => "BUY",
            _ => throw new InvalidOperationException("Unsupported trade direction."),
        };

    public static string ToIgPriceResolution(PriceResolution resolution)
        => resolution switch
        {
            PriceResolution.Second => "SECOND",
            PriceResolution.Minute => "MINUTE",
            PriceResolution.TwoMinutes => "MINUTE_2",
            PriceResolution.ThreeMinutes => "MINUTE_3",
            PriceResolution.FiveMinutes => "MINUTE_5",
            PriceResolution.TenMinutes => "MINUTE_10",
            PriceResolution.FifteenMinutes => "MINUTE_15",
            PriceResolution.ThirtyMinutes => "MINUTE_30",
            PriceResolution.Hour => "HOUR",
            PriceResolution.TwoHours => "HOUR_2",
            PriceResolution.ThreeHours => "HOUR_3",
            PriceResolution.FourHours => "HOUR_4",
            PriceResolution.Day => "DAY",
            PriceResolution.Week => "WEEK",
            PriceResolution.Month => "MONTH",
            _ => throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unsupported price resolution."),
        };

    public static PriceResolution? ParsePriceResolution(string? resolution)
        => resolution?.ToUpperInvariant() switch
        {
            "SECOND" => PriceResolution.Second,
            "MINUTE" => PriceResolution.Minute,
            "MINUTE_2" => PriceResolution.TwoMinutes,
            "MINUTE_3" => PriceResolution.ThreeMinutes,
            "MINUTE_5" => PriceResolution.FiveMinutes,
            "MINUTE_10" => PriceResolution.TenMinutes,
            "MINUTE_15" => PriceResolution.FifteenMinutes,
            "MINUTE_30" => PriceResolution.ThirtyMinutes,
            "HOUR" => PriceResolution.Hour,
            "HOUR_2" => PriceResolution.TwoHours,
            "HOUR_3" => PriceResolution.ThreeHours,
            "HOUR_4" => PriceResolution.FourHours,
            "DAY" => PriceResolution.Day,
            "WEEK" => PriceResolution.Week,
            "MONTH" => PriceResolution.Month,
            _ => null,
        };

    public static DateTimeOffset ParseDate(string? value)
    {
        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed))
        {
            return parsed;
        }

        throw new TradingGatewayException(
            TradingErrorCode.BrokerError,
            $"IG returned an invalid date value '{value ?? "<null>"}'.");
    }

    public static DateTimeOffset? ParseNullableDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out var parsed))
        {
            return parsed;
        }

        throw new TradingGatewayException(
            TradingErrorCode.BrokerError,
            $"IG returned an invalid date value '{value}'.");
    }

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
        if (ParseMarketStatus(market.Snapshot.MarketStatus) != MarketStatus.Tradeable)
        {
            throw new TradingGatewayException(TradingErrorCode.MarketClosed, $"Market is not tradeable. Status: {market.Snapshot.MarketStatus}.");
        }
    }

    public static MarketStatus ParseMarketStatus(string? value)
        => value?.ToUpperInvariant() switch
        {
            "TRADEABLE" => MarketStatus.Tradeable,
            "CLOSED" => MarketStatus.Closed,
            "SUSPENDED" => MarketStatus.Suspended,
            _ => MarketStatus.Unknown,
        };

    private static TradingGatewayException CreateInvalidBrokerValueException(string parameterName, string? value)
        => new(
            TradingErrorCode.BrokerError,
            $"IG returned an unsupported value for {parameterName}: '{value ?? "<null>"}'.");
}
