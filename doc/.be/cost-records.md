# Fuel và dịch vụ nhiều hạng mục — #32, #33, #42

Source trên nhánh `feature/32-fuel-service-flows`; chưa nghiệm thu PostgreSQL
hoặc browser E2E do môi trường thiếu Docker. Không coi source là bằng chứng
đã deploy/migrate; không áp migration hoặc test phá dữ liệu lên NAS.

## API và DTO

Base `/api/v1/vehicles/{vehicleId}`. DTO ở Shared/Contracts/Records, TypeScript
được generator sinh trong record-contracts.ts. Cookie/session, verified account,
CSRF cho POST/PUT/DELETE. Actor/creator từ phiên; strict JSON cả header/items.

| Method/path | Input | Output |
| --- | --- | --- |
| GET `/fuel-records` | fuel.read | FuelResponse[] |
| POST `/fuel-records` | FuelWriteRequest, version=null | 201 FuelResponse |
| PUT `/fuel-records/{id}` | FuelWriteRequest, version hiện tại | 200 FuelResponse |
| DELETE `/fuel-records/{id}?version=n` | fuel.delete | 204 |
| GET `/service-records` | maintenance.read | ServiceResponse[] |
| POST `/service-records` | ServiceWriteRequest, version=null | 201 ServiceResponse |
| PUT `/service-records/{id}` | ServiceWriteRequest, version hiện tại | 200 ServiceResponse |
| DELETE `/service-records/{id}?version=n` | maintenance.delete | 204 |

FuelWriteRequest: requestId UUID khác rỗng, date YYYY-MM-DD, odo, cost, liters,
note, version nullable. Tiền VNĐ nguyên dương ≤10 tỷ; lít >0 và ≤500, tối đa
3 số lẻ; ODO tối đa 2 số lẻ, ≤9999999999.99, ≥baseline, nhất quán trước/sau;
ngày không tương lai, note≤500 ký tự.

ServiceWriteRequest: requestId/date/odo/note/version và items (1–50). Item có
type Repair/Maintenance, title2–100 ký tự, cost nguyên không âm, planId nullable.
Server tính tổng items >0 và ≤10 tỷ; không nhận tổng header từ client. UI cộng
header cost đúng một lần, không cộng thêm item totals.

Plan-link chưa khả dụng: mọi planId khác null trả400. Contract chừa chỗ #36/#37,
không cấp completion API hoặc tính lại kỳ/inbox. Legacy service có plan link
đọc được nhưng chặn sửa/xóa đến khi #37 tích hợp, tránh mất liên kết. Form
không bật lựa chọn plan. Owner/Manager quản lý mọi record; Contributor chỉ
sửa/xóa own CreatedByUserId; Viewer đọc. Non-member/revoked/cross-vehicle404,
thiếu quyền/archive403, validation400, version/key conflict409.

## Transaction và replay

Mutation giữ VehicleWriteLock và recheck membership sau khóa, cùng thứ tự
revoke/archive. Advisory lock theo actor+RequestId serialize cả retry trên xe
khác. Hash gồm operation, vehicle, resource ID và toàn request. Cùng key/payload
trả response đã lưu; khác payload/xe/resource409. Scope là current actor, không
phải owner legacy. Source/items/reading/audit/replay cùng transaction.
ExpiresAtUtc=7 ngày; module chưa chạy purge job. Replay vẫn kiểm quyền hiện tại,
không cấp lại quyền sau revoke/archive. DELETE dùng version, không RequestId;
xóa lại resource đã mất404.

Mỗi fuel/service có một source OdometerReading qua FK nullable unique/cascade.
Sửa/xóa source cập nhật/xóa reading cùng transaction. CurrentOdo từ toàn bộ
readings. GET odometer-readings trả manual để UI không nhân đôi lịch sử/tiền;
đoán source reading ID rồi PUT/DELETE manual409.

## Migration và frontend

`20261120031330_AddTransactionalCostRecords` thêm service_items, FK nguồn trên
readings, concurrency token fuel/service và fuel note500. Backfill mỗi typed
record cũ thành reading nguồn, mỗi maintenance record thành một Maintenance
item, giữ tiền/link/actor/history. Preflight dừng nếu fuel note>500 hoặc chi phí
maintenance có phần lẻ/vượt10 tỷ; không tự cắt/round. Fuel legacy độ chính xác
cũ vẫn đọc được; ghi mới tuân validation mới. Forward-only vì downgrade mất
chi tiết item; rollback bằng backup đã kiểm chứng. Apply chủ động qua
scripts/updatedb.ps1 trên DB owner chọn, không tự migrate/seed.

Form RHF/Zod thêm/xóa items, tổng readonly, pending, lỗi giữ input. Retry cùng
payload giữ RequestId; submit payload khác đổi key. Refetch xe/readings/chi phí
sau commit; tổng dashboard/lịch sử/báo cáo hiện có dùng nguồn này, không thêm
Reports API. Kiểm tra ghi tại [testing](testing.md), không coi mock FE là E2E.
