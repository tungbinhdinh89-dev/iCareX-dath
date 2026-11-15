using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using iCareX.Application.Abstractions;
using iCareX.Application.Features.Authorization.Contracts;
using iCareX.Application.Features.Identity.Contracts;
using iCareX.Application.Features.Vehicles.Contracts;
using iCareX.Domain.Authorization;
using iCareX.Domain.Common;
using iCareX.Domain.Entities;
using iCareX.Infrastructure.Persistence;
using iCareX.Shared.Contracts.Vehicles;

namespace iCareX.Infrastructure.Services.Implementations;

public sealed class VehicleInvitationService(GarageDbContext db, IVehicleAccessService access,
    UserManager<ApplicationUser> users, IAccountEmailSender email, IOptions<AccountEmailOptions> settings,
    IBusinessClock clock) : IVehicleInvitationService
{
    public async Task<IReadOnlyCollection<VehicleInvitationResponse>> ListAsync(Guid actor, Guid vehicleId, CancellationToken ct)
    {
        var membership = await access.RequireAsync(actor, vehicleId, VehiclePermissions.VehicleRead, ct);
        if (membership.Role != VehicleMemberRole.Owner) throw new VehicleAccessDeniedException();
        return await ProjectAsync(db.VehicleInvitations.AsNoTracking().Where(x => x.VehicleId == vehicleId), ct);
    }

    public async Task<VehicleInvitationResponse> CreateAsync(Guid actor, Guid vehicleId, CreateVehicleInvitationRequest request, CancellationToken ct)
    {
        await using var transaction = await VehicleWriteLock.BeginAsync(db, vehicleId, ct);
        await access.RequireAsync(actor, vehicleId, VehiclePermissions.MemberInvite, ct);
        email.EnsureConfigured();
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 320
            || !MailAddress.TryCreate(request.Email.Trim(), out var address)
            || !string.Equals(address.Address, request.Email.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new VehicleValidationException(["Email không hợp lệ."]);
        if (!Enum.TryParse<VehicleMemberRole>(request.Role, false, out var role) || !Enum.IsDefined(role)
            || role == VehicleMemberRole.Owner || role.ToString() != request.Role)
            throw new VehicleValidationException(["Chỉ có thể mời Manager, Contributor hoặc Viewer."]);
        var normalized = users.NormalizeEmail(address.Address);
        if (await (from member in db.VehicleMembers join user in db.Users on member.UserId equals user.Id
                   where member.VehicleId == vehicleId && member.RevokedAtUtc == null && user.NormalizedEmail == normalized
                   select member.Id).AnyAsync(ct)) throw new VehicleConflictException("Người dùng đã là thành viên của xe.");
        // Expire previous pending rows before inserting a new one under the same vehicle lock.
        await db.VehicleInvitations.Where(x => x.VehicleId == vehicleId && x.NormalizedInvitedEmail == normalized
                && x.Status == VehicleInvitationStatus.Pending && x.ExpiresAtUtc <= clock.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, VehicleInvitationStatus.Expired).SetProperty(x => x.Version, x => x.Version + 1), ct);
        if (await db.VehicleInvitations.AnyAsync(x => x.VehicleId == vehicleId && x.NormalizedInvitedEmail == normalized
            && x.Status == VehicleInvitationStatus.Pending, ct)) throw new VehicleConflictException("Đã có lời mời đang chờ cho email này.");
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var invitation = new VehicleInvitation
        {
            VehicleId = vehicleId, InvitedByUserId = actor, InvitedEmail = address.Address,
            NormalizedInvitedEmail = normalized!, TokenHash = Hash(token), Role = role,
            ExpiresAtUtc = clock.UtcNow.AddDays(7), CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow
        };
        db.VehicleInvitations.Add(invitation);
        Audit(actor, invitation, "vehicle.invitation.created");
        await db.SaveChangesAsync(ct);
        var response = (await ProjectAsync(db.VehicleInvitations.Where(x => x.Id == invitation.Id), ct)).Single();
        await SendAsync(invitation, response, token, ct);
        await transaction.CommitAsync(ct);
        return response;
    }

    public async Task<VehicleInvitationResponse> ResendAsync(Guid actor, Guid vehicleId, Guid invitationId, long version, CancellationToken ct)
    {
        await using var transaction = await VehicleWriteLock.BeginAsync(db, vehicleId, ct);
        await access.RequireAsync(actor, vehicleId, VehiclePermissions.MemberInvite, ct);
        var invitation = await db.VehicleInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.VehicleId == vehicleId, ct)
            ?? throw new VehicleNotFoundException();
        if (version < 0 || invitation.Version != version) throw new VehicleConflictException("Lời mời đã thay đổi. Vui lòng tải lại.");
        EnsurePending(invitation);
        await EnsureSenderOwner(invitation, ct);
        if (clock.UtcNow < VehicleInvitationRules.ResendAvailableAt(invitation, clock.UtcNow))
            throw new VehicleConflictException("Vui lòng chờ ít nhất 60 giây giữa các lần gửi lời mời.");
        if (await (from member in db.VehicleMembers join user in db.Users on member.UserId equals user.Id
                   where member.VehicleId == vehicleId && member.RevokedAtUtc == null && user.NormalizedEmail == invitation.NormalizedInvitedEmail
                   select member.Id).AnyAsync(ct)) throw new VehicleConflictException("Người dùng đã là thành viên của xe.");
        email.EnsureConfigured();
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        invitation.TokenHash = Hash(token);
        invitation.UpdatedAtUtc = clock.UtcNow;
        invitation.Version++;
        // Keep the original expiry and role; resend never extends access or changes its scope.
        Audit(actor, invitation, "vehicle.invitation.resent");
        await db.SaveChangesAsync(ct);
        var response = (await ProjectAsync(db.VehicleInvitations.Where(x => x.Id == invitationId), ct)).Single();
        await SendAsync(invitation, response, token, ct);
        await transaction.CommitAsync(ct);
        return response;
    }

    public async Task RevokeAsync(Guid actor, Guid vehicleId, Guid invitationId, long version, CancellationToken ct)
    {
        await using var transaction = await VehicleWriteLock.BeginAsync(db, vehicleId, ct);
        await access.RequireAsync(actor, vehicleId, VehiclePermissions.MemberRemove, ct);
        var invitation = await db.VehicleInvitations.SingleOrDefaultAsync(x => x.Id == invitationId && x.VehicleId == vehicleId, ct)
            ?? throw new VehicleNotFoundException();
        if (invitation.Version != version || version < 0) throw new VehicleConflictException("Lời mời đã thay đổi. Vui lòng tải lại.");
        EnsurePending(invitation);
        invitation.Status = VehicleInvitationStatus.Revoked;
        invitation.Version++;
        invitation.UpdatedAtUtc = clock.UtcNow;
        Audit(actor, invitation, "vehicle.invitation.revoked");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task<VehicleInvitationResponse> PreviewAsync(Guid invitationId, string token, CancellationToken ct)
    {
        var invitation = await db.VehicleInvitations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == invitationId, ct)
            ?? throw new VehicleNotFoundException();
        ValidateToken(invitation, token);
        EnsurePending(invitation);
        var vehicle = await db.Vehicles.AsNoTracking().SingleAsync(x => x.Id == invitation.VehicleId, ct);
        if (vehicle.ArchivedAtUtc != null) throw new VehicleConflictException("Xe đang được lưu trữ.");
        await EnsureSenderOwner(invitation, ct);
        return (await ProjectAsync(db.VehicleInvitations.AsNoTracking().Where(x => x.Id == invitationId), ct)).Single();
    }

    public async Task<IReadOnlyCollection<VehicleInvitationResponse>> InboxAsync(Guid actor, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(actor.ToString()) ?? throw new VehicleAccessDeniedException();
        if (!user.EmailConfirmed) throw new VehicleAccessDeniedException();
        return await ProjectAsync(db.VehicleInvitations.AsNoTracking().Where(invitation =>
            invitation.NormalizedInvitedEmail == user.NormalizedEmail && invitation.Status == VehicleInvitationStatus.Pending
            && invitation.ExpiresAtUtc > clock.UtcNow
            && db.Vehicles.Any(v => v.Id == invitation.VehicleId && v.ArchivedAtUtc == null)
            && db.VehicleMembers.Any(m => m.VehicleId == invitation.VehicleId && m.UserId == invitation.InvitedByUserId
                && m.Role == VehicleMemberRole.Owner && m.RevokedAtUtc == null)), ct);
    }

    public async Task RespondAsync(Guid actor, Guid invitationId, string token, bool accept, CancellationToken ct)
    {
        var vehicleId = await db.VehicleInvitations.AsNoTracking().Where(x => x.Id == invitationId)
            .Select(x => (Guid?)x.VehicleId).SingleOrDefaultAsync(ct) ?? throw new VehicleNotFoundException();
        await using var transaction = await VehicleWriteLock.BeginAsync(db, vehicleId, ct);
        var invitation = await db.VehicleInvitations.SingleAsync(x => x.Id == invitationId, ct);
        ValidateToken(invitation, token);
        var user = await users.FindByIdAsync(actor.ToString()) ?? throw new VehicleAccessDeniedException();
        if (!user.EmailConfirmed || user.NormalizedEmail != invitation.NormalizedInvitedEmail
            || await users.IsLockedOutAsync(user)) throw new VehicleAccessDeniedException();
        EnsurePending(invitation);
        if (await db.Vehicles.AnyAsync(x => x.Id == vehicleId && x.ArchivedAtUtc != null, ct))
            throw new VehicleConflictException("Xe đang được lưu trữ.");
        if (accept)
        {
            if (await db.VehicleMembers.AnyAsync(x => x.VehicleId == vehicleId && x.UserId == actor && x.RevokedAtUtc == null, ct))
                throw new VehicleConflictException("Bạn đã là thành viên của xe.");
            // Sender must still be the active Owner at acceptance time.
            await EnsureSenderOwner(invitation, ct);
            db.VehicleMembers.Add(new VehicleMember
            { VehicleId = vehicleId, UserId = actor, Role = invitation.Role, JoinedAtUtc = clock.UtcNow, CreatedAtUtc = clock.UtcNow });
            invitation.AcceptedByUserId = actor;
            invitation.AcceptedAtUtc = clock.UtcNow;
        }
        invitation.Status = accept ? VehicleInvitationStatus.Accepted : VehicleInvitationStatus.Rejected;
        invitation.Version++;
        invitation.UpdatedAtUtc = clock.UtcNow;
        Audit(actor, invitation, accept ? "vehicle.invitation.accepted" : "vehicle.invitation.rejected");
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    private void EnsurePending(VehicleInvitation invitation)
    {
        if (invitation.ExpiresAtUtc <= clock.UtcNow || invitation.Status == VehicleInvitationStatus.Expired) throw new InvitationExpiredException();
        if (invitation.Status != VehicleInvitationStatus.Pending) throw new VehicleConflictException("Lời mời đã được xử lý.");
    }
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static void ValidateToken(VehicleInvitation invitation, string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 200 || !CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(invitation.TokenHash), Encoding.ASCII.GetBytes(Hash(token)))) throw new VehicleNotFoundException();
    }
    private async Task EnsureSenderOwner(VehicleInvitation invitation, CancellationToken ct)
    {
        if (!await db.VehicleMembers.AnyAsync(m => m.VehicleId == invitation.VehicleId && m.UserId == invitation.InvitedByUserId
            && m.Role == VehicleMemberRole.Owner && m.RevokedAtUtc == null, ct)) throw new VehicleConflictException("Lời mời không còn hiệu lực.");
    }

    private async Task<VehicleInvitationResponse[]> ProjectAsync(IQueryable<VehicleInvitation> query, CancellationToken ct)
    {
        var rows = await (from invitation in query
                          join vehicle in db.Vehicles on invitation.VehicleId equals vehicle.Id
                          join sender in db.Accounts on invitation.InvitedByUserId equals sender.Id
                          orderby invitation.CreatedAtUtc descending, invitation.Id
                          select new { Invitation = invitation, Name = vehicle.Name ?? vehicle.VehicleModel!.Name,
                              vehicle.Plate, SenderName = sender.Name }).ToArrayAsync(ct);
        return rows.Select(x => new VehicleInvitationResponse(x.Invitation.Id, x.Invitation.VehicleId, x.Name,
            x.Invitation.Role.ToString(), x.Invitation.Status == VehicleInvitationStatus.Pending && x.Invitation.ExpiresAtUtc <= clock.UtcNow
                ? "Expired" : x.Invitation.Status.ToString(), x.Invitation.ExpiresAtUtc, x.Invitation.Version,
            x.Invitation.InvitedEmail, x.SenderName, x.Plate, VehicleInvitationRules.ResendAvailableAt(x.Invitation, clock.UtcNow))).ToArray();
    }

    private Task SendAsync(VehicleInvitation invitation, VehicleInvitationResponse response, string token, CancellationToken ct)
    {
        var link = settings.Value.Link($"loi-moi/{invitation.Id}", $"token={Uri.EscapeDataString(token)}");
        return email.SendAsync(invitation.InvitedEmail, "iCareX - Lời mời chia sẻ xe",
            $"{response.InvitedByName} mời bạn tham gia xe {response.VehicleName} ({response.VehiclePlate ?? "không có biển số"}) với vai trò {response.Role}.\n{link}\nHết hạn lúc {invitation.ExpiresAtUtc:O}. Mỗi lời mời chỉ áp dụng cho xe và vai trò nêu trên. Bạn cần đăng nhập đúng email đã xác thực và chủ động chấp nhận lời mời. Khi nhận thư gửi lại, hãy dùng liên kết mới nhất.", ct);
    }
    private void Audit(Guid actor, VehicleInvitation invitation, string action) => db.AuditLogs.Add(new AuditLog
    {
        ActorUserId = actor, VehicleId = invitation.VehicleId, Action = action, EntityType = nameof(VehicleInvitation),
        EntityId = invitation.Id, CreatedAtUtc = clock.UtcNow,
        NewValues = JsonSerializer.Serialize(new { role = invitation.Role.ToString(), status = invitation.Status.ToString() })
    });
}
