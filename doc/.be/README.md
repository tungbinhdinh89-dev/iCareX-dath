# Hướng dẫn backend iCareX

Fuel/service #32/#33/#42: [API, schema, replay và form](cost-records.md).
Đã có source trên nhánh feature; PostgreSQL/browser E2E còn bị chặn Docker.

Vehicle registration mới: [schema, constraints và kế hoạch migration](vehicle-registration-schema.md).
Ngày 09/11/2026 đã xác nhận migration `20261109090758` và `20261109123244`
có trong lịch sử database. API local đã restart theo schema mới; backend,
Shared contracts và frontend phải được triển khai cùng phiên bản.

Runtime auth/access #58: [contract, SMTP, policies và transaction](access-control.md).
Đây là nguồn hiện trạng mới cho authentication, membership và invitation;
các mô tả baseline owner-only bên dưới cần đọc theo mốc lịch sử.

Đọc [AGENTS.md](../../AGENTS.md), [kiến trúc](architecture.md), [API](api-contracts.md), [dữ liệu](data-model.md) và [kiểm thử](testing.md) trước sửa backend. Cấu trúc đã chuyển khỏi be/; source và test ở src/.

## Project và trách nhiệm

| Project | Vai trò |
| --- | --- |
| src/iCareX.Domain | Entity/enum và domain rules thuần. |
| src/iCareX.Shared | DTO public dùng chung, TypeScript được sinh cho frontend. |
| src/iCareX.Application | Service interfaces, internal contracts, ports và lỗi use case. |
| src/iCareX.Infrastructure | EF/migrations, repository, Identity và service implementation. |
| src/iCareX.Web/iCareX.Web.Api | Controllers, auth middleware, health/OpenAPI và DI. |
| src/iCareX.UnitTests | Domain/architecture tests. |
| src/iCareX.IntegrationTests | Host tests và PostgreSQL Testcontainers. |
| tools/iCareX.DatabaseTool | Check/provision/smoke khi được gọi có chủ đích. |

Solution iCareX.slnx có đủ 8 project, bao gồm Shared và DatabaseTool. [Cây đầy đủ](../structure-architecture.md) mô tả frontend, scripts, docs và metadata.

## Cấu hình database và tương thích

Backend dùng .NET User Secrets hoặc environment. UserSecretsId vẫn là **VehicleCare.Api** ở API/Infrastructure/DatabaseTool để đọc store local đã có. Key mới **ConnectionStrings:iCareX** ưu tiên; **ConnectionStrings:VehicleCare** là fallback. Không copy secret vào source, VITE_* hoặc log.

```powershell
dotnet user-secrets --project src/iCareX.Web/iCareX.Web.Api set "ConnectionStrings:iCareX" "<local-connection-string>"
```

Việc đổi tên ứng dụng không đổi database NAS hiện có **vehiclecare**, không provision/migrate tự động và không thay container/volume. DatabaseTool provision chỉ cho tên icarex hoặc vehiclecare và chỉ chạy khi được gọi riêng. Ngày 06/11/2026 đã xác nhận kết nối bằng health ready và DatabaseTool check; không ghi dữ liệu.

## Build, test và chạy

PostgreSQL local có thể khởi tạo bằng `docker-compose up -d` từ root với
[docker-compose.yml](../../docker-compose.yml): PostgreSQL 18 Alpine, database
`icarex`, user/password phát triển `admin`/`123456`, loopback port 5432 và volume
bền vững. Xem [hướng dẫn Docker Desktop](../../README.md#database-local-bằng-docker-desktop)
để đặt connection environment, đổi cổng và áp dụng migration chủ động.
Compose không tự migrate/seed hoặc thay cấu hình database NAS/User Secrets.

Chạy từ root; SDK theo global.json. Script ưu tiên dotnet trên PATH; SDK cục bộ fallback ở tools/.local/dotnet.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/be.ps1 restore
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/be.ps1 build
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/be.ps1 test
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/be.ps1 run
```

Action test chạy UnitTests và Category=Host, không cần Docker. Action integration chạy cả PostgreSQL Testcontainers và cần Docker; không dùng NAS cho automated tests. Các action migration/migrate/check/provision/smoke tác động theo contract riêng; không phải bước mặc định kiểm tài liệu hoặc đổi tên.

Các entrypoint ngắn: `scripts/add.ps1 <Name>` tạo migration, `scripts/check.ps1` liệt kê offline và so model/snapshot, thêm `-Database` để đọc applied/pending; `scripts/updatedb.ps1` áp dụng migration; `scripts/checkdb.ps1` kiểm kết nối bằng SELECT; `scripts/run.ps1` chạy FE/BE hoặc `-Only Frontend|Backend`. Add/update hỗ trợ `-WhatIf`/`-Confirm`. Run không migrate, tái sử dụng dịch vụ đang chạy và chỉ dừng process do chính nó tạo khi Ctrl+C. Tham số cổng, log, lệnh thủ công và quy trình đầy đủ ở [README root](../../README.md).

Run mặc định bind http://localhost:5080 nếu ASPNETCORE_URLS chưa có; frontend Vite proxy /api tới API này. Live kiểm process, ready kiểm kết nối DB và không chứng nhận schema; /openapi/v1.json và /scalar/v1 chỉ Development.

## DTO, Identity và logging

Đọc [auth/access nghiệp vụ](../authentication-authorization.md),
[luồng](functional-flows.md), [API](api-contracts.md), [schema sharing](../schema-database.md#auth-sharing-schema)
và [test matrix](testing.md#access-test-matrix) cho thiết kế đích mới.
Owner/Manager/Contributor/Viewer là role từng xe từ active membership, khác
Identity role Admin. Hiện source vẫn owner-only; chưa có membership/invitation/
transfer/audit/email verification. Account status và email verification là
phần đích cần policy, không tự bật từ ví dụ đặc tả.

DTO nằm trong iCareX.Shared.Contracts.Identity/Vehicles. Service interfaces ở Application/Features; entity/enum ở Domain/Entities; mapping ở Infrastructure/Persistence. Chạy generator TypeScript và --check khi đổi DTO.

Auth hiện dùng Identity + cookie/session DB 8 giờ, không sliding; cookie/purpose prefix hiện là iCareX/icarex. Đăng ký chưa tự login, tạo User role, Account và Settings cùng transaction; mật khẩu tối thiểu 12 ký tự theo policy code và lockout 5 lần/15 phút. Source Wiki định hướng JWT là gap đã ghi trong bộ doc; refactor này không chuyển JWT.

Đổi prefix cookie làm cần đăng nhập lại sau chuyển bản chạy. Giữ Data Protection key ring phù hợp môi trường khi deploy; không đưa vào Git. Host test trong sandbox có thể bị chặn ghi key ở user profile; phải báo đúng lỗi quyền, không ghi thành Pass.

Serilog ghi console và file rolling trong content root API/logs, prefix icarex-. Log request chứa method/path/status/elapsed; auth dùng email đã che và ID. Không ghi password, token, cookie, body nhạy cảm hoặc connection string.

## Hiện trạng và kiểm chứng

Build solution đạt 0 warning/0 error; 12 unit và 3 host tests passed ngày 06/11/2026. EF xác nhận model không đổi so với migration cuối; không áp dụng migration. Chi tiết lệnh/giới hạn ở [testing.md](testing.md).

Fuel/service/plan/inbox APIs đầy đủ vẫn là phần còn phát triển. Hai source file frontend NUL đã được phục hồi; UI khách chạy được. Backend live/ready/OpenAPI trả 200, auth/me trả 401 khi chưa login; proxy frontend chuyển request tới backend thành công. Chưa kiểm thử giao dịch người dùng có đăng nhập. Mỗi backend change phải cập nhật các trang liên quan và [changelog](changelog.md).
