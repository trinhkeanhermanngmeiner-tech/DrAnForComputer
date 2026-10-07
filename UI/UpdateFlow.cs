using BacSiMayTinh.Core;
using BacSiMayTinh.Modules;

namespace BacSiMayTinh.UI
{
    /// <summary>Luồng cập nhật: hỏi người dùng → tải bản mới từ GitHub → thay tệp và mở lại.</summary>
    public static class UpdateFlow
    {
        /// <summary>Bản mới tìm được khi tự kiểm tra lúc mở ứng dụng.</summary>
        public static UpdateInfo Pending { get; private set; }

        /// <summary>Tự kiểm tra trong nền khi mở ứng dụng (tối đa 1 lần/ngày, có thể tắt trong Cài đặt).</summary>
        public static async Task AutoCheckAsync()
        {
            if (!Updater.ShouldAutoCheck()) return;
            try
            {
                await Task.Delay(8000);
                var info = await Updater.CheckAsync();
                if (info == null) return;
                Pending = info;
                MainWindow.Instance?.ShowUpdateBanner(info.Version);
                MainWindow.Instance?.Toast(L.F("Có phiên bản mới {0}! Bấm « Có bản mới » ở thanh bên trái để cập nhật.", info.Version), ToastKind.Info);
            }
            catch (Exception ex) { Logger.Warn("Tự kiểm tra cập nhật: " + ex.Message); }
        }

        /// <summary>Kiểm tra theo yêu cầu (nút trong Cài đặt).</summary>
        public static async Task CheckManualAsync()
        {
            var main = MainWindow.Instance;
            UpdateInfo info;
            main.ShowBusy(L.T("Đang kiểm tra phiên bản mới…"));
            try { info = await Updater.CheckAsync(); }
            catch (Exception ex)
            {
                main.HideBusy();
                main.Toast(L.T("Không kiểm tra được cập nhật: ") + ex.Message, ToastKind.Warning);
                return;
            }
            main.HideBusy();
            if (info == null) { main.Toast(L.F("Bạn đang dùng phiên bản mới nhất ({0}).", AppInfo.Version), ToastKind.Success); return; }
            Pending = info;
            await OfferAsync(info);
        }

        /// <summary>Hỏi và cài bản mới.</summary>
        public static async Task OfferAsync(UpdateInfo info)
        {
            var main = MainWindow.Instance;
            var notes = (info.Notes ?? "").Replace("\r", "").Trim();
            if (notes.Length > 900) notes = notes.Substring(0, 900) + "…";
            var msg = L.F("Bạn đang dùng {0}. Có bản mới {1} trên GitHub.", AppInfo.Version, info.Version) + "\n\n" + notes;
            bool canAuto = !string.IsNullOrEmpty(info.ExeUrl);
            if (!Dialog.Confirm(L.F("Cập nhật lên phiên bản {0}?", info.Version), msg,
                canAuto ? L.T("Cập nhật ngay") : L.T("Mở trang tải về"), kind: ToastKind.Info)) return;

            if (!canAuto) { ProcessRunner.Open(info.PageUrl ?? AppInfo.GitHubUrl); return; }

            main.ShowBusy(L.F("Đang tải phiên bản {0}…", info.Version));
            string file;
            try { file = await Updater.DownloadAsync(info); }
            catch (Exception ex)
            {
                main.HideBusy();
                Logger.Error("Tải bản cập nhật", ex);
                if (Dialog.Confirm(L.T("Không tải được bản cập nhật"), ex.Message + "\n\n" + L.T("Mở trang GitHub để tải thủ công?"), L.T("Mở trang tải về"), kind: ToastKind.Warning))
                    ProcessRunner.Open(info.PageUrl ?? AppInfo.GitHubUrl);
                return;
            }
            main.SetBusyText(L.T("Đang cài bản mới – phần mềm sẽ tự mở lại sau vài giây…"));
            Updater.InstallAfterExit(file);
            await Task.Delay(800);
            main.ExitApp();
        }
    }
}
