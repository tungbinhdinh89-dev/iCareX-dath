using System.Text.Json.Serialization;

namespace iCareX.Shared.Contracts.Records;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ServiceItemRequest(string Type, string Title, decimal Cost, Guid? PlanId);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record FuelWriteRequest(Guid RequestId, DateOnly Date, decimal Odo, decimal Cost, decimal Liters, string Note, long? Version);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ServiceWriteRequest(Guid RequestId, DateOnly Date, decimal Odo, string Note, IReadOnlyList<ServiceItemRequest> Items, long? Version);
public sealed record FuelResponse(Guid Id, Guid VehicleId, Guid CreatedByUserId, DateOnly Date, decimal Odo, decimal Cost, decimal Liters, string Note, long Version);
public sealed record ServiceResponse(Guid Id, Guid VehicleId, Guid CreatedByUserId, DateOnly Date, decimal Odo, decimal Cost, string Note, IReadOnlyList<ServiceItemRequest> Items, long Version);
