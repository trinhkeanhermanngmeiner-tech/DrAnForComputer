using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using BacSiMayTinh.Controls;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    public class RegSet
    {
        public string Path, Name;
        public object Value;
        public RegistryValueKind Kind;
        public object Default; // giá trị khi hoàn tác mà không có bản sao lưu (null = xóa giá trị)

        public RegSet(string path, string name, object value, RegistryValueKind kind = RegistryValueKind.DWord, object def = null)
        {
            Path = path; Name = name; Value = value; Kind = kind; Default = def;
        }

        public bool IsApplied() => RegistryHelper.ValueEquals(RegistryHelper.GetValue(Path, Name), Value);
    }

    public class Tweak : ObservableObject
    {
        public string Id { get; set; }
        public string Category { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Note { get; set; }
        public bool HasNote => !string.IsNullOrEmpty(Note);
        public Risk Risk { get; set; }
        public bool Recommended { get; set; }
        public bool LowEnd { get; set; }
        public List<RegSet> Sets { get; } = new List<RegSet>();
        public string RevertDeleteKey { get; set; }
        public Func<bool> CustomCheck { get; set; }
        public Action CustomApply { get; set; }
        public Action CustomRevert { get; set; }
        public Func<bool> Supported { get; set; } = () => true;

        public string RiskText => RiskInfo.Text(Risk);
        public Brush RiskFg => Pal.RiskFg(Risk);
        public Brush RiskBg => Pal.RiskBg(Risk);

        private bool _isApplied;
        public bool IsApplied { get => _isApplied; set => Set(ref _isApplied, value); }

        private bool _busy;
        public bool IsBusy { get => _busy; set { Set(ref _busy, value); OnPropertyChanged(nameof(IsIdle)); } }
        public bool IsIdle => !_busy;

        public bool Check()
        {
            try
            {
                if (CustomCheck != null) return CustomCheck();
                return Sets.Count > 0 && Sets.All(s => s.IsApplied());
            }
            catch { return false; }
        }

        public void Refresh() => IsApplied = Check();

        /// <summary>Tinh chỉnh này do DrAn For Computer áp dụng (có thể hoàn tác).</summary>
        public bool AppliedByUs => UndoStore.Current.Misc.ContainsKey("applied:" + Id) || UndoStore.Current.Tweaks.ContainsKey(Id);

        public void Apply()
        {
            var store = UndoStore.Current;
            if (Sets.Count > 0 && !store.Tweaks.ContainsKey(Id))
                store.Tweaks[Id] = Sets.Select(s => UndoStore.Capture(s.Path, s.Name)).ToList();
            foreach (var s in Sets) RegistryHelper.SetValue(s.Path, s.Name, s.Value, s.Kind);
            CustomApply?.Invoke();
            store.Misc["applied:" + Id] = DateTime.Now.ToString("s");
            UndoStore.SaveCurrent();
            Logger.Info("Áp dụng tinh chỉnh: " + Name);
        }

        public void Revert()
        {
            var store = UndoStore.Current;
            if (store.Tweaks.TryGetValue(Id, out var backup))
            {
                foreach (var b in backup) UndoStore.Restore(b);
                store.Tweaks.Remove(Id);
            }
            // Nếu vẫn còn áp dụng (vd. giá trị đã có từ trước), đưa về mặc định của Windows.
            if (Sets.Count > 0 && Sets.All(s => s.IsApplied()))
                foreach (var s in Sets)
                {
                    if (s.Default != null) RegistryHelper.SetValue(s.Path, s.Name, s.Default, s.Kind);
                    else RegistryHelper.DeleteValue(s.Path, s.Name);
                }
            if (RevertDeleteKey != null) RegistryHelper.DeleteKeyTree(RevertDeleteKey);
            CustomRevert?.Invoke();
            store.Misc.Remove("applied:" + Id);
            UndoStore.SaveCurrent();
            Logger.Info("Hoàn tác tinh chỉnh: " + Name);
        }
    }

    public class TweakCategory
    {
        public string Name { get; set; }
        public string Glyph { get; set; }
        public List<Tweak> Items { get; set; }
    }

    public static class TweakCatalog
    {
        private const string Adv = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        private const string Cdm = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
        private const string Desk = @"HKCU\Control Panel\Desktop";
        private const string Sp = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        private const RegistryValueKind S = RegistryValueKind.String;

        public static readonly Dictionary<string, string> CategoryGlyphs = new Dictionary<string, string>
        {
            ["Hiệu năng"] = "",
            ["Giao diện & Explorer"] = "",
            ["Quảng cáo & gợi ý"] = "",
            ["Quyền riêng tư"] = "",
            ["Chơi game"] = "",
            ["Mạng & cập nhật"] = "",
        };

        private static Tweak T(string id, string cat, string name, string desc, Risk risk, bool rec, bool lowEnd, params RegSet[] sets)
        {
            var t = new Tweak { Id = id, Category = cat, Name = name, Description = desc, Risk = risk, Recommended = rec, LowEnd = lowEnd };
            t.Sets.AddRange(sets);
            return t;
        }

        public static List<Tweak> Build()
        {
            var list = new List<Tweak>();
            string c;

            // ---------- Hiệu năng ----------
            c = "Hiệu năng";
            var visual = T("perf_visual", c, L.T("Tối ưu hiệu ứng hình ảnh cho hiệu năng"),
                L.T("Tắt hoạt ảnh, bóng đổ, hiệu ứng mờ dần… nhưng vẫn giữ chữ mịn và ảnh xem trước. Rất hiệu quả trên máy yếu."),
                Risk.Safe, true, true,
                new RegSet(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 3, def: 0),
                new RegSet(Desk, "UserPreferencesMask", new byte[] { 0x90, 0x12, 0x03, 0x80, 0x10, 0x00, 0x00, 0x00 }, RegistryValueKind.Binary,
                    new byte[] { 0x9E, 0x1E, 0x07, 0x80, 0x12, 0x00, 0x00, 0x00 }),
                new RegSet(Desk + @"\WindowMetrics", "MinAnimate", "0", S, "1"),
                new RegSet(Desk, "DragFullWindows", "0", S, "1"),
                new RegSet(Desk, "FontSmoothing", "2", S, "2"),
                new RegSet(Adv, "TaskbarAnimations", 0, def: 1),
                new RegSet(Adv, "ListviewAlphaSelect", 0, def: 1),
                new RegSet(Adv, "ListviewShadow", 0, def: 1),
                new RegSet(Adv, "IconsOnly", 0, def: 0),
                new RegSet(@"HKCU\Software\Microsoft\Windows\DWM", "EnableAeroPeek", 0, def: 1));
            visual.Note = L.T("Có hiệu lực đầy đủ sau khi đăng xuất hoặc khởi động lại.");
            list.Add(visual);

            list.Add(T("perf_transparency", c, L.T("Tắt hiệu ứng trong suốt"),
                L.T("Thanh Taskbar, menu Start không còn trong suốt – giảm tải cho card đồ họa yếu."),
                Risk.Safe, false, true,
                new RegSet(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0, def: 1)));

            var bg = T("perf_bgapps", c, L.T("Chặn ứng dụng Store chạy ngầm"),
                L.T("Ứng dụng tải từ Microsoft Store (Thời tiết, Tin tức, Thư…) không được chạy ngầm khi bạn không mở chúng."),
                Risk.Moderate, false, true,
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "LetAppsRunInBackground", 2));
            bg.Note = L.T("Ứng dụng Thư/Lịch sẽ không báo thư mới khi đang đóng.");
            list.Add(bg);

            list.Add(T("perf_menu", c, L.T("Mở menu nhanh hơn"),
                L.T("Giảm độ trễ khi rê chuột vào menu con từ 400ms xuống 100ms."),
                Risk.Safe, true, true,
                new RegSet(Desk, "MenuShowDelay", "100", S, "400")));

            list.Add(T("perf_shutdown", c, L.T("Tăng tốc tắt máy"),
                L.T("Rút ngắn thời gian Windows chờ các ứng dụng/dịch vụ bị treo khi tắt máy."),
                Risk.Moderate, false, true,
                new RegSet(@"HKLM\SYSTEM\CurrentControlSet\Control", "WaitToKillServiceTimeout", "2000", S, "5000"),
                new RegSet(Desk, "HungAppTimeout", "3000", S, "5000"),
                new RegSet(Desk, "WaitToKillAppTimeout", "5000", S, "20000")));

            var power = new Tweak
            {
                Id = "perf_power", Category = c, Name = L.T("Bật chế độ nguồn Hiệu suất cao"),
                Description = L.T("CPU luôn sẵn sàng chạy ở xung nhịp cao, máy phản hồi nhanh hơn."),
                Note = L.T("Laptop sẽ tốn pin hơn một chút."),
                Risk = Risk.Safe, Recommended = false, LowEnd = true,
                CustomCheck = PowerPlan.IsHighPerformance,
                CustomApply = PowerPlan.ApplyHighPerformance,
                CustomRevert = PowerPlan.Restore
            };
            list.Add(power);

            list.Add(new Tweak
            {
                Id = "perf_hibernate", Category = c, Name = L.T("Tắt ngủ đông (Hibernate) – giải phóng ổ C"),
                Description = L.T("Xóa tệp hiberfil.sys (thường bằng 40–75% dung lượng RAM). Phù hợp khi ổ C sắp đầy."),
                Note = L.T("Tính năng Ngủ đông và Khởi động nhanh (Fast Startup) sẽ bị tắt."),
                Risk = Risk.Moderate,
                CustomCheck = () => RegistryHelper.ValueEquals(RegistryHelper.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Control\Power", "HibernateEnabled"), 0),
                CustomApply = () => ProcessRunner.CmdAsync("powercfg /h off").Wait(),
                CustomRevert = () => ProcessRunner.CmdAsync("powercfg /h on").Wait()
            });

            // ---------- Giao diện & Explorer ----------
            c = "Giao diện & Explorer";
            list.Add(T("ui_ext", c, L.T("Hiện đuôi mở rộng của tệp (.exe, .pdf…)"),
                L.T("Giúp nhận ra virus giả mạo kiểu \"hoadon.pdf.exe\". Rất nên bật."),
                Risk.Safe, true, false, new RegSet(Adv, "HideFileExt", 0, def: 1)));
            list.Add(T("ui_hidden", c, L.T("Hiện tệp và thư mục ẩn"),
                L.T("Thấy được các tệp bị ẩn (hữu ích khi USB bị virus ẩn dữ liệu)."),
                Risk.Safe, false, false, new RegSet(Adv, "Hidden", 1, def: 2)));
            list.Add(T("ui_thispc", c, L.T("Mở File Explorer tại \"This PC\""),
                L.T("Khi mở File Explorer sẽ thấy ngay các ổ đĩa thay vì Quick Access."),
                Risk.Safe, false, false, new RegSet(Adv, "LaunchTo", 1, def: 2)));

            var classic = T("ui_classicmenu", c, L.T("Dùng menu chuột phải kiểu cũ (Windows 11)"),
                L.T("Hiện đầy đủ menu chuột phải như Windows 10, không cần bấm \"Show more options\"."),
                Risk.Safe, false, false,
                new RegSet(@"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "", "", S));
            classic.RevertDeleteKey = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
            classic.Note = L.T("Cần khởi động lại Explorer để thấy thay đổi.");
            classic.Supported = () => AppInfo.IsWindows11;
            list.Add(classic);

            var left = T("ui_taskbarleft", c, L.T("Căn trái các biểu tượng trên Taskbar (Windows 11)"),
                L.T("Đưa nút Start và các biểu tượng về bên trái như Windows 10."),
                Risk.Safe, false, false, new RegSet(Adv, "TaskbarAl", 0, def: 1));
            left.Supported = () => AppInfo.IsWindows11;
            list.Add(left);

            list.Add(T("ui_taskview", c, L.T("Ẩn nút Task View trên Taskbar"),
                L.T("Gọn thanh Taskbar. Vẫn mở được bằng phím Windows + Tab."),
                Risk.Safe, false, false, new RegSet(Adv, "ShowTaskViewButton", 0, def: 1)));

            list.Add(T("ui_news", c, L.T("Tắt Tin tức & Widgets trên Taskbar"),
                L.T("Widget tin tức/thời tiết tốn RAM và mạng. Tắt giúp máy yếu nhẹ hơn rõ rệt."),
                Risk.Safe, true, true,
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Feeds", "EnableFeeds", 0),
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0)));

            list.Add(T("ui_websearch", c, L.T("Tắt kết quả Bing trong tìm kiếm Start"),
                L.T("Tìm kiếm trong Start chỉ tìm trên máy – nhanh và gọn hơn."),
                Risk.Safe, true, true,
                new RegSet(@"HKCU\Software\Policies\Microsoft\Windows\Explorer", "DisableSearchBoxSuggestions", 1),
                new RegSet(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", 0)));

            var cortana = T("ui_cortana", c, L.T("Tắt Cortana"),
                L.T("Tắt trợ lý ảo Cortana (không hỗ trợ tiếng Việt)."),
                Risk.Safe, false, true,
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search", "AllowCortana", 0));
            cortana.Supported = () => !AppInfo.IsWindows11;
            list.Add(cortana);

            var copilot = T("ui_copilot", c, L.T("Tắt Copilot (Windows 11)"),
                L.T("Ẩn và tắt trợ lý Copilot tích hợp trong Windows."),
                Risk.Safe, false, false,
                new RegSet(@"HKCU\Software\Policies\Microsoft\Windows\WindowsCopilot", "TurnOffWindowsCopilot", 1),
                new RegSet(Adv, "ShowCopilotButton", 0, def: 1));
            copilot.Supported = () => AppInfo.IsWindows11;
            list.Add(copilot);

            // ---------- Quảng cáo & gợi ý ----------
            c = "Quảng cáo & gợi ý";
            list.Add(T("ads_tips", c, L.T("Tắt mẹo, gợi ý và quảng cáo của Windows"),
                L.T("Không còn gợi ý ứng dụng trong Start, Cài đặt và thông báo \"mẹo\"."),
                Risk.Safe, true, true,
                new RegSet(Cdm, "SubscribedContent-338388Enabled", 0, def: 1),
                new RegSet(Cdm, "SubscribedContent-338389Enabled", 0, def: 1),
                new RegSet(Cdm, "SubscribedContent-353694Enabled", 0, def: 1),
                new RegSet(Cdm, "SubscribedContent-353696Enabled", 0, def: 1),
                new RegSet(Cdm, "SubscribedContent-310093Enabled", 0, def: 1),
                new RegSet(Cdm, "SubscribedContent-338393Enabled", 0, def: 1),
                new RegSet(Cdm, "SystemPaneSuggestionsEnabled", 0, def: 1),
                new RegSet(Cdm, "SoftLandingEnabled", 0, def: 1)));
            list.Add(T("ads_autoinstall", c, L.T("Chặn Windows tự cài ứng dụng quảng cáo"),
                L.T("Ngăn Windows tự tải về Candy Crush, TikTok… và các ứng dụng được \"gợi ý\"."),
                Risk.Safe, true, true,
                new RegSet(Cdm, "SilentInstalledAppsEnabled", 0, def: 1),
                new RegSet(Cdm, "PreInstalledAppsEnabled", 0, def: 1),
                new RegSet(Cdm, "OemPreInstalledAppsEnabled", 0, def: 1),
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1)));
            list.Add(T("ads_lockscreen", c, L.T("Tắt quảng cáo trên màn hình khóa"),
                L.T("Bỏ các mẩu \"Bạn có biết?\" và quảng cáo trên màn hình khóa."),
                Risk.Safe, false, false,
                new RegSet(Cdm, "RotatingLockScreenOverlayEnabled", 0, def: 1),
                new RegSet(Cdm, "SubscribedContent-338387Enabled", 0, def: 1)));
            list.Add(T("ads_explorer", c, L.T("Tắt quảng cáo OneDrive/Office trong File Explorer"),
                L.T("Ẩn các thông báo quảng cáo của nhà cung cấp đồng bộ trong File Explorer."),
                Risk.Safe, true, false, new RegSet(Adv, "ShowSyncProviderNotifications", 0, def: 1)));
            list.Add(T("ads_id", c, L.T("Tắt mã quảng cáo cá nhân hóa"),
                L.T("Ứng dụng không dùng mã định danh để hiển thị quảng cáo theo sở thích."),
                Risk.Safe, true, false,
                new RegSet(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0, def: 1)));
            list.Add(T("ads_scoobe", c, L.T("Tắt màn hình \"Hoàn tất thiết lập thiết bị\""),
                L.T("Không còn bị nhắc đăng ký Microsoft 365, OneDrive… sau khi cập nhật."),
                Risk.Safe, true, false,
                new RegSet(@"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement", "ScoobeSystemSettingEnabled", 0, def: 1)));

            // ---------- Quyền riêng tư ----------
            c = "Quyền riêng tư";
            list.Add(T("priv_telemetry", c, L.T("Giảm thu thập dữ liệu chẩn đoán (Telemetry)"),
                L.T("Hạn chế dữ liệu sử dụng gửi về Microsoft ở mức tối thiểu."),
                Risk.Safe, true, true,
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0),
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DataCollection", "DoNotShowFeedbackNotifications", 1)));
            list.Add(T("priv_activity", c, L.T("Tắt lịch sử hoạt động (Activity History)"),
                L.T("Windows không ghi lại và đồng bộ các hoạt động bạn đã làm trên máy."),
                Risk.Safe, true, false,
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "EnableActivityFeed", 0),
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "PublishUserActivities", 0),
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\System", "UploadUserActivities", 0)));
            list.Add(T("priv_tailored", c, L.T("Tắt trải nghiệm tùy chỉnh theo dữ liệu chẩn đoán"),
                L.T("Microsoft không dùng dữ liệu chẩn đoán để gợi ý, quảng cáo riêng cho bạn."),
                Risk.Safe, true, false,
                new RegSet(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", 0, def: 1)));
            list.Add(T("priv_feedback", c, L.T("Không hỏi ý kiến phản hồi"),
                L.T("Tắt các hộp thoại khảo sát \"Bạn có hài lòng với Windows?\"."),
                Risk.Safe, true, false,
                new RegSet(@"HKCU\Software\Microsoft\Siuf\Rules", "NumberOfSIUFInPeriod", 0)));
            list.Add(T("priv_inking", c, L.T("Tắt thu thập dữ liệu gõ phím & viết tay"),
                L.T("Windows không gửi mẫu chữ bạn gõ/viết để \"cải thiện nhận dạng\"."),
                Risk.Safe, true, false,
                new RegSet(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitInkCollection", 1, def: 0),
                new RegSet(@"HKCU\Software\Microsoft\InputPersonalization", "RestrictImplicitTextCollection", 1, def: 0),
                new RegSet(@"HKCU\Software\Microsoft\Input\TIPC", "Enabled", 0, def: 1)));
            var loc = T("priv_location", c, L.T("Tắt dịch vụ định vị"),
                L.T("Ứng dụng không thể biết vị trí của máy."),
                Risk.Moderate, false, false,
                new RegSet(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\location", "Value", "Deny", S, "Allow"));
            loc.Note = L.T("Bản đồ, Thời tiết, Tìm thiết bị sẽ không dùng được vị trí.");
            list.Add(loc);
            list.Add(T("priv_remote", c, L.T("Tắt Hỗ trợ từ xa (Remote Assistance)"),
                L.T("Đóng một cổng có thể bị kẻ xấu lợi dụng để điều khiển máy từ xa."),
                Risk.Safe, true, false,
                new RegSet(@"HKLM\SYSTEM\CurrentControlSet\Control\Remote Assistance", "fAllowToGetHelp", 0, def: 1)));

            // ---------- Chơi game ----------
            c = "Chơi game";
            list.Add(T("game_dvr", c, L.T("Tắt quay video nền (Game DVR)"),
                L.T("Game DVR liên tục ghi hình khi chơi game, gây tụt FPS trên máy yếu."),
                Risk.Safe, true, true,
                new RegSet(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0, def: 1),
                new RegSet(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0, def: 1),
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0)));
            list.Add(T("game_mode", c, L.T("Bật Game Mode"),
                L.T("Windows ưu tiên tài nguyên cho game đang chạy và hạn chế cập nhật nền."),
                Risk.Safe, true, false,
                new RegSet(@"HKCU\Software\Microsoft\GameBar", "AutoGameModeEnabled", 1),
                new RegSet(@"HKCU\Software\Microsoft\GameBar", "AllowAutoGameMode", 1)));
            list.Add(T("game_priority", c, L.T("Ưu tiên CPU/GPU cho game"),
                L.T("Tăng mức ưu tiên lập lịch cho game và giảm giới hạn băng thông mạng đa phương tiện."),
                Risk.Moderate, false, false,
                new RegSet(Sp, "SystemResponsiveness", 10, def: 20),
                new RegSet(Sp, "NetworkThrottlingIndex", unchecked((int)0xFFFFFFFF), def: 10),
                new RegSet(Sp + @"\Tasks\Games", "GPU Priority", 8, def: 8),
                new RegSet(Sp + @"\Tasks\Games", "Priority", 6, def: 2),
                new RegSet(Sp + @"\Tasks\Games", "Scheduling Category", "High", S, "Medium"),
                new RegSet(Sp + @"\Tasks\Games", "SFIO Priority", "High", S, "Normal")));
            var mouse = T("game_mouse", c, L.T("Tắt tăng tốc chuột (Enhance pointer precision)"),
                L.T("Chuột di chuyển chính xác, đều tay – game thủ bắn súng rất ưa dùng."),
                Risk.Safe, false, false,
                new RegSet(@"HKCU\Control Panel\Mouse", "MouseSpeed", "0", S, "1"),
                new RegSet(@"HKCU\Control Panel\Mouse", "MouseThreshold1", "0", S, "6"),
                new RegSet(@"HKCU\Control Panel\Mouse", "MouseThreshold2", "0", S, "10"));
            mouse.CustomApply = () => MouseAccel.Set(false);
            mouse.CustomRevert = () => MouseAccel.Set(true);
            list.Add(mouse);
            var hags = T("game_hags", c, L.T("Bật lập lịch GPU tăng tốc phần cứng (HAGS)"),
                L.T("Giảm độ trễ đồ họa trên card NVIDIA GTX 1000 / AMD RX 5000 trở lên."),
                Risk.Moderate, false, false,
                new RegSet(@"HKLM\SYSTEM\CurrentControlSet\Control\GraphicsDrivers", "HwSchMode", 2, def: 1));
            hags.Note = L.T("Cần card đồ họa hỗ trợ và khởi động lại máy.");
            list.Add(hags);

            // ---------- Mạng & cập nhật ----------
            c = "Mạng & cập nhật";
            list.Add(T("net_dop2p", c, L.T("Tắt chia sẻ bản cập nhật qua mạng (P2P)"),
                L.T("Máy bạn không dùng mạng để gửi bản cập nhật Windows cho máy khác – tiết kiệm băng thông."),
                Risk.Safe, true, true,
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization", "DODownloadMode", 0)));
            list.Add(T("wu_noreboot", c, L.T("Không tự khởi động lại khi đang dùng máy"),
                L.T("Windows Update sẽ không tự khởi động lại máy khi bạn đang đăng nhập."),
                Risk.Safe, true, false,
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate\AU", "NoAutoRebootWithLoggedOnUsers", 1)));
            var drivers = T("wu_nodrivers", c, L.T("Không cài driver qua Windows Update"),
                L.T("Tránh việc Windows tự thay driver card màn hình/âm thanh bạn đã cài."),
                Risk.Moderate, false, false,
                new RegSet(@"HKLM\SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate", "ExcludeWUDriversInQualityUpdate", 1));
            drivers.Note = L.T("Bạn cần tự cập nhật driver từ trang của hãng.");
            list.Add(drivers);

            return list.Where(t => { try { return t.Supported(); } catch { return false; } }).ToList();
        }
    }

    internal static class MouseAccel
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SystemParametersInfo(uint action, uint param, int[] pv, uint winIni);

        public static void Set(bool enabled)
        {
            var v = enabled ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 };
            try { SystemParametersInfo(0x0004 /* SPI_SETMOUSE */, 0, v, 0x01 | 0x02); } catch { }
        }
    }

    /// <summary>Quản lý gói điện năng (Power Plan).</summary>
    public static class PowerPlan
    {
        public const string Balanced = "381b4222-f694-41f0-9685-ff5bb260df2e";
        public const string HighPerf = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public const string Ultimate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        private const string BestPerfOverlay = "ded574b5-45a0-4f42-8737-46345c09c238";

        public static string ActiveScheme() =>
            (RegistryHelper.GetValue(@"HKLM\SYSTEM\CurrentControlSet\Control\Power\User\PowerSchemes", "ActivePowerScheme") as string ?? "").ToLowerInvariant();

        public static bool IsHighPerformance()
        {
            var a = ActiveScheme();
            if (a == HighPerf || a == Ultimate) return true;
            var misc = UndoStore.Current.Misc;
            return misc.TryGetValue("power_applied", out var g) && g == a && !string.IsNullOrEmpty(a);
        }

        public static void ApplyHighPerformance()
        {
            var current = ActiveScheme();
            var misc = UndoStore.Current.Misc;
            if (!misc.ContainsKey("power_prev")) misc["power_prev"] = current;

            if (ProcessRunner.CmdAsync("powercfg /setactive " + HighPerf).Result.Ok && ActiveScheme() == HighPerf)
            {
                misc["power_applied"] = HighPerf;
                return;
            }
            // Gói Hiệu suất cao bị ẩn (máy Modern Standby) – tạo bản sao rồi kích hoạt.
            var dup = ProcessRunner.CmdAsync("powercfg -duplicatescheme " + HighPerf).Result;
            var m = Regex.Match(dup.Output ?? "", @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
            if (dup.Ok && m.Success)
            {
                var g = m.Value.ToLowerInvariant();
                ProcessRunner.CmdAsync("powercfg /setactive " + g).Wait();
                if (ActiveScheme() == g) { misc["power_applied"] = g; return; }
            }
            // Cuối cùng: dùng chế độ "Hiệu năng tốt nhất" của thanh trượt nguồn.
            ProcessRunner.CmdAsync("powercfg /overlaysetactive " + BestPerfOverlay).Wait();
            misc["power_applied"] = current;
            misc["power_overlay"] = "1";
        }

        public static void Restore()
        {
            var misc = UndoStore.Current.Misc;
            if (misc.ContainsKey("power_overlay"))
            {
                ProcessRunner.CmdAsync("powercfg /overlaysetactive 00000000-0000-0000-0000-000000000000").Wait();
                misc.Remove("power_overlay");
            }
            var prev = misc.TryGetValue("power_prev", out var p) && !string.IsNullOrEmpty(p) && p != HighPerf && p != Ultimate ? p : Balanced;
            ProcessRunner.CmdAsync("powercfg /setactive " + prev).Wait();
            misc.Remove("power_prev");
            misc.Remove("power_applied");
            UndoStore.SaveCurrent();
        }

        public static bool OnBattery()
        {
            return Native.GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;
        }

        public static bool HasBattery()
        {
            return Native.GetSystemPowerStatus(out var s) && s.BatteryFlag != 128 && s.BatteryFlag != 255;
        }
    }
}
