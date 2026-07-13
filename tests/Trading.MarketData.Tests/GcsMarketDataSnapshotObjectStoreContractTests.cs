using FluentAssertions;
using Trading.MarketData;

public sealed class GcsMarketDataSnapshotObjectStoreContractTests
{
    [Fact]
    public void ObjectStore_ShouldImplementBothSnapshotAndGenericUploadContracts()
    {
        typeof(GcsMarketDataSnapshotObjectStore)
            .Should().BeAssignableTo<IMarketDataSnapshotObjectStore>();
        typeof(GcsMarketDataSnapshotObjectStore)
            .Should().BeAssignableTo<IMarketDataObjectStore>();
    }
}
