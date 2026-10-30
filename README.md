# DW System — DrinkWater Backend

ASP.NET Core 10 Web API + EF Core 10 + **PostgreSQL 16**.

## Yêu cầu

- .NET SDK 10 (`dotnet --version`)
- PostgreSQL 16 (đã cài, service `postgresql-x64-16`, cổng 5432)

## 1. Database

Đã tạo sẵn database `drinkwater` với user `postgres`.

```powershell
$env:PGPASSWORD = "postgres"
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -h localhost -U postgres -d drinkwater -c "\dt"
```

Nếu cần tạo lại từ đầu:

```powershell
& "C:\Program Files\PostgreSQL\16\bin\psql.exe" -h localhost -U postgres -d postgres -c "CREATE DATABASE drinkwater"
```

## 2. Cấu hình

`src/DrinkWater.Api/appsettings.Development.json`:

```json
"ConnectionStrings": {
  "Default": "Host=localhost;Port=5432;Database=drinkwater;Username=postgres;Password=<mật-khẩu>"
}
```

- `Jwt:Key` phải dài ≥ 32 ký tự.
- `App:SeedAdmin` tạo admin đầu tiên khi DB chưa có role Admin.

### Cấu hình qua `.env` (khuyến nghị)

Repo có `.env.example` (mẫu) và `.env` (thật). ASP.NET đọc biến môi trường với `__` thay cho `:`,
nên chỉ cần các dòng:

```
ConnectionStrings__Default=Host=localhost;Port=5432;Database=drinkwater;Username=postgres;Password=...
Jwt__Key=<chuỗi ≥ 32 ký tự>
App__SeedAdmin__Password=...
ASPNETCORE_URLS=http://127.0.0.1:5157
Rembg__Url=http://127.0.0.1:7000
```

Chạy kèm nạp `.env`:

```powershell
pwsh "DW System\run-api.ps1"          # mặc định cổng 5157
pwsh "DW System\run-api.ps1" -Port 6000
```

(`run-api.ps1` đọc `.env` → set biến môi trường → `dotnet run`. Production thì set biến môi
trường thật, không dùng file `.env`.)

## 3. Tách nền ảnh cốc (rembg, tuỳ chọn nhưng nên bật)

Ảnh cốc được tách nền **ở server** bằng [rembg](https://github.com/danielgatis/rembg) (model
BiRefNet) — giữ nguyên cốc + nắp + phụ kiện (nơ, móc khoá), chỉ bỏ nền; kết quả là PNG trong suốt
lưu ở `CutoutUrl` và client dùng trực tiếp (không tải model AI về máy người dùng).

```powershell
pip install "rembg[cpu,cli]"     # GPU: rembg[gpu,cli]
pwsh "DW System\run-rembg.ps1"   # http://127.0.0.1:7000
```

Cấu hình trong `appsettings.json` → `Rembg` (`Enabled`, `Url`, `Model` = `isnet-general-use` (nhanh ~1–5 s/ảnh; `birefnet-general` đẹp hơn nhưng rất chậm trên CPU),
`DecontaminateEdges`, `TimeoutSeconds`). Không chạy dịch vụ thì `CutoutUrl` = null và app vẫn
hoạt động (client tự tách bằng model ISNet trong trình duyệt, hoặc thuật toán tại chỗ).

## 4. Chạy

```powershell
# cách 1: nạp .env tự động
pwsh "DW System\run-api.ps1"

# cách 2: chạy trực tiếp (dùng appsettings.Development.json)
dotnet run --project src/DrinkWater.Api
```

- Swagger UI: <http://localhost:5157/swagger>
- Ở Development, app tự `database update` + seed admin khi khởi động.

Admin mặc định (Development): `admin@drinkwater.local` — mật khẩu lấy từ `App__SeedAdmin__Password` trong `.env` (không ghi mật khẩu vào repo).

## 5. Test

```powershell
dotnet test
```

- 36 test: unit (clock, streak, JWT, ảnh, %) + **integration chạy trên PostgreSQL thật**.
- Integration test tạo database tạm `drinkwater_test_<guid>` rồi xoá sau khi chạy, nên
  **cần PostgreSQL đang chạy** với user `postgres` / mật khẩu `postgres`.
  Nếu mật khẩu khác, sửa `tests/DrinkWater.Tests/Integration/PostgresFixture.cs`.

## 6. Build production

```powershell
dotnet publish src/DrinkWater.Api -c Release -o publish
```

Chạy: `dotnet publish/DrinkWater.Api.dll` với biến môi trường `ASPNETCORE_ENVIRONMENT=Production`,
`ConnectionStrings__Default`, `Jwt__Key`. Nhớ cấu hình reverse proxy (IIS/Nginx) trỏ `/hubs`
sang WebSocket và thêm origin frontend vào CORS trong `Program.cs`.

## 7. API

| Module | Endpoint |
|---|---|
| Auth | `POST /api/auth/register` · `POST /api/auth/login` |
| User | `GET /api/user/profile` · `PUT /api/user/profile` · `PUT /api/user/avatar` · `GET /api/user/stats` · `PUT /api/user/targets` · `PUT /api/user/settings` (`RequirePhoto`) · `PUT /api/user/fcm-token` · `GET /api/user/search?q=` |
| Cups | `GET/POST /api/cups` · `PUT/DELETE /api/cups/{id}` · `PUT /api/cups/{id}/image` (tự tách nền → `CutoutUrl`) · `DELETE /api/cups/{id}/image` · `PUT /api/cups/{id}/default` |
| Water | `POST /api/water/log` (multipart, ảnh tuỳ chọn; **400 nếu user bật `RequirePhoto` mà thiếu ảnh**) · `GET /api/water/today` · `GET /api/water/day/{yyyy-MM-dd}` · `GET /api/water/calendar?year=&month=` |
| Social | `POST /api/social/follow/{id}` · `DELETE /api/social/unfollow/{id}` · `GET /api/social/friends` · `PUT /api/social/nickname/{id}` · `GET /api/social/followers` · `GET /api/social/following` · `GET /api/social/feed` · `POST /api/social/nudge/{id}` |
| Follow duyệt | `GET /api/social/follow-requests` · `POST /api/social/follow-requests/{id}/accept` · `POST /api/social/follow-requests/{id}/reject` |
| Partner duyệt ảnh | `PUT /api/user/partner` · `GET /api/social/partner/clients` (kèm `totalDrankMl`, `targetMl`, `cupImageUrl`, `nickname`) · `GET /api/social/partner/me` · `GET /api/social/partner/clients/{userId}/today` · `POST /api/social/partner/clients/{userId}/deduct` · `PUT /api/social/nickname/{userId}` (partner đặt được biệt hiệu cho người mình duyệt) · `GET /api/social/verify/pending` · `POST /api/social/verify/{logId}` |
| Chat | `GET /api/chat/conversations` (bạn bè + tin cuối + số chưa đọc) · `GET /api/chat/{userId}` (100 tin gần nhất, tự đánh dấu đã đọc) · `POST /api/chat/{userId}` `{ body }` (chỉ bạn bè) · `POST /api/chat/{userId}/read` |
| Notifications | `GET /api/notifications` · `PUT /api/notifications/{id}/read` · `PUT /api/notifications/read-all` |
| Admin | `GET /api/admin/users` · `PUT /api/admin/users/{id}/status` · `PUT /api/admin/users/{id}/role` · `POST /api/admin/broadcast` · `GET /api/admin/logs?level=` |
| Hub | `/hubs/notifications` (SignalR, JWT qua `?access_token=`) |

## 8. Ghi chú thiết kế

- Mọi phép tính "hôm nay", chia ca và streak dùng `App:TimeZone` (`Asia/Ho_Chi_Minh`).
- JWT 12 giờ, **không refresh token** ở v1 — hết hạn đăng nhập lại.
- Ảnh lưu qua `IImageStorage` (hiện `LocalDiskStorage` → `wwwroot/uploads`); thay bằng
  Firebase/S3 chỉ cần thêm implementation và đổi DI.
- Push: `IPushSender` với `LogPushSender` (mock); thêm `FirebasePushSender` khi có service account.
- Nudge (`POST /api/social/nudge/{id}`) **không giới hạn** số lần, chỉ cần **là bạn bè** (một trong hai chiều đã chấp nhận) — nhờ vậy partner cũng nhắc được cho khách đang nhờ mình duyệt; mỗi lần tạo 1 notification `Nudge`
  và đẩy realtime (nếu online) hoặc push (nếu offline).
- **Nhắc uống nước tự động**: `Users.ReminderEnabled` / `ReminderStyle` (`gentle`/`normal`/`demon`) /
  `ReminderIntervalMinutes` (15–480) / `ReminderMl` (50–2000) / `ReminderStartHour` (0–23) /
  `ReminderEndHour` (1–24, phải sau giờ bắt đầu), đổi qua `PUT /api/user/settings`.
  `ReminderService` (background, tick 30 s) nhắc khi đã quá khoảng cách kể từ lần uống gần nhất,
  nhắc lại tới khi uống với khoảng cách theo tính cách (nhẹ nhàng 10 phút · trung bình 3 · ác quỷ 1 phút = liên tục),
  chỉ trong khung giờ người dùng chọn (mặc định 07:00–23:00). Gửi SignalR `ReceiveReminder` + `IPushSender`.
  Quy tắc nằm ở `ReminderCadence` (thuần, có unit test) — đổi khung giờ/khoảng cách ở đó.
- **Web Push (VAPID)**: `Users` không cần đổi; bảng `PushSubscriptions` lưu endpoint/p256dh/auth
  của từng thiết bị. `GET /api/push/key` trả khoá công khai, `POST /api/push/subscribe` (và
  `/unsubscribe`) để client đăng ký. `WebPushSender` gửi tới mọi thiết bị của người dùng, tự xoá
  thiết bị đã huỷ (404/410). Cấu hình `WebPush__PublicKey` / `WebPush__PrivateKey` /
  `WebPush__Subject` trong `.env` (tạo cặp khoá bằng `npx web-push generate-vapid-keys`).
  Nhờ vậy nhắc uống nước vẫn tới khi app/tab đã đóng, kể cả PWA trên iOS 16.4+.
- Streak luôn tính lại từ `DailySummaries`; ngày không có log = đứt chuỗi.
- **Follow cần chấp nhận**: `Follows.Status` = `Pending`/`Accepted`; bảng tin chỉ hiện người đã
  chấp nhận. Người kia đã follow mình trước thì tự động `Accepted`. Khi chấp nhận, server tạo
  luôn lượt follow ngược lại (`Accepted`) để hai bên là bạn bè thật và thấy tiến trình của nhau;
  gửi lời mời và chấp nhận đều sinh `Notification` type `Friend` + đẩy realtime qua SignalR.
- **Bắt buộc kèm ảnh**: `Users.RequirePhoto` (mặc định `true`, migration `AddRequirePhotoSetting`).
  Bật thì `POST /api/water/log` không kèm `image` trả 400 (`Bắt buộc kèm ảnh cốc nước`); client cũng
  khoá nút thêm nước. Đổi bằng `PUT /api/user/settings` với `{ "requirePhoto": false }`.
- **Partner duyệt ảnh**: `Users.PartnerId` trỏ tới người duyệt — phải là **bạn bè** (một trong hai
  chiều đã `Accepted`); chọn xong sinh thông báo type `Partner` cho người đó.
  `WaterLogs.Status`:
  - `Unverified` — không có ảnh, **vẫn cộng** ml
  - `Pending` — có ảnh + có partner, **chưa cộng**, chờ partner duyệt
  - `Approved` — đã duyệt, cộng ml
  - `Rejected` — bị từ chối, không cộng
  - `Deducted` — partner trừ nước; `AmountMl` **âm** nên cộng vào tổng là trừ ra
  `Approved` + `Unverified` + `Deducted` vào tổng, % và streak. `CK_WaterLogs_Amount` cho phép
  số âm trong khoảng ±20000; `GET /api/water/today` trả `deductedMl` riêng và tiến độ theo ca chỉ
  tính log dương (không bị âm).
  `POST /api/social/verify/{logId}` nhận `{ "approve": true|false, "message": "..." }` — `message`
  (≤ 200 ký tự, không bắt buộc) thay thế câu thông báo mặc định gửi cho khách; để trống thì dùng
  mặc định ("… ml đã được cộng vào tiến trình của bạn." / "Ảnh … ml không được duyệt…").
- **Ảnh cá nhân & biệt hiệu**: `Users.AvatarUrl` (upload qua `PUT /api/user/avatar`, dùng chung
  `IImageStorage`); `FriendNicknames(OwnerId, TargetId, Nickname)` cho biệt hiệu mỗi người tự đặt
  cho bạn bè — độc lập với quan hệ follow nên ai đang theo dõi bạn cũng đặt được.
- **Ảnh cốc**: `CupPresets.ImageUrl` + `IsDefault`. Ảnh **không** được dựng lại thành mesh 3D —
  client dùng nó làm texture dán quanh một hình trụ (xem `WaterCup3D`). Muốn mô hình thật thì
  cần dịch vụ photogrammetry/AI 3D hoặc cho người dùng tải file `.glb`.
#   D e a r l y - S y s t e m  
 