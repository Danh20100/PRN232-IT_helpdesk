# IT Helpdesk Ticket Management System

Bài tập nhóm PRN: **ASP.NET Core 8 Web API** (JWT) + **SQL Server 2022** (Database First) + **React (Vite)**, chạy bằng Docker.

## Chạy dự án

```bash
docker compose up db -d
```

Đợi SQL Server sẵn sàng (lần đầu khoảng 30–60 giây). Mở **SQL Server Management Studio**:

- Server: `localhost`
- Authentication: SQL Server Authentication
- Login: `sa`
- Password: `Root@12345`

File → Open → `db/ITHelpdesk_DatabaseFirst.sql` → Execute (F5). Script tạo database `ITHelpdeskDB`, các bảng và 6 danh mục. Chạy script lần hai sẽ lỗi vì bảng đã tồn tại; muốn tạo lại thì xóa database `ITHelpdeskDB` trong Object Explorer rồi Execute lại.

Sau đó chạy API (và frontend nếu cần):

```bash
docker compose up --build
```

| Thành phần | Địa chỉ |
|---|---|
| Frontend React | http://localhost:3000 |
| Swagger (API) | http://localhost:8080/swagger |
| SQL Server | `localhost,1433`, login `sa`, password `Root@12345`, DB `ITHelpdeskDB` |

API tự seed 5 tài khoản mẫu khi khởi động, sau khi bảng đã có.
Muốn xóa dữ liệu container: `docker compose down -v` rồi chạy lại và Execute script trong SSMS.

Nếu máy đã có SQL Server chiếm cổng 1433, map Docker sang `14333:1433` và đổi connection string local thành `localhost,14333`.

### Chạy dev không qua Docker
```bash
docker compose up db -d                     # chỉ SQL Server
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

- Schema chạy trong SSMS bằng `db/ITHelpdesk_DatabaseFirst.sql`. `db/init.sql` là bản MySQL cũ, container không dùng file đó.
- Backend **không** tạo/migrate bảng. `Data/AppDbContext.cs` chỉ map entity vào tên bảng/cột có sẵn.
- `TicketAttachments` và `Notifications` đã có trong DB nhưng chưa có API (Phase 2).

## Kịch bản demo
1. **User** login → *+ Tạo ticket* (Network) → `TCK-0001`, Open.
2. **Admin** → mở ticket → chọn Staff, *Phân công*.
3. **Staff A** → *Bắt đầu xử lý* → trao đổi qua bình luận → nhập ghi chú, *Đã xử lý xong*.
4. **User** → *Xác nhận đã hết lỗi* (hoặc *Vẫn còn lỗi*) → gửi đánh giá 5 sao.
5. **Admin** → Dashboard xem thống kê.

## Cấu trúc
```
db/ITHelpdesk_DatabaseFirst.sql   schema SQL Server + dữ liệu danh mục
HelpdeskApi/         Controllers, Services (TicketService = workflow + phân quyền), Models, Dtos, Data
frontend/src/        pages (Login, Dashboard, Tickets, NewTicket, TicketDetail, Admin), api.js, auth.jsx
```
Mọi kiểm tra quyền/workflow nằm ở backend; React chỉ ẩn/hiện nút cho tiện.
