using System.Globalization;
using Spectre.Console;
using Spectre.Console.Cli;
using Trading.Abstractions;

internal static class CliParsing
{
    public static TradeDirection ParseDirection(string value)
        => value.Equals("sell", StringComparison.OrdinalIgnoreCase)
            ? TradeDirection.Sell
            : TradeDirection.Buy;

    public static bool IsValidDirection(string? value)
        => value is not null
           && (value.Equals("buy", StringComparison.OrdinalIgnoreCase)
               || value.Equals("sell", StringComparison.OrdinalIgnoreCase));

    public static WorkingOrderType ParseWorkingOrderType(string value)
        => value.Equals("stop", StringComparison.OrdinalIgnoreCase)
            ? WorkingOrderType.Stop
            : WorkingOrderType.Limit;

    public static bool IsValidWorkingOrderType(string? value)
        => value is not null
           && (value.Equals("limit", StringComparison.OrdinalIgnoreCase)
               || value.Equals("stop", StringComparison.OrdinalIgnoreCase));

    public static WorkingOrderTimeInForce ParseTimeInForce(string value)
        => value.Equals("gtd", StringComparison.OrdinalIgnoreCase)
           || value.Equals("good_till_date", StringComparison.OrdinalIgnoreCase)
           || value.Equals("good-till-date", StringComparison.OrdinalIgnoreCase)
            ? WorkingOrderTimeInForce.GoodTillDate
            : WorkingOrderTimeInForce.GoodTillCancelled;

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
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        try
        {
            _ = ParsePriceResolution(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    public static string FormatDecimal(decimal? value)
        => value?.ToString(CultureInfo.InvariantCulture) ?? "n/a";

    public static string FormatDate(DateTimeOffset? value)
        => value?.ToString("O", CultureInfo.InvariantCulture) ?? "n/a";

    public static ValidationResult Require(bool condition, string message)
        => condition ? ValidationResult.Success() : ValidationResult.Error(message);
}
