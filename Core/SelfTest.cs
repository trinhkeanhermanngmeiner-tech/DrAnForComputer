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
