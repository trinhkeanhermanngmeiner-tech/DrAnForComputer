using System.Diagnostics;
using BacSiMayTinh.Core;

namespace BacSiMayTinh.Modules
{
    public class AppxItem : ObservableObject
    {
        public string Name { get; set; }
        public string FullName { get; set; }
        public string Title { get; set; }
        public bool Known { get; set; }
        public string Detail => Name;

        private bool _checked;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
    }

    /// <summary>Gỡ ứng dụng cài sẵn (bloatware) của Windows.</summary>
    public static class AppxDebloat
    {
        // Tên gói → tên hiển thị. Tên kết thúc bằng * là so khớp tiền tố.
        private static readonly Dictionary<string, string> Known = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Microsoft.BingNews"] = "Tin tức (Microsoft News)",
            ["Microsoft.BingWeather"] = "Thời tiết (MSN Weather)",
            ["Microsoft.BingFinance"] = "Tài chính (MSN Money)",
            ["Microsoft.BingSports"] = "Thể thao (MSN Sports)",
            ["Microsoft.BingSearch"] = "Bing Search",
            ["Microsoft.GetHelp"] = "Nhận trợ giúp (Get Help)",
            ["Microsoft.Getstarted"] = "Mẹo (Tips)",
            ["Microsoft.MicrosoftSolitaireCollection"] = "Microsoft Solitaire Collection",
            ["Microsoft.MicrosoftOfficeHub"] = "Office Hub / Microsoft 365 (quảng cáo Office)",
            ["Microsoft.People"] = "Danh bạ (People)",
            ["Microsoft.SkypeApp"] = "Skype",
            ["Microsoft.WindowsFeedbackHub"] = "Trung tâm phản hồi (Feedback Hub)",
            ["Microsoft.XboxApp"] = "Xbox Console Companion",
            ["Microsoft.GamingApp"] = "Ứng dụng Xbox",
            ["Microsoft.ZuneVideo"] = "Phim & TV",
            ["Microsoft.ZuneMusic"] = "Groove Music / Media Player",
            ["Microsoft.YourPhone"] = "Liên kết điện thoại (Phone Link)",
            ["Microsoft.MixedReality.Portal"] = "Mixed Reality Portal",
            ["Microsoft.Microsoft3DViewer"] = "Trình xem 3D",
            ["Microsoft.Print3D"] = "Print 3D",
            ["Microsoft.MSPaint"] = "Paint 3D",
            ["Microsoft.Wallet"] = "Ví (Wallet)",
            ["Microsoft.WindowsMaps"] = "Bản đồ (Maps)",
            ["Microsoft.Messaging"] = "Tin nhắn (Messaging)",
            ["Microsoft.OneConnect"] = "Gói di động (Mobile Plans)",
            ["Microsoft.549981C3F5F10"] = "Cortana",
            ["Microsoft.Todos"] = "Microsoft To Do",
            ["Microsoft.PowerAutomateDesktop"] = "Power Automate",
            ["Microsoft.Windows.DevHome"] = "Dev Home",
            ["Microsoft.OutlookForWindows"] = "Outlook (mới)",
            ["Microsoft.Copilot"] = "Copilot",
            ["MicrosoftCorporationII.MicrosoftFamily"] = "Microsoft Family",
            ["microsoft.windowscommunicationsapps"] = "Thư và Lịch (Mail & Calendar)",
            ["Clipchamp.Clipchamp"] = "Clipchamp (chỉnh sửa video)",
            ["MicrosoftTeams"] = "Microsoft Teams (cá nhân)",
            ["MSTeams"] = "Microsoft Teams (mới)",
            ["king.com.*"] = "Game Candy Crush (King)",
            ["SpotifyAB.SpotifyMusic"] = "Spotify (cài sẵn)",
            ["*Disney*"] = "Disney+",
            ["*Facebook*"] = "Facebook",
            ["*Instagram*"] = "Instagram",
            ["BytedancePte.Ltd.TikTok"] = "TikTok",
            ["AmazonVideo.PrimeVideo"] = "Prime Video",
            ["4DF9E0F8.Netflix"] = "Netflix",
            ["*McAfee*"] = "McAfee (bản dùng thử)",
            ["*Dropbox*"] = "Dropbox (bản OEM)",
        };

        private static string KnownTitle(string name)
        {
            if (Known.TryGetValue(name, out var t)) return L.T(t);
            foreach (var kv in Known)
            {
                var k = kv.Key;
                if (k.StartsWith("*") && k.EndsWith("*") && name.IndexOf(k.Trim('*'), StringComparison.OrdinalIgnoreCase) >= 0) return L.T(kv.Value);
                if (k.EndsWith("*") && !k.StartsWith("*") && name.StartsWith(k.TrimEnd('*'), StringComparison.OrdinalIgnoreCase)) return L.T(kv.Value);
            }
            return null;
        }

        public static async Task<List<AppxItem>> ListAsync()
        {
            const string script = "Get-AppxPackage | Where-Object { -not $_.IsFramework -and -not $_.NonRemovable -and $_.SignatureKind -ne 'System' } | ForEach-Object { $_.Name + '|' + $_.PackageFullName }";
            var r = await ProcessRunner.PowerShellAsync(script).ConfigureAwait(false);
            var list = new List<AppxItem>();
            foreach (var line in (r.Output ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|');
                if (parts.Length < 2) continue;
                var title = KnownTitle(parts[0]);
                list.Add(new AppxItem { Name = parts[0], FullName = parts[1].Trim(), Title = title ?? parts[0], Known = title != null });
            }
            return list.GroupBy(a => a.Name).Select(g => g.First()).OrderBy(a => a.Title).ToList();
        }

        public static async Task<bool> RemoveAsync(AppxItem item)
        {
            var r = await ProcessRunner.PowerShellAsync($"Remove-AppxPackage -Package '{item.FullName.Replace("'", "''")}'; if ($?) {{ 'OK' }}");
            bool ok = r.Output != null && r.Output.Contains("OK");
            Logger.Info("Gỡ ứng dụng " + item.Name + (ok ? " – thành công" : " – thất bại: " + r.Output));
            return ok;
        }
    }

    /// <summary>Giải phóng RAM: thu gọn bộ nhớ làm việc của tiến trình và xóa danh sách chờ (standby).</summary>
    public static class MemoryCleaner
    {
        public static long AvailableBytes() => (long)Native.GetMemory().ullAvailPhys;

        public static long Free()
        {
            long before = AvailableBytes();
            Native.EnablePrivilege("SeDebugPrivilege");
            Native.EnablePrivilege("SeProfileSingleProcessPrivilege");
            Native.EnablePrivilege("SeIncreaseQuotaPrivilege");
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Id == 0 || p.Id == 4 || p.Id == self) continue;
                    var h = Native.OpenProcess(0x1000 | 0x0100, false, p.Id); // QUERY_LIMITED_INFORMATION | SET_QUOTA
                    if (h != IntPtr.Zero)
                    {
                        Native.EmptyWorkingSet(h);
                        Native.CloseHandle(h);
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            try
            {
                int cmd = 4; // MemoryPurgeStandbyList
                Native.NtSetSystemInformation(80, ref cmd, sizeof(int));
            }
            catch { }
            Thread.Sleep(400);
            long freed = Math.Max(0, AvailableBytes() - before);
            Logger.Info("Giải phóng RAM: " + FileUtil.FormatSize(freed));
            return freed;
        }
    }
}
