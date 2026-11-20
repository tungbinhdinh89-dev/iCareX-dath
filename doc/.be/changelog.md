# Backend changelog

## 2026-11-20 — Fuel/service persistence và form (#32, #33, #42)

- Thêm Shared DTO/API CRUD, server membership/own-record/version validation,
  actor+RequestId replay, transactional source reading/items/audit và migration.
- RHF/Zod form nhiều item, retry giữ key theo payload, refetch nguồn chi phí;
  không giả completion/plan API. [Contract và migration](cost-records.md).
- Build/unit/host/FE checks và blocker thực tế ghi tại testing.md.
- Chưa nghiệm thu DB/E2E vì Docker unavailable; không đóng ticket hoặc merge.

## 2026-11-20 — PostgreSQL local trên Docker Desktop

- Thêm Compose dùng PostgreSQL 18 Alpine, database icarex, tài khoản phát triển
  admin/123456, cổng loopback có thể đổi, healthcheck và named volume.
- README hướng dẫn start/stop, kết nối bằng environment trong terminal và
  chạy migration chủ động; không tự migrate/seed lúc startup.
- Chưa có issue được cung cấp; giữ thay đổi local để review, không tạo số ticket.
- Kiểm thử Compose/runtime bị chặn vì Docker CLI không có trên PATH môi trường.

## 2026-11-09 — Manager sửa xe, Manager/Contributor thêm ODO (#58)

- Thay ODO Owner-only guard bằng policy/service permission riêng:
  odometer.create cho Owner/Manager/Contributor; update/delete chỉ Owner.
  Giữ archive/revoke/tenant/creator/vehicle-lock và validation lịch sử.
- ODO request strict, không nhận actor/creator/role; permission snapshot tự
  chứa catalog mới. Không thay schema hoặc cơ chế authentication.
- Frontend tách create khỏi manage ODO trên quick action/card/details/form;
  nút sửa xe rõ hơn, recheck quyền khi submit, bỏ duplicate plate check toàn
  garage client sai scope; backend giữ unique plate theo owner AccountId.
- Unit53, Host21, frontend25 passed; typecheck/build passed. PostgreSQL test
  thật được thêm cho Manager edit/shared ODO/attribution/archive, xem testing.

## 2026-11-09 — Invitation độc lập theo từng xe và resend (#58)

- Giữ unique Pending theo VehicleId+email; cùng email có role khác nhau trên
  các xe của cùng Owner. Thêm regression test ba xe/ba role và token scope.
- Resend POST version-only, cookie/CSRF/Owner, cooldown60s, rotate hash/token,
  giữ expiry/role, audit và rollback khi SMTP lỗi. Không đổi schema.
- Response/UI thêm recipient email, sender name, plate và resend deadline;
  inbox/preview kiểm sender còn Owner. [Contract](vehicle-invitations.md).
- Unit49, Host16, frontend24 passed; hai PostgreSQL tests bị chặn Docker.

## 2026-11-09 — API và form cập nhật hồ sơ current user (#58)

- Thêm PUT /api/v1/auth/me, UpdateProfileRequest và TS được sinh từ Shared.
  Current user từ claim server; cookie/CSRF, strict JSON, không cho sửa email/
  roles/status/identity. Response dùng AccountResponse hiện có.
- AccountProfileRules trim/validate tên2–60 và phone Việt Nam10 chữ số hoặc
  rỗng. AuthService khóa Account trong transaction, chỉ ghi Name/Phone.
  Không cần migration; không thay authentication hay verification guard.
- Nối form hồ sơ với API thật, GET lại /auth/me sau lưu, cập nhật session và
  cache hiện tại; bỏ thông báo placeholder chưa hỗ trợ hồ sơ.
- Unit/host/frontend checks và kiểm thử PostgreSQL persistence riêng được thêm;
  phân biệt test host dùng service double với kiểm tra persistence thực.

## 2026-11-09 — Khôi phục vehicle fetch sau nâng schema

- Xác nhận log lỗi GET vehicles 500: SQLSTATE42P01, relation vehicle_types
  không tồn tại. Database đã migrate nhưng tiến trình API vẫn chạy binary cũ.
- Kiểm tra migration history/model thành công; build 0 warning/error;
  restart API local5080 bằng binary hiện tại. Không apply migration/seed/ghi DB.
- Health ready200; kiểm tra read-only repository mới đọc thành công catalog,
  xe và ODO cho 3 membership actor, tổng4 dòng xe theo scope.
- Khi nâng schema phải restart API cùng phiên bản; không dùng bản binary
  lưu tạm trước migration để phục vụ frontend mới.

## 2026-11-09 — Thiết kế lại Vehicle registration theo nghiệp vụ và card UX

- VehicleType/FuelType/OdometerUnit enum; bỏ entity/table VehicleType theo kế
  hoạch backfill, Brand/Model giữ entity với NormalizedName unique theo scope.
- Nullable Name/Plate/Year và metadata nhiên liệu, VIN/số đăng ký, giá mua/bán
  VNĐ, ghi chú/ảnh; Odometer12,2 rename từ BaseOdo. Bỏ SpeedLimit/HasTwoTanks theo
  chỉ đạo mới nhất. Giữ legacy tenant/creator, active membership và Owner trigger.
- Composite FK model+brand và brand+type, Restrict, CHECK/index/precision rõ;
  active/catalog validation, giữ catalog inactive cũ khi edit và unit history guard.
- Migration RedesignVehicleRegistrationSchema có preflight/backfill/duplicate
  guard/flush deferred checks; forward-only và **chưa apply NAS**.
- Shared DTO/TS và API client/form cập nhật; card16:9, ODO/fuel/role, menu quyền,
  tab all/owned/shared/archive theo Owner membership. Details giữ metadata và
  timestamps; form edit prefill dữ liệu đầy đủ, biển số có ví dụ, giá gắn VNĐ.
- Không triển khai Fuel/Maintenance CRUD hay mock status; maintenance summary chỉ
  chạy khi có source thực, chưa có API thì không hiện trạng thái giả.
- [Dictionary và quyết định](vehicle-registration-schema.md).

## 2026-11-09 — Cấu hình Gmail SMTP local

- Thêm scripts/configure-gmail.ps1: nhập App Password bằng hidden input, yêu cầu
  SEND trước khi gửi thư thử tới chính người nhập; lưu User Secrets bằng JSON
  stdin chỉ sau khi SMTP thành công, không ghi password vào argv/source/log.
- Gmail SMTP smtp.gmail.com:587 STARTTLS đã chấp nhận thư thử. Cấu hình đã lưu
  vào User Secrets; API local đã restart và health trả 200.
- Endpoint gửi lại verification nhận request và trả 202 generic. Không tự
  EmailConfirmed/accept token thay người dùng; người dùng vẫn phải mở thư và
  xác thực. 202 không xác nhận một email cụ thể tồn tại trong hệ thống.

## 2026-11-09 — Tách đăng ký khỏi lỗi SMTP (#58)

- Commit user/profile/settings/role trước khi gửi verification; SMTP unavailable
  không rollback account hoặc làm đăng ký trả 503. RegistrationResponse giữ
  field account cũ và thêm verificationEmailSent; account vẫn PendingVerification.
- UI phân biệt tài khoản đã tạo với email đã gửi; login/resend/verify hiện có
  phục hồi được, không bypass verified-account guard.
- Deadline SMTP 10 giây (1–60 cấu hình), log reason code an toàn; token issuance
  transaction riêng, không tạo queue/outbox/raw-token fallback hoặc migration.
- Build + 24 unit + 13 Host + 18 frontend tests/typecheck/build đạt. Ba SMTP/DB
  regression cases bị chặn vì Docker chưa chạy; gửi thư thật chưa kiểm chứng.
- [Kế hoạch và vận hành](registration-email-recovery.md).

## 2026-11-09 — Fix thêm/cập nhật vehicle bị rollback bởi Owner trigger (#58)

- Log runtime xác nhận SQLSTATE 42703: trigger dùng NEW.VehicleId trên bảng
  vehicles chỉ có Id, gây POST /vehicles 500 khi commit.
- Thêm migration 20261109073130_FixVehicleOwnerConstraintTrigger, không sửa các
  migration đã áp dụng. Hàm phân biệt bảng/operation, dùng vehicles.Id hoặc
  members.VehicleId, kiểm cả xe cũ/mới khi membership đổi xe. Giữ deferred
  exactly-one-Owner constraint và trả SQLSTATE 23514 khi vi phạm.
- Đã kiểm migration history: chỉ bản sửa này pending; áp dụng thành công bằng
  scripts/updatedb.ps1 vào database cấu hình hiện tại. Không thêm/xóa xe thật.
- API client whitelist field của Create/UpdateVehicle thay vì spread cả form;
  thêm regression test payload, và PostgreSQL test cho insert/update/archive/
  member delete/Owner revoke/missing Owner.
- Build 0 warnings/errors, frontend 17 tests + typecheck/build passed. Unit/Host
  và PostgreSQL test xem kết quả mới nhất trong testing.md; Docker vẫn là yêu
  cầu cho test DB riêng. Không dùng NAS để chạy fixture/destructive test.

## 2026-11-09 — Authorization theo Vehicle và auth recovery/email (#58)

- Catalog/mapping 26 permission, membership resolver, access service và ASP.NET
  policies/resource handler; SystemAdmin không bypass membership.
- Current/vehicle permissions, active-members list; Owner đổi role/soft revoke,
  strict DTO, stale version 409, bảo vệ Owner và audit cùng transaction.
- List/read xe theo membership; Manager update xe; ODO writes giữ Owner-only.
  Khóa hàng xe dùng chung với member/invitation để recheck quyền sau khi chờ lock.
- AccountResponse thêm trạng thái/emailVerified; register pending verification,
  verify/resend/forgot/reset/change password qua Identity token 1h, hash-only
  consumption, revoke mọi session khi đổi/reset; role/lockout refresh mỗi request.
- SMTP qua secrets và invitation email 7d, preview/inbox, accept/reject/revoke,
  email match, single-use, hash-only và audit.
- Giữ cookie/CSRF, thêm rate limit/no-store; frontend nối contract Shared thật,
  verified/access guards, permission context, member/invitation/password UI.
- Build 0 warning/error; 24 unit + 10 Host tests passed. PostgreSQL Testcontainers
  bị chặn do Docker daemon không sẵn sàng; email thật chưa kiểm vì thiếu SMTP.
  Không migrate/seed/mutate NAS hoặc khởi động lại API của owner.
- Chi tiết: [access-control.md](access-control.md).
- Frontend typecheck + 16 Node tests + build passed; readonly Chrome với API thật
  kiểm login/register/validation/guest guard ở 1440/768/360px, không overflow/JS
  exception. API local mới khởi động riêng từ artifacts khi cổng 5080 trống;
  không thay DB hoặc chạy mutation để kiểm UI.

## 2026-11-09 — Typed vehicle management database schema

- Added Domain entities for vehicle membership and typed odometer, fuel and
  maintenance records, with direct Identity-user actor FKs.
- Added EF migration `20261109052305_NewTypedVehicleTables`: backfills vehicle
  creators and Owner memberships, copies every legacy Entry kind, then drops
  `entries`; installs PostgreSQL partial unique indexes and a deferred
  exactly-one-Owner constraint trigger.
- No API/frontend or live/NAS database was changed. PostgreSQL migration tests
  remain dependent on the local Testcontainers/Docker environment.

## 2026-11-09 — Vehicle sharing persistence tables

- Added invitation status/expiry and normalized-email model with hash-only token
  storage and pending-invite uniqueness.
- Added independent reminders, recipient-scoped notifications with snapshots
  and deduplication, and JSON audit history with direct Identity actor FKs.
- Added `AddVehicleSharingTables` migration and persistence coverage; no API,
  frontend, or live/NAS database was changed.

## 2026-11-09 — Cập nhật đặc tả authentication/authorization và sharing (#58)

- Hợp nhất đặc tả owner về account states, bốn vehicle roles, permission/
  authored-by, invitation hash-only/email match/7d và transfer hai bước.
- Bổ sung schema đích/index/transaction/backfill owner legacy, audit và cache/UI
  invalidation; phân biệt current source với API/migration đề xuất.
- Thêm ma trận kiểm thử role/resource/token/race/revoke/transfer; mọi scenario
  mở rộng đang Planned/Not run, không dùng evidence UI giả lập làm DB E2E.
- Khôi phục requirements.md và functional-flows.md vốn chỉ có byte NUL thành
  mô tả auth/access đọc được, không tuyên bố phục hồi nguyên văn tài liệu cũ.
- Chỉ đổi tài liệu/skill/rules; không sửa backend, DTO, EF hoặc live database.

## 2026-11-06 — Scripts vận hành local và kiểm chứng backend runtime

- Thêm add/check/updatedb/checkdb.ps1; dùng chung be.ps1 cho build/tool/đường dẫn và truyền lỗi. Migration name bắt buộc; add/updatedb hỗ trợ WhatIf/Confirm. Check mặc định offline, tùy chọn Database đọc migration history; checkdb chỉ SELECT.
- Thêm run.ps1 chạy FE/BE, chọn service/cổng, chờ HTTP, hiển thị log và quản lý process tự tạo; tái sử dụng dịch vụ sẵn có. Không tự migrate/seed.
- API thực tế tại 5080: live/ready/OpenAPI 200, auth/me 401 khi chưa login. Check migration xác nhận 3 migration đã áp dụng, model không đổi; DatabaseTool check kết nối thành công. Rebuild sửa Npgsql.dll bị toàn NUL trong output DatabaseTool cũ.
- Bổ sung README root với setup, User Secrets, toàn bộ scripts, tham số, cách chạy manual và lệnh EF. Không áp dụng migration/provision hoặc ghi dữ liệu người dùng trong lần kiểm tra này.

## 2026-11-06 — Chuẩn hóa hai project Web và xác nhận Client UI

- API thành `src/iCareX.Web/iCareX.Web.Api/iCareX.Web.Api.csproj`; đổi namespace, solution, ProjectReference và scripts. Giữ UserSecretsId cũ để đọc cấu hình local hiện có.
- Client thành `src/iCareX.Web/iCareX.Web.Client`; cập nhật commands và docs. Chuyển Figma scripts/site config vào Client; root giữ entrypoint chuyển tiếp. Xóa marker runtime `.figaro`.
- Phục hồi hai file frontend toàn NUL từ commit `0e1c3566`, nối lại DTO Shared. Backend build 0 warnings/errors; 12 unit + 3 host tests passed. Frontend typecheck, 3 tests và build đạt; Chrome xác nhận 10 route khách, login dialog và desktop/mobile không exception JavaScript.
- Restore cần truy cập NuGet; host tests cần quyền ghi Data Protection key trong user profile. Không vô hiệu hóa NuGet audit hoặc CSRF. Không apply migration hay thực hiện giao dịch NAS. Chưa chạy PostgreSQL Testcontainers (thiếu Docker) và host/deploy Figma thực tế.


## 2026-11-06 — Đổi tên iCareX và chuẩn hóa repository

- Đổi solution/project/assembly/namespace VehicleCare thành iCareX, sửa ProjectReference và giữ cả hai project test trong src.
- Thêm src/iCareX.Shared, chuyển transport DTO auth/vehicle/catalog/ODO/version từ Application/API vào Shared; giữ service interfaces/lỗi ở Application.
- Sinh TypeScript từ DTO C# tại Shared/typescript, cập nhật Vite/TS alias và thêm generator --check.
- Chuyển SDK local về tools/.local, frontend dependencies/lockfile vào frontend package, guideline/plan/HTTP examples về doc; root package làm lệnh điều phối.
- Phục hồi API/integration csproj và script backend vốn toàn NUL từ bản Git; cập nhật đường dẫn và cấu hình cần cho build. Hai file frontend NUL được giữ để phục hồi từ nguồn đáng tin cậy.
- Giữ UserSecretsId VehicleCare.Api và fallback connection key cũ; không rename database, không apply migration. Cookie/purpose prefix đổi nên cần login lại khi đổi bản chạy.
- Cập nhật CLR namespace trong migration model/designer; EF xác nhận không pending model changes, giữ nguyên ID và DDL.
- Build 8 project thành công, 12 unit +3 host tests passed; Shared contract check/typecheck đạt. Frontend typecheck/test bị chặn NUL; database Testcontainers chưa chạy vì thiếu Docker. Chi tiết tại testing.md.


## 2026-10-31 — Auth/session API

- Đăng ký tài khoản atomically với Identity user, Account và UserSettings mặc định.
- Thêm login/logout/me và CSRF token endpoint dưới `/api/v1/auth`.
- Cookie HttpOnly, SameSite=Lax, session server-side 8 giờ; mỗi request kiểm tra revoke/expiry.
- Cấu hình Identity password policy và lockout 5 lần trong 15 phút; không tự login sau đăng ký.
- Thêm host coverage cho CSRF và unauthenticated `/me`. Các mutation/business API và rate limit vẫn chưa triển khai.
- Refactor auth theo controller + repository/service boundary: shared DTO contract,
  Application interface, Infrastructure implementation, Identity role `User` và
  DI extension. Frontend gọi CSRF/register/login/me/logout thật qua Vite proxy,
  hiển thị notification cho thành công/thất bại.
- Chuyển cấu hình database cục bộ sang .NET User Secrets (`VehicleCare.Api`);
  script backend không còn đọc `.env` và `.env.example` đã được loại bỏ.
- Thêm preflight cho các action cần database để báo chính xác khi thiếu
  `ConnectionStrings:iCareX`, thay vì lỗi EF/connection chung chung.
- Sửa `be.ps1 run` mặc định bind `http://localhost:5080`, tránh đụng port 5000
  đang dùng và khớp với Vite proxy/Scalar documentation.
- Thêm kiểm tra placeholder trong User Secrets và trả HTTP 503 rõ ràng khi auth
  không kết nối được database, thay vì trả HTTP 500 chung chung.
- Sửa kiểm tra User Secrets không nhầm giá trị mật khẩu đã được CLI che thành
  placeholder; API đang chạy trên 5080 được giữ nguyên, không chạy instance thứ hai.
- Tường minh hóa JSON body binding cho register/login và giữ `Content-Type:
  application/json` ổn định trong frontend auth client.
- Tích hợp Serilog console/file rolling cho API và DatabaseTool; thêm request
  logging, auth logging có structured properties và hướng dẫn tránh ghi secret.
- T03: thêm Vehicle CRUD/list/detail, archive/restore, optimistic version
  conflict và ODO manual CRUD theo owner; tạo migration cho version và nối FE
  Vehicle/ODO vào API, loại bỏ garage seed/mock data.
- Chuẩn hóa catalog phương tiện thành VehicleType → VehicleBrand → VehicleModel;
  thêm migration backfill loại xe cũ, catalog GET/Admin API và cập nhật Vehicle
  CRUD nhận model active. Frontend dùng cascading selectors, tên xe vẫn do user
  nhập.
- Tách domain entity và enum thành các file riêng trong `Domain/Entities` để
  dễ bảo trì và giữ entity model tách biệt khỏi EF mapping.
- Chuẩn hóa tài liệu theo blueprint `reference.md` nhưng giữ stack iCareX
  hiện tại; bổ sung quy ước thư mục, naming, frontend teaching stack và custom
  agents workspace cho backend/frontend/QA.
- Refactor backend layout theo tổ chức Domain/Application/Infrastructure/Api
  trong `reference.md`: domain có `Entities/Models/Common/Exceptions`,
  Application có `Abstractions/Features/*/Contracts`, Infrastructure có
  `Persistence/Repositories/Services`, Api có `Controllers/CompositionRoot`.
  Chỉ đổi đường dẫn/namespace, giữ .NET 10, controller, EF/PostgreSQL và
  cookie/Identity runtime.

## 2026-10-30 — Nền tảng đầu tiên

- Thêm solution .NET 10 gồm Domain/Application/Infrastructure/Api, UnitTests/IntegrationTests và DatabaseTool.
- Thêm PostgreSQL mappings, Identity/session/idempotency model, domain ODO/plan rules, migration và health/Scalar.
- Thêm bộ FR/ERD/contract/flow/development/testing; nối hướng dẫn agent từ root và be.
- Chưa triển khai auth/business APIs hoặc nối frontend. Kết quả chạy thực tế tại testing.md.
- Đã tạo/migrate icarex trên PostgreSQL 18.6 và pass persistence smoke (cleanup fixture). 12 unit + 2 host tests pass; database Testcontainers bị chặn do không có Docker daemon.
- Nâng dependency có NuGet advisory lên bản vá; dùng console logging và deferred DI configuration/validate-on-start để HTTP host test không truy cập nhầm cấu hình database thật.
