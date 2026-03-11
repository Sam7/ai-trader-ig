using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;
using Trading.Charting;

internal static class CliParsing
{
    public static TradeDirection ParseDirection(string value)
        => value.ToLowerInvariant() switch
        {
            "buy" => TradeDirection.Buy,
            "sell" => TradeDirection.Sell,
            _ => throw new ArgumentException($"Unsupported direction '{value}'.", nameof(value)),
        };

    public static bool IsValidDirection(string? value)
        => value is not null
           && (value.Equals("buy", StringComparison.OrdinalIgnoreCase)
               || value.Equals("sell", StringComparison.OrdinalIgnoreCase));

    public static WorkingOrderType ParseWorkingOrderType(string value)
        => value.ToLowerInvariant() switch
        {
            "limit" => WorkingOrderType.Limit,
            "stop" => WorkingOrderType.Stop,
            _ => throw new ArgumentException($"Unsupported working order type '{value}'.", nameof(value)),
        };

    public static bool IsValidWorkingOrderType(string? value)
        => value is not null
           && (value.Equals("limit", StringComparison.OrdinalIgnoreCase)
               || value.Equals("stop", StringComparison.OrdinalIgnoreCase));

    public static WorkingOrderTimeInForce ParseTimeInForce(string value)
        => value.ToLowerInvariant() switch
        {
            "gtc" or "goodtillcancelled" or "good-till-cancelled" => WorkingOrderTimeInForce.GoodTillCancelled,
            "gtd" or "good_till_date" or "good-till-date" => WorkingOrderTimeInForce.GoodTillDate,
            _ => throw new ArgumentException($"Unsupported time in force '{value}'.", nameof(value)),
        };

    public static bool IsValidTimeInForce(string? value)
        => value is not null
           && (value.Equals("gtc", StringComparison.OrdinalIgnoreCase)
               || value.Equals("gtd", StringComparison.OrdinalIgnoreCase)
               || value.Equals("good_till_date", StringComparison.OrdinalIgnoreCase)
               || value.Equals("good-till-date", StringComparison.OrdinalIgnoreCase)
               || value.Equals("goodtillcancelled", StringComparison.OrdinalIgnoreCase)
               || value.Equals("good-till-cancelled", StringComparison.OrdinalIgnoreCase));

    public static PriceResolution ParsePriceResolution(string value)
        => value.ToLowerInvariant() switch
        {
            "second" => PriceResolution.Second,
            "minute" => PriceResolution.Minute,
            "2minute" or "minute_2" => PriceResolution.TwoMinutes,
            "3minute" or "minute_3" => PriceResolution.ThreeMinutes,
            "5minute" or "minute_5" => PriceResolution.FiveMinutes,
            "10minute" or "minute_10" => PriceResolution.TenMinutes,
            "15minute" or "minute_15" => PriceResolution.FifteenMinutes,
            "30minute" or "minute_30" => PriceResolution.ThirtyMinutes,
            "hour" => PriceResolution.Hour,
            "2hour" or "hour_2" => PriceResolution.TwoHours,
            "3hour" or "hour_3" => PriceResolution.ThreeHours,
            "4hour" or "hour_4" => PriceResolution.FourHours,
            "day" => PriceResolution.Day,
            "week" => PriceResolution.Week,
            "month" => PriceResolution.Month,
            _ => throw new ArgumentException($"Unsupported resolution '{value}'.", nameof(value)),
        };

    public static bool IsValidPriceResolution(string? value)
        => value?.ToLowerInvariant() is
            "second"
            or "minute"
            or "2minute"
            or "minute_2"
            or "3minute"
            or "minute_3"
            or "5minute"
            or "minute_5"
            or "10minute"
            or "minute_10"
            or "15minute"
            or "minute_15"
            or "30minute"
            or "minute_30"
            or "hour"
            or "2hour"
            or "hour_2"
            or "3hour"
            or "hour_3"
            or "4hour"
            or "hour_4"
            or "day"
            or "week"
            or "month";

    public static PriceChartStyle ParsePriceChartStyle(string value)
        => value.ToLowerInvariant() switch
        {
            "candlestick" => PriceChartStyle.Candlestick,
            "ohlc" => PriceChartStyle.Ohlc,
            _ => throw new ArgumentException($"Unsupported chart style '{value}'.", nameof(value)),
        };

    public static bool IsValidPriceChartStyle(string? value)
        => value?.ToLowerInvariant() is "candlestick" or "ohlc";

    public static PriceGapMode ParsePriceGapMode(string value)
        => value.ToLowerInvariant() switch
        {
            "compress" => PriceGapMode.Compress,
            "preserve" => PriceGapMode.Preserve,
            _ => throw new ArgumentException($"Unsupported gap mode '{value}'.", nameof(value)),
        };

    public static bool IsValidPriceGapMode(string? value)
        => value?.ToLowerInvariant() is "compress" or "preserve";

    public static IReadOnlyList<int> ParseIntegerList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var values = value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.Parse(part, CultureInfo.InvariantCulture))
            .ToArray();

        return values;
    }

    public static string FormatDecimal(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    public static string FormatDate(DateTimeOffset? value)
        => value?.ToString("O", CultureInfo.InvariantCulture) ?? "n/a";

    public static ValidationResult Require(bool condition, string message)
        => condition ? ValidationResult.Success() : ValidationResult.Error(message);
}
