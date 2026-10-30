using BacSiMayTinh.Modules;
using Microsoft.Win32;

namespace BacSiMayTinh.Core
{
    /// <summary>
    /// Bộ tự kiểm tra (chạy: BacSiMayTinh.exe --selftest &lt;tệp kết quả&gt;).
    /// Chỉ dùng khóa registry HKCU\Software\BacSiMayTinh_SelfTest và thư mục tạm riêng – không đụng tới cài đặt thật.
    /// </summary>
    public static class SelfTest
    {
        private const string Key = @"HKCU\Software\DrAnForComputer_SelfTest";

        public static int Run(string outFile)
        {
            var log = new StringBuilder();
            int fail = 0;
            void Check(string name, bool ok, string detail = "")
            {
                log.AppendLine((ok ? "[ĐẠT] " : "[LỖI] ") + name + (detail.Length > 0 ? " – " + detail : ""));
                if (!ok) fail++;
            }

            // Không để bản sao lưu thật bị ảnh hưởng: dùng id riêng và xóa sau khi kiểm tra.
            try
            {
                RegistryHelper.DeleteKeyTree(Key);
                RegistryHelper.SetValue(Key, "Dw", 5, RegistryValueKind.DWord);
                RegistryHelper.SetValue(Key, "Str", "goc", RegistryValueKind.String);
                RegistryHelper.SetValue(Key, "Bin", new byte[] { 1, 2, 3 }, RegistryValueKind.Binary);

                var t = new Tweak { Id = "selftest_tweak", Name = "Kiểm tra" };
                t.Sets.Add(new RegSet(Key, "Dw", 0));
                t.Sets.Add(new RegSet(Key, "Str", "moi", RegistryValueKind.String));
                t.Sets.Add(new RegSet(Key, "Bin", new byte[] { 9, 9 }, RegistryValueKind.Binary));
                t.Sets.Add(new RegSet(Key, "Moi", 1)); // giá trị chưa tồn tại
                Check("Tinh chỉnh chưa áp dụng ban đầu", !t.Check());
                t.Apply();
                Check("Áp dụng tinh chỉnh", t.Check());
                Check("Có bản sao lưu", UndoStore.Current.Tweaks.ContainsKey(t.Id) && t.AppliedByUs);
                t.Revert();
                Check("Hoàn tác DWORD", RegistryHelper.ValueEquals(RegistryHelper.GetValue(Key, "Dw"), 5));
                Check("Hoàn tác chuỗi", (RegistryHelper.GetValue(Key, "Str") as string) == "goc");
                Check("Hoàn tác nhị phân", (RegistryHelper.GetValue(Key, "Bin") as byte[])?.SequenceEqual(new byte[] { 1, 2, 3 }) == true);
                Check("Xóa giá trị vốn không tồn tại", !RegistryHelper.ValueExists(Key, "Moi"));
                Check("Xóa cờ đã áp dụng", !t.AppliedByUs);

                // Áp dụng khi giá trị đã sẵn bằng giá trị đích → hoàn tác phải đưa về mặc định.
                var t2 = new Tweak { Id = "selftest_tweak2", Name = "Kiểm tra 2" };
                t2.Sets.Add(new RegSet(Key, "Same", 0, def: 1));
                RegistryHelper.SetValue(Key, "Same", 0, RegistryValueKind.DWord);
                t2.Apply();
                t2.Revert();
                Check("Hoàn tác về giá trị mặc định khi bản gốc đã giống", RegistryHelper.ValueEquals(RegistryHelper.GetValue(Key, "Same"), 1));

                var t3 = new Tweak { Id = "selftest_tweak3", Name = "Kiểm tra 3", RevertDeleteKey = Key + @"\Sub" };
                t3.Sets.Add(new RegSet(Key + @"\Sub\Inproc", "", "", RegistryValueKind.String));
                t3.Apply();
                Check("Tạo khóa với giá trị mặc định rỗng", t3.Check());
                t3.Revert();
                Check("Hoàn tác xóa cả khóa", !RegistryHelper.KeyExists(Key + @"\Sub"));
            }
            catch (Exception ex) { Check("Registry", false, ex.ToString()); }
            finally
            {
                RegistryHelper.DeleteKeyTree(Key);
                foreach (var id in new[] { "selftest_tweak", "selftest_tweak2", "selftest_tweak3" })
                {
                    UndoStore.Current.Tweaks.Remove(id);
                    UndoStore.Current.Misc.Remove("applied:" + id);
                }
                UndoStore.SaveCurrent();
            }

            // JSON lưu/đọc
            try
            {
                var tmp = Path.Combine(Path.GetTempPath(), "bsmt_selftest.json");
                var u = new UndoStore();
                u.Tweaks["a"] = new List<RegBackup> { new RegBackup { Path = "HKCU\\X", Name = "Tên có dấu", Existed = true, Kind = "DWord", Data = "-1" } };
                u.Services["SysMain"] = 2;
                JsonFile.Save(tmp, u);
                var r = JsonFile.Load<UndoStore>(tmp);
                Check("Lưu/đọc JSON", r.Tweaks["a"][0].Name == "Tên có dấu" && r.Services["SysMain"] == 2 && r.Misc != null);
                var s = JsonFile.Load<AppSettings>(Path.Combine(Path.GetTempPath(), "khong_ton_tai.json"));
                Check("Cài đặt mặc định", s.AutoRestorePoint && s.AutoFreeRamThreshold == 85 && s.Theme == "dark");
                File.Delete(tmp);
            }
            catch (Exception ex) { Check("JSON", false, ex.Message); }

            // Dọn rác theo tuổi tệp
            try
            {
                var dir = Path.Combine(Path.GetTempPath(), "bsmt_selftest_dir");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(Path.Combine(dir, "thu_muc_con"));
                File.WriteAllBytes(Path.Combine(dir, "moi.tmp"), new byte[1000]);
                var old1 = Path.Combine(dir, "cu.tmp");
                var old2 = Path.Combine(dir, "thu_muc_con", "cu2.tmp");
                File.WriteAllBytes(old1, new byte[2000]);
                File.WriteAllBytes(old2, new byte[3000]);
                File.SetLastWriteTime(old1, DateTime.Now.AddDays(-3));
                File.SetLastWriteTime(old2, DateTime.Now.AddDays(-3));
                File.SetAttributes(old2, FileAttributes.ReadOnly);
                var item = new CleanItem { Id = "t", Name = "t" };
                item.Paths.Add(new PathSpec(dir, olderThanDays: 1));
                CleanerEngine.Scan(item);
                Check("Quét chỉ tính tệp cũ", item.Size == 5000 && item.FileCount == 2, item.Size + " byte / " + item.FileCount + " tệp");
                long freed = CleanerEngine.Clean(item);
                Check("Dọn đúng tệp cũ (kể cả tệp chỉ đọc)", freed == 5000 && File.Exists(Path.Combine(dir, "moi.tmp")) && !File.Exists(old1) && !File.Exists(old2), "giải phóng " + freed);
                Check("Xóa thư mục con rỗng", !Directory.Exists(Path.Combine(dir, "thu_muc_con")));
                Directory.Delete(dir, true);
            }
            catch (Exception ex) { Check("Dọn rác", false, ex.ToString()); }

            // Phân tích dòng lệnh
            Check("Tách exe có ngoặc kép", IconHelper.ExeFromCommand("\"C:\\Program Files\\A B\\x.exe\" --min") == "C:\\Program Files\\A B\\x.exe");
            Check("Tách exe không ngoặc kép", IconHelper.ExeFromCommand("C:\\Apps\\run.exe -silent") == "C:\\Apps\\run.exe");
            var m = UninstallerService.ParseCommand("MsiExec.exe /I{12345678-1234-1234-1234-123456789012}");
            Check("MSI /I → /X", m.file.EndsWith("msiexec.exe", StringComparison.OrdinalIgnoreCase) && m.args == "/X{12345678-1234-1234-1234-123456789012}", m.args);
            var q = UninstallerService.ParseCommand("\"C:\\Program Files\\Zalo\\Uninstall Zalo.exe\" /currentuser");
            Check("Lệnh gỡ có khoảng trắng", q.file == "C:\\Program Files\\Zalo\\Uninstall Zalo.exe" && q.args == "/currentuser");

            // Định dạng
            Check("Định dạng dung lượng", FileUtil.FormatSize(1536) == "1,5 KB" && FileUtil.FormatSize(5L * 1024 * 1024 * 1024) == "5 GB", FileUtil.FormatSize(1536));

            // Chạy lệnh với tiếng Việt
            try
            {
                // where.exe là chương trình ngoài, in ra đường dẫn có dấu tiếng Việt của chính ứng dụng.
                var exe = AppInfo.ExePath;
                var c = ProcessRunner.CmdAsync($"where \"{Path.GetDirectoryName(exe)}:{Path.GetFileName(exe)}\"").Result;
                Check("CMD xuất đường dẫn tiếng Việt", c.Output.IndexOf(exe, StringComparison.OrdinalIgnoreCase) >= 0, c.Output.Trim());
                var p = ProcessRunner.PowerShellAsync("Write-Output 'Xin chào Việt Nam'").Result;
                Check("PowerShell xuất tiếng Việt", p.Output.Contains("Xin chào Việt Nam"), p.Output.Trim());
            }
            catch (Exception ex) { Check("Chạy lệnh", false, ex.Message); }

            // Đọc thông tin chỉ-đọc
            try
            {
                Check("Đọc danh sách khởi động", StartupManager.GetItems() != null);
                Check("Đọc danh sách tinh chỉnh", TweakCatalog.Build().Count >= 30, TweakCatalog.Build().Count + " mục");
                Check("Đọc dịch vụ", ServiceCatalog.Build().Count > 5);
                Check("Đọc phần mềm đã cài", UninstallerService.List().Count > 0);
                Check("Loại ổ hệ thống", SystemQuick.SystemDiskType() != null, SystemQuick.SystemDiskType());
                var wifi = Task.Run(() => WifiPasswords.GetAsync()).Result;
                Check("Đọc hồ sơ Wi-Fi", wifi != null, wifi.Count + " mạng");
            }
            catch (Exception ex) { Check("Đọc hệ thống", false, ex.Message); }

            // Đọc bảng kết quả winget
            try
            {
                var sample = "Name                              Id                         Version   Available Source\n" +
                             "-------------------------------------------------------------------------------------\n" +
                             "Microsoft Visual C++ v14 Redis…   Microsoft.VCRedist.2015+.x64 14.50.1 14.51.2  winget\n" +
                             "Foxit PDF Reader                  Foxit.FoxitReader.Inno     11.2.2    2025.2.0  winget\n" +
                             "2 upgrades available.\n";
                var rows = Winget.ParseTable(sample);
                Check("Đọc bảng winget", rows.Count >= 2 && rows[1][1] == "Foxit.FoxitReader.Inno" && rows[1][3] == "2025.2.0",
                    string.Join(" | ", rows.Select(r => string.Join(",", r))));
            }
            catch (Exception ex) { Check("Đọc bảng winget", false, ex.Message); }

            // Đo tốc độ & kiểm tra dữ liệu thật (64 MB, ghi/đọc không qua bộ đệm rồi tự xóa)
            try
            {
                var root = Path.GetPathRoot(Path.GetTempPath());
                var prog = new DriveTestProgress();
                var r = DriveTest.RunAsync(root, 64L * 1024 * 1024, prog, CancellationToken.None).Result;
                Check("Kiểm tra ổ đĩa ghi/đọc 64 MB không lỗi", r.Error == null && r.BadBytes == 0 && r.GoodBytes == 64L * 1024 * 1024,
                    $"ghi {r.WriteMBps:0} MB/s, đọc {r.ReadMBps:0} MB/s, lỗi: {r.Error}");
                Check("Dọn tệp thử của kiểm tra ổ đĩa", !Directory.Exists(DriveTest.TestDir(root)));
            }
            catch (Exception ex) { Check("Kiểm tra ổ đĩa", false, ex.Message); }

            // Cập nhật từ GitHub: đọc bản mới nhất, tải exe về thư mục tạm, kiểm tra rồi xóa (không cài)
            try
            {
                var info = Task.Run(() => Updater.LatestAsync()).Result;
                Check("Đọc bản phát hành mới nhất trên GitHub", info != null && !string.IsNullOrEmpty(info.ExeUrl),
                    info == null ? "null" : $"v{info.Version}, exe {info.ExeSize} byte, mới hơn bản đang chạy: {Updater.IsNewer(info)}");
                if (info?.ExeUrl != null)
                {
                    var file = Task.Run(() => Updater.DownloadAsync(info)).Result;
                    Check("Tải và kiểm tra tệp cập nhật", File.Exists(file) && new FileInfo(file).Length == info.ExeSize, file);
                    File.Delete(file);
                }
                Check("So sánh phiên bản", Updater.IsNewer(new UpdateInfo { Version = "99.0.0" }) && !Updater.IsNewer(new UpdateInfo { Version = "1.0.0" }));
            }
            catch (Exception ex) { Check("Cập nhật từ GitHub", false, ex.GetBaseException().Message); }

            // ===== v1.2: Sao lưu & phục hồi cấu hình =====
            var bkRoot = Path.Combine(Path.GetTempPath(), "dran_selftest_backup");
            const string BkKey = @"HKCU\Software\DrAnForComputer_SelfTest_Backup";
            var oldBefore = AppSettings.Current.AutoBackupBeforeChange;
            var oldKeep = AppSettings.Current.AutoBackupKeep;
            try
            {
                if (Directory.Exists(bkRoot)) Directory.Delete(bkRoot, true);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var set = ConfigBackup.Create(new BackupOptions { Root = bkRoot, Kind = "manual", WifiPasswords = false,
                    Components = new HashSet<string>(ConfigBackup.QuickComponents) });
                Check("Tạo bản sao lưu cấu hình", File.Exists(Path.Combine(set.Folder, ConfigBackup.ManifestName)) && set.Manifest.Registry.Count >= 40,
                    $"{set.Manifest.Registry.Count} giá trị registry, {set.Manifest.ServiceValues.Count} dịch vụ, {set.Manifest.Startup.Count} mục khởi động, " +
                    $"{set.Manifest.ContextMenu.Count} menu, {set.Manifest.Dns.Count} card mạng, nguồn {set.Manifest.PowerScheme}, {sw.ElapsedMilliseconds} ms");
                var loaded = ConfigBackup.Load(set.Folder);
                Check("Đọc lại bản sao lưu", loaded != null && loaded.Manifest.Components.Count == ConfigBackup.QuickComponents.Length,
                    loaded == null ? "null" : string.Join(",", loaded.Manifest.Components));

                // Phục hồi trên khóa thử: giá trị bị đổi phải trở về, giá trị vốn không có phải bị xóa.
                RegistryHelper.DeleteKeyTree(BkKey);
                RegistryHelper.SetValue(BkKey, "Dw", 7, RegistryValueKind.DWord);
                RegistryHelper.SetValue(BkKey, "Sz", "goc", RegistryValueKind.String);
                var m2 = new BackupManifest { Created = DateTime.Now.ToString("s"), Kind = "manual", Machine = Environment.MachineName };
                m2.Components.Add("tweaks");
                m2.Registry.Add(UndoStore.Capture(BkKey, "Dw"));
                m2.Registry.Add(UndoStore.Capture(BkKey, "Sz"));
                m2.Registry.Add(UndoStore.Capture(BkKey, "Missing"));
                var dir2 = Path.Combine(bkRoot, "thu_phuc_hoi");
                Directory.CreateDirectory(dir2);
                JsonFile.Save(Path.Combine(dir2, ConfigBackup.ManifestName), m2);
                RegistryHelper.SetValue(BkKey, "Dw", 99, RegistryValueKind.DWord);
                RegistryHelper.SetValue(BkKey, "Missing", 1, RegistryValueKind.DWord);
                int undoCount = UndoStore.Current.Tweaks.Count + UndoStore.Current.Misc.Count;
                AppSettings.Current.AutoBackupBeforeChange = false;
                var rep = ConfigBackup.Restore(ConfigBackup.Load(dir2), new HashSet<string> { "tweaks" });
                Check("Phục hồi giá trị registry đã bị đổi", RegistryHelper.ValueEquals(RegistryHelper.GetValue(BkKey, "Dw"), 7) &&
                    (RegistryHelper.GetValue(BkKey, "Sz") as string) == "goc", string.Join(" | ", rep.Lines));
                Check("Phục hồi xóa giá trị vốn không có", !RegistryHelper.ValueExists(BkKey, "Missing"));
                Check("Giữ nguyên dữ liệu hoàn tác khi bản sao lưu không kèm", UndoStore.Current.Tweaks.Count + UndoStore.Current.Misc.Count == undoCount);

                // Giữ lại N bản tự động mới nhất
                for (int i = 0; i < 4; i++)
                {
                    var d = Path.Combine(bkRoot, "auto_" + i);
                    Directory.CreateDirectory(d);
                    JsonFile.Save(Path.Combine(d, ConfigBackup.ManifestName), new BackupManifest { Kind = "auto", Created = DateTime.Now.AddDays(-i).ToString("s") });
                }
                AppSettings.Current.AutoBackupKeep = 2;
                ConfigBackup.ApplyRetention(bkRoot, "auto");
                var autos = Directory.GetDirectories(bkRoot, "auto_*").Select(Path.GetFileName).OrderBy(x => x).ToList();
                Check("Chỉ giữ 2 bản tự động mới nhất, không đụng bản thủ công", autos.SequenceEqual(new[] { "auto_0", "auto_1" }) && Directory.Exists(set.Folder),
                    string.Join(",", autos));
            }
            catch (Exception ex) { Check("Sao lưu & phục hồi cấu hình", false, ex.ToString()); }
            finally
            {
                AppSettings.Current.AutoBackupBeforeChange = oldBefore;
                AppSettings.Current.AutoBackupKeep = oldKeep;
                AppSettings.Current.BackupRoots?.RemoveAll(x => string.Equals(x, bkRoot, StringComparison.OrdinalIgnoreCase));
                AppSettings.SaveCurrent();
                RegistryHelper.DeleteKeyTree(BkKey);
                try { if (Directory.Exists(bkRoot)) Directory.Delete(bkRoot, true); } catch { }
            }

            // ===== v1.2: Sao lưu dữ liệu cá nhân (robocopy, tên tiếng Việt) =====
            var dataSrc = Path.Combine(Path.GetTempPath(), "dran_selftest_data_src");
            var dataDst = Path.Combine(Path.GetTempPath(), "dran_selftest_data_dst");
            var dataBack = Path.Combine(Path.GetTempPath(), "dran_selftest_data_back");
            var oldLast = AppSettings.Current.LastDataBackup;
            try
            {
                foreach (var d in new[] { dataSrc, dataDst, dataBack }) if (Directory.Exists(d)) Directory.Delete(d, true);
                Directory.CreateDirectory(Path.Combine(dataSrc, "Thư mục con"));
                File.WriteAllBytes(Path.Combine(dataSrc, "Bảng lương tháng 10.xlsx"), new byte[3000]);
                File.WriteAllBytes(Path.Combine(dataSrc, "Thư mục con", "ảnh đẹp.jpg"), new byte[5000]);
                var src = new DataSource { Id = "custom:" + dataSrc, Name = "Thử", IsCustom = true };
                src.Parts.Add(new DataPart { Source = dataSrc, SubFolder = "khac_thu" });
                var r1 = DataBackup.RunAsync(new List<DataSource> { src }, dataDst, null, CancellationToken.None).Result;
                var tgt = DataBackup.TargetFolder(dataDst);
                Check("Sao lưu dữ liệu (robocopy) – đếm tệp & dung lượng", r1.Files == 2 && r1.Bytes == 8000 && r1.Errors == 0 &&
                    File.Exists(Path.Combine(tgt, "khac_thu", "Thư mục con", "ảnh đẹp.jpg")), $"{r1.Files} tệp, {r1.Bytes} byte, lỗi {r1.Errors} | {DataBackup.LastRun}");
                var r2 = DataBackup.RunAsync(new List<DataSource> { src }, dataDst, null, CancellationToken.None).Result;
                Check("Sao lưu lần 2 chỉ chép tệp thay đổi", r2.Files == 0, r2.Files + " tệp");
                var man = DataBackup.LoadBackup(tgt);
                man.Entries[0].OriginalPath = dataBack;
                var r3 = DataBackup.RestoreAsync(tgt, man, true, null, CancellationToken.None).Result;
                Check("Phục hồi dữ liệu về thư mục gốc", r3.Files == 2 && File.Exists(Path.Combine(dataBack, "Bảng lương tháng 10.xlsx")), r3.Files + " tệp | " + DataBackup.LastRun);
            }
            catch (Exception ex) { Check("Sao lưu dữ liệu cá nhân", false, ex.GetBaseException().ToString()); }
            finally
            {
                AppSettings.Current.LastDataBackup = oldLast;
                AppSettings.SaveCurrent();
                foreach (var d in new[] { dataSrc, dataDst, dataBack }) try { if (Directory.Exists(d)) Directory.Delete(d, true); } catch { }
            }

            // ===== v1.2: Driver =====
            try
            {
                var drv = DriverManager.List(true);
                Check("Đọc danh sách driver", drv.Count > 5, $"{drv.Count} driver, {drv.Count(d => d.IsThirdParty)} của hãng thứ ba");
                var pr = DriverManager.Problems();
                Check("Đọc thiết bị lỗi", pr != null, pr.Count + " thiết bị: " + string.Join("; ", pr.Take(3).Select(x => x.Name + " " + x.Code)));
                var store = DriverStore.FindOldDuplicates();
                Check("Quét driver cũ trùng lặp", store != null, store.Count + " gói, " + FileUtil.FormatSize(store.Sum(x => x.Size)));
                Check("So sánh phiên bản driver", DriverStore.ParseVersion("31.0.101.4502") > DriverStore.ParseVersion("30.0.101.1191") &&
                    DriverStore.ParseVersion("10.0.19041.1") < DriverStore.ParseVersion("10.0.22621.1"));
                Check("Gợi ý công cụ của hãng", DriverManager.VendorTools() != null);
            }
            catch (Exception ex) { Check("Driver", false, ex.ToString()); }

            // Windows Update (COM): đọc lịch sử, tìm driver mới (không cài)
            try
            {
                var last = Wua.LastSuccessfulInstall();
                Check("Đọc lịch sử Windows Update", true, last?.ToString("dd/MM/yyyy") ?? "không có");
                var t = Task.Run(() => DriverUpdater.Search(CancellationToken.None));
                if (t.Wait(TimeSpan.FromMinutes(6)))
                    Check("Tìm driver qua Windows Update", t.Result != null, t.Result.Count + " bản: " + string.Join("; ", t.Result.Take(3).Select(u => u.Title)));
                else Check("Tìm driver qua Windows Update", true, "quá 6 phút – bỏ qua");
            }
            catch (Exception ex)
            {
                var msg = ex.GetBaseException().Message;
                // Máy chủ CI có thể chặn Windows Update – chỉ lỗi khi không phải lỗi mạng/chính sách.
                Check("Tìm driver qua Windows Update", ex.GetBaseException() is InvalidOperationException, msg);
            }

            // ===== v1.2: Bảo mật & Windows Update =====
            try
            {
                var items = SecurityCheck.RunAsync().Result;
                Check("Kiểm tra bảo mật", items.Count >= 8 && SecurityCheck.Score(items) >= 0,
                    string.Join(" | ", items.Select(i => i.Id + ":" + i.Level)) + " → " + SecurityCheck.Score(items) + " điểm");
            }
            catch (Exception ex) { Check("Kiểm tra bảo mật", false, ex.ToString()); }
            try
            {
                var saved = WuManager.ManagedValues.Select(v => UndoStore.Capture(v.path, v.name)).ToList();
                try
                {
                    WuManager.Pause(14);
                    var until = WuManager.PausedUntil;
                    Check("Tạm dừng Windows Update 2 tuần", until.HasValue && Math.Abs((until.Value - DateTime.Now).TotalDays - 14) < 0.1, until?.ToString("s"));
                    WuManager.Resume();
                    Check("Tiếp tục Windows Update", WuManager.PausedUntil == null);
                    WuManager.SetActiveHours(7, 23);
                    var ah = WuManager.ActiveHours;
                    Check("Đặt giờ hoạt động", ah.start == 7 && ah.end == 23 && ah.manual);
                    bool threw = false;
                    try { WuManager.SetActiveHours(1, 23); } catch (ArgumentException) { threw = true; }
                    Check("Chặn giờ hoạt động quá 18 tiếng", threw);
                }
                finally { foreach (var b in saved) UndoStore.Restore(b); }
            }
            catch (Exception ex) { Check("Windows Update", false, ex.ToString()); }

            // Từ điển tiếng Anh: placeholder khớp
            int badFmt = Strings.En.Count(kv =>
                string.Join(",", System.Text.RegularExpressions.Regex.Matches(kv.Key, @"\{(\d+)").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x)) !=
                string.Join(",", System.Text.RegularExpressions.Regex.Matches(kv.Value, @"\{(\d+)").Cast<System.Text.RegularExpressions.Match>().Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x)));
            Check("Bản dịch tiếng Anh khớp định dạng", badFmt == 0, Strings.En.Count + " mục");

            log.AppendLine(fail == 0 ? "==> TẤT CẢ ĐẠT" : $"==> {fail} LỖI");
            File.WriteAllText(outFile, log.ToString(), Encoding.UTF8);
            return fail;
        }
    }
}
