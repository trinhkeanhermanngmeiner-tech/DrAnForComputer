using System.Management;

namespace BacSiMayTinh.Core
{
    public static class Wmi
    {
        public static List<ManagementObject> Query(string query, string scope = @"root\cimv2")
        {
            var list = new List<ManagementObject>();
            try
            {
                using (var s = new ManagementObjectSearcher(scope, query))
                {
                    s.Options.Timeout = TimeSpan.FromSeconds(20);
                    foreach (ManagementObject o in s.Get()) list.Add(o);
                }
            }
            catch (Exception ex) { Logger.Warn("WMI '" + query + "': " + ex.Message); }
            return list;
        }

        public static ManagementObject First(string query, string scope = @"root\cimv2") => Query(query, scope).FirstOrDefault();

        public static string Str(ManagementBaseObject o, string prop)
        {
            try { return o?[prop]?.ToString()?.Trim() ?? ""; } catch { return ""; }
        }

        public static long Long(ManagementBaseObject o, string prop)
        {
            try
            {
                var v = o?[prop];
                return v == null ? 0 : Convert.ToInt64(v);
            }
            catch { return 0; }
        }

        public static DateTime? Date(ManagementBaseObject o, string prop)
        {
            try
            {
                var s = Str(o, prop);
                if (string.IsNullOrEmpty(s)) return null;
                return ManagementDateTimeConverter.ToDateTime(s);
            }
            catch { return null; }
        }
    }
}
