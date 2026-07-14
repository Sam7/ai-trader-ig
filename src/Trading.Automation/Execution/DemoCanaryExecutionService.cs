using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ig.Trading.Sdk.Configuration;
using Trading.Abstractions;
using Trading.Automation.Configuration;
using Trading.Execution;
using Trading.Strategy.Shared;

namespace Trading.Automation.Execution;

public sealed class DemoCanaryExecutionService
{
    private static readonly TimeSpan ConfirmationTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan ProtectionTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly AutomationOptions _automationOptions;
    private readonly IgClientOptions _igClientOptions;
    private readonly ITradingGateway _gateway;
    private readonly IExecutionSubmissionService _executionSubmissionService;
    private readonly ExecutionBoundaryService _executionBoundaryService;
    private readonly DecisionEvidenceSidecarWriter _sidecarWriter;
    private readonly IExecutionClock _clock;
    private readonly ILogger<DemoCanaryExecutionService> _logger;

    public DemoCanaryExecutionService(
        IOptions<AutomationOptions> automationOptions,
        IOptions<IgClientOptions> igClientOptions,
        ITradingGateway gateway,
        IExecutionSubmissionService executionSubmissionService,
        ExecutionBoundaryService executionBoundaryService,
        DecisionEvidenceSidecarWriter sidecarWriter,
        IExecutionClock clock,
        ILogger<DemoCanaryExecutionService> logger)
    {
        _automationOptions = automationOptions.Value;
        _igClientOptions = igClientOptions.Value;
        _gateway = gateway;
        _executionSubmissionService = executionSubmissionService;
        _executionBoundaryService = executionBoundaryService;
        _sidecarWriter = sidecarWriter;
        _clock = clock;
        _logger = logger;
    }

    public async Task<DemoCanaryExecutionSnapshot?> ExecuteAsync(
        IntradayOpportunitySubmitResult result,
        CancellationToken cancellationToken = default)
    {
        var intent = result.WorkflowResult.SelectedShadowIntent;
        if (intent is null)
        {
            return null;
        }

        if (_automationOptions.Execution.Mode != TradingExecutionMode.Demo)
        {
            _logger.LogInformation("Skipping demo canary execution because execution mode is {Mode}.", _automationOptions.Execution.Mode);
            return null;
        }

        var demoOptions = _automationOptions.Execution.Demo;
        demoOptions.Validate();

        var existingBoundary = await _executionBoundaryService.GetAsync(intent.DecisionId, cancellationToken);
        if (existingBoundary is null)
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                "The reserved execution boundary record is missing.",
                cancellationToken);
        }

        if (!demoOptions.Armed)
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                "Demo canary execution is disarmed.",
                cancellationToken,
                existingBoundary.State);
        }

        if (demoOptions.KillSwitchEngaged)
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                "Demo canary kill switch is engaged.",
                cancellationToken,
                existingBoundary.State);
        }

        if (demoOptions.AllowedInstruments.Length != 1
            || !string.Equals(demoOptions.AllowedInstruments[0].Trim(), intent.Instrument.Value, StringComparison.Ordinal))
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                $"Instrument '{intent.Instrument.Value}' is not the single allowlisted demo canary instrument.",
                cancellationToken,
                existingBoundary.State);
        }

        if (intent.EntryMethod != TradeEntryMethod.Market)
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                "Only market-entry candidates are supported for the demo canary.",
                cancellationToken,
                existingBoundary.State);
        }

        if (intent.SetupExpiresAtUtc <= _clock.UtcNow)
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                "The selected intent expired before demo submission could begin.",
                cancellationToken,
                existingBoundary.State);
        }

        if (!BaseUrlsMatch(_igClientOptions.BaseUrl, demoOptions.ApprovedBaseUrl))
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                $"Configured IG base URL '{_igClientOptions.BaseUrl}' does not match the approved demo endpoint.",
                cancellationToken,
                existingBoundary.State);
        }

        if (existingBoundary.State != ExecutionBoundaryState.Reserved)
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                $"The execution boundary is already in state '{existingBoundary.State}'.",
                cancellationToken);
        }

        var unresolvedOperations = await _executionBoundaryService.GetUnresolvedOperationsAsync(cancellationToken);
        if (unresolvedOperations.Any(operation => !string.Equals(operation.OperationId, intent.DecisionId, StringComparison.Ordinal)))
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                "There are unresolved execution operations that must be reconciled first.",
                cancellationToken,
                boundaryState: existingBoundary.State);
        }

        var sameDayOperations = await _executionBoundaryService.GetOperationsByTradingDateAsync(intent.TradingDate, cancellationToken);
        var sameDayExecutionCount = sameDayOperations.Count(operation =>
            !string.Equals(operation.OperationId, intent.DecisionId, StringComparison.Ordinal));
        if (sameDayExecutionCount >= demoOptions.MaxTradesPerTradingDay)
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                $"Trading date {intent.TradingDate:yyyy-MM-dd} already has {sameDayExecutionCount} prior execution operation(s).",
                cancellationToken,
                boundaryState: existingBoundary.State);
        }

        var session = await _gateway.AuthenticateAsync(cancellationToken);
        if (!string.Equals(session.BrokerName, "IG", StringComparison.OrdinalIgnoreCase))
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                $"Unexpected broker session '{session.BrokerName}'.",
                cancellationToken,
                existingBoundary.State);
        }

        if (!string.Equals(session.AccountId, demoOptions.ApprovedAccountId, StringComparison.Ordinal))
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                $"Authenticated account '{session.AccountId}' does not match the approved demo account.",
                cancellationToken,
                existingBoundary.State);
        }

        var openPositions = await _gateway.GetOpenPositionsAsync(cancellationToken);
        if (openPositions.Count > 0)
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                $"Found {openPositions.Count} existing open position(s); the demo canary requires an empty book.",
                cancellationToken,
                boundaryState: existingBoundary.State);
        }

        var workingOrders = await _gateway.GetWorkingOrdersAsync(cancellationToken);
        if (workingOrders.Count > 0)
        {
            return await CreateAndPersistFailureSnapshotAsync(
                result,
                intent,
                $"Found {workingOrders.Count} existing working order(s); the demo canary requires no pending orders.",
                cancellationToken,
                boundaryState: existingBoundary.State);
        }

        var market = await _gateway.GetMarketDetailsAsync(intent.Instrument, cancellationToken);
        ValidateMarket(market, intent);

        var minimumSize = ResolveBrokerMinimumSize(market);
        var orderRequest = new PlaceOrderRequest(
            intent.Instrument,
            intent.Direction,
            minimumSize,
            null,
            intent.StopLossPrice,
            intent.TakeProfitPrice);

        var submission = await _executionSubmissionService.SubmitMarketOrderAsync(
            intent.DecisionId,
            ExecutionOperationSource.AutomatedDecision,
            orderRequest,
            cancellationToken);

        if (submission.Status == OrderStatus.Rejected)
        {
            var rejectedSnapshot = new DemoCanaryExecutionSnapshot(
                intent.DecisionId,
                submission.Record.OperationId,
                TradingExecutionMode.Demo,
                _igClientOptions.BaseUrl,
                demoOptions.ApprovedAccountId,
                session.AccountId,
                intent.Instrument,
                intent.InstrumentName,
                minimumSize,
                intent.StopLossPrice,
                intent.TakeProfitPrice,
                submission.DealReference,
                submission.DealId,
                submission.Record.State,
                submission.Status,
                submission.Status,
                false,
                false,
                submission.TimestampUtc,
                submission.TimestampUtc,
                submission.Message ?? "IG rejected the demo canary submission.");
            await SaveExecutionAuditAsync(result, rejectedSnapshot, cancellationToken);
            return rejectedSnapshot;
        }

        var protectionVerified = false;
        var protectionAmended = false;
        OrderStatus? confirmationStatus = submission.Status;
        DateTimeOffset? completedAtUtc = submission.TimestampUtc;

        var dealId = await ResolveDealIdAsync(submission, intent, minimumSize, cancellationToken);
        if (dealId is null)
        {
            var uncertainSnapshot = CreateSnapshot(
                intent,
                minimumSize,
                session.AccountId,
                demoOptions.ApprovedAccountId,
                submission,
                submission.Status,
                confirmationStatus: null,
                protectionVerified: false,
                protectionAmended: false,
                completedAtUtc: null,
                outcome: "The broker accepted the submission, but the deal ID could not be confirmed.");
            await SaveExecutionAuditAsync(result, uncertainSnapshot, cancellationToken);
            return uncertainSnapshot;
        }

        var orderStatus = await _gateway.GetOrderStatusAsync(submission.DealReference, cancellationToken);
        if (orderStatus is not null)
        {
            confirmationStatus = orderStatus.Status;
            completedAtUtc = orderStatus.TimestampUtc;
        }

        var position = await WaitForPositionAsync(dealId, cancellationToken);
        if (position is null)
        {
            var uncertainSnapshot = CreateSnapshot(
                intent,
                minimumSize,
                session.AccountId,
                demoOptions.ApprovedAccountId,
                submission,
                submission.Status,
                confirmationStatus: null,
                protectionVerified: false,
                protectionAmended: false,
                completedAtUtc: null,
                outcome: "The broker accepted the submission, but the open position never became visible.");
            await SaveExecutionAuditAsync(result, uncertainSnapshot, cancellationToken);
            return uncertainSnapshot;
        }

        protectionVerified = HasExpectedProtection(position, intent);

        if (!protectionVerified)
        {
            var amendment = await _executionSubmissionService.SubmitUpdatePositionAsync(
                $"{intent.DecisionId}-protect",
                ExecutionOperationSource.AutomatedDecision,
                new UpdatePositionRequest(
                    dealId,
                    intent.StopLossPrice,
                    intent.TakeProfitPrice),
                cancellationToken);

            protectionAmended = true;
            confirmationStatus = amendment.Status;
            completedAtUtc = amendment.TimestampUtc;
            position = await WaitForProtectedPositionAsync(dealId, intent, cancellationToken);
            protectionVerified = position is not null;
        }

        if (!protectionVerified)
        {
            var close = await _executionSubmissionService.SubmitClosePositionAsync(
                $"{intent.DecisionId}-abort",
                ExecutionOperationSource.AutomatedDecision,
                new ClosePositionRequest(dealId),
                cancellationToken);

            var failedSnapshot = new DemoCanaryExecutionSnapshot(
                intent.DecisionId,
                submission.Record.OperationId,
                TradingExecutionMode.Demo,
                _igClientOptions.BaseUrl,
                demoOptions.ApprovedAccountId,
                session.AccountId,
                intent.Instrument,
                intent.InstrumentName,
                minimumSize,
                intent.StopLossPrice,
                intent.TakeProfitPrice,
                submission.DealReference,
                dealId,
                close.Record.State,
                submission.Status,
                confirmationStatus,
                false,
                protectionAmended,
                submission.TimestampUtc,
                close.TimestampUtc,
                "A protected position could not be established, so the demo canary position was flattened.");
            await SaveExecutionAuditAsync(result, failedSnapshot, cancellationToken);
            return failedSnapshot;
        }

        var successSnapshot = new DemoCanaryExecutionSnapshot(
            intent.DecisionId,
            submission.Record.OperationId,
            TradingExecutionMode.Demo,
            _igClientOptions.BaseUrl,
            demoOptions.ApprovedAccountId,
            session.AccountId,
            intent.Instrument,
            intent.InstrumentName,
            minimumSize,
            intent.StopLossPrice,
            intent.TakeProfitPrice,
            submission.DealReference,
            dealId,
            submission.Record.State,
            submission.Status,
            confirmationStatus,
            true,
            protectionAmended,
            submission.TimestampUtc,
            completedAtUtc,
            protectionAmended
                ? "Demo canary trade was opened and protection was confirmed after a recovery amendment."
                : "Demo canary trade was opened with confirmed stop and target protection.");

        await SaveExecutionAuditAsync(result, successSnapshot, cancellationToken);
        return successSnapshot;
    }

    private async Task<DemoCanaryExecutionSnapshot> CreateAndPersistFailureSnapshotAsync(
        IntradayOpportunitySubmitResult result,
        ExecutionReadyTradeIntent intent,
        string outcome,
        CancellationToken cancellationToken,
        ExecutionBoundaryState boundaryState = ExecutionBoundaryState.FailedBeforeSubmission)
    {
        var snapshot = new DemoCanaryExecutionSnapshot(
            intent.DecisionId,
            intent.DecisionId,
            TradingExecutionMode.Demo,
            _igClientOptions.BaseUrl,
            _automationOptions.Execution.Demo.ApprovedAccountId,
            string.Empty,
            intent.Instrument,
            intent.InstrumentName,
            0m,
            intent.StopLossPrice,
            intent.TakeProfitPrice,
            "n/a",
            null,
            boundaryState,
            OrderStatus.Unknown,
            null,
            false,
            false,
            _clock.UtcNow,
            _clock.UtcNow,
            outcome);

        await SaveExecutionAuditAsync(result, snapshot, cancellationToken);
        return snapshot;
    }

    private DemoCanaryExecutionSnapshot CreateSnapshot(
        ExecutionReadyTradeIntent intent,
        decimal size,
        string sessionAccountId,
        string approvedAccountId,
        ExecutionSubmissionResult submission,
        OrderStatus submissionStatus,
        OrderStatus? confirmationStatus,
        bool protectionVerified,
        bool protectionAmended,
        DateTimeOffset? completedAtUtc,
        string outcome,
        string? dealId = null,
        ExecutionBoundaryState? boundaryState = null)
        => new(
            intent.DecisionId,
            submission.Record.OperationId,
            TradingExecutionMode.Demo,
            _igClientOptions.BaseUrl,
            approvedAccountId,
            sessionAccountId,
            intent.Instrument,
            intent.InstrumentName,
            size,
            intent.StopLossPrice,
            intent.TakeProfitPrice,
            submission.DealReference,
            dealId ?? submission.DealId,
            boundaryState ?? submission.Record.State,
            submissionStatus,
            confirmationStatus,
            protectionVerified,
            protectionAmended,
            submission.TimestampUtc,
            completedAtUtc,
            outcome);

    private async Task SaveExecutionAuditAsync(
        IntradayOpportunitySubmitResult result,
        DemoCanaryExecutionSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (result.ExecutionArtifacts.DecisionAuditArtifact is null)
        {
            throw new InvalidOperationException("Decision audit artifact is required for demo canary execution.");
        }

        await _sidecarWriter.WriteDemoExecutionAsync(
            result.ExecutionArtifacts.DecisionAuditArtifact.Path,
            snapshot,
            _clock.UtcNow,
            cancellationToken);
    }

    private async Task<string?> ResolveDealIdAsync(
        ExecutionSubmissionResult submission,
        ExecutionReadyTradeIntent intent,
        decimal size,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(submission.DealId))
        {
            return submission.DealId;
        }

        if (submission.Status == OrderStatus.Rejected)
        {
            return null;
        }

        var deadline = DateTimeOffset.UtcNow + ConfirmationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var status = await _gateway.GetOrderStatusAsync(submission.DealReference, cancellationToken);
            if (!string.IsNullOrWhiteSpace(status?.DealId))
            {
                return status!.DealId;
            }

            var positions = await _gateway.GetOpenPositionsAsync(cancellationToken);
            var match = positions.FirstOrDefault(position =>
                string.Equals(position.Instrument.Value, intent.Instrument.Value, StringComparison.Ordinal)
                && position.Direction == intent.Direction
                && position.Size == size);

            if (match is not null)
            {
                return match.DealId;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return null;
    }

    private async Task<PositionSummary?> WaitForPositionAsync(
        string dealId,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ConfirmationTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var positions = await _gateway.GetOpenPositionsAsync(cancellationToken);
            var position = positions.FirstOrDefault(current =>
                string.Equals(current.DealId, dealId, StringComparison.OrdinalIgnoreCase));
            if (position is not null)
            {
                return position;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return null;
    }

    private async Task<PositionSummary?> WaitForProtectedPositionAsync(
        string dealId,
        ExecutionReadyTradeIntent intent,
        CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + ProtectionTimeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            var positions = await _gateway.GetOpenPositionsAsync(cancellationToken);
            var position = positions.FirstOrDefault(current =>
                string.Equals(current.DealId, dealId, StringComparison.OrdinalIgnoreCase));
            if (position is not null && HasExpectedProtection(position, intent))
            {
                return position;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return null;
    }

    private static bool HasExpectedProtection(
        PositionSummary position,
        ExecutionReadyTradeIntent intent)
        => position.StopLevel == intent.StopLossPrice
           && position.LimitLevel == intent.TakeProfitPrice;

    private static void ValidateMarket(MarketDetails market, ExecutionReadyTradeIntent intent)
    {
        if (market.Status != MarketStatus.Tradeable)
        {
            throw new InvalidOperationException($"Market '{market.Instrument.Value}' is not tradeable.");
        }

        if (!market.SupportedOrderTypes.Contains("MARKET", StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Market '{market.Instrument.Value}' does not support market orders.");
        }

        if (market.DealingRules?.MinimumDealSize?.Value is not > 0m)
        {
            throw new InvalidOperationException($"Market '{market.Instrument.Value}' does not expose a valid minimum deal size.");
        }

        if (market.StopsLimitsAllowed == false)
        {
            throw new InvalidOperationException($"Market '{market.Instrument.Value}' does not allow stop and limit protection.");
        }

        var minimumDistance = market.DealingRules?.MinimumStopOrLimitDistance?.Value;
        if (minimumDistance is > 0m)
        {
            var stopDistance = decimal.Abs(intent.ExpectedEntryPrice - intent.StopLossPrice);
            var limitDistance = decimal.Abs(intent.TakeProfitPrice - intent.ExpectedEntryPrice);
            if (stopDistance < minimumDistance.Value || limitDistance < minimumDistance.Value)
            {
                throw new InvalidOperationException(
                    $"The selected stop and target do not meet the broker's minimum stop/limit distance for '{market.Instrument.Value}'.");
            }
        }
    }

    private static decimal ResolveBrokerMinimumSize(MarketDetails market)
    {
        var minimumDealSize = market.DealingRules?.MinimumDealSize?.Value;
        if (minimumDealSize is > 0m)
        {
            return minimumDealSize.Value;
        }

        if (market.LotSize is > 0m)
        {
            return market.LotSize.Value;
        }

        throw new InvalidOperationException($"Market '{market.Instrument.Value}' does not expose a broker minimum deal size.");
    }

    private static bool BaseUrlsMatch(string actual, string expected)
    {
        if (!Uri.TryCreate(actual, UriKind.Absolute, out var actualUri)
            || !Uri.TryCreate(expected, UriKind.Absolute, out var expectedUri))
        {
            return false;
        }

        return string.Equals(
            actualUri.GetLeftPart(UriPartial.Authority).TrimEnd('/'),
            expectedUri.GetLeftPart(UriPartial.Authority).TrimEnd('/'),
            StringComparison.OrdinalIgnoreCase)
            && string.Equals(actualUri.AbsolutePath.TrimEnd('/'), expectedUri.AbsolutePath.TrimEnd('/'), StringComparison.Ordinal);
    }
}
