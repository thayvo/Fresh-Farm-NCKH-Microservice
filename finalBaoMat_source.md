# HƯỚNG DẪN KIỂM THỬ BẢO MẬT TỔNG HỢP CHO HỆ THỐNG FRESHFARM

Ngày tổng hợp: 19/03/2026

## 1. Mục tiêu tài liệu

Tài liệu này được tạo để phục vụ đồng thời 3 mục tiêu:

- Tổng hợp các cơ chế bảo mật hiện có trong mã nguồn FreshFarm.
- Chỉ ra các điểm rủi ro, các nơi cần ưu tiên kiểm thử và các lỗ hổng có dấu hiệu rõ ràng từ source code.
- Cung cấp bộ hướng dẫn test chi tiết bằng tiếng Việt để có thể dùng trực tiếp khi kiểm thử thực tế, chụp minh chứng và viết báo cáo.

## 2. Nguồn tổng hợp

Tài liệu cuối được tổng hợp từ 2 nhóm nguồn:

- Mã nguồn trong repo FreshFarm, tập trung vào:
  - `src/Web/FreshFarm.Web.Bff`
  - `src/Services/Identity/FreshFarm.Identity.API`
  - `src/Services/Catalog/FreshFarm.Catalog.Api`
  - `src/Services/Ordering/FreshFarm.Ordering.Api`
- Các tài liệu Word hiện có trong thư mục `docs/`:
  - `Bao cao ket qua test bao mat va mau test.docx`
  - `GiamDDoS.docx`
  - `GiamDDoS v2.docx`
  - `Huong dan test bao mat da lam v4.docx`
  - `Tong hop bao mat va Cloudflare.docx`

Lưu ý:

- Một số mục trong tài liệu cũ là kết quả kiểm thử đã thực hiện trước đó.
- Tài liệu này ưu tiên đưa kết luận dựa trên bằng chứng từ source code; các mục vận hành hoặc Cloudflare cần xác nhận lại trên môi trường thật khi chạy test.

## 3. Phạm vi hệ thống được rà soát

- FreshFarm.Web.Bff: lớp web MVC/BFF, session, cookie auth, anti-forgery, rate limiting, checkout, VNPay, seller/admin UI.
- FreshFarm.Identity.API: đăng nhập, đăng ký, email verification, forgot/reset password, lockout, JWT, 2FA, auth audit, GeoIP.
- FreshFarm.Catalog.Api: danh mục, sản phẩm, route quản trị catalog, xác thực service nội bộ.
- FreshFarm.Ordering.Api: giỏ hàng, đơn hàng, ownership check, thanh toán, đối soát VNPay, route seller/admin.

## 4. Tóm tắt các cơ chế bảo mật đã có trong mã nguồn

### 4.1. Kiểm tra đầu vào phía server

- Nhiều luồng API và form đã có validation model.
- Các luồng đăng nhập, đăng ký, danh mục, reset password, profile, cart, checkout đều có xử lý dữ liệu rỗng hoặc không hợp lệ ở backend.

### 4.2. Xác thực và phân quyền

- BFF dùng cookie authentication cho web.
- Identity, Catalog và Ordering dùng JWT bearer cho API.
- Có policy theo vai trò như `AdminOnly`, `SellerOnly`, `SellerOrAdmin`.
- Buyer order detail có ownership check để ngăn xem đơn của người khác.

### 4.3. Khóa tài khoản khi đăng nhập sai nhiều lần

- Identity có `MaxFailedLoginAttempts = 5`.
- Sau nhiều lần đăng nhập sai liên tiếp, tài khoản bị khóa tạm thời 15 phút.
- Có ghi audit và log cho các sự kiện failed, locked, success.

### 4.4. Email verification, quên mật khẩu, đặt lại mật khẩu

- Tài khoản local phải xác minh email trước khi đăng nhập.
- Có luồng resend verification.
- Có luồng forgot password và reset password bằng token gửi qua email.
- Response kiểu “nếu tài khoản tồn tại…” giúp giảm nguy cơ lộ danh sách email.

### 4.5. Xác thực hai bước 2FA cho Admin và Seller

- Admin và Seller có luồng 2FA dựa trên TOTP.
- Có ticket trung gian cho bước xác minh 2FA.
- Có hỗ trợ thiết lập secret cho lần đầu.

### 4.6. Chống CSRF cho form web

- Nhiều action POST trên BFF có `[ValidateAntiForgeryToken]`.
- Áp dụng trên sign in, sign up, logout, resend verification, forgot/reset password, profile update, address book và nhiều form quản trị.

### 4.7. Cookie và session hardening

- Cookie auth và cookie session dùng `HttpOnly`.
- `SecurePolicy = Always`.
- `SameSite = Lax`.
- Có cấu hình idle timeout ở session và authentication lifetime.

### 4.8. Security headers và HTTPS

- BFF có `UseHttpsRedirection`.
- Ngoài môi trường dev có `UseHsts`.
- Response có các header:
  - `X-Content-Type-Options: nosniff`
  - `X-Frame-Options: SAMEORIGIN`
  - `Referrer-Policy: strict-origin-when-cross-origin`
  - `Permissions-Policy`
- Có Content Security Policy.

### 4.9. Rate limiting và telemetry

- BFF có global rate limiter và thêm các policy riêng cho:
  - `auth-form`
  - `password-recovery`
  - `public-read`
  - `search-read`
  - `cart-write`
  - `review-write`
  - `checkout-read`
  - `checkout-write`
  - `ghn-read`
- Có logging chi tiết khi bị chặn 429.
- Có endpoint admin xem snapshot telemetry rate limit.

### 4.10. Logging, auth audit và GeoIP

- Identity có `AuthAuditService`.
- Có controller admin để xem log auth.
- Audit lưu thêm IP, forwarded-for, quốc gia, vùng, thành phố, user-agent, device, browser, OS và cờ suspicious.

### 4.11. Bảo vệ thanh toán VNPay

- BFF có validate chữ ký VNPay bằng HMAC SHA512.
- Ordering có logic finalize để chặn:
  - TxnRef không khớp.
  - Giao dịch trùng trên đơn khác.
  - Callback lặp lại.
  - Amount mismatch.
  - Callback đến sau khi reservation đã hết hạn.

### 4.12. SQL Injection hiện có rủi ro thấp hơn ở các phần đã rà

- Chưa thấy dấu hiệu dùng `FromSqlRaw`, `ExecuteSqlRaw`, `SqlCommand`, `Dapper` trong các phần nguồn đã rà soát cho luồng chính.
- Nhiều truy vấn đang đi theo EF Core / LINQ.
- Tuy nhiên vẫn phải test hồi quy trên input công khai và các filter/query string.

## 5. Các phát hiện và trọng tâm kiểm thử ưu tiên

### 5.1. R1 - Nguy cơ tampering giá và tổng tiền đơn hàng từ phía client

Mức độ ưu tiên: Rất cao

Mô tả:

- Dữ liệu `UnitPrice` đang được chấp nhận từ cart/session/query/request thay vì được tính lại hoàn toàn từ nguồn giá tin cậy ở backend.
- Nếu attacker sửa request trước khi tạo đơn, backend có thể ghi nhận đơn với giá thấp hơn giá thật.

Dấu hiệu từ source code:

- `src/Web/FreshFarm.Web.Bff/Controllers/CheckoutController.cs`
  - `BuildCheckoutModelFromCartAsync()` giữ nguyên `UnitPrice` từ cart item.
  - `NormalizeRequest()` chỉ chuẩn hóa số âm về 0, không đối chiếu lại với giá thật từ Catalog.
  - `ParseDirectCheckoutItemFromQuery()` đọc `unitPrice` trực tiếp từ query string.
- `src/Web/FreshFarm.Web.Bff/Services/CartSessionService.cs`
  - Cart/session vẫn lưu và merge `UnitPrice`.
  - `BackfillMissingSnapshotsAsync()` chỉ bổ sung snapshot thiếu, không ép đồng bộ lại giá thật.
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/CartController.cs`
  - `CreateCartItem()` và `ApplySnapshot()` ghi `UnitPrice = item.UnitPrice`.
- `src/Services/Ordering/FreshFarm.Ordering.Api/Controllers/OrdersController.cs`
  - `itemsAmount = request.Items.Sum(item => item.UnitPrice * item.Quantity)`.
  - `totalAmount = itemsAmount + shippingFee`.

Rủi ro:

- Mua hàng sai giá.
- Gian lận thanh toán.
- Sai lệch doanh thu, đối soát và báo cáo.
- Nếu VNPay được thanh toán theo tổng tiền đã bị hạ thấp trước đó thì logic “amount mismatch” vẫn không bảo vệ được vì giá trị gốc trong order đã bị thao túng từ trước.

Kỳ vọng an toàn:

- Ordering phải tự lấy giá sản phẩm từ Catalog hoặc dữ liệu giá đã ký/xác thực phía server.
- Không được tin `UnitPrice` do client gửi lên.

### 5.2. R2 - Stored XSS trong màn hình quản lý sản phẩm của Seller

Mức độ ưu tiên: Rất cao

Mô tả:

- Seller product detail modal đang render HTML từ dữ liệu sản phẩm bằng `.html(...)`.
- Nếu tên sản phẩm hoặc mô tả chứa payload HTML/JS, payload có thể chạy khi mở modal.

Dấu hiệu từ source code:

- `src/Web/FreshFarm.Web.Bff/Areas/Seller/Controllers/ProductController.cs`
  - `GetProductDetails(int id)` trả JSON chứa:
    - `ProductName`
    - `ShortDescription`
    - `LongDescription`
    - `CategoryName`
    - các trường text khác
- `src/Web/FreshFarm.Web.Bff/Areas/Seller/Views/Product/ManageProducts.cshtml`
  - Dùng template literal JS chèn `${p.ProductName}`, `${p.ShortDescription}`, `${p.LongDescription}`, `${response.message}`.
  - Sau đó gán thẳng `$('#productDetailsContent').html(html)`.

Rủi ro:

- Stored XSS ở khu backoffice.
- Chiếm phiên của seller/admin nếu có thể thực thi script.
- Đọc dữ liệu nhạy cảm từ DOM.
- Thực hiện hành động trái phép trong ngữ cảnh người đăng nhập.

Kỳ vọng an toàn:

- Encode hoặc sanitize dữ liệu trước khi render.
- Không gán raw HTML bằng `.html(...)` với dữ liệu người dùng nếu chưa sanitize.

### 5.3. R3 - XSS qua `Html.Raw(TempData["SuccessMessage"])` và `Html.Raw(TempData["ErrorMessage"])`

Mức độ ưu tiên: Cao

Mô tả:

- Một số view seller render `TempData` bằng `Html.Raw`.
- Trong luồng edit sản phẩm, success message ghép trực tiếp `product.ProductName` vào chuỗi HTML.

Dấu hiệu từ source code:

- `src/Web/FreshFarm.Web.Bff/Areas/Seller/Controllers/ProductController.cs`
  - `TempData["SuccessMessage"] = $"✅ Cập nhật sản phẩm <strong>{product.ProductName}</strong> thành công!"`
- `src/Web/FreshFarm.Web.Bff/Areas/Seller/Views/Product/Create.cshtml`
  - `@Html.Raw(TempData["ErrorMessage"])`
  - `@Html.Raw(TempData["SuccessMessage"])`
- `src/Web/FreshFarm.Web.Bff/Areas/Seller/Views/Product/Edit.cshtml`
  - `@Html.Raw(TempData["ErrorMessage"])`
  - `@Html.Raw(TempData["SuccessMessage"])`

Rủi ro:

- Nếu `ProductName` chứa payload HTML/JS, message sau redirect có thể trở thành vector XSS.

### 5.4. R4 - Upload ảnh sản phẩm mới kiểm tra đuôi file và dung lượng

Mức độ ưu tiên: Cao

Mô tả:

- Dịch vụ lưu ảnh chỉ kiểm tra extension và kích thước.
- Chưa thấy kiểm tra MIME type thực tế hoặc magic bytes.

Dấu hiệu từ source code:

- `src/Web/FreshFarm.Web.Bff/Services/ProductImageStorageService.cs`
  - Allow list extension: `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`
  - Max size 5 MB
  - Save bằng tên GUID + extension
  - Chưa thấy verify nội dung file thật sự là ảnh

Rủi ro:

- Upload file giả mạo nội dung.
- Tăng rủi ro lưu trữ nội dung không đúng loại.
- Nếu sau này web server hoặc downstream xử lý file sai cách có thể phát sinh thêm lỗ hổng.

### 5.5. R5 - Secrets và cấu hình nhạy cảm đang nằm trong repo

Mức độ ưu tiên: Cao

Mô tả:

- Trong repo đang có các giá trị dev/nội bộ như JWT key, internal service key, token GHN.

Dấu hiệu từ source code và config:

- `src/Services/Identity/FreshFarm.Identity.API/appsettings.Development.json`
  - Có `Jwt:Key`
- `src/Services/Catalog/FreshFarm.Catalog.Api/appsettings.Development.json`
  - Có `Services:Internal:ServiceKey`
- `src/Services/Catalog/FreshFarm.Catalog.Api/appsettings.json`
  - Có `Services:Internal:ServiceKey`
- `src/Services/Ordering/FreshFarm.Ordering.Api/appsettings.Development.json`
  - Có `Services:InternalAuth:InternalServiceKey`
- `src/Services/Ordering/FreshFarm.Ordering.Api/appsettings.json`
  - Có `Services:Catalog:InternalServiceKey`
- `src/Web/FreshFarm.Web.Bff/appsettings.Development.json`
  - Có `Services:Ordering:InternalServiceKey`
  - Có `ShippingProviders:GhnSandbox:Token`, `ShopId`

Rủi ro:

- Rò rỉ secret khi chia sẻ repo hoặc build artifact.
- Tăng khả năng giả lập internal service request nếu key không được rotate.
- Tạo thói quen xấu về secret management.

### 5.6. R6 - CSP ở Admin và Seller đang ở chế độ report-only

Mức độ ưu tiên: Trung bình đến cao

Mô tả:

- Với path `/Admin`, `/Seller` và `/swagger`, BFF đang trả `Content-Security-Policy-Report-Only` thay vì CSP enforce.
- Điều này giúp quan sát vi phạm nhưng chưa chặn script trái phép ở mức browser policy.

Dấu hiệu từ source code:

- `src/Web/FreshFarm.Web.Bff/Program.cs`
  - Admin/Seller/Swagger dùng `Content-Security-Policy-Report-Only`
  - Buyer/public dùng `Content-Security-Policy`

Rủi ro:

- Khi có XSS ở khu backoffice, browser không có lớp chặn CSP enforce mạnh như public area.

### 5.7. R7 - SQL Injection chưa thấy dấu hiệu rõ trong source, nhưng vẫn phải test hồi quy

Mức độ ưu tiên: Trung bình

Mô tả:

- Từ các phần đã rà soát, chưa thấy raw SQL trực tiếp.
- Tuy nhiên vẫn phải test hồi quy tại tất cả điểm nhập liệu công khai, query string và filter.

Kỳ vọng an toàn:

- Input bị xử lý như dữ liệu bình thường.
- Không lộ stack trace SQL.
- Không có thay đổi số lượng dữ liệu bất thường.

## 6. Hướng dẫn test chi tiết theo nhóm

### 6.1. Nhóm xác thực, tài khoản và chiếm quyền

Mã test: AUTH-01

Mục tiêu: Kiểm tra validation backend cho đăng nhập và đăng ký.

Thành phần liên quan:

- Identity API
- BFF sign in / sign up

Tiền điều kiện:

- Có quyền gọi form web hoặc API test bằng Postman/Swagger.

Bước thực hiện:

1. Gửi login với `identifier = ""`, `password = ""`, `clientLane = ""`.
2. Gửi register với username rỗng hoặc password/confirm không khớp.
3. Bypass giao diện bằng cách gửi request trực tiếp tới API.

Payload gợi ý:

- `{"identifier":"","password":"","clientLane":""}`
- `{"userName":"","email":"abc","password":"123","confirmPassword":"456"}`

Kết quả mong đợi:

- Server trả lỗi validation rõ ràng.
- Không tạo dữ liệu lỗi trong database.

Minh chứng cần chụp:

- Request mẫu.
- Response lỗi.

Mã test: AUTH-02

Mục tiêu: Kiểm tra lockout sau nhiều lần đăng nhập sai.

Thành phần liên quan:

- `src/Services/Identity/FreshFarm.Identity.API/Controllers/AuthController.cs`

Tiền điều kiện:

- Có một tài khoản active, đã xác minh email.

Bước thực hiện:

1. Thử đăng nhập sai mật khẩu liên tiếp 5 lần.
2. Thử đăng nhập lần 6 bằng đúng mật khẩu.
3. Theo dõi response và audit/log.

Kết quả mong đợi:

- Sau ngưỡng sai, tài khoản bị khóa tạm thời.
- Response báo đang bị khóa.
- Có audit event kiểu failed/locked.

Minh chứng cần chụp:

- 5 lần request sai.
- Response lockout.
- Bản ghi auth audit hoặc log server.

Mã test: AUTH-03

Mục tiêu: Kiểm tra email chưa xác minh không thể đăng nhập.

Bước thực hiện:

1. Tạo tài khoản local mới.
2. Không bấm xác minh email.
3. Thử đăng nhập.

Kết quả mong đợi:

- Bị chặn đăng nhập.
- Thông báo yêu cầu xác minh email.

Minh chứng cần chụp:

- Form đăng nhập.
- Response hoặc UI message.

Mã test: AUTH-04

Mục tiêu: Kiểm tra resend verification không làm lộ user enumeration và có cooldown ở giao diện.

Bước thực hiện:

1. Thử resend với tài khoản có thật nhưng chưa verify.
2. Thử resend với tài khoản không tồn tại.
3. Quan sát thông báo UI và tốc độ gửi lại.

Kết quả mong đợi:

- Hai trường hợp đều trả thông điệp chung theo hướng an toàn.
- UI có cooldown, tránh spam.

Minh chứng cần chụp:

- Request hợp lệ.
- Request với user không tồn tại.
- Đồng hồ cooldown nếu có.

Mã test: AUTH-05

Mục tiêu: Kiểm tra forgot password không làm lộ email có tồn tại hay không.

Thành phần liên quan:

- `POST /auth/forgot-password`
- BFF `ForgotPassword`

Bước thực hiện:

1. Gửi forgot password với email có thật.
2. Gửi forgot password với email không tồn tại.
3. So sánh response.

Kết quả mong đợi:

- Cùng một kiểu thông điệp chung.
- Không lộ rõ email nào tồn tại.

Minh chứng cần chụp:

- Hai request.
- Hai response.

Mã test: AUTH-06

Mục tiêu: Kiểm tra reset password với token sai, token hết hạn, token dùng lại.

Bước thực hiện:

1. Yêu cầu quên mật khẩu để nhận token thật.
2. Thử sửa token thành chuỗi khác.
3. Thử dùng token sau khi đổi mật khẩu xong.
4. Nếu có thể, chờ hết hạn rồi thử lại.

Kết quả mong đợi:

- Token sai hoặc hết hạn bị từ chối.
- Reset thành công sẽ xóa trạng thái failed/locked.

Minh chứng cần chụp:

- Email reset.
- Form reset.
- Response lỗi với token sai.
- Response thành công với token đúng.

Mã test: AUTH-07

Mục tiêu: Kiểm tra Admin và Seller bắt buộc qua 2FA.

Thành phần liên quan:

- `AuthController` luồng `login` và `login/2fa`
- `AdminAccountController`
- `SellerAccountController`

Bước thực hiện:

1. Đăng nhập bằng tài khoản Seller/Admin đúng mật khẩu.
2. Quan sát có bị chuyển sang bước 2FA hay không.
3. Thử mã OTP đúng.
4. Thử mã OTP sai.
5. Thử dùng ticket cũ hoặc hết hạn.

Kết quả mong đợi:

- Sau mật khẩu đúng phải yêu cầu 2FA.
- OTP sai bị từ chối.
- Ticket hết hạn bị từ chối.

Minh chứng cần chụp:

- Màn hình 2FA.
- Response sai OTP.
- Response thành công.

Mã test: AUTH-08

Mục tiêu: Kiểm tra timeout phiên và cookie/session hardening.

Thành phần liên quan:

- `FreshFarm.Bff.Session`
- `FreshFarm.Bff.Auth`

Bước thực hiện:

1. Đăng nhập vào hệ thống.
2. Mở DevTools, kiểm tra cookie flags.
3. Để phiên idle quá thời gian cấu hình.
4. Thử thao tác lại.

Kết quả mong đợi:

- Cookie có `HttpOnly`, `Secure`, `SameSite=Lax`.
- Phiên hết hạn sau thời gian idle.

Minh chứng cần chụp:

- Ảnh cookie flags.
- Ảnh màn hình sau khi phiên hết hạn.

Mã test: AUTH-09

Mục tiêu: Kiểm tra chống open redirect trên `returnUrl`.

Thành phần liên quan:

- `AccountController`
- `AdminAccountController`
- `SellerAccountController`

Bước thực hiện:

1. Truy cập login với `returnUrl=https://evil.test`.
2. Đăng nhập thành công.
3. Theo dõi redirect cuối cùng.

Kết quả mong đợi:

- Chỉ chấp nhận local URL.
- Không redirect sang domain ngoài.

Minh chứng cần chụp:

- URL đầu vào.
- Redirect sau đăng nhập.

### 6.2. Nhóm phân quyền và IDOR

Mã test: AUTHZ-01

Mục tiêu: Kiểm tra buyer không xem được đơn hàng của buyer khác.

Thành phần liên quan:

- `GET /api/orders/{id}`

Tiền điều kiện:

- Có hai tài khoản buyer A và B.
- Mỗi tài khoản có ít nhất một đơn hàng.

Bước thực hiện:

1. Đăng nhập bằng buyer A.
2. Lấy `orderId` của buyer B.
3. Gọi `GET /api/orders/{id}` với ID của buyer B.

Kết quả mong đợi:

- Server trả `403` hoặc từ chối truy cập.

Minh chứng cần chụp:

- JWT/cookie của A.
- Request với order ID của B.
- Response từ chối.

Mã test: AUTHZ-02

Mục tiêu: Kiểm tra route admin/seller chỉ cho đúng role.

Bước thực hiện:

1. Dùng buyer truy cập route admin.
2. Dùng buyer truy cập route seller.
3. Dùng seller truy cập route admin-only.

Kết quả mong đợi:

- Bị chặn và chuyển về luồng login hoặc access denied.

Minh chứng cần chụp:

- URL truy cập.
- Response/status hoặc redirect.

Mã test: AUTHZ-03

Mục tiêu: Kiểm tra endpoint telemetry rate limit và auth audit chỉ cho admin.

Thành phần liên quan:

- `GET /api/security/rate-limits/summary`
- `GET /admin/auth-audit`

Bước thực hiện:

1. Gọi bằng quyền buyer.
2. Gọi bằng quyền seller.
3. Gọi bằng quyền admin.

Kết quả mong đợi:

- Buyer/seller bị chặn nếu không đủ quyền.
- Admin xem được dữ liệu.

Minh chứng cần chụp:

- Response 403/401 với quyền không hợp lệ.
- Response thành công với admin.

### 6.3. Nhóm XSS, output encoding và input nguy hiểm

Mã test: XSS-01

Mục tiêu: Kiểm tra reflected XSS trên trang search/public query.

Bước thực hiện:

1. Mở trang search với payload script.
2. Mở lại với payload img onerror.

Payload gợi ý:

- `<script>alert('XSS')</script>`
- `<img src=x onerror=alert('XSS')>`

Kết quả mong đợi:

- Không bật alert.
- Payload hiển thị như text hoặc bị encode.

Minh chứng cần chụp:

- URL test.
- Màn hình hiển thị.

Mã test: XSS-02

Mục tiêu: Kiểm tra stored XSS ở đánh giá, bình luận, tên sản phẩm hoặc mô tả nếu các trường này được render lại ở giao diện.

Bước thực hiện:

1. Gửi payload vào trường text lưu DB.
2. Mở lại mọi vị trí render dữ liệu đó.

Payload gợi ý:

- `<script>alert(1)</script>`
- `<img src=x onerror=alert(document.domain)>`
- `"><svg/onload=alert(1)>`

Kết quả mong đợi:

- Payload chỉ hiện như văn bản.
- Không thực thi JS.

Minh chứng cần chụp:

- Dữ liệu đầu vào.
- Màn hình render sau khi lưu.

Mã test: XSS-03

Mục tiêu: Kiểm tra lỗ hổng stored XSS ở seller product detail modal.

Đây là ca cần ưu tiên test ngay.

Thành phần liên quan:

- `ProductController.GetProductDetails`
- `ManageProducts.cshtml`

Tiền điều kiện:

- Có tài khoản seller có quyền tạo hoặc sửa sản phẩm.

Bước thực hiện:

1. Tạo hoặc sửa sản phẩm với `ProductName`, `ShortDescription` hoặc `LongDescription` chứa payload.
2. Lưu sản phẩm.
3. Vào màn hình quản lý sản phẩm.
4. Bấm xem chi tiết để mở modal.

Payload gợi ý:

- `<img src=x onerror=alert('seller-xss')>`
- `<svg/onload=alert('seller-xss')>`

Kết quả mong đợi trong hệ thống an toàn:

- Modal phải encode text.
- Không có alert.

Kết quả cần đặc biệt quan sát:

- Nếu alert bật hoặc DOM bị chèn HTML ngoài ý muốn thì xác nhận lỗ hổng stored XSS.

Minh chứng cần chụp:

- Form nhập payload.
- Payload trong DB nếu cần.
- Màn hình modal sau khi mở.
- Console hoặc browser alert.

Mã test: XSS-04

Mục tiêu: Kiểm tra XSS qua success/error message dùng `Html.Raw`.

Đây là ca cần ưu tiên test ngay.

Thành phần liên quan:

- `Create.cshtml`
- `Edit.cshtml`
- `TempData["SuccessMessage"]`

Bước thực hiện:

1. Đặt `ProductName` là payload HTML hoặc JS.
2. Thực hiện cập nhật sản phẩm thành công.
3. Quan sát message sau redirect.

Payload gợi ý:

- `<img src=x onerror=alert('tempdata-xss')>`

Kết quả mong đợi trong hệ thống an toàn:

- Tên sản phẩm phải được encode.
- Message không được thực thi script.

Minh chứng cần chụp:

- Form edit.
- Success message.
- Alert nếu có.

### 6.4. Nhóm SQL Injection

Mã test: SQLI-01

Mục tiêu: Kiểm tra SQL injection ở search.

Payload gợi ý:

- `rau' OR 1=1 --`
- `' UNION SELECT 1,2,3 --`
- `'`

Bước thực hiện:

1. Đưa payload vào search query string hoặc API search.
2. Quan sát response, số lượng bản ghi và lỗi.

Kết quả mong đợi:

- Hệ thống xử lý như từ khóa tìm kiếm bình thường.
- Không trả toàn bộ bảng bất thường.
- Không lộ lỗi SQL.

Minh chứng cần chụp:

- URL/request.
- Response hoặc màn hình kết quả.

Mã test: SQLI-02

Mục tiêu: Kiểm tra SQL injection trên các form auth, forgot/reset password và filter quản trị.

Bước thực hiện:

1. Thử payload SQLi vào username/email.
2. Thử payload ở các filter `q`, `role`, `outcome` hoặc các ô nhập tìm kiếm quản trị.

Kết quả mong đợi:

- Không lộ lỗi SQL.
- Không bypass đăng nhập.
- Không có hành vi dữ liệu bất thường.

Minh chứng cần chụp:

- Request mẫu.
- Response.

Nhận xét kỹ thuật:

- Từ phần code đã rà, rủi ro SQLi thấp hơn do EF Core/LINQ.
- Tuy nhiên vẫn bắt buộc test hồi quy khi demo hoặc viết báo cáo.

### 6.5. Nhóm CSRF

Mã test: CSRF-01

Mục tiêu: Kiểm tra form POST web có anti-forgery token và token sai bị chặn.

Thành phần liên quan:

- Sign in, sign up, logout
- Forgot password, reset password
- Profile update, address book
- Form seller/admin có POST

Bước thực hiện:

1. Lấy form hợp lệ từ trình duyệt.
2. Gửi lại request nhưng xóa anti-forgery token.
3. Gửi lại request với token giả.

Kết quả mong đợi:

- Request không có token hoặc token giả bị từ chối.

Minh chứng cần chụp:

- Request thiếu token.
- Response lỗi.

### 6.6. Nhóm cookie, session và browser security

Mã test: SESSION-01

Mục tiêu: Kiểm tra cờ bảo mật trên cookie auth và session.

Bước thực hiện:

1. Đăng nhập.
2. Mở DevTools mục Application hoặc Storage.
3. Kiểm tra cookie `FreshFarm.Bff.Auth` và `FreshFarm.Bff.Session`.

Kết quả mong đợi:

- `HttpOnly = true`
- `Secure = true`
- `SameSite = Lax`

Minh chứng cần chụp:

- Bảng cookie flags.

Mã test: HEADER-01

Mục tiêu: Kiểm tra security headers trên public area.

Bước thực hiện:

1. Truy cập một trang public bằng browser hoặc curl.
2. Kiểm tra response headers.

Kết quả mong đợi:

- Có `X-Content-Type-Options`
- Có `X-Frame-Options`
- Có `Referrer-Policy`
- Có `Permissions-Policy`
- Có `Content-Security-Policy`
- Nếu môi trường không phải dev, có `Strict-Transport-Security`

Minh chứng cần chụp:

- Response headers.

Mã test: HEADER-02

Mục tiêu: Kiểm tra CSP ở admin/seller hiện đang report-only.

Bước thực hiện:

1. Mở một trang `/Admin/...`.
2. Mở một trang `/Seller/...`.
3. Kiểm tra response headers.

Kết quả mong đợi theo source hiện tại:

- Có `Content-Security-Policy-Report-Only`.
- Chưa thấy CSP enforce ở khu backoffice.

Ý nghĩa kiểm thử:

- Đây là bằng chứng để khuyến nghị nâng từ report-only sang enforce sau khi xử lý XSS.

Minh chứng cần chụp:

- Header của route Admin.
- Header của route Seller.

### 6.7. Nhóm rate limiting, spam request, DDoS và Cloudflare

Mã test: DDOS-01

Mục tiêu: Kiểm tra limiter cho đăng nhập và password recovery.

Bước thực hiện:

1. Spam POST login trong thời gian ngắn.
2. Spam resend verification hoặc forgot password.

Kết quả mong đợi:

- Khi vượt ngưỡng sẽ nhận `429 Too Many Requests`.
- Có log rate limiter.

Minh chứng cần chụp:

- Request bị 429.
- Log server có method, path, client, retry-after.

Mã test: DDOS-02

Mục tiêu: Kiểm tra limiter cho search, cart, checkout, GHN.

Bước thực hiện:

1. Spam các route:
   1. `/search`
   2. `/bff/product-search`
   3. `/cart/add`
   4. `/checkout`
   5. `/checkout/ghn/provinces`
2. Quan sát 429 và log.

Kết quả mong đợi:

- Request vượt ngưỡng bị chặn.
- Có thể đối chiếu lại trong telemetry summary.

Minh chứng cần chụp:

- 429 response.
- Log rate limiter.
- Telemetry summary nếu có.

Mã test: DDOS-03

Mục tiêu: Kiểm tra lớp Cloudflare/Tunnel nếu đang triển khai thật.

Lưu ý:

- Mục này cần test trên môi trường thật có Cloudflare.

Bước thực hiện:

1. Spam request vào route public hoặc cart.
2. Quan sát header phản hồi.

Kết quả mong đợi:

- Có thể xuất hiện các header như `server: cloudflare`, `cf-ray`, `retry-after` nếu edge đang xử lý chặn.
- Origin không nên bị truy cập trực tiếp từ Internet nếu đã public qua Cloudflare/Tunnel.

Minh chứng cần chụp:

- Header phản hồi.
- Dashboard Cloudflare nếu có.

### 6.8. Nhóm logging, auth audit và giám sát

Mã test: LOG-01

Mục tiêu: Kiểm tra auth audit ghi nhận đầy đủ sự kiện.

Sự kiện cần xác nhận:

- login success
- login failed
- login locked
- two factor success
- two factor failed
- external login success nếu có

Bước thực hiện:

1. Thực hiện lần lượt các tình huống ở trên.
2. Mở màn hình auth audit admin hoặc query DB/log.

Kết quả mong đợi:

- Có bản ghi cho từng sự kiện.
- Có IP, user-agent, thời gian, role, outcome.

Minh chứng cần chụp:

- Ảnh log audit.

Mã test: LOG-02

Mục tiêu: Kiểm tra rate limit telemetry summary chỉ cho admin và có số liệu hợp lý.

Bước thực hiện:

1. Tạo một số request bị 429.
2. Dùng admin gọi `/api/security/rate-limits/summary`.

Kết quả mong đợi:

- Có snapshot phản ánh các lần bị chặn.
- Non-admin không xem được.

Minh chứng cần chụp:

- 429 đã tạo.
- Response summary.

Mã test: LOG-03

Mục tiêu: Kiểm tra log không làm lộ password, token reset hoặc secret cấu hình.

Bước thực hiện:

1. Thực hiện đăng nhập sai.
2. Thực hiện forgot/reset password.
3. Rà log application và log reverse proxy.

Kết quả mong đợi:

- Log chỉ nên có identifier đã che hoặc metadata an toàn.
- Không in plain password, reset token, JWT key, secret nội bộ.

Minh chứng cần chụp:

- Log đã che dữ liệu.

### 6.9. Nhóm thanh toán VNPay và đối soát

Mã test: PAY-01

Mục tiêu: Kiểm tra chữ ký VNPay sai bị từ chối.

Thành phần liên quan:

- `VnPayService.ValidateReturn`
- `CheckoutController.VnPayReturn`

Bước thực hiện:

1. Lấy một callback/query mẫu từ VNPay sandbox.
2. Sửa `vnp_SecureHash` hoặc một tham số bất kỳ.
3. Gọi lại route return hoặc IPN.

Kết quả mong đợi:

- Callback bị từ chối.
- Không finalize thanh toán.
- Có log lỗi tương ứng.

Minh chứng cần chụp:

- Query ban đầu.
- Query đã sửa.
- Response lỗi.

Mã test: PAY-02

Mục tiêu: Kiểm tra amount mismatch ở bước finalize.

Thành phần liên quan:

- `OrdersController.FinalizeVnPayPaymentCore`

Bước thực hiện:

1. Tạo một order chờ thanh toán.
2. Gửi finalize với amount khác `order.TotalAmount`.

Kết quả mong đợi:

- Bị từ chối.
- Có reconciliation log `VNPayAmountMismatch`.

Minh chứng cần chụp:

- Request finalize.
- Response mismatch.
- Log hoặc DB reconciliation.

Mã test: PAY-03

Mục tiêu: Kiểm tra callback replay cùng một giao dịch đã paid.

Bước thực hiện:

1. Cho một callback hợp lệ xử lý thành công.
2. Gửi lại callback y hệt lần nữa.

Kết quả mong đợi:

- Hệ thống bỏ qua an toàn.
- Không tạo bản ghi bất thường.

Minh chứng cần chụp:

- Lần 1 success.
- Lần 2 replay ignored.

Mã test: PAY-04

Mục tiêu: Kiểm tra transaction number VNPay trùng sang đơn khác bị chặn.

Bước thực hiện:

1. Dùng `TransactionNo` đã dùng cho đơn A.
2. Gửi finalize cho đơn B với cùng `TransactionNo`.

Kết quả mong đợi:

- Ordering trả conflict.
- Ghi reconciliation log `VNPayDuplicateProviderRef`.

Minh chứng cần chụp:

- Hai order id.
- Response conflict.

Mã test: PAY-05

Mục tiêu: Kiểm tra price tampering qua giỏ hàng hoặc request tạo đơn.

Đây là ca ưu tiên cao nhất.

Tiền điều kiện:

- Có buyer đăng nhập.
- Có sản phẩm đang bán với giá biết trước.

Bước thực hiện:

1. Thêm sản phẩm vào giỏ bình thường.
2. Mở DevTools hoặc proxy chặn request.
3. Sửa `UnitPrice` xuống giá rất thấp khi:
   1. thêm vào cart,
   2. đồng bộ cart,
   3. gửi request checkout,
   4. hoặc gửi trực tiếp request tạo order.
4. Hoàn tất đặt hàng.
5. Kiểm tra order detail, total amount và luồng thanh toán.

Kết quả mong đợi trong hệ thống an toàn:

- Backend bỏ qua giá gửi từ client.
- Order phải lấy giá thật từ backend.

Dấu hiệu xác nhận lỗ hổng:

- Order được tạo với giá đã bị hạ trái phép.

Minh chứng cần chụp:

- Giá thật của sản phẩm.
- Request trước và sau khi sửa.
- Order tạo thành công với giá sai nếu khai thác được.

Mã test: PAY-06

Mục tiêu: Kiểm tra direct checkout qua query string có cho phép sửa `unitPrice`.

Đây là ca ưu tiên rất cao.

Thành phần liên quan:

- `CheckoutController.ParseDirectCheckoutItemFromQuery`

Bước thực hiện:

1. Tạo URL direct checkout có đủ `productId`, `sellerId`, `quantity`, `unitPrice`.
2. Thay `unitPrice` bằng giá rất thấp.
3. Mở URL đó.
4. Tiếp tục checkout và tạo order.

Kết quả mong đợi trong hệ thống an toàn:

- URL không được phép quyết định giá cuối.
- Backend phải tự đối chiếu giá thật.

Minh chứng cần chụp:

- URL chứa `unitPrice`.
- Giá hiển thị ở checkout.
- Order sau khi tạo.

### 6.10. Nhóm upload file

Mã test: UPLOAD-01

Mục tiêu: Kiểm tra upload file không đúng nội dung nhưng đổi đuôi thành ảnh.

Thành phần liên quan:

- `ProductImageStorageService.Validate`

Bước thực hiện:

1. Tạo một file text hoặc HTML.
2. Đổi tên thành `.jpg` hoặc `.png`.
3. Upload qua form sản phẩm.
4. Tải lại file đã upload bằng URL public.

Kết quả mong đợi trong hệ thống an toàn:

- Backend phát hiện file không phải ảnh thật và từ chối.

Dấu hiệu rủi ro:

- Upload thành công dù nội dung không phải ảnh.

Minh chứng cần chụp:

- File gốc.
- Kết quả upload.
- Response hoặc file public đã lưu.

Mã test: UPLOAD-02

Mục tiêu: Kiểm tra file vượt quá dung lượng giới hạn.

Bước thực hiện:

1. Tạo file ảnh lớn hơn 5 MB.
2. Upload qua form sản phẩm.

Kết quả mong đợi:

- Bị từ chối với thông báo kích thước vượt ngưỡng.

Minh chứng cần chụp:

- Kích thước file.
- Response lỗi.

Mã test: UPLOAD-03

Mục tiêu: Kiểm tra content type và magic bytes mismatch.

Bước thực hiện:

1. Dùng proxy hoặc tool chỉnh `Content-Type: image/jpeg`.
2. Nhưng nội dung file thật không phải ảnh.
3. Thử upload.

Kết quả mong đợi trong hệ thống an toàn:

- Phải bị từ chối sau khi kiểm tra chữ ký file thật.

Ghi chú:

- Source hiện tại cho thấy khả năng hệ thống chưa chặn được ca này.

### 6.11. Nhóm cấu hình nhạy cảm và bí mật

Mã test: CONF-01

Mục tiêu: Kiểm tra repo không chứa secret thật và xác nhận kế hoạch rotate.

Bước thực hiện:

1. Rà toàn bộ `appsettings*.json`.
2. Lập danh sách các key nhạy cảm:
   1. JWT key
   2. Internal service key
   3. Token GHN
   4. SMTP nếu có
3. Xác nhận xem các giá trị đó chỉ là dev sample hay đang dùng thật.

Kết quả mong đợi:

- Không commit secret thật vào repo.
- Secret phải đi qua env var, secret store hoặc vault.

Minh chứng cần chụp:

- Danh sách file chứa secret.
- Bằng chứng đã chuyển sang secret manager nếu có.

Mã test: CONF-02

Mục tiêu: Kiểm tra internal service key không bị lạm dụng để gọi nội bộ trái phép.

Bước thực hiện:

1. Xác định các endpoint internal như finalize VNPay nội bộ.
2. Thử gọi mà không có internal key.
3. Thử gọi với key sai.
4. Nếu có môi trường cho phép, đánh giá nguy cơ nếu key bị lộ.

Kết quả mong đợi:

- Thiếu key hoặc key sai phải bị từ chối.
- Key phải được rotate nếu từng commit vào repo.

Minh chứng cần chụp:

- Response 401/403 khi thiếu hoặc sai key.

## 7. Danh sách payload gợi ý để dùng nhanh

- XSS:
  - `<script>alert('XSS')</script>`
  - `<img src=x onerror=alert('XSS')>`
  - `"><svg/onload=alert(1)>`
- SQLi:
  - `rau' OR 1=1 --`
  - `' UNION SELECT 1,2,3 --`
  - `'`
- Username nguy hiểm:
  - `<script>alert(1)</script>`
  - `javascript:alert(1)`
  - `https://evil.test`
- Price tampering:
  - sửa `UnitPrice` về `1`, `100`, `1000`
  - sửa `quantity` kết hợp quan sát tổng tiền
- Upload:
  - file `.txt` đổi đuôi `.jpg`
  - file HTML đổi đuôi `.png`

## 8. Bộ minh chứng nên chụp cho báo cáo cuối

- Ảnh request/response cho login validation, register validation, forgot/reset password.
- Ảnh 429 response và log rate limiter.
- Ảnh response headers cho CSP, HSTS, security headers.
- Ảnh cookie flags trong browser.
- Ảnh auth audit thể hiện login success, failed, locked, suspicious.
- Ảnh 2FA setup và verify.
- Ảnh price tampering trước và sau khi sửa request.
- Ảnh stored XSS nếu tái hiện được ở seller modal hoặc TempData message.
- Ảnh amount mismatch và replay handling của VNPay.
- Ảnh upload file giả mạo nếu hệ thống chấp nhận.

## 9. Thứ tự ưu tiên thực hiện kiểm thử

Ưu tiên 1:

- PAY-05 Price tampering qua cart/order.
- PAY-06 Direct checkout sửa `unitPrice` qua query string.
- XSS-03 Stored XSS trong seller product detail modal.
- XSS-04 XSS qua `Html.Raw(TempData)`.

Ưu tiên 2:

- CONF-01 Secrets trong repo.
- UPLOAD-01 và UPLOAD-03 kiểm tra file giả mạo.
- HEADER-02 CSP report-only ở Admin/Seller.
- AUTHZ-01 IDOR đơn hàng.

Ưu tiên 3:

- DDOS-01, DDOS-02, DDOS-03.
- LOG-01, LOG-02, LOG-03.
- SQLI-01, SQLI-02.
- AUTH-01 đến AUTH-09.

## 10. Kết luận kỹ thuật sơ bộ

Từ các phần mã nguồn đã rà soát, có thể kết luận sơ bộ như sau:

- Hệ thống đã có khá nhiều lớp bảo vệ nền tảng tốt như JWT/cookie auth, phân quyền role, anti-forgery, password hashing, lockout, 2FA, security headers, rate limiting, auth audit và đối soát VNPay.
- Tuy nhiên vẫn tồn tại một số điểm rủi ro nghiêm trọng cần ưu tiên xử lý và kiểm thử ngay:
  - Tin `UnitPrice` từ client trong cart/checkout/order.
  - Stored XSS ở khu seller do render HTML không an toàn.
  - Upload ảnh chưa xác thực loại file thực.
  - Secret/cấu hình nhạy cảm đang nằm trong repo.
  - CSP khu backoffice mới ở report-only.
- SQL Injection hiện chưa có dấu hiệu rõ ràng trong các phần đã rà vì truy vấn chính đi theo EF Core/LINQ, nhưng vẫn phải test hồi quy để có minh chứng kiểm thử đầy đủ.

## 11. Khuyến nghị xử lý sau kiểm thử

- Chuyển toàn bộ tính giá đơn hàng sang backend tin cậy, không nhận `UnitPrice` từ client.
- Encode hoặc sanitize dữ liệu text trước khi render trong seller/admin UI.
- Loại bỏ `Html.Raw` với dữ liệu có thể bị ảnh hưởng bởi người dùng.
- Thêm kiểm tra MIME type thực và magic bytes cho upload ảnh.
- Tách và rotate tất cả secret đã commit trong repo.
- Sau khi xử lý XSS, nâng CSP khu Admin/Seller từ report-only sang enforce.
- Duy trì checklist test trong tài liệu này như checklist regression cho các lần release tiếp theo.
