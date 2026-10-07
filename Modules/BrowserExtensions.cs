using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using BacSiMayTinh.Core;

namespace BacSiMayTinh.Modules
{
    public class BrowserExtension
    {
        public string Browser { get; set; }
        public string Name { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public string Folder { get; set; }
        public string ExeName { get; set; }
        public bool Risky { get; set; }
        public string Detail => Browser + "  ·  v" + Version;
        public string RiskText => L.T("Đọc/sửa được dữ liệu trên mọi trang web");
    }

    [DataContract]
    internal class ChromeManifest
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "version")] public string Version { get; set; }
        [DataMember(Name = "description")] public string Description { get; set; }
        [DataMember(Name = "default_locale")] public string DefaultLocale { get; set; }
    }

    [DataContract]
    internal class FirefoxAddons
    {
        [DataMember(Name = "addons")] public List<FirefoxAddon> Addons { get; set; }
    }

    [DataContract]
    internal class FirefoxAddon
    {
        [DataMember(Name = "id")] public string Id { get; set; }
        [DataMember(Name = "type")] public string Type { get; set; }
        [DataMember(Name = "version")] public string Version { get; set; }
        [DataMember(Name = "location")] public string Location { get; set; }
        [DataMember(Name = "path")] public string Path { get; set; }
        [DataMember(Name = "defaultLocale")] public FirefoxLocale DefaultLocale { get; set; }
    }

    [DataContract]
    internal class FirefoxLocale
    {
        [DataMember(Name = "name")] public string Name { get; set; }
        [DataMember(Name = "description")] public string Description { get; set; }
    }

    /// <summary>Liệt kê tiện ích mở rộng của trình duyệt – giúp phát hiện tiện ích quảng cáo/độc hại.</summary>
    public static class BrowserExtensions
    {
        private static readonly Regex RiskyPerm = new Regex(@"""(<all_urls>|\*://\*/\*|https?://\*/\*)""|""(webRequestBlocking|debugger|proxy|nativeMessaging)""", RegexOptions.Compiled);

        private static T Parse<T>(string json) where T : class
        {
            try
            {
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return new DataContractJsonSerializer(typeof(T), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).ReadObject(ms) as T;
            }
            catch { return null; }
        }

        private static string Message(string extDir, string locale, string key)
        {
            foreach (var loc in new[] { L.IsEnglish ? "en" : "vi", locale, "en", "en_US" }.Where(x => !string.IsNullOrEmpty(x)).Distinct())
            {
                var f = Path.Combine(extDir, "_locales", loc, "messages.json");
                if (!File.Exists(f)) continue;
                try
                {
                    var json = File.ReadAllText(f);
                    var m = Regex.Match(json, "\"" + Regex.Escape(key) + "\"\\s*:\\s*\\{[^}]*?\"message\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    if (m.Success) return Regex.Unescape(m.Groups[1].Value);
                }
                catch { }
            }
            return null;
        }

        private static string Localize(string value, string dir, string locale)
        {
            if (value == null || !value.StartsWith("__MSG_")) return value;
            return Message(dir, locale, value.Substring(6).TrimEnd('_')) ?? value;
        }

        public static List<BrowserExtension> GetAll()
        {
            var list = new List<BrowserExtension>();
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var browsers = new[]
            {
                ("Google Chrome", Path.Combine(local, @"Google\Chrome\User Data"), "chrome.exe"),
                ("Microsoft Edge", Path.Combine(local, @"Microsoft\Edge\User Data"), "msedge.exe"),
                ("Cốc Cốc", Path.Combine(local, @"CocCoc\Browser\User Data"), "browser.exe"),
                ("Brave", Path.Combine(local, @"BraveSoftware\Brave-Browser\User Data"), "brave.exe"),
                ("Vivaldi", Path.Combine(local, @"Vivaldi\User Data"), "vivaldi.exe"),
                ("Opera", Path.Combine(roaming, @"Opera Software\Opera Stable"), "opera.exe"),
            };
            foreach (var (bname, userData, exe) in browsers)
            {
                if (!Directory.Exists(userData)) continue;
                IEnumerable<string> profiles;
                try
                {
                    profiles = File.Exists(Path.Combine(userData, "Preferences"))
                        ? new[] { userData }
                        : Directory.GetDirectories(userData).Where(d => File.Exists(Path.Combine(d, "Preferences")));
                }
                catch { continue; }
                foreach (var prof in profiles)
                {
                    var extRoot = Path.Combine(prof, "Extensions");
                    if (!Directory.Exists(extRoot)) continue;
                    var profName = Path.GetFileName(prof);
                    foreach (var idDir in SafeDirs(extRoot))
                    {
                        var verDir = SafeDirs(idDir).OrderByDescending(d => d).FirstOrDefault();
                        if (verDir == null) continue;
                        var mf = Path.Combine(verDir, "manifest.json");
                        if (!File.Exists(mf)) continue;
                        string json;
                        try { json = File.ReadAllText(mf); } catch { continue; }
                        var m = Parse<ChromeManifest>(json);
                        var name = Localize(m?.Name ?? Regex.Match(json, "\"name\"\\s*:\\s*\"([^\"]*)\"").Groups[1].Value, verDir, m?.DefaultLocale);
                        if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileName(idDir);
                        list.Add(new BrowserExtension
                        {
                            Browser = bname + (profName == "Default" || prof == userData ? "" : " (" + profName + ")"),
                            Name = name,
                            Version = m?.Version ?? "",
                            Description = Localize(m?.Description, verDir, m?.DefaultLocale),
                            Folder = verDir,
                            ExeName = exe,
                            Risky = RiskyPerm.IsMatch(json)
                        });
                    }
                }
            }

            // Firefox
            var ffRoot = Path.Combine(roaming, @"Mozilla\Firefox\Profiles");
            foreach (var prof in SafeDirs(ffRoot))
            {
                var f = Path.Combine(prof, "extensions.json");
                if (!File.Exists(f)) continue;
                FirefoxAddons data;
                try { data = Parse<FirefoxAddons>(File.ReadAllText(f)); } catch { continue; }
                if (data?.Addons == null) continue;
                foreach (var a in data.Addons.Where(a => a.Type == "extension" && a.Location == "app-profile"))
                {
                    list.Add(new BrowserExtension
                    {
                        Browser = "Firefox", Name = a.DefaultLocale?.Name ?? a.Id, Version = a.Version ?? "",
                        Description = a.DefaultLocale?.Description, Folder = a.Path, ExeName = "firefox.exe"
                    });
                }
            }
            return list.OrderBy(e => e.Browser).ThenBy(e => e.Name).ToList();
        }

        private static IEnumerable<string> SafeDirs(string dir)
        {
            try { return Directory.Exists(dir) ? Directory.GetDirectories(dir) : new string[0]; }
            catch { return new string[0]; }
        }

        /// <summary>Mở trang quản lý tiện ích của trình duyệt.</summary>
        public static void OpenManager(BrowserExtension e)
        {
            var exe = RegistryHelper.GetValue(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + e.ExeName, "") as string
                      ?? RegistryHelper.GetValue(@"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + e.ExeName, "") as string
                      ?? e.ExeName;
            ProcessRunner.Open(exe.Trim('"'), e.ExeName == "firefox.exe" ? "about:addons" : "chrome://extensions");
        }
    }
}
