# IT Helpdesk Ticket Management System

Bài tập nhóm PRN: **ASP.NET Core 8 Web API** (JWT) + **MySQL 8** (Database First) + **React (Vite)**, chạy bằng Docker.

## Chạy dự án

```bash
docker compose up --build
```

| Thành phần | Địa chỉ |
|---|---|
| Frontend React | http://localhost:3000 |
| Swagger (API) | http://localhost:8080/swagger |
| MySQL | `localhost:3307`, user `root`, pass `root123`, DB `ITHelpdeskDB` |

Lần đầu MySQL tự chạy `db/init.sql` (tạo bảng + 6 danh mục); API tự seed 5 tài khoản mẫu khi khởi động.
Muốn tạo lại DB từ đầu: `docker compose down -v` rồi chạy lại.

### Chạy dev không qua Docker
```bash
docker compose up db -d                     # chỉ MySQL
cd HelpdeskApi && dotnet run                # API  :8080
cd frontend && npm install && npm run dev   # React :5173 (proxy /api -> :8080)
```

## Tài khoản mẫu

| Role  | Email                  | Mật khẩu   |
|-------|------------------------|------------|
| Admin | admin@helpdesk.local   | Admin@123  |
| Staff | staff1@helpdesk.local  | Staff@123  |
| Staff | staff2@helpdesk.local  | Staff@123  |
| User  | user1@helpdesk.local   | User@123   |
| User  | user2@helpdesk.local   | User@123   |

## Database First

- `db/init.sql` là bản MySQL của `ITHelpdesk_DatabaseFirst.sql` (giữ nguyên tên bảng, cột, ràng buộc CHECK/UNIQUE/FK).
- Backend **không** tạo/migrate bảng. `Data/AppDbContext.cs` chỉ map entity vào tên bảng/cột có sẵn.
- `TicketAttachments` và `Notifications` đã có trong DB nhưng chưa có API (Phase 2).
- Nếu nhóm dùng SQL Server thật: đổi package `Pomelo.EntityFrameworkCore.MySql` thành `Microsoft.EntityFrameworkCore.SqlServer`, `UseMySql(...)` thành `UseSqlServer(...)`, và chạy file SQL gốc của nhóm. Entity và controller giữ nguyên.

## Kịch bản demo
1. **User** login → *+ Tạo ticket* (Network) → `TCK-0001`, Open.
2. **Admin** → mở ticket → chọn Staff, *Phân công*.
3. **Staff A** → *Bắt đầu xử lý* → trao đổi qua bình luận → nhập ghi chú, *Đã xử lý xong*.
4. **User** → *Xác nhận đã hết lỗi* (hoặc *Vẫn còn lỗi*) → gửi đánh giá 5 sao.
5. **Admin** → Dashboard xem thống kê.

## Cấu trúc
```
db/init.sql          schema MySQL + dữ liệu danh mục
HelpdeskApi/         Controllers, Services (TicketService = workflow + phân quyền), Models, Dtos, Data
frontend/src/        pages (Login, Dashboard, Tickets, NewTicket, TicketDetail, Admin), api.js, auth.jsx
```
Mọi kiểm tra quyền/workflow nằm ở backend; React chỉ ẩn/hiện nút cho tiện.
