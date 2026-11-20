using System.Security.Claims;
using iCareX.Application.Features.Records;
using iCareX.Shared.Contracts.Records;
using iCareX.Web.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace iCareX.Web.Api.Controllers;

[ApiController, Authorize(Policy = "VerifiedAccount")]
[Route("api/v1/vehicles/{vehicleId:guid}")]
[ServiceFilter(typeof(VehicleCsrfFilter))]
public sealed class RecordsController(IRecordService records) : ControllerBase
{
    private Guid Actor => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    [HttpGet("fuel-records")]
    public async Task<ActionResult<IReadOnlyList<FuelResponse>>> Fuel(Guid vehicleId, CancellationToken ct) => Ok(await records.FuelAsync(Actor, vehicleId, ct));
    [HttpGet("service-records")]
    public async Task<ActionResult<IReadOnlyList<ServiceResponse>>> Services(Guid vehicleId, CancellationToken ct) => Ok(await records.ServicesAsync(Actor, vehicleId, ct));
    [HttpPost("fuel-records")]
    public async Task<ActionResult<FuelResponse>> CreateFuel(Guid vehicleId, FuelWriteRequest request, CancellationToken ct)
    {
        var result = await records.WriteFuelAsync(Actor, vehicleId, null, request, ct);
        return Created($"/api/v1/vehicles/{vehicleId}/fuel-records", result);
    }
    [HttpPut("fuel-records/{id:guid}")]
    public async Task<ActionResult<FuelResponse>> UpdateFuel(Guid vehicleId, Guid id, FuelWriteRequest request, CancellationToken ct) => Ok(await records.WriteFuelAsync(Actor, vehicleId, id, request, ct));
    [HttpPost("service-records")]
    public async Task<ActionResult<ServiceResponse>> CreateService(Guid vehicleId, ServiceWriteRequest request, CancellationToken ct)
    {
        var result = await records.WriteServiceAsync(Actor, vehicleId, null, request, ct);
        return Created($"/api/v1/vehicles/{vehicleId}/service-records", result);
    }
    [HttpPut("service-records/{id:guid}")]
    public async Task<ActionResult<ServiceResponse>> UpdateService(Guid vehicleId, Guid id, ServiceWriteRequest request, CancellationToken ct) => Ok(await records.WriteServiceAsync(Actor, vehicleId, id, request, ct));
    [HttpDelete("fuel-records/{id:guid}")]
    public async Task<IActionResult> DeleteFuel(Guid vehicleId, Guid id, [FromQuery] long version, CancellationToken ct)
    {
        await records.DeleteAsync(Actor, vehicleId, id, true, version, ct); return NoContent();
    }
    [HttpDelete("service-records/{id:guid}")]
    public async Task<IActionResult> DeleteService(Guid vehicleId, Guid id, [FromQuery] long version, CancellationToken ct)
    {
        await records.DeleteAsync(Actor, vehicleId, id, false, version, ct); return NoContent();
    }
}
