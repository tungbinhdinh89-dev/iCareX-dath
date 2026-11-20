using iCareX.Shared.Contracts.Records;

namespace iCareX.Application.Features.Records;

public interface IRecordService
{
    Task<IReadOnlyList<FuelResponse>> FuelAsync(Guid actor, Guid vehicleId, CancellationToken ct);
    Task<IReadOnlyList<ServiceResponse>> ServicesAsync(Guid actor, Guid vehicleId, CancellationToken ct);
    Task<FuelResponse> WriteFuelAsync(Guid actor, Guid vehicleId, Guid? id, FuelWriteRequest request, CancellationToken ct);
    Task<ServiceResponse> WriteServiceAsync(Guid actor, Guid vehicleId, Guid? id, ServiceWriteRequest request, CancellationToken ct);
    Task DeleteAsync(Guid actor, Guid vehicleId, Guid id, bool fuel, long version, CancellationToken ct);
}
