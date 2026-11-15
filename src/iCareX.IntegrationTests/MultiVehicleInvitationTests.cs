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
    public async Task SameEmailReceivesIndependentRolesFromSameOwnerForThreeVehicles()
    {
        var mail = new CapturedEmail();
        using var host = SecurityHost(mail);
        async Task<(HttpClient Client, RegistrationResponse Account)> Actor(string name)
        {
            var client = await SecurityClient(host);
            var email = $"{Guid.NewGuid():N}@example.test";
            var created = await client.PostAsJsonAsync("/api/v1/auth/register", new { name, email, password = "TestPassword@123", phone = "" });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var account = (await created.Content.ReadFromJsonAsync<RegistrationResponse>())!;
            await using (var db = Context()) await db.Users.Where(u => u.Id == account.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.EmailConfirmed, true));
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = "TestPassword@123" })).StatusCode);
            return (client, account);
        }
        var (owner, husband) = await Actor("Husband");
        var (recipient, wife) = await Actor("Wife");
        using (owner) using (recipient)
        {
            var model = TestVehicleModel();
            await using (var db = Context()) { db.VehicleModels.Add(model); await db.SaveChangesAsync(); }
            var invitations = new List<(VehicleInvitationResponse Invite, string Token)>();
            foreach (var (name, role) in new[] { ("Car A", "Manager"), ("Car B", "Contributor"), ("Car C", "Viewer") })
            {
                var created = await owner.PostAsJsonAsync("/api/v1/vehicles", new { name, vehicleType = "Car", vehicleBrandId = model.VehicleBrandId, vehicleModelId = model.Id, fuelType = "Gasoline", odometerUnit = "Kilometres", odometer = 0 });
                Assert.Equal(HttpStatusCode.Created, created.StatusCode);
                var vehicle = (await created.Content.ReadFromJsonAsync<VehicleResponse>())!;
                var response = await owner.PostAsJsonAsync($"/api/v1/vehicles/{vehicle.Id}/invitations", new { email = wife.Email, role });
                Assert.Equal(HttpStatusCode.Created, response.StatusCode);
                var invitation = (await response.Content.ReadFromJsonAsync<VehicleInvitationResponse>())!;
                Assert.Equal(role, invitation.Role);
                Assert.Equal("Husband", invitation.InvitedByName);
                Assert.Equal(wife.Email, invitation.InvitedEmail);
                invitations.Add((invitation, mail.Token(wife.Email)));
            }
            var first = invitations[0];
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/v1/vehicles/{first.Invite.VehicleId}/invitations", new { email = $" {wife.Email.ToUpperInvariant()} ", role = "Viewer" })).StatusCode);
            var inbox = (await recipient.GetFromJsonAsync<VehicleInvitationResponse[]>("/api/v1/invitations/inbox"))!;
            Assert.Equal(3, inbox.Length);
            Assert.Equal(3, inbox.Select(i => i.VehicleId).Distinct().Count());
            // A token cannot authorize a different vehicle's invitation.
            Assert.Equal(HttpStatusCode.NotFound, (await recipient.PostAsJsonAsync($"/api/v1/invitations/{invitations[1].Invite.Id}/accept", new { token = first.Token })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await recipient.PostAsJsonAsync($"/api/v1/invitations/{first.Invite.Id}/accept", new { token = first.Token })).StatusCode);
            await using (var db = Context())
            {
                var member = await db.VehicleMembers.SingleAsync(m => m.UserId == wife.Id && m.RevokedAtUtc == null);
                Assert.Equal(first.Invite.VehicleId, member.VehicleId);
                Assert.Equal(VehicleMemberRole.Manager, member.Role);
                Assert.Equal(2, await db.VehicleInvitations.CountAsync(i => i.NormalizedInvitedEmail == wife.Email.ToUpperInvariant() && i.Status == VehicleInvitationStatus.Pending));
            }
            Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/v1/vehicles/{first.Invite.VehicleId}/invitations", new { email = wife.Email, role = "Viewer" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await recipient.PostAsJsonAsync($"/api/v1/invitations/{invitations[1].Invite.Id}/accept", new { token = invitations[1].Token })).StatusCode);
            var simultaneousAccepts = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ =>
                recipient.PostAsJsonAsync($"/api/v1/invitations/{invitations[2].Invite.Id}/accept", new { token = invitations[2].Token })));
            Assert.Single(simultaneousAccepts, r => r.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(simultaneousAccepts, r => r.StatusCode == HttpStatusCode.Conflict);
            await using (var db = Context())
            {
                var members = await db.VehicleMembers.Where(m => m.UserId == wife.Id && m.RevokedAtUtc == null).ToArrayAsync();
                Assert.Equal(3, members.Length);
                foreach (var entry in invitations)
                    Assert.Equal(entry.Invite.Role, members.Single(m => m.VehicleId == entry.Invite.VehicleId).Role.ToString());
            }
        }
    }
}
