namespace Trading.Abstractions;

public enum TradingErrorCode
{
    Unknown = 0,
    AuthenticationFailed = 1,
    SessionExpired = 2,
    InvalidInstrument = 3,
    MarketClosed = 4,
    InsufficientFunds = 5,
    InvalidRequest = 6,
    NetworkFailure = 7,
    BrokerError = 8,
}
