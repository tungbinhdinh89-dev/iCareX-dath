using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using iCareX.Application.Abstractions;
using iCareX.Application.Features.Authorization.Contracts;
using iCareX.Application.Features.Records;
using iCareX.Application.Features.Vehicles.Contracts;
using iCareX.Domain.Authorization;
using iCareX.Domain.Common;
using iCareX.Domain.Entities;
using iCareX.Domain.Exceptions;
using iCareX.Infrastructure.Persistence;
using iCareX.Shared.Contracts.Records;
using Microsoft.EntityFrameworkCore;

namespace iCareX.Infrastructure.Services.Implementations;

public sealed class RecordService(GarageDbContext db, IVehicleMembershipResolver memberships, IBusinessClock clock) : IRecordService
{
    public async Task<IReadOnlyList<FuelResponse>> FuelAsync(Guid actor, Guid vehicleId, CancellationToken ct)
    {
        await Require(actor, vehicleId, VehiclePermissions.FuelRead, null, ct);
        return (await db.FuelEntries.AsNoTracking().Where(x => x.VehicleId == vehicleId)
            .OrderByDescending(x => x.Date).ThenBy(x => x.Id).ToListAsync(ct)).Select(Fuel).ToArray();
    }

    public async Task<IReadOnlyList<ServiceResponse>> ServicesAsync(Guid actor, Guid vehicleId, CancellationToken ct)
    {
        await Require(actor, vehicleId, VehiclePermissions.MaintenanceRead, null, ct);
        return (await db.MaintenanceRecords.AsNoTracking().Include(x => x.Items).Where(x => x.VehicleId == vehicleId)
            .OrderByDescending(x => x.Date).ThenBy(x => x.Id).ToListAsync(ct)).Select(Service).ToArray();
    }

    public async Task<FuelResponse> WriteFuelAsync(Guid actor, Guid vehicleId, Guid? id, FuelWriteRequest request, CancellationToken ct)
    {
        await using var tx = await VehicleWriteLock.BeginAsync(db, vehicleId, ct);
        var record = id is null ? null : await db.FuelEntries.SingleOrDefaultAsync(x => x.Id == id && x.VehicleId == vehicleId, ct)
            ?? throw new VehicleNotFoundException();
        var member = await Require(actor, vehicleId, id is null ? VehiclePermissions.FuelCreate : VehiclePermissions.FuelUpdate, record?.CreatedByUserId, ct);
        await LockKey(actor, request.RequestId, ct);
        var hash = Hash(new { vehicleId, id, operation = "fuel", request });
        var replay = await Replay<FuelResponse>(actor, request.RequestId, hash, ct);
        if (replay is not null) return replay;
        Version(record, request.Version);
        try { CostRecordRules.Fuel(request.Cost, request.Liters); }
        catch (RuleViolation e) { Invalid(e.Message); }
        record ??= new FuelEntry { AccountId = member.OwnerAccountId, VehicleId = vehicleId, CreatedByUserId = actor };
        var reading = await SourceReading(record.Id, true, ct);
        await ValidateReading(vehicleId, member.OwnerAccountId, reading?.Id, request.Date, request.Odo, request.Note, ct);
        record.Date = request.Date; record.Odo = request.Odo; record.Cost = request.Cost;
        record.Liters = request.Liters; record.Note = request.Note.Trim();
        if (id is null) db.FuelEntries.Add(record); else record.Version++;
        record.UpdatedAtUtc = clock.UtcNow;
        SyncReading(reading, record.Id, true, member.OwnerAccountId, vehicleId, actor, request.Date, request.Odo, record.Note);
        var result = Fuel(record);
        AddReplay(actor, request.RequestId, hash, result, id is null);
        Audit(actor, vehicleId, record.Id, id is null ? "fuel.created" : "fuel.updated");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task<ServiceResponse> WriteServiceAsync(Guid actor, Guid vehicleId, Guid? id, ServiceWriteRequest request, CancellationToken ct)
    {
        await using var tx = await VehicleWriteLock.BeginAsync(db, vehicleId, ct);
        var record = id is null ? null : await db.MaintenanceRecords.Include(x => x.Items)
            .SingleOrDefaultAsync(x => x.Id == id && x.VehicleId == vehicleId, ct) ?? throw new VehicleNotFoundException();
        var member = await Require(actor, vehicleId, id is null ? VehiclePermissions.MaintenanceCreate : VehiclePermissions.MaintenanceUpdate, record?.CreatedByUserId, ct);
        await LockKey(actor, request.RequestId, ct);
        var hash = Hash(new { vehicleId, id, operation = "service", request });
        var replay = await Replay<ServiceResponse>(actor, request.RequestId, hash, ct);
        if (replay is not null) return replay;
        Version(record, request.Version);
        ValidateItems(request.Items);
        if (record?.PlanId is not null || record?.Items.Any(x => x.PlanId is not null) == true)
            Invalid("Dịch vụ cũ có liên kết kế hoạch; chờ tích hợp completion ở ticket #37 trước khi sửa.");
        record ??= new MaintenanceRecord { AccountId = member.OwnerAccountId, VehicleId = vehicleId, CreatedByUserId = actor };
        var reading = await SourceReading(record.Id, false, ct);
        await ValidateReading(vehicleId, member.OwnerAccountId, reading?.Id, request.Date, request.Odo, request.Note, ct);
        if (id is not null) db.ServiceItems.RemoveRange(record.Items);
        record.Items = request.Items.Select(x => new ServiceItem
        {
            MaintenanceRecordId = record.Id, Type = x.Type, Title = x.Title.Trim(), Cost = x.Cost, PlanId = x.PlanId
        }).ToList();
        // Explicitly add replacements: their server-generated GUIDs must not be treated as existing rows.
        db.ServiceItems.AddRange(record.Items);
        record.PlanId = null;
        record.Title = record.Items[0].Title;
        record.Date = request.Date; record.Odo = request.Odo; record.Cost = record.Items.Sum(x => x.Cost);
        record.Note = request.Note.Trim(); record.UpdatedAtUtc = clock.UtcNow;
        if (id is null) db.MaintenanceRecords.Add(record); else record.Version++;
        SyncReading(reading, record.Id, false, member.OwnerAccountId, vehicleId, actor, request.Date, request.Odo, record.Note);
        var result = Service(record);
        AddReplay(actor, request.RequestId, hash, result, id is null);
        Audit(actor, vehicleId, record.Id, id is null ? "service.created" : "service.updated");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return result;
    }

    public async Task DeleteAsync(Guid actor, Guid vehicleId, Guid id, bool fuel, long version, CancellationToken ct)
    {
        await using var tx = await VehicleWriteLock.BeginAsync(db, vehicleId, ct);
        // Resolve membership before exposing resource existence.
        await Require(actor, vehicleId, fuel ? VehiclePermissions.FuelRead : VehiclePermissions.MaintenanceRead, null, ct);
        AuditedEntity record;
        Guid creator;
        if (fuel)
        {
            var value = await db.FuelEntries.SingleOrDefaultAsync(x => x.Id == id && x.VehicleId == vehicleId, ct) ?? throw new VehicleNotFoundException();
            record = value; creator = value.CreatedByUserId;
        }
        else
        {
            var value = await db.MaintenanceRecords.SingleOrDefaultAsync(x => x.Id == id && x.VehicleId == vehicleId, ct) ?? throw new VehicleNotFoundException();
            record = value; creator = value.CreatedByUserId;
        }
        await Require(actor, vehicleId, fuel ? VehiclePermissions.FuelDelete : VehiclePermissions.MaintenanceDelete, creator, ct);
        if (!fuel && (record is MaintenanceRecord { PlanId: not null }
            || await db.ServiceItems.AnyAsync(x => x.MaintenanceRecordId == id && x.PlanId != null, ct)))
            Invalid("Dịch vụ có liên kết kế hoạch; chờ tích hợp completion ở ticket #37 trước khi xóa.");
        Version(record, version);
        db.Remove(record); // FK cascade removes its source reading/items in the same transaction.
        Audit(actor, vehicleId, id, fuel ? "fuel.deleted" : "service.deleted");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    private async Task<VehicleMembership> Require(Guid actor, Guid vehicleId, string permission, Guid? creator, CancellationToken ct)
    {
        var member = await memberships.ResolveAsync(actor, vehicleId, ct) ?? throw new VehicleNotFoundException();
        if (!VehiclePermissionRules.Allows(member.Role, permission, actor, creator, archived: member.IsArchived))
            throw new VehicleAccessDeniedException();
        return member;
    }

    private async Task ValidateReading(Guid vehicleId, Guid owner, Guid? readingId, DateOnly date, decimal odo, string? note, CancellationToken ct)
    {
        if (note is null || note.Length > 500 || decimal.Round(odo, 2) != odo) Invalid("ODO tối đa 2 số lẻ; ghi chú tối đa 500 ký tự.");
        var vehicle = await db.Vehicles.SingleAsync(x => x.Id == vehicleId, ct);
        var history = await db.OdometerReadings.AsNoTracking().Where(x => x.VehicleId == vehicleId).ToListAsync(ct);
        try
        {
            GarageRules.ValidateEntry(vehicle, new Entry { Id = readingId ?? Guid.NewGuid(), AccountId = owner, VehicleId = vehicleId,
                Kind = EntryKind.Odo, Title = "ODO nguồn", Date = date, Odo = odo, Note = note! },
                history.Select(x => new Entry { Id = x.Id, VehicleId = vehicleId, Date = x.Date, Odo = x.Odo }), clock.Today);
        }
        catch (RuleViolation e) { Invalid(e.Message); }
    }

    private Task<OdometerReading?> SourceReading(Guid id, bool fuel, CancellationToken ct) => db.OdometerReadings
        .SingleOrDefaultAsync(x => fuel ? x.FuelEntryId == id : x.MaintenanceRecordId == id, ct);

    private void SyncReading(OdometerReading? reading, Guid id, bool fuel, Guid owner, Guid vehicleId, Guid actor, DateOnly date, decimal odo, string note)
    {
        if (reading is null)
        {
            reading = new OdometerReading { AccountId = owner, VehicleId = vehicleId, CreatedByUserId = actor,
                FuelEntryId = fuel ? id : null, MaintenanceRecordId = fuel ? null : id };
            db.OdometerReadings.Add(reading);
        }
        else reading.Version++;
        reading.Date = date; reading.Odo = odo; reading.Note = note; reading.UpdatedAtUtc = clock.UtcNow;
    }

    // A transaction advisory lock serializes the same actor/key across different vehicles/resources.
    // Actor scope intentionally differs from the vehicle's legacy owner AccountId.
    private Task<int> LockKey(Guid actor, Guid key, CancellationToken ct)
    {
        if (key == Guid.Empty) Invalid("RequestId phải là UUID khác rỗng.");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{actor}:{key}"));
        var lockId = System.Buffers.Binary.BinaryPrimitives.ReadInt64BigEndian(bytes);
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockId})", ct);
    }

    private async Task<T?> Replay<T>(Guid actor, Guid key, string hash, CancellationToken ct) where T : class
    {
        var existing = await db.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.AccountId == actor && x.Key == key.ToString(), ct);
        if (existing is null) return null;
        if (existing.RequestHash != hash) throw new VehicleConflictException("RequestId đã được dùng cho nội dung khác.");
        return JsonSerializer.Deserialize<T>(existing.ResponseBody)!;
    }

    private void AddReplay<T>(Guid actor, Guid key, string hash, T response, bool create) => db.IdempotencyRecords.Add(new()
    {
        AccountId = actor, Key = key.ToString(), RequestHash = hash, ResponseStatus = create ? 201 : 200,
        ResponseBody = JsonSerializer.Serialize(response), CreatedAtUtc = clock.UtcNow, ExpiresAtUtc = clock.UtcNow.AddDays(7)
    });
    private void Audit(Guid actor, Guid vehicleId, Guid id, string action) => db.AuditLogs.Add(new()
    {
        ActorUserId = actor, VehicleId = vehicleId, EntityId = id, EntityType = "Record", Action = action, CreatedAtUtc = clock.UtcNow
    });
    private static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static void Version(AuditedEntity? record, long? version)
    {
        if (record is null ? version is not null : version is null || version < 0 || record.Version != version)
            throw new VehicleConflictException("Phiên bản đã thay đổi. Tải lại bản ghi trước khi sửa.");
    }
    private static void ValidateItems(IReadOnlyList<ServiceItemRequest>? items)
    {
        if (items is null || items.Count is < 1 or > 50) Invalid("Dịch vụ cần từ 1 đến 50 hạng mục.");
        foreach (var item in items!)
        {
            if (item is null) Invalid("Hạng mục không hợp lệ.");
            if (item.PlanId is not null) Invalid("Liên kết kế hoạch chưa khả dụng; chờ ticket #36/#37.");
        }
        try { CostRecordRules.Service(items!.Select(x => new ServiceItem { Type = x.Type, Title = x.Title, Cost = x.Cost, PlanId = x.PlanId }).ToArray()); }
        catch (RuleViolation e) { Invalid(e.Message); }
    }
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Invalid(string message) => throw new VehicleValidationException([message]);
    private static FuelResponse Fuel(FuelEntry x) => new(x.Id, x.VehicleId, x.CreatedByUserId, x.Date, x.Odo, x.Cost, x.Liters, x.Note, x.Version);
    private static ServiceResponse Service(MaintenanceRecord x) => new(x.Id, x.VehicleId, x.CreatedByUserId, x.Date, x.Odo, x.Cost, x.Note,
        x.Items.OrderBy(i => i.Id).Select(i => new ServiceItemRequest(i.Type, i.Title, i.Cost, i.PlanId)).ToArray(), x.Version);
}
