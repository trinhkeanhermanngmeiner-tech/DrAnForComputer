namespace BacSiMayTinh.Core
{
    /// <summary>Nhật ký thao tác, ghi ra %LocalAppData%\BacSiMayTinh\NhatKy\yyyy-MM-dd.log</summary>
    public static class Logger
    {
        private static readonly object Gate = new object();

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception ex = null) =>
            Write("LỖI", ex == null ? message : message + " | " + ex.GetType().Name + ": " + ex.Message);

        private static void Write(string level, string message)
        {
            try
            {
                var file = Path.Combine(AppInfo.LogDir, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
                var line = $"{DateTime.Now:HH:mm:ss} [{level}] {message}{Environment.NewLine}";
                lock (Gate) File.AppendAllText(file, line, Encoding.UTF8);
            }
            catch { /* không để lỗi ghi log làm hỏng ứng dụng */ }
        }
    }
}
