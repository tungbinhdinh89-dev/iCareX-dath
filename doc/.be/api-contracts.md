# API contracts — hiện tại và thiết kế auth/access

Fuel/Service #32/#33/#42 có [contract riêng](cost-records.md): CRUD theo xe,
strict Shared DTO, RequestId replay, version, source reading và items. Đây là
source nhánh feature, chưa xác nhận deploy/DB integration.

## Shared vehicle: edit và ODO

PUT vehicles/{id} cần vehicle.update: Owner/Manager. POST
vehicles/{id}/odometer-readings cần odometer.create: Owner/Manager/Contributor.
PUT/DELETE odometer-readings cần odometer.update/delete: chỉ Owner. Viewer
chỉ đọc. API policy và service cùng kiểm membership/quyền, service kiểm lại
trong vehicle transaction lock. Archived/revoked không được ghi.

Permission codes mới nằm trong catalog code và trả qua me/permissions;
không cần schema migration. ODO request strict, không nhận actor/creator/role.
AccountId của record giữ owner tenant, CreatedByUserId lấy user đang đăng nhập.
Giữ date/ODO history validation, CSRF và version409 khi sửa/xóa.

Frontend tách canCreateOdo khỏi canManageOdo, Owner/Manager có nút sửa xe trong
Details/menu. Duplicate plate kiểm server theo vehicle.AccountId: không chặn
vì xe shared/owned của chủ khác có cùng biển số. Triển khai frontend/backend
cùng phiên bản, refresh permissions sau restart.

Invitation đa xe và POST resend: [contract/validation](vehicle-invitations.md).
Response invitation thêm recipient email, sender name, plate và resend deadline;
token/hash không trả cho UI. DTO Shared/TS mới phải triển khai cùng backend.

## Cập nhật hồ sơ current user

`PUT /api/v1/auth/me` nhận `UpdateProfileRequest`, trả `200 AccountResponse`
giống GET `/auth/me`. Yêu cầu cookie phiên hợp lệ và `X-CSRF-TOKEN` từ
GET `/auth/csrf` (giữ cookie CSRF cùng phiên). Request JSON:

```json
{ "name": "Nguyễn Văn An", "phone": "0912345678" }
```

Tên trim, bắt buộc 2–60 ký tự, không chứa ký tự điều khiển. Phone tùy chọn;
null/thiếu/chuỗi rỗng sau trim xóa phone thành chuỗi rỗng; có giá trị phải
đúng 10 chữ số ASCII bắt đầu0. Tên thiếu/null hoặc dữ liệu không hợp lệ trả400.
JSON có trường ngoài DTO (userId/accountId/email/roles/password/accountStatus/
emailVerified...) bị từ chối400; không dùng client ID để chọn tài khoản.
Anonymous/phiên hết hạn/thu hồi trả401; CSRF thiếu/sai trả400. PendingVerification
có phiên hợp lệ vẫn sửa thông tin liên hệ được, không được cấp quyền business.

Chỉ cập nhật Account.Name/Phone, giữ nguyên Identity email/password/roles/
verification, membership, creator và GarageRevision. Giao dịch khóa hàng
Account serialize hai lần sửa; request commit sau cùng thắng; không có version
DTO hoặc migration mới. Response auth có Cache-Control no-store. Frontend
dùng DTO Shared sinh tự động, PUT rồi GET current user, đồng bộ session/cache,
hủy garage query cũ và không ghi snapshot toàn bộ garage.

> Contract Vehicle registration mới: [chi tiết](vehicle-registration-schema.md).
> /vehicle-catalog/types dùng enum English codes, không còn UUID; POST/PUT types
> được bỏ. Create/UpdateVehicle nhận type/brand/model/fuel/unit/odometer, nullable
> metadata; response thêm timestamps, bỏ baseOdo/vehicleTypeId và hai field đã
> loại bỏ. Client+backend+schema cần deploy cùng phiên bản; chưa apply live.

Register hiện trả 201 RegistrationResponse: giữ id/name/email/phone/roles/
accountStatus/emailVerified và thêm verificationEmailSent. SMTP unavailable sau
commit không đổi thành lỗi đăng ký; false cho biết cần gửi lại khi email sẵn
sàng. Resend/forgot vẫn trả 503 nếu không gửi được. Xem
[contract và kế hoạch khôi phục](registration-email-recovery.md).

### Lưu ý POST /vehicles khi kiểm bằng Scalar/API client

Body là CreateVehicleRequest trực tiếp (không bọc trong `request`).
`vehicleModelId` phải là UUID thật lấy từ `/vehicle-catalog/brands/{id}/models`,
không phải chuỗi `"1"`. `year` trong khoảng 1980–2100. JSON không parse được
vehicleModelId có thể kèm lỗi `request field is required`; đây là lỗi binding,
không phải yêu cầu thêm một object bọc ngoài. Frontend map đúng các field:
name, plate, vehicleModelId, year, baseOdo, imageUrl; không gửi image/type/brand
UI metadata. Lỗi 500 do Owner trigger được sửa bằng migration riêng, không liên
quan lỗi 400 khi API client gửi UUID/year sai.

> **Implementation #58:** [runtime contract mới](access-control.md) bổ sung
> verification/recovery, permissions, members và invitation endpoints. Bảng
> baseline bên dưới giữ convention cũ; các đoạn "chưa sửa API" là lịch sử.
> Public DTO/TypeScript ở Shared là chuẩn.

**Đối chiếu 09/11/2026.** [Đặc tả auth/authorization](../authentication-authorization.md)
là nguồn quyền/flow/mã lỗi đích. Ví dụ Garage snapshot/ETag/PATCH/DELETE vật lý
của bản tài liệu cũ không phải endpoint runtime. Chưa sửa controller/DTO ở tác vụ này.

## Convention và tương thích

- /api/v1; camelCase JSON; UUID opaque string; date-only YYYY-MM-DD, audit UTC.
- DTO ở src/iCareX.Shared/Contracts; TypeScript sinh, không expose Identity/
  entity/DbContext/IQueryable hoặc credential ra client.
- Auth cookie HttpOnly/session DB và CSRF. Không thêm JWT/refresh hay /me trùng
  /auth/me; không tự đổi response DTO sang envelope do ví dụ nguồn.
- Vehicle/ODO hiện dùng Version và 409, không phải Garage If-Match/412/428.
- Owner hiện lấy server claim. Đích actor từ phiên, role từ active membership
  theo xe và record belongs-to; effective permissions client chỉ phục vụ UX.
- 204 không body; validation 400/ProblemDetails hiện có cần giữ tương thích.

## Controller hiện có

| Method/path | Input / policy | Output |
| --- | --- | --- |
| GET /auth/csrf | anonymous | CSRF token + cookie |
| POST /auth/register | name,email,password,phone? + CSRF | 201 AccountResponse, không tự login |
| POST /auth/login | email,password + CSRF | 200 account + cookie, hoặc 401 |
| GET /auth/me | valid session | AccountResponse + Identity roles, hoặc 401 |
| POST /auth/logout | session + CSRF | 204 revoke session/clear cookie |
| GET /vehicle-catalog/types | anonymous | active types |
| GET /vehicle-catalog/types/{id}/brands | anonymous | active brands |
| GET /vehicle-catalog/brands/{id}/models | anonymous | active models |
| Catalog mutation | Identity role Admin | Không grant mọi xe |
| GET /vehicles | authenticated owner hiện tại; includeArchived | VehicleListResponse |
| POST /vehicles | name,plate,vehicleModelId,year,baseOdo,imageUrl? | 201 VehicleResponse |
| GET /vehicles/{id} | owner hiện tại | VehicleResponse |
| PUT /vehicles/{id} | allowed fields + version | VehicleResponse |
| POST /vehicles/{id}/archive và /restore | version + owner | VehicleResponse |
| GET/POST /vehicles/{id}/odometer-readings | owner; date/odo/note khi tạo | list/reading DTO |
| PUT /vehicles/{id}/odometer-readings/{readingId} | fields + version | reading DTO |
| DELETE /vehicles/{id}/odometer-readings/{readingId} | version query | 204 |

Không có DELETE vehicle vật lý, /garage snapshot, membership/invitation/
transfer hoặc Fuel/Service/Reminder API đầy đủ được chứng minh ở baseline.

## Hợp đồng đích mới — phải thiết kế trước implementation

Danh mục đầy đủ ở [bảng API đích](../authentication-authorization.md#7-api-hiện-có-và-api-đề-xuất).
Các request tối thiểu cần:

| Operation | Request đích | Response / guard |
| --- | --- | --- |
| Vehicle list/detail | filters/paging chốt riêng, không nhận actor | role, accessType Owned/Shared, permission/capability theo xe; memberCount theo quyền |
| GET vehicle permissions | vehicleId từ route | quyền account hiện tại trên xe này |
| List members | vehicle.member.read | Owner/Manager; chỉ dữ liệu hồ sơ tối thiểu |
| Update member | role Manager/Contributor/Viewer + version | Owner only; không mutate Owner |
| Remove member | member/vehicle/version theo contract | Revoke thay hard-delete; 204, giữ history |
| Create invitation | email + allowed role | 201 metadata Pending/expiresAt, không raw token |
| List/revoke invitation | owner + same vehicle; version cho mutation | pending only khi revoke |
| Preview invitation | authenticated + recipient email/owner quản lý | thông tin xe/người mời/role tối thiểu, không mở toàn xe |
| Accept/reject | token trong body; CSRF; current recipient email | Accept membership + acceptedBy/at; Reject chỉ status |
| Transfer start | target active member + version | request metadata, không đổi owner ngay |
| Transfer confirm | requestId/version theo contract | đúng recipient, atomically one Owner |

AccountStatus/email verification field đích không tự thêm vào AccountResponse
hiện có; cần DTO C# + generator/check + version/compatibility. Dùng username/
email normalization Identity, không accept Creator/Owner/permission từ body.
Token không đi vào query/log/audit; endpoint preview không public grant xe.

## Status và lỗi đích

401 INVALID_CREDENTIALS/SESSION_EXPIRED; 403 ACCOUNT_SUSPENDED/ACCOUNT_LOCKED/
VEHICLE_PERMISSION_DENIED/INVITATION_PERMISSION_DENIED/INVITATION_EMAIL_MISMATCH;
404 VEHICLE_NOT_FOUND/INVITATION_NOT_FOUND; 409 EMAIL_ALREADY_EXISTS/
VEHICLE_CONFLICT/VEHICLE_OWNER_CANNOT_LEAVE/INVITATION_ALREADY_PROCESSED/
USER_ALREADY_VEHICLE_MEMBER/PENDING_INVITATION_EXISTS/OWNERSHIP_TRANSFER_PENDING;
410 INVITATION_EXPIRED; 422 EMAIL_NOT_VERIFIED (conditional)/VEHICLE_ARCHIVED/
VEHICLE_OWNER_REQUIRED/OWNER_CANNOT_BE_REMOVED/INVALID_MEMBER_ROLE;
429 TOO_MANY_ATTEMPTS. Đây là mã đích, chưa phải assertion API đang trả đủ.

Chưa có membership/xe ngoài phạm vi trả 404; active member đọc được nhưng thiếu
operation trả 403. 403 không clear toàn session. Worker expiry không thay
request-time check; 5xx/network có retry, không báo invitation chắc đã/ chưa
được accept khi kết quả commit chưa rõ.

## Verification và chuyển đổi

Kiểm [ma trận quyền/test](testing.md#access-test-matrix), CSRF mọi cookie
mutation và ownership/resource từ API trực tiếp, không chỉ UI. Version,
idempotency/replay và DTO mapping phải giữ data boundary khi đổi owner/member.
Không retry command của account trước sau login; không tự thay HTTP conventions.
