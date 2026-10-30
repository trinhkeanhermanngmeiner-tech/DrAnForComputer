# DrAn For Computer

Phần mềm **miễn phí** tối ưu, dọn dẹp và sửa lỗi Windows 10/11 – nhẹ, chạy mượt cả trên máy cấu hình yếu. Tác giả: **BS. Trịnh Kế An**.
*Free Windows 10/11 optimizer, cleaner and repair tool – Vietnamese & English.*

## Tải về / Download

Vào mục **[Releases](../../releases)**, tải `DrAnForComputer.exe` (chạy ngay, không cần cài đặt) hoặc tệp `.zip` kèm hướng dẫn.
Nếu Windows SmartScreen cảnh báo: bấm **More info → Run anyway** (phần mềm chưa ký số).

## Tính năng chính

- Khám sức khỏe máy, theo dõi CPU/RAM/ổ đĩa/mạng, nút **Tối ưu ngay**
- Dọn rác (Windows, trình duyệt, Zalo, Discord…), dọn sâu WinSxS
- Tối ưu: ứng dụng khởi động, 35 tinh chỉnh có hoàn tác, dịch vụ, ứng dụng cài sẵn, menu chuột phải, **Chế độ máy yếu**
- Sửa 19 lỗi Windows thường gặp (mạng, Windows Update, USB nhiễm virus, không gõ được tiếng Việt…)
- Thông tin phần cứng, sức khỏe ổ cứng/pin, xuất báo cáo
- Quản lý phần mềm: gỡ sạch, cập nhật hàng loạt, cài nhanh qua winget
- Phân tích ổ đĩa, tệp trùng lặp, đo tốc độ và **phát hiện USB giả dung lượng**
- **Sao lưu & Phục hồi** (mới 1.2): chụp lại tinh chỉnh Windows, dịch vụ, ứng dụng khởi động, menu chuột phải, gói điện năng, hosts, DNS, Wi‑Fi, driver – phục hồi từng phần, mang sang máy mới hoặc sau khi cài lại Win; sao lưu tự động theo lịch và trước mỗi lần tối ưu
- **Sao lưu dữ liệu cá nhân** (mới 1.2): Desktop, Tài liệu, Ảnh, dữ liệu Zalo PC, dấu trang Chrome/Edge/Cốc Cốc… sang ổ khác/USB (chép tăng dần)
- **Driver** (mới 1.2): danh sách driver, phát hiện thiết bị lỗi/thiếu driver, sao lưu – phục hồi driver, **cập nhật driver qua Windows Update** (tự sao lưu bản cũ, có nút quay lại), dọn driver cũ trùng lặp giải phóng ổ C
- **Bảo mật & Cập nhật** (mới 1.2): chấm điểm bảo mật (diệt virus, tường lửa, UAC, SMBv1, AutoRun USB…) và sửa một chạm, quét virus bằng Microsoft Defender (nhanh/toàn bộ/USB), tạm dừng Windows Update 1–5 tuần, giờ hoạt động, ẩn bản cập nhật gây lỗi
- **Tự cập nhật** từ GitHub Releases (bấm một nút là tải và cài bản mới)

## Ủng hộ tác giả

Hoàn toàn tự nguyện – Ngân hàng TMCP Bản Việt (BVBank), STK **9021642814421** (có mã VietQR trong phần mềm).

## Báo lỗi & góp ý

Gửi email tới **bstrinhkean@gmail.com** (hoặc dùng nút « Báo lỗi / Góp ý » trong phần mềm). Có thể mở [Issue](../../issues) trên GitHub.
*Bug reports & feature requests: bstrinhkean@gmail.com*

## Giấy phép

Miễn phí cho cá nhân, phi thương mại; mã nguồn công khai để tham khảo. Xem [LICENSE.md](LICENSE.md).

---

## Dành cho nhà phát triển

- C# + WPF trên **.NET Framework 4.8** (có sẵn trong Win10 1903+/Win11) → một tệp `DrAnForComputer.exe` ~680 KB.
- Không dùng thư viện NuGet; giao diện tự thiết kế (Themes/), tối/sáng, **song ngữ Việt/Anh**.
- Chạy với quyền quản trị (`app.manifest`).

## Build

```bash
dotnet build -c Release
```

Kết quả: `bin/Release/net48/DrAnForComputer.exe`. (Thư mục/namespace mã nguồn vẫn tên `BacSiMayTinh` – chỉ là tên nội bộ.)

## Cấu trúc

| Thư mục | Nội dung |
|---|---|
| `Core/` | Registry, chạy lệnh (tự nhận dạng UTF-8/OEM), WMI, nhật ký, sao lưu/hoàn tác, điểm khôi phục, **đa ngôn ngữ** (`Localization.cs`, `Strings.En*.cs`), bộ tự kiểm tra |
| `Modules/` | Dọn rác, tinh chỉnh, dịch vụ, khởi động (+Task Scheduler), ứng dụng cài sẵn, menu chuột phải, sửa lỗi, thông tin máy, gỡ phần mềm, winget, phân tích ổ đĩa, kiểm tra USB giả, tiện ích trình duyệt, cài đặt/cập nhật |
| `Pages/` | Các trang giao diện |
| `UI/` | Cửa sổ chính, hộp thoại, điều khoản, ủng hộ (VietQR), khay hệ thống |
| `Controls/` | Control tự vẽ, converter |
| `Themes/` | Bảng màu và style |

## Đa ngôn ngữ

- Chuỗi tiếng Việt trong mã là **khóa**: C# dùng `L.T("…")` / `L.F("… {0}", x)`, XAML dùng `{c:T '…'}`.
- Bản dịch tiếng Anh nằm trong `Core/Strings.En*.cs`. Thêm chuỗi mới → thêm bản dịch tương ứng
  (bộ tự kiểm tra báo lỗi nếu placeholder `{0}` không khớp).

## Tham số dòng lệnh

| Tham số | Tác dụng |
|---|---|
| `--tray` | Khởi động ẩn dưới khay |
| `--autoclean` | Dọn rác an toàn rồi thoát (lịch dọn tự động) |
| `--autobackup` | Sao lưu cấu hình (và dữ liệu cá nhân nếu bật) rồi thoát (lịch sao lưu tự động) |
| `--uninstall` | Gỡ cài đặt (được đăng ký trong Cài đặt › Ứng dụng của Windows) |
| `--lang en\|vi` | Chọn ngôn ngữ cho lần chạy này |
| `--selftest <tệp>` | Bộ tự kiểm tra (chỉ dùng khóa registry/thư mục tạm riêng) |
| `--snap <trang> <ảnh.png> [tab]` | Chụp giao diện khi phát triển (`terms`, `donate`, `toggle` để thử đổi ngôn ngữ) |

## Cấu hình

- `AppInfo.BankAccount` / `BankName`: thông tin ủng hộ; mã QR ở `Assets/donate_qr.png` (VietQR, BIN 970454).
- `AppInfo.UpdateUrl`: API GitHub `releases/latest` của kho này. Phát hành bản mới = tạo Release có tag `vX.Y.Z`
  kèm tệp `DrAnForComputer.exe` – ứng dụng sẽ tự phát hiện, tải và thay bản cũ (để trống = tắt).

## Dữ liệu người dùng

`%LocalAppData%\DrAnForComputer\` – `caidat.json`, `SaoLuu\hoantac.json`, `SaoLuu\*.reg`, `SaoLuu\lichsu_driver.json`, `NhatKy\*.log`.

Bản sao lưu cấu hình (mặc định `SaoLuu\BanSaoLuu\`, đổi được sang ổ D/USB): mỗi bản là một thư mục độc lập gồm `manifest.json`
(giá trị registry, dịch vụ, khởi động, menu chuột phải, DNS…), `hoantac.json`, `power.pow`, `hosts`, `WiFi\*.xml`, `Startup\*.lnk`, `Drivers\`.
Chép cả thư mục sang máy khác rồi dùng « Mở bản sao lưu khác… » để phục hồi.

Bản sao lưu dữ liệu cá nhân: `<nơi lưu>\DrAn_DuLieu_<máy>_<người dùng>\` kèm `DrAn_DuLieu.json`.

## Kiểm tra tự động (GitHub Actions)

`.github/workflows/build.yml` build trên Windows mỗi lần đẩy mã, chạy `--selftest` và chụp giao diện.
Tăng `AppInfo.Version` (và `<Version>` trong .csproj) rồi đẩy lên `main` – hoặc đẩy tag `vX.Y.Z` khớp phiên bản – → tự tạo Release kèm `DrAnForComputer.exe` và `.zip`; ghi chú phát hành lấy từ `docs/release-notes/vX.Y.Z.md`.
Dữ liệu của bản 1.0 (`%LocalAppData%\BacSiMayTinh\`) được tự chép sang ở lần chạy đầu.
