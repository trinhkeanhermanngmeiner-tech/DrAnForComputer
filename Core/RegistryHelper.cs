using Microsoft.Win32;

namespace BacSiMayTinh.Core
{
    /// <summary>Đọc/ghi registry bằng đường dẫn dạng "HKCU\Software\...".</summary>
    public static class RegistryHelper
    {
        public static RegistryKey OpenBase(string fullPath, out string subKey)
        {
            int i = fullPath.IndexOf('\\');
            string hive = i < 0 ? fullPath : fullPath.Substring(0, i);
            subKey = i < 0 ? "" : fullPath.Substring(i + 1);
            RegistryHive h;
            switch (hive.ToUpperInvariant())
            {
                case "HKCU": case "HKEY_CURRENT_USER": h = RegistryHive.CurrentUser; break;
                case "HKLM": case "HKEY_LOCAL_MACHINE": h = RegistryHive.LocalMachine; break;
                case "HKCR": case "HKEY_CLASSES_ROOT": h = RegistryHive.ClassesRoot; break;
                case "HKU": case "HKEY_USERS": h = RegistryHive.Users; break;
                default: throw new ArgumentException("Hive không hợp lệ: " + hive);
            }
            return RegistryKey.OpenBaseKey(h, Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
        }

        public static object GetValue(string path, string name)
        {
            try
            {
                using (var b = OpenBase(path, out var sub))
                using (var k = b.OpenSubKey(sub, false))
                    return k?.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            }
            catch { return null; }
        }

        public static RegistryValueKind? GetKind(string path, string name)
        {
            try
            {
                using (var b = OpenBase(path, out var sub))
                using (var k = b.OpenSubKey(sub, false))
                {
                    if (k == null || !k.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase)) return null;
                    return k.GetValueKind(name);
                }
            }
            catch { return null; }
        }

        public static bool ValueExists(string path, string name) => GetKind(path, name) != null;

        public static bool KeyExists(string path)
        {
            try
            {
                using (var b = OpenBase(path, out var sub))
                using (var k = b.OpenSubKey(sub, false))
                    return k != null;
            }
            catch { return false; }
        }

        public static void SetValue(string path, string name, object value, RegistryValueKind kind)
        {
            using (var b = OpenBase(path, out var sub))
            using (var k = b.CreateSubKey(sub, true))
            {
                if (k == null) throw new InvalidOperationException("Không mở được khóa " + path);
                if (kind == RegistryValueKind.DWord && value is long l) value = unchecked((int)l);
                if (kind == RegistryValueKind.DWord && value is uint u) value = unchecked((int)u);
                k.SetValue(name, value, kind);
            }
        }

        public static void DeleteValue(string path, string name)
        {
            try
            {
                using (var b = OpenBase(path, out var sub))
                using (var k = b.OpenSubKey(sub, true))
                    k?.DeleteValue(name, false);
            }
            catch (Exception ex) { Logger.Error("Xóa giá trị registry " + path + "\\" + name, ex); }
        }

        public static void DeleteKeyTree(string path)
        {
            try
            {
                using (var b = OpenBase(path, out var sub))
                    b.DeleteSubKeyTree(sub, false);
            }
            catch (Exception ex) { Logger.Error("Xóa khóa registry " + path, ex); }
        }

        public static bool ValueEquals(object current, object expected)
        {
            if (current == null || expected == null) return current == null && expected == null;
            if (current is int ci)
            {
                if (expected is int ei) return ci == ei;
                if (expected is long el) return ci == unchecked((int)el);
                if (expected is uint eu) return ci == unchecked((int)eu);
            }
            if (current is long cl && expected is long el2) return cl == el2;
            if (current is byte[] cb && expected is byte[] eb) return cb.SequenceEqual(eb);
            if (current is string[] cs && expected is string[] es) return cs.SequenceEqual(es);
            return string.Equals(Convert.ToString(current), Convert.ToString(expected), StringComparison.OrdinalIgnoreCase);
        }

        // ---- Mã hóa giá trị để lưu bản sao lưu ----
        public static string Encode(object value, RegistryValueKind kind)
        {
            if (value == null) return null;
            switch (kind)
            {
                case RegistryValueKind.Binary: return BitConverter.ToString((byte[])value);
                case RegistryValueKind.MultiString: return string.Join("\u0001", (string[])value);
                default: return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        public static object Decode(string data, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: return unchecked((int)long.Parse(data));
                case RegistryValueKind.QWord: return long.Parse(data);
                case RegistryValueKind.Binary:
                    return string.IsNullOrEmpty(data) ? new byte[0] : data.Split('-').Select(x => Convert.ToByte(x, 16)).ToArray();
                case RegistryValueKind.MultiString: return data.Split('\u0001');
                default: return data;
            }
        }
    }
}
