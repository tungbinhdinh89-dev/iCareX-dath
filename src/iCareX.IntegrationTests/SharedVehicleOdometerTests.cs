using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using iCareX.Domain.Entities;
using iCareX.Shared.Contracts.Identity;
using iCareX.Shared.Contracts.Vehicles;
using Xunit;

namespace iCareX.IntegrationTests;

public sealed partial class DatabaseTests
{
    [Fact]
    public async Task SharedVehicleManagerEditsAndContributorAddsOdoWithoutChangingOwnerOrCreator()
    {
        using var host = SecurityHost(new CapturedEmail());
        var clients = new List<HttpClient>();
        async Task<(HttpClient Client, Guid Id)> Actor(string name)
        {
            var client = await SecurityClient(host); clients.Add(client);
            var email = $"{Guid.NewGuid():N}@example.test";
            var created = await client.PostAsJsonAsync("/api/v1/auth/register", new { name, email, password = "TestPassword@123", phone = "" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var account = (await created.Content.ReadFromJsonAsync<RegistrationResponse>())!;
            await using (var db = Context()) await db.Users.Where(u => u.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmed, true));
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "TestPassword@123" })).StatusCode);
            return (client, account.Id);
        }
        try
        {
            var (owner, ownerId) = await Actor("Owner");
            var (manager, managerId) = await Actor("Manager");
            var (contributor, contributorId) = await Actor("Contributor");
            var (viewer, viewerId) = await Actor("Viewer");
            var (outsider, _) = await Actor("Outsider");
            var model = TestVehicleModel();
            await using (var db = Context()) { db.VehicleModels.Add(model); await db.SaveChangesAsync(); }
            object Body(string name, long version = 0) => new { name, plate = "51A-12345", vehicleType = "Car", vehicleBrandId = model.VehicleBrandId,
                vehicleModelId = model.Id, fuelType = "Gasoline", odometerUnit = "Kilometres", odometer = 0, version };
            object CreateBody() => new { name = "Shared vehicle", plate = "51A-12345", vehicleType = "Car", vehicleBrandId = model.VehicleBrandId,
                vehicleModelId = model.Id, fuelType = "Gasoline", odometerUnit = "Kilometres", odometer = 0 };
            var created = await owner.PostAsJsonAsync("/api/v1/vehicles", CreateBody());
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var vehicle = (await created.Content.ReadFromJsonAsync<VehicleResponse>())!;
            // Same plate in another owner's garage must not block editing this shared vehicle.
            Assert.Equal(HttpStatusCode.Created, (await manager.PostAsJsonAsync("/api/v1/vehicles", CreateBody())).StatusCode);
            await using (var db = Context())
            {
                foreach (var (id, role) in new[] { (managerId, VehicleMemberRole.Manager), (contributorId, VehicleMemberRole.Contributor), (viewerId, VehicleMemberRole.Viewer) })
                    db.VehicleMembers.Add(new VehicleMember { VehicleId = vehicle.Id, UserId = id, Role = role });
                await db.SaveChangesAsync();
            }
            var url = $"/api/v1/vehicles/{vehicle.Id}";
            var edited = await manager.PutAsJsonAsync(url, Body("Manager updated vehicle", vehicle.Version));
            Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
            var updated = (await edited.Content.ReadFromJsonAsync<VehicleResponse>())!;
            Assert.Equal("Manager updated vehicle", updated.Name);
            Assert.Equal(HttpStatusCode.Forbidden, (await contributor.PutAsJsonAsync(url, Body("Forbidden", updated.Version))).StatusCode);
            var readingUrl = url + "/odometer-readings";
            var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
            foreach (var (client, value) in new[] { (owner, 100), (manager, 200), (contributor, 300) })
                Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync(readingUrl, new { date, odo = value, note = "Shared ODO" })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync(readingUrl, new { date, odo = 400 })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await outsider.PostAsJsonAsync(readingUrl, new { date, odo = 400 })).StatusCode);
            await using (var db = Context())
            {
                var readings = await db.OdometerReadings.Where(r => r.VehicleId == vehicle.Id).ToArrayAsync();
                Assert.Equal(3, readings.Length);
                Assert.All(readings, r => Assert.Equal(ownerId, r.AccountId));
                Assert.Equal(managerId, readings.Single(r => r.Odo == 200).CreatedByUserId);
                Assert.Equal(contributorId, readings.Single(r => r.Odo == 300).CreatedByUserId);
                var car = await db.Vehicles.SingleAsync(v => v.Id == vehicle.Id);
                Assert.Equal(ownerId, car.AccountId); Assert.Equal(ownerId, car.CreatedByUserId);
                var row = readings.Single(r => r.Odo == 300);
                Assert.Equal(HttpStatusCode.Forbidden, (await contributor.DeleteAsync(readingUrl + $"/{row.Id}?version={row.Version}")).StatusCode);
            }
            var permissions = (await manager.GetFromJsonAsync<CurrentUserPermissionsResponse>("/api/v1/me/permissions"))!;
            var grant = permissions.Vehicles.Single(v => v.VehicleId == vehicle.Id);
            Assert.Contains(grant.Permissions, p => p.Code == "odometer.create");
            Assert.Contains(grant.Permissions, p => p.Code == "vehicle.update");
            Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(url + "/archive", new { version = updated.Version })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(readingUrl, new { date, odo = 400 })).StatusCode);
        }
        finally { foreach (var client in clients) client.Dispose(); }
    }
}
