using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using iCareX.Application.Abstractions;
using iCareX.Application.Features.Authorization.Contracts;
using iCareX.Application.Features.Identity.Contracts;
using iCareX.Application.Features.Vehicles.Contracts;
using iCareX.Domain.Entities;
using iCareX.Infrastructure.Persistence;
using iCareX.Shared.Contracts.Vehicles;
using Xunit;

namespace iCareX.IntegrationTests;

public sealed partial class DatabaseTests
{
    private sealed class InvitationClock : IBusinessClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public DateOnly Today => DateOnly.FromDateTime(UtcNow.UtcDateTime);
    }

    [Fact]
    public async Task ResendRotatesOnlyThisTokenEnforcesCooldownAndRollsBackOnMailFailure()
    {
        var mail = new CapturedEmail();
        var sender = new RecoverableEmail(mail);
        var clock = new InvitationClock();
        using var baseHost = SecurityHost(mail);
        using var host = baseHost.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IBusinessClock>(); s.AddSingleton<IBusinessClock>(clock);
            s.RemoveAll<IAccountEmailSender>(); s.AddSingleton<IAccountEmailSender>(sender);
        }));
        var owner = Guid.NewGuid();
        var model = TestVehicleModel();
        var vehicle = new Vehicle { AccountId = owner, CreatedByUserId = owner, Name = "Car A", VehicleModelId = model.Id,
            VehicleBrandId = model.VehicleBrandId, VehicleType = iCareX.Domain.Enums.VehicleType.Car, FuelType = iCareX.Domain.Enums.FuelType.Gasoline };
        await using (var db = Context())
        {
            db.Users.Add(new ApplicationUser { Id = owner, UserName = "owner", Email = "owner@example.test", NormalizedEmail = "OWNER@EXAMPLE.TEST", EmailConfirmed = true });
            db.Accounts.Add(new Account { Id = owner, Name = "Owner" });
            db.VehicleModels.Add(model); db.Vehicles.Add(vehicle);
            db.VehicleMembers.Add(new VehicleMember { VehicleId = vehicle.Id, UserId = owner, Role = VehicleMemberRole.Owner });
            await db.SaveChangesAsync();
        }
        async Task<T> Call<T>(Func<IVehicleInvitationService, Task<T>> action)
        {
            await using var scope = host.Services.CreateAsyncScope();
            return await action(scope.ServiceProvider.GetRequiredService<IVehicleInvitationService>());
        }
        var invitation = await Call(s => s.CreateAsync(owner, vehicle.Id, new("wife@example.test", "Manager"), default));
        var oldToken = mail.Token("wife@example.test");
        await Assert.ThrowsAsync<VehicleConflictException>(() => Call(s => s.ResendAsync(owner, vehicle.Id, invitation.Id, invitation.Version, default)));
        clock.UtcNow = clock.UtcNow.AddSeconds(61);
        var resent = await Call(s => s.ResendAsync(owner, vehicle.Id, invitation.Id, invitation.Version, default));
        var newToken = mail.Token("wife@example.test");
        Assert.NotEqual(oldToken, newToken);
        Assert.Equal(invitation.ExpiresAtUtc, resent.ExpiresAtUtc);
        Assert.Equal(invitation.Role, resent.Role);
        Assert.Equal(invitation.Version + 1, resent.Version);
        await Assert.ThrowsAsync<VehicleNotFoundException>(() => Call(s => s.PreviewAsync(invitation.Id, oldToken, default)));
        Assert.Equal(invitation.Id, (await Call(s => s.PreviewAsync(invitation.Id, newToken, default))).Id);
        await Assert.ThrowsAsync<VehicleNotFoundException>(() => Call(s => s.ResendAsync(owner, Guid.NewGuid(), invitation.Id, resent.Version, default)));
        clock.UtcNow = clock.UtcNow.AddSeconds(61);
        sender.Failure = "transport";
        await Assert.ThrowsAsync<EmailDeliveryUnavailableException>(() => Call(s => s.ResendAsync(owner, vehicle.Id, invitation.Id, resent.Version, default)));
        Assert.Equal(resent.Version, (await Call(s => s.PreviewAsync(invitation.Id, newToken, default))).Version);
        await using (var db = Context())
        {
            Assert.Equal(1, await db.VehicleInvitations.CountAsync());
            Assert.Equal(1, await db.AuditLogs.CountAsync(a => a.Action == "vehicle.invitation.resent"));
        }
    }
}
