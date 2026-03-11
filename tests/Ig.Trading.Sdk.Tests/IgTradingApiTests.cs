using FluentAssertions;
using Ig.Trading.Sdk.Auth;
using Ig.Trading.Sdk.Configuration;
using Ig.Trading.Sdk.Contracts;
using Ig.Trading.Sdk.Errors;
using Ig.Trading.Sdk.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Refit;
using System.Net;
using System.Net.Http.Headers;

namespace Ig.Trading.Sdk.Tests;

public class IgTradingApiTests
{
    [Fact]
    public async Task AuthenticateAsync_WithEncryptedPasswordEnabled_ShouldFetchEncryptionKeyAndSendEncryptedPassword()
    {
        var sessionApi = new FakeSessionApi();
        var api = CreateApi(
            sessionApi,
            new IgClientOptions
            {
                BaseUrl = "https://demo-api.ig.com/gateway/deal",
                ApiKey = "key",
                Identifier = "user",
                Password = "pass",
                UseEncryptedPassword = true,
            });

        var session = await api.AuthenticateAsync();

        sessionApi.EncryptionKeyRequests.Should().Be(1);
        sessionApi.LastCreateSessionRequest.Should().NotBeNull();
        sessionApi.LastCreateSessionRequest!.EncryptedPassword.Should().BeTrue();
        sessionApi.LastCreateSessionRequest.Password.Should().NotBe("pass");
        session.CurrentAccountId.Should().Be("ACC1");
        session.TimezoneOffsetHours.Should().Be(11);
    }

    [Fact]
    public async Task AuthenticateAsync_WithConfiguredAccountId_ShouldRefreshSessionTimezoneAfterSwitch()
    {
        var sessionApi = new FakeSessionApi
        {
            CreateSessionResponse = new SessionResponse("ACC1", null, null, 10),
            GetSessionResponse = new SessionResponse("ACC2", null, null, 11),
        };
        var api = CreateApi(
            sessionApi,
            new IgClientOptions
            {
                BaseUrl = "https://demo-api.ig.com/gateway/deal",
                ApiKey = "key",
                Identifier = "user",
                Password = "pass",
                AccountId = "ACC2",
            });

        var session = await api.AuthenticateAsync();

        sessionApi.SwitchAccountRequests.Should().Be(1);
        sessionApi.GetSessionRequests.Should().Be(1);
        session.CurrentAccountId.Should().Be("ACC2");
        session.TimezoneOffsetHours.Should().Be(11);
    }

    [Fact]
    public async Task GetPricesAsync_WithMaxPoints_ShouldUsePointsEndpointAndNormalizeSnapshotTime()
    {
        var marketsApi = new FakeMarketsApi();
        marketsApi.PointsResponse = new PricesResponse(
        [
            new PricePoint(
                null,
                new PriceLevel(10m, 11m),
                new PriceLevel(12m, 13m),
                new PriceLevel(9m, 10m),
                new PriceLevel(11m, 12m),
                42,
                "2026/03/11 10:00:00"),
        ],
        null,
        null);

        var api = CreateApi(new FakeSessionApi(), CreateOptions(), marketsApi: marketsApi);
        await api.AuthenticateAsync();

        var response = await api.GetPricesAsync(new GetPricesRequest("CC.D.VIX.UMA.IP", "MINUTE", 5));

        marketsApi.Calls.Should().ContainSingle(call => call == "points:CC.D.VIX.UMA.IP:MINUTE:5");
        response.Prices.Should().ContainSingle();
        response.Prices![0].TimestampUtc.Should().Be(DateTimeOffset.Parse("2026-03-10T23:00:00Z"));
    }

    [Fact]
    public async Task GetPricesAsync_WithRange_ShouldUseRangeEndpoint()
    {
        var marketsApi = new FakeMarketsApi();
        var api = CreateApi(new FakeSessionApi(), CreateOptions(), marketsApi: marketsApi);

        await api.GetPricesAsync(new GetPricesRequest(
            "CC.D.VIX.UMA.IP",
            "MINUTE",
            null,
            DateTimeOffset.Parse("2026-03-10T00:00:00Z"),
            DateTimeOffset.Parse("2026-03-10T01:00:00Z")));

        marketsApi.Calls.Should().ContainSingle(call => call.StartsWith("range:CC.D.VIX.UMA.IP:MINUTE:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetPricesAsync_WhenBothSnapshotFieldsArePresent_ShouldPreferSnapshotTimeUtc()
    {
        var marketsApi = new FakeMarketsApi
        {
            PointsResponse = new PricesResponse(
            [
                new PricePoint(
                    "2026-03-11T01:00:00Z",
                    new PriceLevel(10m, 11m),
                    new PriceLevel(12m, 13m),
                    new PriceLevel(9m, 10m),
                    new PriceLevel(11m, 12m),
                    42,
                    "2026/03/11 10:00:00"),
            ],
            null,
            null),
        };

        var api = CreateApi(new FakeSessionApi(), CreateOptions(), marketsApi: marketsApi);
        await api.AuthenticateAsync();

        var response = await api.GetPricesAsync(new GetPricesRequest("CC.D.VIX.UMA.IP", "MINUTE", 1));

        response.Prices![0].TimestampUtc.Should().Be(DateTimeOffset.Parse("2026-03-11T01:00:00Z"));
    }

    [Fact]
    public async Task GetPricesAsync_WhenSnapshotTimeRequiresTimezoneButNoneIsAvailable_ShouldThrow()
    {
        var marketsApi = new FakeMarketsApi
        {
            PointsResponse = new PricesResponse(
            [
                new PricePoint(
                    null,
                    new PriceLevel(10m, 11m),
                    new PriceLevel(12m, 13m),
                    new PriceLevel(9m, 10m),
                    new PriceLevel(11m, 12m),
                    42,
                    "2026/03/11 10:00:00"),
            ],
            null,
            null),
        };

        var api = CreateApi(
            new FakeSessionApi
            {
                CreateSessionResponse = new SessionResponse("ACC1", null, null, null),
            },
            CreateOptions(),
            marketsApi: marketsApi);

        var action = () => api.GetPricesAsync(new GetPricesRequest("CC.D.VIX.UMA.IP", "MINUTE", 1));

        await action.Should().ThrowAsync<IgApiException>()
            .WithMessage("*timezone offset*");
    }

    private static IgTradingApi CreateApi(
        FakeSessionApi sessionApi,
        IgClientOptions? options = null,
        FakeMarketsApi? marketsApi = null)
    {
        return new IgTradingApi(
            sessionApi,
            marketsApi ?? new FakeMarketsApi(),
            new FakePositionsApi(),
            new FakeOrderStateApi(),
            new FakeWorkingOrdersApi(),
            new FakeAccountsApi(),
            new InMemoryIgSessionStore(),
            new RsaIgPasswordEncryptor(),
            Options.Create(options ?? CreateOptions()),
            NullLogger<IgTradingApi>.Instance);
    }

    private static IgClientOptions CreateOptions()
    {
        return new IgClientOptions
        {
            BaseUrl = "https://demo-api.ig.com/gateway/deal",
            ApiKey = "key",
            Identifier = "user",
            Password = "pass",
        };
    }

    private sealed class FakeSessionApi : IIgSessionApi
    {
        public int EncryptionKeyRequests { get; private set; }
        public int SwitchAccountRequests { get; private set; }
        public int GetSessionRequests { get; private set; }

        public SessionRequest? LastCreateSessionRequest { get; private set; }
        public SessionResponse CreateSessionResponse { get; set; } = new("ACC1", null, null, 11);
        public SessionResponse GetSessionResponse { get; set; } = new("ACC1", null, null, 11);

        public Task<EncryptionKeyResponse> GetEncryptionKeyAsync(CancellationToken cancellationToken = default)
        {
            EncryptionKeyRequests++;
            return Task.FromResult(new EncryptionKeyResponse(
                "MFwwDQYJKoZIhvcNAQEBBQADSwAwSAJBAKQ0lqX5K6q7cI9XgKGY7gUVgNnFC2oVQAZmUrr6f0Mh0H0/4FfUuU0WsrhUh1o90xjV2Q4Dq5D2zKzj8w4nfE0CAwEAAQ==",
                1710000000000));
        }

        public Task<ApiResponse<SessionResponse>> CreateSessionAsync(SessionRequest request, CancellationToken cancellationToken = default)
        {
            LastCreateSessionRequest = request;

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("CST", "cst");
            response.Headers.Add("X-SECURITY-TOKEN", "token");

            return Task.FromResult(new ApiResponse<SessionResponse>(
                response,
                CreateSessionResponse,
                new RefitSettings(),
                null));
        }

        public Task<ApiResponse<SessionResponse>> GetSessionAsync(CancellationToken cancellationToken = default)
        {
            GetSessionRequests++;

            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("CST", "cst");
            response.Headers.Add("X-SECURITY-TOKEN", "token");

            return Task.FromResult(new ApiResponse<SessionResponse>(
                response,
                GetSessionResponse,
                new RefitSettings(),
                null));
        }

        public Task<ApiResponse<SessionResponse>> SwitchAccountAsync(SwitchAccountRequest request, CancellationToken cancellationToken = default)
        {
            SwitchAccountRequests++;
            var response = new HttpResponseMessage(HttpStatusCode.OK);
            response.Headers.Add("CST", "cst");
            response.Headers.Add("X-SECURITY-TOKEN", "token");

            return Task.FromResult(new ApiResponse<SessionResponse>(
                response,
                new SessionResponse(request.AccountId, null, null, null),
                new RefitSettings(),
                null));
        }
    }

    private sealed class FakeMarketsApi : IIgMarketsApi
    {
        public List<string> Calls { get; } = [];
        public PricesResponse RecentResponse { get; set; } = new([], null, null);
        public PricesResponse PointsResponse { get; set; } = new([], null, null);
        public PricesResponse RangeResponse { get; set; } = new([], null, null);

        public Task<MarketDetailsResponse> GetMarketByEpicAsync(string epic, CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketDetailsResponse(
                new MarketInstrument(epic, null, null, "DFB", [new MarketCurrency("USD", true)]),
                new MarketSnapshot("TRADEABLE", 100m, 101m),
                new MarketDealingRules(new MarketRuleDistance(1m, "POINTS"))));

        public Task<MarketSearchResponse> SearchMarketsAsync(string searchTerm, CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketSearchResponse([]));

        public Task<MarketNavigationResponse> GetMarketNavigationRootAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketNavigationResponse("Markets", [], []));

        public Task<MarketNavigationResponse> GetMarketNavigationNodeAsync(string nodeId, CancellationToken cancellationToken = default)
            => Task.FromResult(new MarketNavigationResponse(nodeId, [], []));

        public Task<PricesResponse> GetRecentPricesAsync(string epic, CancellationToken cancellationToken = default)
        {
            Calls.Add($"recent:{epic}");
            return Task.FromResult(RecentResponse);
        }

        public Task<PricesResponse> GetPricesByPointsAsync(string epic, string resolution, int numPoints, CancellationToken cancellationToken = default)
        {
            Calls.Add($"points:{epic}:{resolution}:{numPoints}");
            return Task.FromResult(PointsResponse);
        }

        public Task<PricesResponse> GetPricesByRangeAsync(string epic, string resolution, string from, string to, CancellationToken cancellationToken = default)
        {
            Calls.Add($"range:{epic}:{resolution}:{from}:{to}");
            return Task.FromResult(RangeResponse);
        }
    }

    private sealed class FakePositionsApi : IIgPositionsApi
    {
        public Task<CreatePositionResponse> CreatePositionAsync(CreatePositionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ClosePositionResponse> ClosePositionAsync(ClosePositionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PositionsResponse> GetOpenPositionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<PositionEnvelope> GetPositionByDealIdAsync(string dealId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<UpdatePositionResponse> UpdatePositionAsync(string dealId, UpdatePositionRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeOrderStateApi : IIgOrderStateApi
    {
        public Task<DealConfirmationResponse> GetDealConfirmationAsync(string dealReference, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<ActivityResponse> GetActivityAsync(string from, string to, bool detailed, int pageSize, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<TransactionHistoryResponse> GetTransactionsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeWorkingOrdersApi : IIgWorkingOrdersApi
    {
        public Task<WorkingOrdersResponse> GetWorkingOrdersAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingOrderMutationResponse> CreateWorkingOrderAsync(CreateWorkingOrderRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingOrderMutationResponse> UpdateWorkingOrderAsync(string dealId, UpdateWorkingOrderRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkingOrderMutationResponse> DeleteWorkingOrderAsync(string dealId, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FakeAccountsApi : IIgAccountsApi
    {
        public Task<AccountsResponse> GetAccountsAsync(CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
