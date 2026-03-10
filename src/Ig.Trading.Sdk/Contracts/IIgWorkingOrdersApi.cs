using Ig.Trading.Sdk.Models;
using Refit;

namespace Ig.Trading.Sdk.Contracts;

public interface IIgWorkingOrdersApi
{
    [Get("/workingorders")]
    [Headers("Version: 2")]
    Task<WorkingOrdersResponse> GetWorkingOrdersAsync(CancellationToken cancellationToken = default);

    [Post("/workingorders/otc")]
    [Headers("Version: 2")]
    Task<WorkingOrderMutationResponse> CreateWorkingOrderAsync([Body] CreateWorkingOrderRequest request, CancellationToken cancellationToken = default);

    [Put("/workingorders/otc/{dealId}")]
    [Headers("Version: 2")]
    Task<WorkingOrderMutationResponse> UpdateWorkingOrderAsync(string dealId, [Body] UpdateWorkingOrderRequest request, CancellationToken cancellationToken = default);

    [Delete("/workingorders/otc/{dealId}")]
    [Headers("Version: 2")]
    Task<WorkingOrderMutationResponse> DeleteWorkingOrderAsync(string dealId, CancellationToken cancellationToken = default);
}
