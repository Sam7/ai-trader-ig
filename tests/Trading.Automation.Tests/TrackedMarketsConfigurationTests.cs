using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Trading.Automation.Configuration;

public sealed class TrackedMarketsConfigurationTests
{
    [Fact]
    public void AddConfiguredTrackedMarketsFile_WhenOverrideIsConfigured_ShouldLoadThatFile()
    {
        var tempDirectory = Directory.CreateTempSubdirectory();
        var trackedMarketsPath = Path.Combine(tempDirectory.FullName, "tracked-markets.verification.json");
        File.WriteAllText(
            trackedMarketsPath,
            """
            {
              "AI": {
                "DailyBriefing": {
                  "TrackedMarkets": [
                    {
                      "DisplayName": "Bitcoin",
                      "InstrumentId": "CS.D.BITCOIN.CFD.IP",
                      "Sector": "Crypto",
                      "Aliases": [ "Bitcoin", "BTC" ]
                    }
                  ]
                }
              }
            }
            """);

        try
        {
            var configuration = new ConfigurationManager();
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TrackedMarketsConfiguration.SettingPath] = trackedMarketsPath,
            });

            TrackedMarketsConfiguration.AddConfiguredTrackedMarketsFile(configuration);

            configuration["AI:DailyBriefing:TrackedMarkets:0:InstrumentId"].Should().Be("CS.D.BITCOIN.CFD.IP");
        }
        finally
        {
            tempDirectory.Delete(true);
        }
    }
}
