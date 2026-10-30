using System.Diagnostics;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using BacSiMayTinh.Core;
using Microsoft.Win32;

namespace BacSiMayTinh.Modules
{
    /// <summary>Tự cài đặt vào Program Files (lối tắt Start menu, Desktop, mục gỡ cài đặt) và tự gỡ bỏ.</summary>
    public static class Installer
    {
        private const string UninstallKey = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" + AppInfo.Id;

        public static string InstallDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppInfo.Name);
        public static string InstallExe => Path.Combine(InstallDir, AppInfo.Id + ".exe");
        private static string StartMenuLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), AppInfo.Name + ".lnk");
        private static string DesktopLink => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), AppInfo.Name + ".lnk");

        public static bool IsInstalled => File.Exists(InstallExe) && RegistryHelper.KeyExists(UninstallKey);
        public static bool RunningFromInstall => string.Equals(Path.GetFullPath(AppInfo.ExePath), Path.GetFullPath(InstallExe), StringComparison.OrdinalIgnoreCase);

        public static void Install(bool desktopShortcut)
        {
            Directory.CreateDirectory(InstallDir);
            if (!RunningFromInstall) File.Copy(AppInfo.ExePath, InstallExe, true);

            CreateShortcut(StartMenuLink);
            if (desktopShortcut) CreateShortcut(DesktopLink);

            RegistryHelper.SetValue(UninstallKey, "DisplayName", AppInfo.Name, RegistryValueKind.String);
            RegistryHelper.SetValue(UninstallKey, "DisplayVersion", AppInfo.Version, RegistryValueKind.String);
            RegistryHelper.SetValue(UninstallKey, "Publisher", AppInfo.Author, RegistryValueKind.String);
            RegistryHelper.SetValue(UninstallKey, "DisplayIcon", InstallExe, RegistryValueKind.String);
            RegistryHelper.SetValue(UninstallKey, "InstallLocation", InstallDir, RegistryValueKind.String);
            RegistryHelper.SetValue(UninstallKey, "UninstallString", "\"" + InstallExe + "\" --uninstall", RegistryValueKind.String);
            RegistryHelper.SetValue(UninstallKey, "InstallDate", DateTime.Now.ToString("yyyyMMdd"), RegistryValueKind.String);
            RegistryHelper.SetValue(UninstallKey, "EstimatedSize", (int)Math.Max(1, new FileInfo(InstallExe).Length / 1024), RegistryValueKind.DWord);
            RegistryHelper.SetValue(UninstallKey, "NoModify", 1, RegistryValueKind.DWord);
            RegistryHelper.SetValue(UninstallKey, "NoRepair", 1, RegistryValueKind.DWord);

            // Các tác vụ đã lên lịch phải trỏ tới bản đã cài.
            var s = AppSettings.Current;
            if (s.StartWithWindows) Scheduler.SetStartupAsync(true, InstallExe).Wait();
            if (s.AutoClean != "off") Scheduler.SetAutoCleanAsync(s.AutoClean, InstallExe).Wait();
            if (s.AutoBackup != "off") Scheduler.SetAutoBackupAsync(s.AutoBackup, InstallExe).Wait();
            Logger.Info("Cài đặt vào " + InstallDir);
        }

        private static void CreateShortcut(string lnk)
        {
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                dynamic sc = shell.CreateShortcut(lnk);
                sc.TargetPath = InstallExe;
                sc.WorkingDirectory = InstallDir;
                sc.IconLocation = InstallExe + ",0";
                sc.Description = AppInfo.Name + " – " + AppInfo.Author;
                sc.Save();
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(sc);
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
            catch (Exception ex) { Logger.Error("Tạo lối tắt " + lnk, ex); }
        }

        /// <summary>Mở bản đã cài sau khi ứng dụng hiện tại thoát.</summary>
        public static void LaunchInstalledAfterExit()
        {
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"),
                $"/d /c ping -n 3 127.0.0.1 >nul & start \"\" \"{InstallExe}\"") { CreateNoWindow = true, UseShellExecute = false });
        }

        /// <summary>Gỡ cài đặt hoàn toàn.</summary>
        public static void Uninstall(bool revertChanges, bool deleteData)
        {
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName(AppInfo.Id).Concat(Process.GetProcessesByName(AppInfo.OldId)))
            {
                try { if (p.Id != self) { p.Kill(); p.WaitForExit(3000); } } catch { }
                finally { p.Dispose(); }
            }

            if (revertChanges)
            {
                foreach (var t in TweakCatalog.Build().Where(t => t.AppliedByUs)) { try { t.Revert(); } catch { } }
                foreach (var s in ServiceCatalog.Build().Where(s => UndoStore.Current.Services.ContainsKey(s.Name))) { try { s.Restore(); } catch { } }
                Native.BroadcastSettingChange();
            }

            Scheduler.RemoveAllAsync().Wait();
            foreach (var l in new[] { StartMenuLink, DesktopLink }) try { if (File.Exists(l)) File.Delete(l); } catch { }
            RegistryHelper.DeleteKeyTree(UninstallKey);
            Logger.Info("Gỡ cài đặt " + AppInfo.Name);

            var cmd = new StringBuilder("/d /c ping -n 4 127.0.0.1 >nul");
            if (Directory.Exists(InstallDir)) cmd.Append($" & rd /s /q \"{InstallDir}\"");
            if (deleteData) cmd.Append($" & rd /s /q \"{AppInfo.DataDir}\"");
            Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"), cmd.ToString()) { CreateNoWindow = true, UseShellExecute = false });
        }
    }

}
