using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using iCareX.Application.Features.Authorization.Contracts;
using iCareX.Application.Features.Vehicles.Contracts;
using iCareX.Domain.Authorization;
using iCareX.Domain.Entities;
using iCareX.Shared.Contracts.Identity;
using iCareX.Shared.Contracts.Vehicles;
using iCareX.Web.Api.Authorization;
using Xunit;

namespace iCareX.IntegrationTests;

// HTTP middleware/controller contract checks with a server-side access double.
// Persistence, cookie sessions and concurrency are covered separately by PostgreSQL tests.
[Trait("Category", "Host")]
public sealed class VehicleAuthorizationHostTests
{
    [Fact]
    public async Task CostMutationsRequireCsrfAndRejectForgedCreatorFields()
    {
        using var host = Host(VehicleMemberRole.Owner);
        using var client = host.CreateClient();
        var vehicleId = Guid.NewGuid();
        foreach (var kind in new[] { "fuel", "service" })
        {
            var url = $"/api/v1/vehicles/{vehicleId}/{kind}-records";
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, new { })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync(url + $"/{Guid.NewGuid()}?version=0")).StatusCode);
        }
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/vehicles/{vehicleId}/fuel-records", new
        {
            requestId = Guid.NewGuid(), date = "2026-11-02", odo = 10100, cost = 200000, liters = 10, note = "", createdByUserId = Guid.NewGuid()
        })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/v1/vehicles/{vehicleId}/service-records", new
        {
            requestId = Guid.NewGuid(), date = "2026-11-02", odo = 10100, note = "",
            items = new[] { new { type = "Repair", title = "Sửa xe", cost = 100000, ownerId = Guid.NewGuid() } }
        })).StatusCode);
    }

    [Theory]
    [InlineData(VehicleMemberRole.Owner, 200)]
    [InlineData(VehicleMemberRole.Manager, 200)]
    [InlineData(VehicleMemberRole.Contributor, 403)]
    [InlineData(VehicleMemberRole.Viewer, 403)]
    [InlineData(null, 404)]
    public async Task MembershipListUsesCurrentVehicleRole(VehicleMemberRole? role, int expected)
    {
        using var host = Host(role);
        using var client = host.CreateClient();
        var response = await client.GetAsync($"/api/v1/vehicles/{Guid.NewGuid()}/members");
        Assert.Equal(expected, (int)response.StatusCode);
    }

    [Fact]
    public async Task OnlyOwnerCanChangeRolesAndCsrfAndStrictDtoAreRequired()
    {
        var url = $"/api/v1/vehicles/{Guid.NewGuid()}/members/{Guid.NewGuid()}";
        using var host = Host(VehicleMemberRole.Owner);
        using var owner = host.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync(url, new { role = "Manager", version = 0 })).StatusCode);
        var csrf = await owner.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        owner.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PatchAsJsonAsync(url, new { role = "Manager", version = 0, userId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PatchAsJsonAsync(url, new { role = "Manager", version = 0 })).StatusCode);
        using var managerHost = Host(VehicleMemberRole.Manager);
        using var manager = managerHost.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PatchAsJsonAsync(url, new { role = "Owner", version = 0 })).StatusCode);
    }

    [Fact]
    public async Task ResendInvitationRequiresOwnerCsrfAndVersionOnlyDto()
    {
        var url = $"/api/v1/vehicles/{Guid.NewGuid()}/invitations/{Guid.NewGuid()}/resend";
        using var ownerHost = Host(VehicleMemberRole.Owner);
        using var owner = ownerHost.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(url, new { version = 0 })).StatusCode);
        var csrf = await owner.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        owner.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(url, new { version = 0, role = "Owner", email = "other@example.test" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await owner.PostAsJsonAsync(url, new { version = 0 })).StatusCode);
        using var managerHost = Host(VehicleMemberRole.Manager);
        using var manager = managerHost.CreateClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await manager.PostAsJsonAsync(url, new { version = 0 })).StatusCode);
    }

    [Theory]
    [InlineData(VehicleMemberRole.Owner, 201, 200)]
    [InlineData(VehicleMemberRole.Manager, 201, 200)]
    [InlineData(VehicleMemberRole.Contributor, 201, 403)]
    [InlineData(VehicleMemberRole.Viewer, 403, 403)]
    [InlineData(null, 404, 404)]
    public async Task VehicleEditAndOdoCreateUseSeparatePolicies(VehicleMemberRole? role, int createStatus, int editStatus)
    {
        using var host = Host(role);
        using var client = host.CreateClient();
        var csrf = await client.GetFromJsonAsync<CsrfResponse>("/api/v1/auth/csrf");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", csrf!.Token);
        var url = $"/api/v1/vehicles/{Guid.NewGuid()}";
        Assert.Equal(createStatus, (int)(await client.PostAsJsonAsync(url + "/odometer-readings", new { date = "2026-01-01", odo = 100, note = "" })).StatusCode);
        Assert.Equal(editStatus, (int)(await client.PutAsJsonAsync(url, new { name = "Edited", vehicleType = "Car", vehicleBrandId = Guid.NewGuid(), vehicleModelId = Guid.NewGuid(), fuelType = "Gasoline", odometerUnit = "Kilometres", odometer = 0, version = 0 })).StatusCode);
        if (role is VehicleMemberRole.Owner or VehicleMemberRole.Manager or VehicleMemberRole.Contributor)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url + "/odometer-readings", new { date = "2026-01-01", odo = 100, createdByUserId = Guid.NewGuid() })).StatusCode);
        }
        if (role is VehicleMemberRole.Manager or VehicleMemberRole.Contributor or VehicleMemberRole.Viewer)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(url + $"/odometer-readings/{Guid.NewGuid()}?version=0")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(url + $"/odometer-readings/{Guid.NewGuid()}", new { date = "2026-01-01", odo = 100, version = 0 })).StatusCode);
        }
    }

    private static WebApplicationFactory<Program> Host(VehicleMemberRole? role) => new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
    {
        b.UseEnvironment("Development");
        b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:iCareX"] = "Host=127.0.0.1;Port=1;Database=unavailable;Username=test;Password=test;Timeout=1" }));
        b.ConfigureServices(services =>
        {
            services.AddSingleton<IDataProtectionProvider>(new EphemeralDataProtectionProvider());
            services.AddAuthentication(o => { o.DefaultAuthenticateScheme = "Test"; o.DefaultChallengeScheme = "Test"; o.DefaultForbidScheme = "Test"; })
                .AddScheme<AuthenticationSchemeOptions, TestAuthentication>("Test", _ => { });
            services.RemoveAll<IVehicleAccessService>();
            services.AddSingleton<IVehicleAccessService>(new AccessDouble(role));
            services.RemoveAll<IVehicleMemberService>();
            services.AddSingleton<IVehicleMemberService, MembersDouble>();
            services.RemoveAll<IVehicleService>();
            services.AddSingleton<IVehicleService, VehiclesDouble>();
            services.RemoveAll<IVehicleInvitationService>();
            services.AddSingleton<IVehicleInvitationService, InvitationDouble>();
            services.Remove(services.Single(x => x.ServiceType == typeof(IAuthorizationHandler)
                && x.ImplementationType == typeof(VerifiedAccountHandler)));
            services.AddSingleton<IAuthorizationHandler, VerifiedDouble>();
        });
    });

    private sealed class VehiclesDouble : IVehicleService
    {
        public Task<OdometerReadingResponse> CreateReadingAsync(Guid actor, Guid vehicleId, CreateOdometerReadingRequest request, CancellationToken ct) =>
            Task.FromResult(new OdometerReadingResponse(Guid.NewGuid(), vehicleId, request.Date, request.Odo, "ODO", request.Note ?? "", 0));
        public Task<VehicleResponse> UpdateAsync(Guid actor, Guid vehicleId, UpdateVehicleRequest request, CancellationToken ct) =>
            Task.FromResult(new VehicleResponse(vehicleId, request.Name, request.Plate, request.VehicleType, "Ô tô", request.VehicleBrandId, "Brand", request.VehicleModelId, "Model",
                request.Year, request.Odometer, request.Odometer, request.FuelType, request.FuelCapacity, request.OdometerUnit, request.Vin, request.RegistrationId,
                request.PurchasePrice, request.SalePrice, request.Notes, request.ImageUrl, false, request.Version + 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
        public Task<IReadOnlyCollection<VehicleResponse>> ListAsync(Guid actor, bool archived, CancellationToken ct) => throw new NotSupportedException();
        public Task<VehicleResponse?> GetAsync(Guid actor, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<VehicleResponse> CreateAsync(Guid actor, CreateVehicleRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task<VehicleResponse> ArchiveAsync(Guid actor, Guid id, long version, CancellationToken ct) => throw new NotSupportedException();
        public Task<VehicleResponse> RestoreAsync(Guid actor, Guid id, long version, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<OdometerReadingResponse>?> ListReadingsAsync(Guid actor, Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<OdometerReadingResponse> UpdateReadingAsync(Guid actor, Guid id, Guid readingId, UpdateOdometerReadingRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteReadingAsync(Guid actor, Guid id, Guid readingId, long version, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class InvitationDouble : IVehicleInvitationService
    {
        public Task<VehicleInvitationResponse> ResendAsync(Guid actor, Guid vehicleId, Guid invitationId, long version, CancellationToken ct) =>
            Task.FromResult(new VehicleInvitationResponse(invitationId, vehicleId, "Car A", "Manager", "Pending", DateTimeOffset.UtcNow.AddDays(7), version + 1,
                "wife@example.test", "Owner", "51G-12345", DateTimeOffset.UtcNow.AddSeconds(60)));
        public Task<IReadOnlyCollection<VehicleInvitationResponse>> ListAsync(Guid actor, Guid vehicleId, CancellationToken ct) => throw new NotSupportedException();
        public Task<VehicleInvitationResponse> CreateAsync(Guid actor, Guid vehicleId, CreateVehicleInvitationRequest request, CancellationToken ct) => throw new NotSupportedException();
        public Task RevokeAsync(Guid actor, Guid vehicleId, Guid invitationId, long version, CancellationToken ct) => throw new NotSupportedException();
        public Task<VehicleInvitationResponse> PreviewAsync(Guid invitationId, string token, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyCollection<VehicleInvitationResponse>> InboxAsync(Guid actor, CancellationToken ct) => throw new NotSupportedException();
        public Task RespondAsync(Guid actor, Guid invitationId, string token, bool accept, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class TestAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, "b192b10a-4005-40ef-b650-4dcfcb503172"),
                new Claim(ClaimTypes.Role, "SystemAdmin")], "Test")), "Test")));
    }

    private sealed class VerifiedDouble : AuthorizationHandler<VerifiedAccountRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, VerifiedAccountRequirement requirement)
        { context.Succeed(requirement); return Task.CompletedTask; }
    }

    private sealed class AccessDouble(VehicleMemberRole? role) : IVehicleAccessService
    {
        public Task<VehicleMembership> RequireAsync(Guid userId, Guid vehicleId, string permission, CancellationToken ct)
        {
            if (role is null) throw new VehicleNotFoundException();
            if (!VehiclePermissionRules.Allows(role.Value, permission, userId)) throw new VehicleAccessDeniedException();
            return Task.FromResult(new VehicleMembership(vehicleId, userId, userId, role.Value, false));
        }
        public Task<bool> CanAccessResourceAsync(Guid userId, VehicleResourceReference resource, string permission, CancellationToken ct) => throw new NotSupportedException();
        public Task<VehiclePermissionsResponse> GetPermissionsAsync(Guid userId, Guid vehicleId, CancellationToken ct) => throw new NotSupportedException();
        public Task<CurrentUserPermissionsResponse> GetCurrentPermissionsAsync(Guid userId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class MembersDouble : IVehicleMemberService
    {
        public Task<IReadOnlyCollection<VehicleMemberResponse>> ListAsync(Guid actorId, Guid vehicleId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyCollection<VehicleMemberResponse>>([]);
        public Task<VehicleMemberResponse> UpdateAsync(Guid actorId, Guid vehicleId, Guid userId, UpdateVehicleMemberRequest request, CancellationToken ct) =>
            Task.FromResult(new VehicleMemberResponse(userId, "Member", request.Role, DateTimeOffset.UtcNow, request.Version + 1));
        public Task RevokeAsync(Guid actorId, Guid vehicleId, Guid userId, long version, CancellationToken ct) => Task.CompletedTask;
    }
}
