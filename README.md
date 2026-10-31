# IT Helpdesk Ticket Management System

Bài tập nhóm PRN232: ASP.NET Core 8 (JWT, EF Core, SQL Server 2022) + Redis Pub/Sub + gRPC + BackgroundService. React (Vite) là phần thêm, nằm ở thư mục `fontend` cạnh repo này.

## Kiến trúc

```
React --> HelpdeskApi (REST, JWT)
            |-- SQL Server (EF Core, Database First)
            |-- gRPC Helpdesk.Priority (priority + SLA khi tạo ticket)
            |-- Redis channel ticket.status.changed
HelpdeskWorker (BackgroundService) <-- Redis --> bảng Notifications
```

API → Service (`TicketService`, `UserService`, `CategoryService`) → `AppRepository` → `AppDbContext`.

## Công nghệ

ASP.NET Core 8, Entity Framework Core, SQL Server, JWT, Swagger, Dependency Injection, Redis Pub/Sub, gRPC, `BackgroundService`, Docker Compose.

## Cài đặt

Cần Docker Desktop và (để chạy script DB) SQL Server Management Studio.

1. Bật SQL Server:

```bash
docker compose up db -d
```

Đợi lần đầu khoảng 30–60 giây. SSMS: server `localhost`, SQL Authentication, login `sa`, password `Root@12345`. Mở `db/ITHelpdesk_DatabaseFirst.sql` rồi Execute. Script tạo `ITHelpdeskDB`, bảng và 6 danh mục. Chạy lần hai sẽ lỗi vì bảng đã có; muốn tạo lại thì xóa database rồi Execute lại.

2. Dựng cả hệ:

```bash
docker compose up --build
```

| Thành phần | Địa chỉ |
|---|---|
| Swagger | http://localhost:8080/swagger |
| gRPC priority | `localhost:5001` (HTTP/2) |
| Redis | `localhost:6379`, channel `ticket.status.changed` |
| SQL Server | `localhost,1433`, DB `ITHelpdeskDB` |
| React | http://localhost:5173 khi chạy `npm run dev` trong `../fontend` |

Compose gồm `db`, `redis`, `priority`, `worker`, `api`. Service `web` đang tắt vì frontend nằm ngoài repo.

API seed 5 tài khoản khi khởi động, sau khi bảng đã có. Xóa volume: `docker compose down -v`, rồi Execute lại script SQL.

Nếu cổng 1433 đã bị SQL Server trên máy chiếm, đổi map thành `14333:1433` và connection string local thành `localhost,14333`.

### Chạy dev không Docker hóa API

```bash
docker compose up db redis priority -d
cd HelpdeskApi && dotnet run
cd HelpdeskWorker && dotnet run
cd ../fontend && npm install && npm run dev
```

`appsettings.json` trỏ Redis `localhost:6379` và gRPC `http://localhost:5001`.

## Luồng liên service

- Tạo ticket: API gọi gRPC. Network/Security → High, SLA 8 giờ. Hardware/Printer → Medium, 24 giờ. Còn lại → Low, 72 giờ. `DueDate` = thời điểm tạo + SLA. gRPC không lên thì giữ priority client gửi và ghi log.
- Đổi trạng thái: API publish Redis. Worker ghi `Notifications` cho người tạo và staff được giao. Redis không lên thì đổi trạng thái vẫn lưu, chỉ mất thông báo.

Danh sách ticket: `search`, `status`, `categoryId`, `priority`, `sort` (`created` | `priority` | `status`), `page`, `pageSize`.

## Tài khoản mẫu

| Role | Email | Mật khẩu |
|---|---|---|
| Admin | admin@helpdesk.local | Admin@123 |
| Staff | staff1@helpdesk.local | Staff@123 |
| Staff | staff2@helpdesk.local | Staff@123 |
| User | user1@helpdesk.local | User@123 |
| User | user2@helpdesk.local | User@123 |

## Database First

Schema do `db/ITHelpdesk_DatabaseFirst.sql` tạo. `db/init.sql` là bản MySQL cũ, container không dùng. Backend không migrate; `AppDbContext` chỉ map bảng có sẵn. `TicketAttachments` chưa có API. `Notifications` do worker ghi, chưa có API đọc.

## Demo

1. User login → tạo ticket danh mục Network → mã `TCK-0001`, priority High, DueDate +8 giờ (gRPC).
2. Admin phân công Staff.
3. Staff chuyển InProgress → Resolved (có ghi chú). Mỗi lần đổi trạng thái, worker thêm dòng `Notifications`.
4. User đóng ticket (Closed) hoặc mở lại, rồi gửi đánh giá.
5. Admin xem dashboard.

## Cấu trúc

```
db/ITHelpdesk_DatabaseFirst.sql
HelpdeskApi/          REST, repository, Redis producer, gRPC client
Helpdesk.Priority/    gRPC server
HelpdeskWorker/       Redis consumer
```

## Trách nhiệm thành viên

Điền tên trước khi nộp.

| Việc | Thành viên |
|---|---|
| REST API, JWT, repository | |
| gRPC priority | |
| Redis + worker | |
| Docker, README, demo | |
