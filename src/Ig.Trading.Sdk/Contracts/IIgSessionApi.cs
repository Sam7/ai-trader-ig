using Ig.Trading.Sdk.Models;
using Refit;

namespace Ig.Trading.Sdk.Contracts;

public interface IIgSessionApi
{
    [Get("/session/encryptionKey")]
    [Headers("Version: 1")]
    Task<EncryptionKeyResponse> GetEncryptionKeyAsync(CancellationToken cancellationToken = default);

    [Post("/session")]
    [Headers("Version: 2")]
    Task<ApiResponse<SessionResponse>> CreateSessionAsync([Body] SessionRequest request, CancellationToken cancellationToken = default);

    [Put("/session")]
    [Headers("Version: 1")]
    Task<ApiResponse<SessionResponse>> SwitchAccountAsync([Body] SwitchAccountRequest request, CancellationToken cancellationToken = default);
}
