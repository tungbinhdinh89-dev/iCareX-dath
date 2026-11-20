# Kiểm thử và kết quả

## Fuel/service — 20/11/2026 (#32, #33, #42)

Trên nhánh feature/32-fuel-service-flows:

- `dotnet build iCareX.slnx --no-restore -p:UseAppHost=false -v quiet`: pass,
  0 warning/error.
- `scripts/check.ps1` qua PowerShell ExecutionPolicy Bypass: pass; model/snapshot
  khớp, liệt kê migration offline; không chứng nhận applied DB hoặc migrate live.
- `dotnet test src/iCareX.UnitTests/iCareX.UnitTests.csproj --no-restore -v quiet`:
  60/60 pass, gồm 7 case CostRecordRules mới.
- `dotnet test src/iCareX.IntegrationTests/iCareX.IntegrationTests.csproj --no-restore --filter Category=Host -v quiet`:
  22/22 pass, gồm CSRF/strict nested DTO tests mới.
- `pnpm.cmd typecheck`, `pnpm.cmd test`: pass, frontend27/27.
- `pnpm.cmd build`: pass, còn cảnh báo bundle>500kB.
- `python tools/contracts/generate_typescript.py --check`: pass.
- Test `CostRecordsReplayUpdateDeleteAndContributorScope` đã chạy trong/ngoài
  sandbox: **failed tại setup**, DockerUnavailableException, không kết nối được
  npipe docker_engine. Không chạy được transaction assertion; không ghi DB pass.

CostRecordTests đã viết thêm concurrent retry, fault sau SQL save trước commit,
invalid item/plan/archive/revoke và authenticated HTTP reload/source guard.
Các test này compile nhưng chưa thực thi được PostgreSQL. Browser1440/768/360,
API thật và tình huống mất response vẫn chưa được kiểm chứng ở trình duyệt.
Frontend HTTP stubs chỉ kiểm payload/key/CSRF, không thay E2E. Giữ ticket mở và
PR Draft đến khi DB/E2E đạt; không migrate/seed/test NAS để thay thế Docker.

Chạy lại khi Docker Desktop sẵn sàng:

```powershell
dotnet test src/iCareX.IntegrationTests/iCareX.IntegrationTests.csproj --no-restore
```

## Manager edit/shared ODO — 09/11/2026 (#58)

Unit53/53, Host21/21, frontend25/25 passed; solution build0 warning/error,
frontend typecheck/build passed (bundle>500kB warning). Host kiểm riêng POST
ODO/PUT vehicle cho Owner/Manager/Contributor/Viewer/non-member, strict creator
injection và không cho non-Owner sửa/xóa reading; dùng service/auth doubles.
Frontend kiểm permission theo xe, create tách update, account scope và revoke.

SharedVehicleOdometerTests đã viết cho PostgreSQL: Manager cập nhật xe shared
khi xe owned có cùng biển số, ba role tạo reading với owner AccountId/actor
CreatedBy, Viewer/outsider deny, permission projection và archived deny. Chạy
thử thất bại ở khởi tạo Testcontainers: DockerUnavailableException/npipe
docker_engine; chưa xác nhận persistence hoặc thao tác browser bằng phiên thật.
Không dùng NAS làm fixture tạo/xóa để thay thế Docker tests.

## Invitation đa phương tiện và resend — 09/11/2026 (#58)

Unit49/49, Host16/16, frontend24/24 passed; typecheck/build/generator --check
passed, frontend còn cảnh báo bundle>500kB. Host kiểm Owner/Manager, CSRF,
version-only DTO bằng service double. Metadata kiểm unique VehicleId+email;
unit/frontend kiểm cooldown, terminal states, URL scope và cookie/payload.

Đã chạy MultiVehicleInvitationTests và InvitationResendTests: cả2 bị chặn tại
Testcontainers DockerUnavailableException/npipe docker_engine. Chưa xác nhận
PostgreSQL persistence/concurrency/SMTP delivery thật. Không xem host double
là bằng chứng DB tests passed. [Runtime contract](vehicle-invitations.md).

## Cập nhật hồ sơ current user — 09/11/2026 (#58)

Working tree nhánh enhanced/58-authentication-authorization, chưa commit:
solution/API build0 warning/error; unit44/44, Host15/15, frontend22/22 passed;
frontend typecheck/build và Shared generator --check passed. Frontend còn cảnh
báo bundle trên500kB. Host test dùng test-only auth/service double để kiểm401,
CSRF400, strict DTO chống userId/email/roles/status injection, current actor,
response200 và no-store; không thay thế kiểm tra service persistence thật.

ProfileDatabaseTests kiểm cập nhật/đọc lại tên/phone bằng cookie thật, phone
null xóa được, invalid/forged request bị chặn, tài khoản khác và Identity giữ
nguyên. Đã chạy thử: thất bại ở khởi tạo Testcontainers với
DockerUnavailableException, không kết nối được npipe docker_engine; persistence
chưa được xác nhận. Không dùng NAS để tạo/xóa tài khoản test thay thế.

API local5080 đã restart bản mới; OpenAPI thực có PUT /api/v1/auth/me với schema
UpdateProfileRequest, health ready200. Chưa xác minh browser submit hồ sơ bằng
phiên người dùng thật; không tự thay dữ liệu cá nhân để làm smoke test.

## Khôi phục fetch sau migration — 09/11/2026

Mốc này cập nhật trạng thái deployment của kiểm thử Vehicle registration bên dưới:
database đã có `20261109090758` và `20261109123244` trong migration history;
model/snapshot khớp, API build 0 warning/error. Tiến trình API cũ truy vấn
`vehicle_types` đã bị bỏ nên GET vehicles trả500/42P01. Sau restart đúng binary,
log request thật GET vehicles trả200 (19:38:38, 20:12:42 và 20:19:13 giờ máy),
catalog200, health ready200. Kiểm tra repository read-only theo3 membership
actor đọc được tổng4 dòng xe và truy vấn ODO/catalog thành công. Không tạo
phiên giả, không ghi DB/migrate/seed trong phiên khắc phục này. PostgreSQL
Testcontainers vẫn chưa được xác nhận; không suy kết quả test migration từ
việc database hiện có schema mới.

## Vehicle registration và card UX — 09/11/2026

Kết quả trên working tree nhánh `enhanced/58-authentication-authorization`
(chưa commit, không có final SHA):

- Unit: 33/33 passed; Host: 14/14 passed, gồm EF model/schema metadata.
- Frontend: 21/21 passed, gồm membership tabs, edit prefill, DTO whitelist và
  maintenance summary chỉ khi có nguồn dữ liệu; typecheck/build passed.
- Solution build passed, không warning/error; frontend build có cảnh báo
  bundle JavaScript lớn hơn 500 kB.
- Shared TypeScript generator `--check` và `git diff --check` passed.
- Migration/model snapshot check passed ở chế độ offline; đây không phải
  bằng chứng SQL migration đã chạy thành công.
- PostgreSQL migration test đã viết để nâng schema cũ, giữ ODO/Owner/creator,
  kiểm tra backfill và composite FK. Lần chạy bị chặn tại Testcontainers do
  Docker engine không khả dụng; chưa kiểm chứng SQL trên PostgreSQL thật.
- Chưa kiểm thử browser với backend/schema mới. Migration mới chưa áp dụng
  NAS; API local cũ không tương thích DTO mới. Cần migrate trên database thử
  được khôi phục từ backup rồi triển khai backend/frontend cùng phiên bản,
  kiểm tra tạo/sửa xe, optional fields, inactive catalog, tabs và quyền.

Chi tiết trường/constraints và preflight tại
[Vehicle registration schema](vehicle-registration-schema.md).

## Đăng ký khi SMTP lỗi — 09/11/2026

Build 0 warnings/errors; unit 24, Host 13, frontend 18 passed; typecheck/build
và shared contracts --check passed. Ba cases trong RegistrationDeliveryTests
(configuration/transport/success) failed ở Testcontainers initialization với
DockerUnavailableException; chưa chứng minh DB regression bằng execution.
Test kiểm commit-before-send, pending-account guard, duplicate409 và resend/
verification recovery. EmailConfigurationTests chạy không cần DB, kiểm safe
configuration errors và giới hạn deadline. Không ghi fixture trên NAS.
[Kế hoạch và contract](registration-email-recovery.md).

## Fix vehicle Owner trigger — 09/11/2026

- Runtime log: POST /vehicles rollback với SQLSTATE 42703, function đọc NEW.VehicleId
  trên vehicles. Sửa bằng migration 20261109073130_FixVehicleOwnerConstraintTrigger.
- scripts/check.ps1 -Database xác nhận chỉ migration này pending; updatedb.ps1
  áp dụng thành công; kiểm lại không còn pending và model/snapshot không drift.
  Không tạo fixture hoặc thêm/xóa phương tiện trên NAS để kiểm thử.
- dotnet build: 0 warnings/errors; UnitTests 24 passed, Host 10 passed.
- pnpm test: 17 passed; typecheck/build passed (cảnh báo chunk lớn vẫn có).
  Regression payload kiểm create/update không gửi dư image/type/brand metadata.
- OwnerConstraintAcceptsVehicleAndMemberWritesButRejectsMissingOwner: Blocked/
  Failed tại container startup do DockerUnavailableException (docker_engine).
  Test gồm create/update/archive, member update/delete, mất Owner và chuyển
  membership để xe cũ thiếu Owner; chưa có kết quả PostgreSQL test passed.
- Scalar request trong ảnh có vehicleModelId="1", year=1 là lỗi 400 độc lập:
  cần UUID của catalog và year từ 1980 đến 2100, body không bọc request.

## Auth/access implementation #58 — 09/11/2026

- `dotnet build iCareX.slnx --artifacts-path .tmp/auth-artifacts --no-restore -p:UseAppHost=false`: Passed, 0 warnings/errors.
- UnitTests: 24 passed; Host filter: 10 passed. Host fixture dùng ephemeral Data
  Protection keys; role/access double chỉ dành cho kiểm HTTP middleware.
- DatabaseTests: Blocked/Failed ở khởi tạo container do Docker endpoint
  npipe://./pipe/docker_engine không sẵn sàng, kể cả ngoài sandbox. Không đánh
  dấu token/accept/revoke concurrency với PostgreSQL là passed.
- Integration code có cookie/Identity thật, email capture trong memory fixture,
  token replay, hai reset/accept, email mismatch, cross-vehicle, authorship,
  audit/revoke và write đang chờ khóa sau revoke. Chạy lại khi Docker sẵn sàng.
- Xem [runtime contract và SMTP](access-control.md). Không gửi email thật hoặc
  ghi NAS database để thay kiểm thử container; chưa có bằng chứng DB E2E.
- Frontend: `pnpm.cmd typecheck`, `pnpm.cmd test` **16/16**, `pnpm.cmd build`
  passed; Vite vẫn cảnh báo JS chunk >500kB (khoảng 1.08MB).
- Chrome dùng API thật mới tại 5080 + Vite 8443: login/register và guest redirect
  trên 1440/768/360px passed; form rỗng báo lỗi tiếng Việt, không overflow,
  không JavaScript exception hoặc HTTP/network error ngoài 401 dự kiến.
  Public forgot/verify/reset/invitation states và việc xóa fragment đã kiểm.
  Không gửi credentials hợp lệ/đăng ký account hoặc ghi database thật.
- Báo cáo/ảnh local: `.tmp-auth-screens/browser-report.json`,
  `.tmp-auth-screens/protected-report.json`, login/register/protected screenshots.
  Chưa kiểm SMTP delivery hoặc luồng authenticated member/accept trên DB thật.

## Backend runtime và scripts ngày 06/11/2026

- API thực tế: `/health/live`, `/health/ready`, `/openapi/v1.json` trả 200; `/api/v1/auth/me` trả 401 khi chưa có phiên. Ready xác nhận kết nối database hiện có, không chứng nhận tất cả schema/use case.
- `scripts/check.ps1 -Database`: liệt kê 3 migration đã áp dụng, EF model không có thay đổi so snapshot. `scripts/checkdb.ps1`: kết nối và SELECT thông tin server thành công. Không thực hiện update/provision/seed/smoke.
- Parse tất cả scripts PowerShell không lỗi; add/updatedb kiểm tra bằng `-WhatIf`, không tạo migration hoặc áp dụng thay đổi DB.
- `scripts/run.ps1 -NoBuild` tái sử dụng các dịch vụ đang chạy. Chạy thêm trên 5081/8444 đã khởi động cả API và Vite, live/ready/UI trả 200; auth/me qua proxy trả 401. Ctrl+C dừng cả hai process mới, không dừng dịch vụ 5080/8443 sẵn có.
- Output DatabaseTool cũ có Npgsql.dll toàn NUL; rebuild project khôi phục dependency và checkdb chạy thành công. Không sửa/ghi đè DLL bằng file tự tạo.
- Lần chạy trong sandbox không đọc/ghi được Data Protection key; chạy lại với quyền user profile thật đạt. Không thay chính sách auth/CSRF để bỏ qua lỗi môi trường.

## Lệnh

Chạy từ root: `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/be.ps1 <action>`.

| Action | Ý nghĩa |
|---|---|
| build | Build toàn solution |
| test | UnitTests và HTTP Host tests, không cần Docker hoặc DB thật |
| integration | PostgreSQL Testcontainers, cần Docker đang chạy |
| check | Npgsql connect đích và đọc database/version/TLS |
| migrate | Áp migration rõ ràng |
| smoke | Commit/read/cleanup fixture trên đích |

## Coverage hiện có

- ODO earlier/later/same-day, loại chính entry đang sửa, xóa max giảm current ODO, base ODO.
- Fuel/service cost/liters, archive guard, numeric precision.
- Latest service date/odo, sửa/xóa service, tháng cuối/năm nhuận, hạn OR và đúng hạn soon, không cộng thêm cost.
- Reflection architecture guard Domain/Application không phụ thuộc outer layer.
- Integration: migrate PostgreSQL riêng, persistence, duplicate plate, ownership FK, kind constraint, unlink plan giữ chi phí, cascade vehicle; health/OpenAPI/Scalar qua WebApplicationFactory.
- Host tests độc lập Docker: liveness/OpenAPI/Scalar, readiness 503 khi DB unavailable và docs 404 trong Production.
- Host auth test: cấp CSRF token, từ chối mutation thiếu CSRF và trả 401 cho `/auth/me` khi chưa xác thực.
- Vehicle catalog tests cần bao phủ cascading GET, Admin-only catalog mutation,
  model thuộc đúng brand/type, inactive catalog bị từ chối, vehicle owner
  isolation và migration backfill từ enum type.
- Kết quả mới nhất: `scripts/be.ps1 test` pass 12 unit tests và 3 host tests.

Testcontainers không lấy connection từ User Secrets/NAS và không dùng NAS. Test
thiếu Docker phải báo thất bại, không silently skip/pass.
Khi thêm API phải bổ sung IDOR, CSRF/session revoke, archive mọi đường ghi, rollback/concurrency/If-Match/idempotency và report/CSV parity.

## Kết quả phiên triển khai 2026-10-30

- SDK riêng .NET 10.0.403 đã cài ở `.tools`; restore thành công với EF/Microsoft 10.0.14, Npgsql provider 10.0.3, Scalar 2.18.6, Testcontainers 4.16.0. Các dependency bị NuGet advisory ở lần restore đầu đã được nâng bản vá; không tắt audit.
- Build solution thành công, 0 warning/0 error.
- UnitTests: 12 pass. Host tests không Docker: 2 pass. `scripts/be.ps1 test` exit 0.
- Integration PostgreSQL Testcontainers: 2 test database không chạy được do Docker daemon unavailable (không có docker CLI/pipe docker_engine); lần chạy trả failed, không đánh dấu pass/skip. Cấu hình image PostgreSQL 18 tương ứng major server đích; chạy lại `integration` khi có Docker.
- Đã tạo database `vehiclecare` trước đó chưa tồn tại trên `192.168.1.200:5433`, áp migration `20261030034612_InitialCreate` thành công.
- `check` exit 0: PostgreSQL 18.6, TLS false (trạng thái thực tế LAN; không ép SSL Disable trong code).
- `smoke` exit 0: ghi commit fixture GUID riêng, đọc qua DbContext khác, xóa đúng fixture. Không seed demo, không thay container/volume.
- API thực chạy tạm: `/health/live`, `/health/ready`, `/openapi/v1.json`, `/scalar/v1` đều HTTP 200 khi process có quyền mạng tới NAS; đã dừng process kiểm chứng.
- Khi chạy trong sandbox hạn chế mạng, ready trả 503 như thiết kế; kiểm tra ngoài sandbox đã xác nhận 200.

Giới hạn: chưa kiểm chứng database constraints/delete policies và register/login
với PostgreSQL Testcontainers do thiếu Docker; database smoke chỉ xác nhận
persistence. Cần chạy lại integration auth với Docker trước nghiệm thu.

## Kiểm chứng refactor iCareX — 06/11/2026

- `dotnet restore iCareX.slnx --ignore-failed-sources -p:NuGetAudit=false`: exit 0, dùng dependency hiện có cho kiểm tra local; không thay cấu hình audit của repository.
- `dotnet build iCareX.slnx --no-restore -p:UseAppHost=false`: exit 0, 8 project, 0 warning/0 error.
- `dotnet test src/iCareX.UnitTests/iCareX.UnitTests.csproj --no-build --no-restore`: 12 passed.
- `dotnet test src/iCareX.IntegrationTests/iCareX.IntegrationTests.csproj --no-build --no-restore --filter Category=Host`: 3 passed khi chạy được cấp quyền ngoài sandbox. Lần đầu 1 fail do sandbox chặn ghi Data Protection key tại user profile; 2 test còn lại pass. Không sửa test để che lỗi.
- `dotnet tool run dotnet-ef -- migrations has-pending-model-changes --project src/iCareX.Infrastructure --startup-project src/iCareX.Web/iCareX.Web.Api --no-build`: exit 0, no model changes; dùng ConnectionStrings__iCareX localhost giả, không kết nối/migrate NAS.
- `python tools/contracts/generate_typescript.py --check`: exit 0.
- TypeScript compiler kiểm riêng Shared types với noEmit/strict/skipLibCheck: exit 0.
- Hai file frontend iCareXApi.ts và garage.ts đã phục hồi từ commit `0e1c3566`, cập nhật import Shared. Frontend đạt typecheck, 3 tests và production build. Chrome xác nhận 10 route khách, hộp đăng nhập, desktop 1440px/mobile 390px, không exception JavaScript. Chạy `pnpm dev`, mở http://localhost:8443/. Chưa kiểm thử các giao dịch có đăng nhập với database.
- PostgreSQL Testcontainers chưa chạy do không có docker command. Không ghi database integration Pass.

Kết quả và source paths trước mục này là lịch sử triển khai; tên database thật cũ vẫn là vehiclecare. Đổi tên project không phải thao tác rename database.

<a id="access-test-matrix"></a>

## Ma trận auth/access theo đặc tả 09/11/2026

**Planned/Not run trong tác vụ tài liệu.** [Hợp đồng chuẩn](../authentication-authorization.md)
và [schema](../schema-database.md#auth-sharing-schema) là oracle đích; test
hiện có hoặc kết quả lịch sử không chứng minh membership/sharing đã đạt.

Fixture tách biệt: A Owner xe1/Viewer xe2, B Contributor xe1/Owner xe2,
C Viewer xe1, D Manager xe1, E non-member; account Suspended/Locked riêng.
Record của A/B trên xe1, driver khác creator, reminder assigned cho Viewer;
clock cố định cho expiry 7 ngày. Dùng DB PostgreSQL Testcontainers riêng.

| ID tài liệu | Scenario | Expected | Mức kiểm |
| --- | --- | --- | --- |
| AUTH-01 | Register hợp lệ/trùng email, lỗi giữa tạo Identity và Account | Transaction đầy đủ hoặc rollback; 201 không tự login | Host + DB |
| AUTH-02 | Sai password/lockout, valid login, suspended/locked account | Policy đúng; không cấp hoặc dùng phiên bị chặn; mã lỗi theo contract đã chốt | Unit + Host + DB |
| AUTH-03 | Email verification bật/tắt theo policy | Không tự coi flow đã có; khi bật, chưa verify không vào business | Contract + DB |
| AUTH-04 | /auth/me chậm, cookie hết hạn, revoke/logout, reload valid cookie | Không nháy home; 401 clear account; valid restore; 5xx/network retry; 403 giữ session | FE + browser + API thật |
| AUTH-05 | Return URL ngoài origin, token trong return, request muộn account A sau login B | Reject unsafe return; không log/store token; không xóa/rò state B | Unit + browser |
| ACCESS-01 | A tạo một/nhiều xe; failure trước tạo membership | Mỗi xe đúng một Owner active hoặc rollback, không xe mồ côi | DB transaction |
| ACCESS-02 | A Viewer xe2 nhưng Owner xe1; E query/detail/report/filter | Quyền theo xe, không role global; E không thấy dữ liệu, filter trước paging/tổng | Host + DB |
| ACCESS-03 | Anonymous/API trực tiếp; role/actor/creator giả từ client | 401; forged permission không grant, actor server authoritative | Host + security |
| ACCESS-04 | B thêm fuel/maintenance, sửa/xóa own và record A | Create/own được, other trả403; driver không grant own-record | Unit + DB |
| ACCESS-05 | C Viewer đọc/ghi; D Manager sửa xe/mời/role/remove/archive | C read đúng scope, write403; D update được, các mutation Owner-only403 | Unit + Host + DB |
| ACCESS-06 | Manager đọc members, Contributor/Viewer mở members | D read được; B/C không member.read theo nguồn | Contract + browser + API |
| ACCESS-07 | Invitation Contributor, duplicate normalized email/pending/member | One pending; allowed roles only; không Owner invitation | DB + race |
| ACCESS-08 | DB/response/log/email chứa invitation token | Chỉ hash DB; raw token chỉ trong kênh email/request accept; không response production/log/audit | Security + mail test sink |
| ACCESS-09 | Mở link trước login, đăng ký/login đúng email, explicit Accept/Reject | GET không cấp quyền; preview tối thiểu; Accept tạo membership, Reject không | Browser + Host + DB |
| ACCESS-10 | Email mismatch, expired, used/revoked link | 403/410/409 theo ngữ cảnh; không leak xe không được phép | Host + DB |
| ACCESS-11 | Hai Accept; Accept đối đầu Revoke/Archive; worker expiry chưa chạy | Một transition thành công, không duplicate member, expiry check bằng server clock | DB hai request đồng thời |
| ACCESS-12 | Owner revoke B trong phiên còn hạn; B dùng URL cũ và browser cache | Mất quyền request sau commit, history/creator B còn nguyên, không notification xe mới | DB + browser |
| ACCESS-13 | Authorize-write đối đầu revoke hoặc role downgrade | Không commit write dựa quyền cũ sau revoke; đúng thứ tự serialize | DB concurrency |
| ACCESS-14 | Owner tự remove/demote; role CRUD gửi Owner | Không mất Owner; reject, mã Owner-specific đã chốt | Host + DB |
| ACCESS-15 | Transfer chưa confirm, recipient sai/inactive, hai confirm/transfer | Chưa confirm không đổi quyền; đúng recipient; chỉ một Owner tại commit, projection/FK/audit nhất quán | DB transaction/race |
| ACCESS-16 | Inject lỗi giữa demote/promote/audit/owner projection | Rollback toàn bộ; không 0 hoặc 2 Owner, không đổi creator/history | DB fault injection |
| ACCESS-17 | Archived xe, restore bởi non-Owner; member bị revoke rồi restore xe | Không write/create mới; read theo quyền, Owner restore theo policy; không hồi sinh membership revoked | Host + DB |
| ACCESS-18 | Contributor reminder own/assigned; Viewer assigned; plan.read/ODO/snooze chưa chốt | Không nâng quyền Viewer; quyền chưa định nghĩa không tự grant | Unit + contract |
| ACCESS-19 | Một reminder nhiều recipient; read notification người khác; stale reference sau revoke | One task, notification riêng mỗi recipient, không truy cập projection xe qua snapshot cũ | DB + browser |
| ACCESS-20 | CSRF thiếu/sai/đúng cho auth và mọi cookie mutation; rate limit | Từ chối thiếu/sai; valid works;429 theo policy, không mất transaction do retry | Host + security |
| ACCESS-21 | Migration dữ liệu owner-only, creator chưa rõ, plate conflict khi transfer | Không grant giả, không đổi creator; count/ODO/tiền giữ; ngoại lệ được xử lý rõ | Migration + restore DB thử |
| ACCESS-22 | 1440/768/360; chuyển xe/account; revoke/403 khi form mở | Permission UI đúng, không cache chéo, confirm/pending/validation/error/retry rõ | Browser + API thật |

Backend tests dùng lệnh tại phần Lệnh và README; DTO thay đổi chạy generator
+ --check; FE chạy pnpm typecheck/test/build. Khi chỉ đổi docs/skill, kiểm
liên kết/anchors/matrix và không chạy DB hoặc coi các dòng trên Passed.
Evidence phải có final SHA, level, fixture, input/expected/actual và môi trường.
Mocks/stubs/browser intercepted response không thay test authenticated DB thật.
