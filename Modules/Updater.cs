using System.Diagnostics;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using BacSiMayTinh.Core;

namespace BacSiMayTinh.Modules
{
    [DataContract]
    internal class GhRelease
    {
        [DataMember(Name = "tag_name")] public string TagName { get; set; }
        [DataMember(Name = "html_url")] public string HtmlUrl { get; set; }
        [DataMember(Name = "body")] public string Body { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
        [DataMember(Name = "assets")] public List<GhAsset> Assets { get; set; }
    }

    [DataContract]
    internal class GhAsset
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "browser_download_url")] public string Url { get; set; }
        [DataMember(Name = "size")] public long Size { get; set; }
    }

    public class UpdateInfo
    {
        public string Version { get; set; }
        public string PageUrl { get; set; }
        public string Notes { get; set; }
        public string ExeUrl { get; set; }
        public long ExeSize { get; set; }
    }

    /// <summary>
    /// Kiểm tra & cài bản mới từ GitHub Releases. Chỉ hỏi GitHub thông tin bản phát hành mới nhất –
    /// không gửi dữ liệu cá nhân nào.
    /// </summary>
    public static class Updater
    {
        public static bool Enabled => !string.IsNullOrWhiteSpace(AppInfo.UpdateUrl);

        private static WebClient Client()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var wc = new WebClient();
            wc.Headers[HttpRequestHeader.UserAgent] = AppInfo.Id + "/" + AppInfo.Version;
            return wc;
        }

        /// <summary>Lấy thông tin bản phát hành mới nhất (kể cả khi không mới hơn bản đang chạy).</summary>
        public static Task<UpdateInfo> LatestAsync() => Task.Run(() =>
        {
            using (var wc = Client())
            {
                wc.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                var data = wc.DownloadData(AppInfo.UpdateUrl);
                GhRelease r;
                using (var ms = new MemoryStream(data))
                    r = (GhRelease)new DataContractJsonSerializer(typeof(GhRelease)).ReadObject(ms);
                if (r == null || r.Draft || r.Prerelease || string.IsNullOrEmpty(r.TagName)) return null;
                var exe = r.Assets?.FirstOrDefault(a => string.Equals(a.Name, AppInfo.Id + ".exe", StringComparison.OrdinalIgnoreCase));
                return new UpdateInfo
                {
                    Version = r.TagName.TrimStart('v', 'V'), PageUrl = r.HtmlUrl, Notes = r.Body,
                    ExeUrl = exe?.Url, ExeSize = exe?.Size ?? 0
                };
            }
        });

        public static bool IsNewer(UpdateInfo info) =>
            info != null && System.Version.TryParse(info.Version, out var remote) && remote > System.Version.Parse(AppInfo.Version);

        /// <summary>Trả về bản mới nếu có, ngược lại null.</summary>
        public static async Task<UpdateInfo> CheckAsync()
        {
            var info = await LatestAsync().ConfigureAwait(false);
            AppSettings.Current.LastUpdateCheck = DateTime.Now.ToString("s");
            AppSettings.SaveCurrent();
            return IsNewer(info) ? info : null;
        }

        /// <summary>Có nên tự kiểm tra lúc mở ứng dụng không (bật trong Cài đặt, tối đa 1 lần/ngày).</summary>
        public static bool ShouldAutoCheck()
        {
            var s = AppSettings.Current;
            if (!Enabled || !s.AutoCheckUpdate) return false;
            return !DateTime.TryParse(s.LastUpdateCheck, out var last) || (DateTime.Now - last).TotalHours >= 20;
        }

        /// <summary>Tải tệp exe mới về thư mục tạm và kiểm tra tính toàn vẹn. Trả về đường dẫn tệp đã tải.</summary>
        public static Task<string> DownloadAsync(UpdateInfo info) => Task.Run(() =>
        {
            var file = Path.Combine(Path.GetTempPath(), AppInfo.Id + "_" + info.Version + ".exe");
            using (var wc = Client()) wc.DownloadFile(info.ExeUrl, file);
            var len = new FileInfo(file).Length;
            var head = new byte[2];
            using (var fs = File.OpenRead(file)) fs.Read(head, 0, 2);
            if (head[0] != (byte)'M' || head[1] != (byte)'Z' || (info.ExeSize > 0 && len != info.ExeSize))
            {
                try { File.Delete(file); } catch { }
                throw new InvalidDataException(L.T("Tệp tải về bị lỗi, hãy thử lại sau."));
            }
            return file;
        });

        /// <summary>Thay tệp exe đang chạy bằng bản mới rồi mở lại (chạy sau khi ứng dụng thoát).</summary>
        public static void InstallAfterExit(string newExe)
        {
            var cur = AppInfo.ExePath;
            Logger.Info("Cập nhật: " + newExe + " → " + cur);
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                $"/d /c ping -n 4 127.0.0.1 >nul & copy /Y \"{newExe}\" \"{cur}\" >nul & del \"{newExe}\" & start \"\" \"{cur}\"")
            { CreateNoWindow = true, UseShellExecute = false });
        }
    }
}
