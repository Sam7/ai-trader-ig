using System.Text;
using Trading.AI.Configuration;

namespace Trading.AI.DailyBriefing;

public sealed class TrackedMarketsFormatter
{
    public string Format(IEnumerable<TrackedMarketOptions> trackedMarkets)
    {
        var builder = new StringBuilder();
        foreach (var market in trackedMarkets)
        {
            builder.Append("- ");
            builder.Append(market.DisplayName);
            builder.Append(" | instrumentId: ");
            builder.Append(market.InstrumentId);

            if (!string.IsNullOrWhiteSpace(market.Sector))
            {
                builder.Append(" | sector: ");
                builder.Append(market.Sector);
            }

            if (market.Aliases.Length > 0)
            {
                builder.Append(" | aliases: ");
                builder.Append(string.Join(", ", market.Aliases));
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }
}
