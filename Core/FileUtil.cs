using System.Globalization;

namespace BacSiMayTinh.Core
{
    public static class FileUtil
    {

        public static string FormatSize(long bytes)
        {
            if (bytes < 0) bytes = 0;
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return (i == 0 ? v.ToString("0", L.Culture) : v.ToString(v >= 100 ? "0" : "0.#", L.Culture)) + " " + u[i];
        }

        public static string FormatNumber(long n) => n.ToString("N0", L.Culture);

        public static string Expand(string path) => Environment.ExpandEnvironmentVariables(path);

        /// <summary>Liệt kê file an toàn (bỏ qua thư mục không có quyền, không đi theo junction).</summary>
        public static IEnumerable<FileInfo> EnumerateFiles(string dir, string pattern = "*", bool recursive = true)
        {
            var stack = new Stack<DirectoryInfo>();
            DirectoryInfo root;
            try { root = new DirectoryInfo(dir); if (!root.Exists) yield break; }
            catch { yield break; }
            stack.Push(root);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                FileInfo[] files = null;
                try { files = d.GetFiles(pattern); } catch { }
                if (files != null) foreach (var f in files) yield return f;
                if (!recursive) continue;
                DirectoryInfo[] subs = null;
                try { subs = d.GetDirectories(); } catch { }
                if (subs == null) continue;
                foreach (var s in subs)
                {
                    try { if ((s.Attributes & FileAttributes.ReparsePoint) != 0) continue; } catch { continue; }
                    stack.Push(s);
                }
            }
        }

        public static long DirectorySize(string dir)
        {
            long total = 0;
            foreach (var f in EnumerateFiles(dir)) { try { total += f.Length; } catch { } }
            return total;
        }

        public static bool TryDeleteFile(FileInfo f)
        {
            try
            {
                if ((f.Attributes & (FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Hidden)) != 0)
                    f.Attributes = FileAttributes.Normal;
                f.Delete();
                return true;
            }
            catch { return false; }
        }

        /// <summary>Xóa các thư mục con rỗng (không xóa thư mục gốc).</summary>
        public static void DeleteEmptySubdirs(string dir)
        {
            try
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    try
                    {
                        if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0) continue;
                        DeleteEmptySubdirs(sub);
                        if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub, false);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static bool SendToRecycleBin(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                else if (File.Exists(path))
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Error("Xóa vào Thùng rác " + path, ex);
                return false;
            }
        }

        public static string SystemDrive => (Environment.GetEnvironmentVariable("SystemDrive") ?? "C:") + "\\";
    }
}
