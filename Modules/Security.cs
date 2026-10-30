using System.Windows.Media;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    public enum SecLevel { Good, Info, Warn, Bad }

    public class SecItem : ObservableObject
    {
        public string Id { get; set; }
        public string Glyph { get; set; }
        public string Title { get; set; }
        public string Advice { get; set; }
        public string FixText { get; set; }
        public Func<Task<string>> Fix { get; set; }
        public bool Scored { get; set; } = true;

        private string _detail;
        public string Detail { get => _detail; set => Set(ref _detail, value); }

        private SecLevel _level;
        public SecLevel Level
        {
            get => _level;
            set { Set(ref _level, value); OnPropertyChanged(nameof(LevelText)); OnPropertyChanged(nameof(Fg)); OnPropertyChanged(nameof(Bg)); OnPropertyChanged(nameof(CanFix)); OnPropertyChanged(nameof(StateGlyph)); }
        }

        public bool CanFix => Fix != null && Level != SecLevel.Good;
        public string LevelText => Level == SecLevel.Good ? L.T("Tốt") : Level == SecLevel.Info ? L.T("Lưu ý") : Level == SecLevel.Warn ? L.T("Nên sửa") : L.T("Nguy hiểm");
        public Brush Fg => Level == SecLevel.Good ? Pal.Green : Level == SecLevel.Info ? Pal.Blue : Level == SecLevel.Warn ? Pal.Amber : Pal.Red;
        public Brush Bg => Level == SecLevel.Good ? Pal.GreenBg : Level == SecLevel.Info ? Pal.BlueBg : Level == SecLevel.Warn ? Pal.AmberBg : Pal.RedBg;
        public string StateGlyph => Level == SecLevel.Good ? "" : Level == SecLevel.Info ? "" : "";

        private bool _busy;
        public bool IsBusy { get => _busy; set { Set(ref _busy, value); OnPropertyChanged(nameof(IsIdle)); } }
        public bool IsIdle => !_busy;
    }

    public class ThreatInfo
    {
        public string Name { get; set; }
        public int Severity { get; set; }
        public bool Active { get; set; }
        public string SeverityText => Severity >= 5 ? L.T("Nghiêm trọng") : Severity >= 4 ? L.T("Cao") : Severity >= 2 ? L.T("Trung bình") : L.T("Thấp");
        public string StateText => Active ? L.T("Đang hoạt động") : L.T("Đã xử lý");
        public Brush Fg => Active ? Pal.Red : Pal.Green;
    }

    /// <summary>Kiểm tra nhanh các thiết lập bảo mật quan trọng và sửa một chạm.</summary>
    public static class SecurityCheck
    {
        private const string PolSystem = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System";
        private const string PolExplorer = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\Explorer";
        private const string Ts = @"HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server";

        /// <summary>Các giá trị registry trang Bảo mật thay đổi (được sao lưu cùng phần tinh chỉnh).</summary>
        public static IEnumerable<(string path, string name)> ManagedValues
        {
            get
            {
                yield return (PolSystem, "EnableLUA");
                yield return (PolSystem, "ConsentPromptBehaviorAdmin");
                yield return (PolSystem, "PromptOnSecureDesktop");
                yield return (PolExplorer, "NoDriveTypeAutoRun");
                yield return (Ts, "fDenyTSConnections");
            }
        }

        public static string MpCmdRun => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Windows Defender\MpCmdRun.exe");

        private class Probe
        {
            public readonly Dictionary<string, string> V = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public readonly List<(string name, int state)> Av = new List<(string, int)>();
            public readonly List<(string name, int state)> Fw = new List<(string, int)>();
            public readonly List<(string name, bool on)> Profiles = new List<(string, bool)>();
            public string Get(string k) => V.TryGetValue(k, out var v) ? v : null;
            public bool? Bool(string k) { var v = Get(k); if (v == null) return null; return string.Equals(v, "True", StringComparison.OrdinalIgnoreCase); }
        }

        private static async Task<Probe> ProbeAsync()
        {
            const string script = @"
try { $s = Get-MpComputerStatus -ErrorAction Stop
  'mp=1'; 'rt=' + $s.RealTimeProtectionEnabled; 'avOn=' + $s.AntivirusEnabled; 'am=' + $s.AMServiceEnabled
  'mode=' + $s.AMRunningMode
  if ($s.AntivirusSignatureLastUpdated) { 'sig=' + $s.AntivirusSignatureLastUpdated.ToString('yyyy-MM-ddTHH:mm:ss') }
  if ($s.QuickScanEndTime) { 'quick=' + $s.QuickScanEndTime.ToString('yyyy-MM-ddTHH:mm:ss') }
} catch { 'mp=0' }
try { $p = Get-MpPreference -ErrorAction Stop; 'pua=' + [int]$p.PUAProtection } catch {}
try { Get-NetFirewallProfile -ErrorAction Stop | ForEach-Object { 'fwp=' + $_.Name + '|' + ($_.Enabled.ToString()) } } catch {}
try { 'smb1=' + (Get-SmbServerConfiguration -ErrorAction Stop).EnableSMB1Protocol } catch {}
try { Get-CimInstance -Namespace root/SecurityCenter2 -ClassName AntiVirusProduct -ErrorAction Stop | ForEach-Object { 'av=' + $_.displayName + '|' + $_.productState } } catch {}
try { Get-CimInstance -Namespace root/SecurityCenter2 -ClassName FirewallProduct -ErrorAction Stop | ForEach-Object { 'fw=' + $_.displayName + '|' + $_.productState } } catch {}
try { 'sb=' + (Confirm-SecureBootUEFI -ErrorAction Stop) } catch { 'sb=' }
";
            var r = await ProcessRunner.PowerShellAsync(script).ConfigureAwait(false);
            var p = new Probe();
            foreach (var raw in (r.Output ?? "").Split('\n'))
            {
                var line = raw.Trim();
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                var k = line.Substring(0, i);
                var v = line.Substring(i + 1);
                if (k == "av" || k == "fw")
                {
                    int j = v.LastIndexOf('|');
                    if (j > 0 && int.TryParse(v.Substring(j + 1), out var st)) (k == "av" ? p.Av : p.Fw).Add((v.Substring(0, j), st));
                }
                else if (k == "fwp")
                {
                    int j = v.IndexOf('|');
                    if (j > 0) p.Profiles.Add((v.Substring(0, j), string.Equals(v.Substring(j + 1), "True", StringComparison.OrdinalIgnoreCase)));
                }
                else p.V[k] = v;
            }
            return p;
        }

        private static bool ProductOn(int state) => ((state >> 12) & 0xF) == 1 || ((state >> 12) & 0xF) == 3;
        private static bool ProductUpToDate(int state) => ((state >> 4) & 0xF) == 0;
        private static bool IsDefender(string name) => name.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) >= 0;

        public static async Task<List<SecItem>> RunAsync()
        {
            var p = await ProbeAsync().ConfigureAwait(false);
            var list = new List<SecItem>();
            bool defenderActive = p.Get("mp") == "1" && p.Bool("rt") == true && !string.Equals(p.Get("mode"), "Passive Mode", StringComparison.OrdinalIgnoreCase);
            var thirdAv = p.Av.Where(a => !IsDefender(a.name) && ProductOn(a.state)).ToList();

            // 1. Diệt virus
            var av = new SecItem { Id = "av", Glyph = "", Title = L.T("Bảo vệ chống virus thời gian thực") };
            if (thirdAv.Count > 0)
            {
                av.Level = thirdAv.All(a => ProductUpToDate(a.state)) ? SecLevel.Good : SecLevel.Warn;
                av.Detail = L.F("Đang dùng {0}", string.Join(", ", thirdAv.Select(a => a.name))) +
                            (av.Level == SecLevel.Warn ? L.T(" – mẫu nhận diện chưa được cập nhật") : "");
            }
            else if (defenderActive)
            {
                av.Level = SecLevel.Good;
                av.Detail = L.T("Microsoft Defender đang bảo vệ máy");
            }
            else
            {
                av.Level = SecLevel.Bad;
                av.Detail = L.T("Không có phần mềm diệt virus nào đang bật!");
                av.Advice = L.T("Phần mềm crack, \"tool kích hoạt\" thường tắt Defender để cài mã độc. Hãy bật lại ngay.");
                if (p.Get("mp") == "1")
                {
                    av.FixText = L.T("Bật lại");
                    av.Fix = async () =>
                    {
                        await ProcessRunner.PowerShellAsync("Set-MpPreference -DisableRealtimeMonitoring $false -ErrorAction SilentlyContinue").ConfigureAwait(false);
                        ProcessRunner.Open("windowsdefender://threatsettings");
                        return L.T("Đã gửi lệnh bật lại. Nếu vẫn tắt, hãy bật « Real-time protection » trong cửa sổ Windows Security vừa mở.");
                    };
                }
                else
                {
                    av.FixText = L.T("Mở Windows Security");
                    av.Fix = () => { ProcessRunner.Open("windowsdefender:"); return Task.FromResult(""); };
                }
            }
            list.Add(av);

            // 2. Mẫu nhận diện & 3. Chặn phần mềm rác – chỉ khi Defender là phần mềm chính
            if (defenderActive && thirdAv.Count == 0)
            {
                var sig = new SecItem { Id = "sig", Glyph = "", Title = L.T("Cập nhật mẫu nhận diện virus") };
                if (DateTime.TryParse(p.Get("sig"), out var sd))
                {
                    var days = (DateTime.Now - sd).TotalDays;
                    sig.Level = days > 14 ? SecLevel.Bad : days > 3 ? SecLevel.Warn : SecLevel.Good;
                    sig.Detail = L.F("Cập nhật lần cuối: {0} ({1:0} ngày trước)", sd.ToString("dd/MM/yyyy HH:mm"), Math.Max(0, days));
                }
                else { sig.Level = SecLevel.Warn; sig.Detail = L.T("Không rõ thời gian cập nhật"); }
                sig.FixText = L.T("Cập nhật ngay");
                sig.Fix = async () =>
                {
                    var r = await ProcessRunner.RunAsync(MpCmdRun, "-SignatureUpdate").ConfigureAwait(false);
                    return r.Ok ? L.T("Đã cập nhật mẫu nhận diện virus.") : L.T("Chưa cập nhật được – hãy kiểm tra Internet.");
                };
                list.Add(sig);

                var pua = new SecItem
                {
                    Id = "pua", Glyph = "", Title = L.T("Chặn phần mềm rác, quảng cáo (PUA)"),
                    Advice = L.T("Chặn các bộ cài kèm quảng cáo, đổi trang chủ, \"tối ưu\" giả mạo… thường gặp khi tải phần mềm trên mạng.")
                };
                pua.Level = p.Get("pua") == "1" ? SecLevel.Good : SecLevel.Warn;
                pua.Detail = pua.Level == SecLevel.Good ? L.T("Đang bật") : L.T("Đang tắt");
                pua.FixText = L.T("Bật");
                pua.Fix = async () =>
                {
                    await ProcessRunner.PowerShellAsync("Set-MpPreference -PUAProtection Enabled").ConfigureAwait(false);
                    return L.T("Đã bật chặn phần mềm rác.");
                };
                list.Add(pua);
            }

            // 4. Tường lửa
            var fw = new SecItem { Id = "fw", Glyph = "", Title = L.T("Tường lửa (Firewall)") };
            var thirdFw = p.Fw.Where(f => !IsDefender(f.name) && f.name.IndexOf("Windows", StringComparison.OrdinalIgnoreCase) < 0 && ProductOn(f.state)).ToList();
            var off = p.Profiles.Where(x => !x.on).Select(x => x.name).ToList();
            if (thirdFw.Count > 0) { fw.Level = SecLevel.Good; fw.Detail = L.F("Đang dùng {0}", string.Join(", ", thirdFw.Select(f => f.name))); }
            else if (p.Profiles.Count == 0) { fw.Level = SecLevel.Info; fw.Detail = L.T("Không đọc được trạng thái tường lửa"); }
            else if (off.Count == 0) { fw.Level = SecLevel.Good; fw.Detail = L.T("Đang bật cho mọi loại mạng"); }
            else
            {
                fw.Level = SecLevel.Bad;
                fw.Detail = L.T("Đang tắt cho mạng: ") + string.Join(", ", off.Select(ProfileName));
                fw.Advice = L.T("Tắt tường lửa khiến máy dễ bị tấn công khi dùng Wi-Fi công cộng.");
                fw.FixText = L.T("Bật tường lửa");
                fw.Fix = async () =>
                {
                    await ProcessRunner.PowerShellAsync("Set-NetFirewallProfile -Profile Domain,Public,Private -Enabled True").ConfigureAwait(false);
                    return L.T("Đã bật tường lửa.");
                };
            }
            list.Add(fw);

            // 5. UAC
            var uac = new SecItem { Id = "uac", Glyph = "", Title = L.T("Kiểm soát tài khoản người dùng (UAC)") };
            var lua = RegistryHelper.GetValue(PolSystem, "EnableLUA");
            var cp = RegistryHelper.GetValue(PolSystem, "ConsentPromptBehaviorAdmin");
            if (RegistryHelper.ValueEquals(lua, 0)) { uac.Level = SecLevel.Bad; uac.Detail = L.T("UAC đang bị tắt hoàn toàn – mọi chương trình đều chạy với quyền cao nhất"); }
            else if (RegistryHelper.ValueEquals(cp, 0)) { uac.Level = SecLevel.Warn; uac.Detail = L.T("UAC đặt ở mức \"Không bao giờ thông báo\""); }
            else { uac.Level = SecLevel.Good; uac.Detail = L.T("Đang hỏi trước khi chương trình thay đổi hệ thống"); }
            uac.FixText = L.T("Đặt mức khuyên dùng");
            uac.Fix = () =>
            {
                bool needRestart = RegistryHelper.ValueEquals(RegistryHelper.GetValue(PolSystem, "EnableLUA"), 0);
                RegistryHelper.SetValue(PolSystem, "EnableLUA", 1, RegistryValueKind.DWord);
                RegistryHelper.SetValue(PolSystem, "ConsentPromptBehaviorAdmin", 5, RegistryValueKind.DWord);
                RegistryHelper.SetValue(PolSystem, "PromptOnSecureDesktop", 1, RegistryValueKind.DWord);
                Logger.Info("Đặt UAC về mức khuyên dùng");
                return Task.FromResult(needRestart ? L.T("Đã bật UAC. Cần khởi động lại máy để có hiệu lực.") : L.T("Đã đặt UAC về mức khuyên dùng."));
            };
            list.Add(uac);

            // 6. SMBv1
            var smb = new SecItem
            {
                Id = "smb1", Glyph = "", Title = L.T("Giao thức chia sẻ cũ SMBv1"),
                Advice = L.T("SMBv1 là lỗ hổng mã độc tống tiền WannaCry từng lợi dụng. Chỉ máy in/ổ mạng rất cũ mới cần.")
            };
            var s1 = p.Bool("smb1");
            if (s1 == true)
            {
                smb.Level = SecLevel.Warn; smb.Detail = L.T("Đang bật");
                smb.FixText = L.T("Tắt SMBv1");
                smb.Fix = async () =>
                {
                    await ProcessRunner.PowerShellAsync("Set-SmbServerConfiguration -EnableSMB1Protocol $false -Force; Disable-WindowsOptionalFeature -Online -FeatureName SMB1Protocol -NoRestart -ErrorAction SilentlyContinue | Out-Null").ConfigureAwait(false);
                    return L.T("Đã tắt SMBv1. Nếu máy in/ổ mạng cũ không truy cập được, có thể bật lại trong Windows Features.");
                };
            }
            else { smb.Level = s1 == false ? SecLevel.Good : SecLevel.Info; smb.Detail = s1 == false ? L.T("Đã tắt") : L.T("Không xác định"); }
            list.Add(smb);

            // 7. AutoRun USB
            var ar = new SecItem
            {
                Id = "autorun", Glyph = "", Title = L.T("Chặn tự chạy (AutoRun) khi cắm USB, đĩa"),
                Advice = L.T("Virus USB lây qua cơ chế tự chạy rất phổ biến ở quán in, phòng máy.")
            };
            ar.Level = RegistryHelper.ValueEquals(RegistryHelper.GetValue(PolExplorer, "NoDriveTypeAutoRun"), 255) ? SecLevel.Good : SecLevel.Warn;
            ar.Detail = ar.Level == SecLevel.Good ? L.T("Đã chặn cho mọi loại ổ đĩa") : L.T("Chưa chặn hoàn toàn");
            ar.FixText = L.T("Chặn");
            ar.Fix = () =>
            {
                RegistryHelper.SetValue(PolExplorer, "NoDriveTypeAutoRun", 255, RegistryValueKind.DWord);
                Logger.Info("Chặn AutoRun cho mọi ổ đĩa");
                return Task.FromResult(L.T("Đã chặn tự chạy cho USB và ổ đĩa."));
            };
            list.Add(ar);

            // 8. Đuôi tệp
            var extTweak = TweakCatalog.Build().FirstOrDefault(t => t.Id == "ui_ext");
            if (extTweak != null)
            {
                var ext = new SecItem
                {
                    Id = "ext", Glyph = "", Title = L.T("Hiện đuôi tệp (.exe, .pdf…)"),
                    Advice = L.T("Giúp nhận ra tệp giả mạo kiểu \"hoadon.pdf.exe\" gửi qua Zalo, email.")
                };
                ext.Level = extTweak.Check() ? SecLevel.Good : SecLevel.Warn;
                ext.Detail = ext.Level == SecLevel.Good ? L.T("Đang hiện") : L.T("Đang ẩn đuôi tệp");
                ext.FixText = L.T("Hiện đuôi tệp");
                ext.Fix = () => { extTweak.Apply(); return Task.FromResult(L.T("Đã bật hiện đuôi tệp (có thể hoàn tác trong Tối ưu › Tinh chỉnh).")); };
                list.Add(ext);
            }

            // 9. Remote Desktop
            var rdp = new SecItem { Id = "rdp", Glyph = "", Title = L.T("Điều khiển từ xa (Remote Desktop)"), Scored = false };
            if (RegistryHelper.ValueEquals(RegistryHelper.GetValue(Ts, "fDenyTSConnections"), 0))
            {
                rdp.Level = SecLevel.Info;
                rdp.Detail = L.T("Đang bật – chỉ nên bật khi bạn thực sự điều khiển máy này từ xa");
                rdp.FixText = L.T("Tắt Remote Desktop");
                rdp.Fix = () =>
                {
                    RegistryHelper.SetValue(Ts, "fDenyTSConnections", 1, RegistryValueKind.DWord);
                    Logger.Info("Tắt Remote Desktop");
                    return Task.FromResult(L.T("Đã tắt Remote Desktop."));
                };
            }
            else { rdp.Level = SecLevel.Good; rdp.Detail = L.T("Đang tắt"); }
            list.Add(rdp);

            // 10. Windows Update
            var wu = new SecItem { Id = "wu", Glyph = "", Title = L.T("Bản vá bảo mật Windows") };
            if (Wua.ServiceDisabled)
            {
                wu.Level = SecLevel.Bad; wu.Detail = L.T("Dịch vụ Windows Update đang bị tắt – máy không nhận bản vá lỗ hổng");
                wu.FixText = L.T("Bật lại");
                wu.Fix = () => { Wua.EnableService(); return Task.FromResult(L.T("Đã bật lại dịch vụ Windows Update.")); };
            }
            else
            {
                var paused = WuManager.PausedUntil;
                var last = await Task.Run(() => Wua.LastSuccessfulInstall()).ConfigureAwait(false);
                var days = last.HasValue ? (DateTime.Now - last.Value).TotalDays : (double?)null;
                if (days.HasValue && days > 60) { wu.Level = SecLevel.Warn; wu.Detail = L.F("Lần cài bản cập nhật gần nhất: {0} ({1:0} ngày trước)", last.Value.ToString("dd/MM/yyyy"), days); }
                else if (days.HasValue) { wu.Level = SecLevel.Good; wu.Detail = L.F("Lần cài bản cập nhật gần nhất: {0}", last.Value.ToString("dd/MM/yyyy")); }
                else { wu.Level = SecLevel.Info; wu.Detail = L.T("Không đọc được lịch sử cập nhật"); }
                if (paused.HasValue) { wu.Detail += L.F("  ·  đang tạm dừng đến {0}", paused.Value.ToString("dd/MM/yyyy")); if (wu.Level == SecLevel.Good) wu.Level = SecLevel.Info; }
            }
            list.Add(wu);

            // 11. Secure Boot
            var sb = new SecItem { Id = "sb", Glyph = "", Title = "Secure Boot", Scored = false };
            var sbv = p.Bool("sb");
            if (string.IsNullOrEmpty(p.Get("sb"))) { sb.Level = SecLevel.Info; sb.Detail = L.T("Máy không hỗ trợ (BIOS kiểu cũ) hoặc không đọc được"); }
            else if (sbv == true) { sb.Level = SecLevel.Good; sb.Detail = L.T("Đang bật – chặn mã độc khởi động cùng máy (bootkit)"); }
            else { sb.Level = SecLevel.Info; sb.Detail = L.T("Đang tắt – có thể bật trong BIOS/UEFI (mục Security › Secure Boot)"); }
            list.Add(sb);

            return list;
        }

        private static string ProfileName(string n)
        {
            switch (n)
            {
                case "Domain": return L.T("Miền");
                case "Private": return L.T("Riêng tư");
                case "Public": return L.T("Công cộng");
                default: return n;
            }
        }

        public static int Score(List<SecItem> items)
        {
            var scored = items.Where(i => i.Scored).ToList();
            if (scored.Count == 0) return 100;
            double pts = scored.Sum(i => i.Level == SecLevel.Good || i.Level == SecLevel.Info ? 1.0 : i.Level == SecLevel.Warn ? 0.5 : 0);
            return (int)Math.Round(100 * pts / scored.Count);
        }

        public static bool DefenderAvailable => File.Exists(MpCmdRun);

        /// <summary>Quét virus bằng Microsoft Defender. scanType: 1 nhanh, 2 toàn bộ, 3 thư mục.</summary>
        public static async Task<RunResult> ScanAsync(int scanType, string path, Action<string> progress, CancellationToken ct)
        {
            var args = "-Scan -ScanType " + scanType + (scanType == 3 ? " -File \"" + (path.EndsWith("\\") ? path + "." : path) + "\"" : "");
            Logger.Info("Quét virus: MpCmdRun " + args);
            using (ct.Register(() => { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(MpCmdRun, "-Scan -Cancel") { CreateNoWindow = true, UseShellExecute = false }); } catch { } }))
                return await ProcessRunner.RunAsync(MpCmdRun, args, (line, pr) => progress?.Invoke(line.Trim()), null, ct).ConfigureAwait(false);
        }

        public static async Task<List<ThreatInfo>> ThreatsAsync()
        {
            var r = await ProcessRunner.PowerShellAsync(
                "Get-MpThreat -ErrorAction SilentlyContinue | ForEach-Object { 't=' + $_.ThreatName + '|' + $_.SeverityID + '|' + $_.IsActive }").ConfigureAwait(false);
            var list = new List<ThreatInfo>();
            foreach (var raw in (r.Output ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (!line.StartsWith("t=")) continue;
                var parts = line.Substring(2).Split('|');
                if (parts.Length < 3) continue;
                list.Add(new ThreatInfo { Name = parts[0], Severity = int.TryParse(parts[1], out var s) ? s : 0, Active = string.Equals(parts[2], "True", StringComparison.OrdinalIgnoreCase) });
            }
            return list.OrderByDescending(t => t.Active).ThenByDescending(t => t.Severity).ToList();
        }

        public static Task<RunResult> RemoveThreatsAsync() => ProcessRunner.PowerShellAsync("Remove-MpThreat -ErrorAction SilentlyContinue; 'OK'");
    }
}
