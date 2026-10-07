using BacSiMayTinh.Core;

namespace BacSiMayTinh.Modules
{
    public class WingetPackage : ObservableObject
    {
        public string Name { get; set; }
        public string Id { get; set; }
        public string Version { get; set; }
        public string Available { get; set; }
        public string Category { get; set; }
        public string Description { get; set; }
        public string VersionText => string.IsNullOrEmpty(Available) ? Version : Version + "  →  " + Available;

        private bool _checked;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }

        private bool _installed;
        public bool IsInstalled { get => _installed; set => Set(ref _installed, value); }

        private string _status;
        public string Status { get => _status; set { Set(ref _status, value); OnPropertyChanged(nameof(HasStatus)); } }
        public bool HasStatus => !string.IsNullOrEmpty(_status);
    }

    public class WingetCategory
    {
        public string Name { get; set; }
        public List<WingetPackage> Items { get; set; }
    }

    /// <summary>Cập nhật và cài phần mềm qua Windows Package Manager (winget) – tải từ nguồn chính thức của nhà phát hành.</summary>
    public static class Winget
    {
        private const string Common = "--accept-source-agreements --disable-interactivity";

        public static async Task<bool> IsAvailableAsync()
        {
            var r = await ProcessRunner.CmdAsync("winget --version").ConfigureAwait(false);
            return r.Ok && (r.Output ?? "").TrimStart().StartsWith("v");
        }

        /// <summary>Phân tích bảng kết quả của winget dựa theo vị trí cột của dòng tiêu đề.</summary>
        public static List<string[]> ParseTable(string output)
        {
            var rows = new List<string[]>();
            var lines = (output ?? "").Replace("\r", "").Split('\n');
            int dash = Array.FindIndex(lines, l => l.Trim().Length > 10 && l.Trim().All(ch => ch == '-'));
            if (dash < 1) return rows;
            var header = lines[dash - 1];
            int h0 = header.Length - header.TrimStart().Length;
            header = header.Substring(h0);
            var starts = new List<int> { 0 };
            for (int i = 1; i < header.Length; i++)
                if (header[i] != ' ' && header[i - 1] == ' ') starts.Add(i);
            for (int k = dash + 1; k < lines.Length; k++)
            {
                var line = lines[k];
                if (line.Trim().Length == 0) break;
                if (line.Length > h0 && h0 > 0) line = line.Substring(h0);
                if (line.Length <= starts[Math.Min(1, starts.Count - 1)]) continue;
                var cells = new string[starts.Count];
                for (int c = 0; c < starts.Count; c++)
                {
                    int s = starts[c];
                    int e = c + 1 < starts.Count ? starts[c + 1] : line.Length;
                    cells[c] = s < line.Length ? line.Substring(s, Math.Min(e, line.Length) - s).Trim() : "";
                }
                rows.Add(cells);
            }
            return rows;
        }

        public static async Task<List<WingetPackage>> ListUpgradesAsync()
        {
            var r = await ProcessRunner.CmdAsync("winget upgrade " + Common).ConfigureAwait(false);
            return ParseTable(r.Output)
                .Where(c => c.Length >= 4 && c[1].Contains(".") && !string.IsNullOrEmpty(c[3]))
                .Select(c => new WingetPackage { Name = c[0].TrimEnd('…'), Id = c[1], Version = c[2], Available = c[3], IsChecked = true })
                .GroupBy(p => p.Id).Select(g => g.First())
                .ToList();
        }

        public static async Task<HashSet<string>> InstalledIdsAsync()
        {
            var r = await ProcessRunner.CmdAsync("winget list " + Common).ConfigureAwait(false);
            return new HashSet<string>(ParseTable(r.Output).Where(c => c.Length >= 2).Select(c => c[1]), StringComparer.OrdinalIgnoreCase);
        }

        public static Task<RunResult> UpgradeAsync(string id, Action<string, bool> onLine) =>
            ProcessRunner.CmdAsync($"winget upgrade --id \"{id}\" --exact --silent --accept-package-agreements {Common}", onLine);

        public static Task<RunResult> InstallAsync(string id, Action<string, bool> onLine) =>
            ProcessRunner.CmdAsync($"winget install --id \"{id}\" --exact --silent --accept-package-agreements {Common}", onLine);

        /// <summary>Danh sách phần mềm phổ biến tại Việt Nam (mã gói đã kiểm tra trên kho winget).</summary>
        public static List<WingetCategory> Catalog()
        {
            WingetPackage P(string id, string name, string desc) => new WingetPackage { Id = id, Name = name, Description = desc };
            return new List<WingetCategory>
            {
                new WingetCategory { Name = L.T("Trình duyệt web"), Items = new List<WingetPackage> {
                    P("Google.Chrome", "Google Chrome", L.T("Trình duyệt phổ biến nhất")),
                    P("CocCoc.CocCoc", "Cốc Cốc", L.T("Trình duyệt Việt, hỗ trợ tải video")),
                    P("Mozilla.Firefox", "Mozilla Firefox", L.T("Trình duyệt mã nguồn mở, bảo vệ quyền riêng tư")),
                    P("Brave.Brave", "Brave", L.T("Trình duyệt chặn quảng cáo sẵn")) } },
                new WingetCategory { Name = L.T("Liên lạc & họp trực tuyến"), Items = new List<WingetPackage> {
                    P("VNGCorp.Zalo", "Zalo", L.T("Nhắn tin, gọi điện")),
                    P("Telegram.TelegramDesktop", "Telegram", L.T("Nhắn tin bảo mật")),
                    P("Zoom.Zoom", "Zoom", L.T("Họp, học trực tuyến")),
                    P("Microsoft.Teams", "Microsoft Teams", L.T("Họp, làm việc nhóm")),
                    P("Discord.Discord", "Discord", L.T("Trò chuyện, cộng đồng game")) } },
                new WingetCategory { Name = L.T("Gõ tiếng Việt"), Items = new List<WingetPackage> {
                    P("UniKey.UniKey", "UniKey", L.T("Bộ gõ tiếng Việt quen thuộc")),
                    P("lamquangminh.EVKey", "EVKey", L.T("Bộ gõ tiếng Việt, sửa lỗi gõ trên trình duyệt")),
                    P("Tuyenvm.OpenKey", "OpenKey", L.T("Bộ gõ tiếng Việt mã nguồn mở")) } },
                new WingetCategory { Name = L.T("Văn phòng & PDF"), Items = new List<WingetPackage> {
                    P("Foxit.FoxitReader", "Foxit PDF Reader", L.T("Đọc PDF nhẹ, nhanh")),
                    P("Adobe.Acrobat.Reader.64-bit", "Adobe Acrobat Reader", L.T("Đọc, ký tài liệu PDF")),
                    P("ONLYOFFICE.DesktopEditors", "ONLYOFFICE", L.T("Bộ văn phòng miễn phí, tương thích Word/Excel")),
                    P("TheDocumentFoundation.LibreOffice", "LibreOffice", L.T("Bộ văn phòng mã nguồn mở")),
                    P("Notepad++.Notepad++", "Notepad++", L.T("Soạn thảo văn bản, mã nguồn")) } },
                new WingetCategory { Name = L.T("Tiện ích & điều khiển từ xa"), Items = new List<WingetPackage> {
                    P("RARLab.WinRAR", "WinRAR", L.T("Nén, giải nén tệp")),
                    P("7zip.7zip", "7-Zip", L.T("Nén, giải nén miễn phí")),
                    P("DucFabulous.UltraViewer", "UltraViewer", L.T("Điều khiển máy tính từ xa (phổ biến ở Việt Nam)")),
                    P("TeamViewer.TeamViewer", "TeamViewer", L.T("Điều khiển máy tính từ xa")),
                    P("AnyDesk.AnyDesk", "AnyDesk", L.T("Điều khiển máy tính từ xa, nhẹ")),
                    P("Google.GoogleDrive", "Google Drive", L.T("Đồng bộ dữ liệu lên đám mây")),
                    P("Microsoft.PowerToys", "PowerToys", L.T("Bộ tiện ích nâng cao của Microsoft")) } },
                new WingetCategory { Name = L.T("Đa phương tiện"), Items = new List<WingetPackage> {
                    P("VideoLAN.VLC", "VLC", L.T("Xem mọi định dạng video")),
                    P("CodecGuide.K-LiteCodecPack.Standard", "K-Lite Codec Pack", L.T("Bộ giải mã video/âm thanh")),
                    P("OBSProject.OBSStudio", "OBS Studio", L.T("Quay màn hình, phát trực tiếp")) } },
                new WingetCategory { Name = L.T("Game & thư viện hệ thống"), Items = new List<WingetPackage> {
                    P("Valve.Steam", "Steam", L.T("Cửa hàng game")),
                    P("Garena.Garena", "Garena", L.T("Nền tảng game (Liên Minh, FC Online…)")),
                    P("Microsoft.VCRedist.2015+.x64", "Visual C++ Redistributable", L.T("Thư viện cần cho nhiều game/phần mềm")),
                    P("Microsoft.DirectX", "DirectX", L.T("Thư viện đồ họa cho game cũ")),
                    P("Microsoft.DotNet.DesktopRuntime.8", ".NET Desktop Runtime 8", L.T("Thư viện cho phần mềm viết bằng .NET")),
                    P("Oracle.JavaRuntimeEnvironment", "Java", L.T("Cần cho một số phần mềm ngân hàng, thuế")) } },
            };
        }
    }
}
