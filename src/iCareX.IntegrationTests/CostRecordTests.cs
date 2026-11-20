using iCareX.Application.Abstractions;
using iCareX.Application.Features.Authorization.Contracts;
using iCareX.Application.Features.Vehicles.Contracts;
using iCareX.Domain.Entities;
using iCareX.Infrastructure.Persistence;
using iCareX.Infrastructure.Services.Implementations;
using iCareX.Shared.Contracts.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using iCareX.Shared.Contracts.Identity;
using iCareX.Shared.Contracts.Vehicles;
using Xunit;

namespace iCareX.IntegrationTests;

public sealed partial class DatabaseTests
{
    private sealed class CostClock : IBusinessClock
    {
        // Keep cookie ticket expiry aligned with ASP.NET's real TimeProvider.
        // Business dates remain deterministic for ODO chronology.
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.UtcNow;
        public DateOnly Today => new(2026, 11, 5);
    }
    private static RecordService Records(GarageDbContext db) => new(db, new VehicleMembershipResolver(db, new CostClock()), new CostClock());
    private async Task<(Guid Owner, Guid Contributor, Vehicle Vehicle, Vehicle Other)> CostFixture()
    {
        await using var db = Context();
        var owner = Guid.NewGuid(); var contributor = Guid.NewGuid();
        foreach (var id in new[] { owner, contributor })
        {
            db.Users.Add(new ApplicationUser { Id = id, UserName = id.ToString(), EmailConfirmed = true });
            db.Accounts.Add(new Account { Id = id, Name = "Cost fixture" });
        }
        var model = TestVehicleModel(); db.VehicleModels.Add(model);
        Vehicle Car() => new() { AccountId = owner, CreatedByUserId = owner, VehicleModelId = model.Id,
            VehicleBrandId = model.VehicleBrandId, VehicleType = iCareX.Domain.Enums.VehicleType.Car,
            FuelType = iCareX.Domain.Enums.FuelType.Gasoline, Odometer = 10_000 };
        var car = Car(); var other = Car(); db.Vehicles.AddRange(car, other);
        foreach (var v in new[] { car, other }) db.VehicleMembers.Add(new() { VehicleId = v.Id, UserId = owner, Role = VehicleMemberRole.Owner });
        db.VehicleMembers.Add(new() { VehicleId = car.Id, UserId = contributor, Role = VehicleMemberRole.Contributor });
        await db.SaveChangesAsync(); return (owner, contributor, car, other);
    }

    [Fact]
    public async Task CostRecordsReplayUpdateDeleteAndContributorScope()
    {
        var (owner, contributor, car, other) = await CostFixture();
        var request = new FuelWriteRequest(Guid.NewGuid(), new(2026, 11, 2), 10_100, 200_000, 10, "", null);
        FuelResponse fuel;
        await using (var db = Context()) fuel = await Records(db).WriteFuelAsync(owner, car.Id, null, request, default);
        await using (var db = Context()) Assert.Equal(fuel, await Records(db).WriteFuelAsync(owner, car.Id, null, request, default));
        await using (var db = Context()) await Assert.ThrowsAsync<VehicleConflictException>(() => Records(db).WriteFuelAsync(owner, other.Id, null, request, default));
        await using (var db = Context()) await Assert.ThrowsAsync<VehicleConflictException>(() => Records(db).WriteFuelAsync(owner, car.Id, null, request with { Cost = 1 }, default));
        var serviceRequest = new ServiceWriteRequest(Guid.NewGuid(), new(2026, 11, 3), 10_200, "", [
            new("Repair", "Sửa xe", 100_000, null), new("Maintenance", "Thay dầu", 150_000, null)
        ], null);
        ServiceResponse service;
        await using (var db = Context()) service = await Records(db).WriteServiceAsync(contributor, car.Id, null, serviceRequest, default);
        await using (var db = Context())
        {
            var replay = await Records(db).WriteServiceAsync(contributor, car.Id, null, serviceRequest, default);
            Assert.Equal(service.Id, replay.Id);
            Assert.Equal(450_000, (await Records(db).FuelAsync(owner, car.Id, default)).Sum(x => x.Cost)
                + (await Records(db).ServicesAsync(owner, car.Id, default)).Sum(x => x.Cost));
            Assert.Equal(2, await db.OdometerReadings.CountAsync(x => x.VehicleId == car.Id));
            Assert.Equal(2, await db.ServiceItems.CountAsync(x => x.MaintenanceRecordId == service.Id));
        }
        await using (var db = Context()) await Assert.ThrowsAsync<VehicleAccessDeniedException>(() => Records(db).WriteFuelAsync(contributor, car.Id, fuel.Id,
            request with { RequestId = Guid.NewGuid(), Version = fuel.Version }, default));
        await using (var db = Context())
        {
            var updated = await Records(db).WriteServiceAsync(contributor, car.Id, service.Id,
                serviceRequest with { RequestId = Guid.NewGuid(), Odo = 10_150, Version = service.Version }, default);
            Assert.Equal(1, updated.Version);
            Assert.Equal(10_150, (await db.OdometerReadings.SingleAsync(x => x.MaintenanceRecordId == service.Id)).Odo);
        }
        await using (var db = Context()) await Assert.ThrowsAsync<VehicleConflictException>(() => Records(db).WriteServiceAsync(contributor, car.Id, service.Id,
            serviceRequest with { RequestId = Guid.NewGuid(), Version = service.Version }, default));
        await using (var db = Context()) await Records(db).DeleteAsync(contributor, car.Id, service.Id, false, 1, default);
        await using (var db = Context())
        {
            Assert.False(await db.OdometerReadings.AnyAsync(x => x.MaintenanceRecordId == service.Id));
            Assert.False(await db.ServiceItems.AnyAsync(x => x.MaintenanceRecordId == service.Id));
            Assert.Equal(10_100, await db.OdometerReadings.Where(x => x.VehicleId == car.Id).MaxAsync(x => x.Odo));
        }
    }

    [Fact]
    public async Task ConcurrentRetryAndFailureAfterSaveAreAtomic()
    {
        var (owner, _, car, _) = await CostFixture();
        var request = new FuelWriteRequest(Guid.NewGuid(), new(2026, 11, 2), 10_100, 200_000, 10, "", null);
        async Task<FuelResponse> Send()
        {
            await using var db = Context(); return await Records(db).WriteFuelAsync(owner, car.Id, null, request, default);
        }
        var responses = await Task.WhenAll(Send(), Send());
        Assert.Equal(responses[0].Id, responses[1].Id);
        await using (var db = Context()) Assert.Equal(1, await db.FuelEntries.CountAsync(x => x.VehicleId == car.Id));
        var serviceRequest = new ServiceWriteRequest(Guid.NewGuid(), new(2026, 11, 3), 10_200, "", [new("Repair", "Sửa xe", 100_000, null)], null);
        await using (var failing = new GarageDbContext(new DbContextOptionsBuilder<GarageDbContext>().UseNpgsql(postgres.GetConnectionString())
            .AddInterceptors(new CostSaveFailure()).Options))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Records(failing).WriteServiceAsync(owner, car.Id, null, serviceRequest, default));
        await using (var db = Context())
        {
            Assert.False(await db.MaintenanceRecords.AnyAsync(x => x.VehicleId == car.Id));
            Assert.False(await db.IdempotencyRecords.AnyAsync(x => x.AccountId == owner && x.Key == serviceRequest.RequestId.ToString()));
            Assert.Equal(1, await db.OdometerReadings.CountAsync(x => x.VehicleId == car.Id));
        }
        await using (var db = Context()) Assert.Equal(100_000, (await Records(db).WriteServiceAsync(owner, car.Id, null, serviceRequest, default)).Cost);
    }

    [Fact]
    public async Task InvalidItemsPlanLinksArchiveAndRevokedMembershipCannotWrite()
    {
        var (owner, contributor, car, _) = await CostFixture();
        foreach (var item in new ServiceItemRequest[] { new("Repair", "Sửa xe", -1, null), new("Repair", "Sửa xe", 1, Guid.NewGuid()), new("Maintenance", "Thay dầu", 1, Guid.NewGuid()) })
        {
            await using var db = Context();
            await Assert.ThrowsAsync<VehicleValidationException>(() => Records(db).WriteServiceAsync(owner, car.Id, null,
                new(Guid.NewGuid(), new(2026, 11, 2), 10_100, "", [item], null), default));
        }
        await using (var db = Context())
        {
            Assert.False(await db.MaintenanceRecords.AnyAsync(x => x.VehicleId == car.Id));
            await db.VehicleMembers.Where(x => x.VehicleId == car.Id && x.UserId == contributor).ExecuteUpdateAsync(x => x.SetProperty(m => m.RevokedAtUtc, new CostClock().UtcNow));
        }
        var fuel = new FuelWriteRequest(Guid.NewGuid(), new(2026, 11, 2), 10_100, 200_000, 10, "", null);
        await using (var db = Context()) await Assert.ThrowsAsync<VehicleNotFoundException>(() => Records(db).WriteFuelAsync(contributor, car.Id, null, fuel, default));
        await using (var db = Context()) await db.Vehicles.Where(x => x.Id == car.Id).ExecuteUpdateAsync(x => x.SetProperty(v => v.ArchivedAtUtc, new CostClock().UtcNow));
        await using (var db = Context()) await Assert.ThrowsAsync<VehicleAccessDeniedException>(() => Records(db).WriteFuelAsync(owner, car.Id, null, fuel, default));
    }

    private sealed class CostSaveFailure : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Injected failure after SQL save, before transaction commit.");
    }

    [Fact]
    public async Task AuthenticatedCostHttpFlowReloadsAndSourceOdoCannotBeChangedManually()
    {
        var mail = new CapturedEmail();
        using var baseHost = SecurityHost(mail);
        using var host = baseHost.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IBusinessClock>(); s.AddSingleton<IBusinessClock>(new CostClock());
        }));
        using var client = await SecurityClient(host);
        var email = $"{Guid.NewGuid():N}@example.test";
        const string password = "CostRecordPassword@123";
        var registered = await client.PostAsJsonAsync("/api/v1/auth/register", new { name = "Cost owner", email, password, phone = "" });
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        var owner = (await registered.Content.ReadFromJsonAsync<AccountResponse>())!.Id;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/verify-email", new { email, token = mail.Token(email) })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password })).StatusCode);
        Guid vehicleId;
        await using (var db = Context())
        {
            var model = TestVehicleModel(); db.VehicleModels.Add(model);
            var car = new Vehicle { AccountId = owner, CreatedByUserId = owner, VehicleType = iCareX.Domain.Enums.VehicleType.Car,
                FuelType = iCareX.Domain.Enums.FuelType.Gasoline, VehicleBrandId = model.VehicleBrandId, VehicleModelId = model.Id, Odometer = 10_000 };
            vehicleId = car.Id; db.Vehicles.Add(car);
            db.VehicleMembers.Add(new() { VehicleId = car.Id, UserId = owner, Role = VehicleMemberRole.Owner });
            await db.SaveChangesAsync();
        }
        var url = $"/api/v1/vehicles/{vehicleId}";
        var fuelRequest = new FuelWriteRequest(Guid.NewGuid(), new(2026, 11, 2), 10_100, 200_000, 10, "", null);
        var fuel = await client.PostAsJsonAsync(url + "/fuel-records", fuelRequest);
        Assert.Equal(HttpStatusCode.Created, fuel.StatusCode);
        var fuelResult = (await fuel.Content.ReadFromJsonAsync<FuelResponse>())!;
        var replay = await client.PostAsJsonAsync(url + "/fuel-records", fuelRequest);
        Assert.Equal(fuelResult.Id, (await replay.Content.ReadFromJsonAsync<FuelResponse>())!.Id);
        var serviceRequest = new ServiceWriteRequest(Guid.NewGuid(), new(2026, 11, 3), 10_200, "", [new("Repair", "Sửa xe", 100_000, null), new("Maintenance", "Thay dầu", 150_000, null)], null);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(url + "/service-records", serviceRequest)).StatusCode);
        Assert.Equal(200_000, (await client.GetFromJsonAsync<FuelResponse[]>(url + "/fuel-records"))!.Single().Cost);
        Assert.Equal(250_000, (await client.GetFromJsonAsync<ServiceResponse[]>(url + "/service-records"))!.Single().Cost);
        Assert.Equal(10_200, (await client.GetFromJsonAsync<VehicleResponse>(url))!.CurrentOdo);
        Assert.Empty((await client.GetFromJsonAsync<OdometerReadingResponse[]>(url + "/odometer-readings"))!);
        Guid sourceId;
        await using (var db = Context()) sourceId = (await db.OdometerReadings.SingleAsync(x => x.FuelEntryId == fuelResult.Id)).Id;
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(url + $"/odometer-readings/{sourceId}", new { date = "2026-11-02", odo = 10101, note = "", version = 0 })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync(url + $"/odometer-readings/{sourceId}?version=0")).StatusCode);
    }
}
