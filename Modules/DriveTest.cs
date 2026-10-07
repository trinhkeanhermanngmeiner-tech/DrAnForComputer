using System.Diagnostics;
using System.Runtime.InteropServices;
using BacSiMayTinh.Core;
using Microsoft.Win32.SafeHandles;

namespace BacSiMayTinh.Modules
{
    public class DriveTestProgress
    {
        public string Phase;
        public long Done;
        public long Total;
        public double WriteMBps;
        public double ReadMBps;
        public long GoodBytes;
        public long BadBytes;
    }

    public class DriveTestResult
    {
        public bool Cancelled;
        public double WriteMBps;
        public double ReadMBps;
        public long Tested;
        public long GoodBytes;
        public long BadBytes;
        public long FirstErrorAt = -1;
        public string Error;
    }

    /// <summary>
    /// Đo tốc độ đọc/ghi và kiểm tra dung lượng thật (phát hiện USB/thẻ nhớ "giả dung lượng").
    /// Ghi/đọc trực tiếp không qua bộ nhớ đệm của Windows để kết quả phản ánh đúng ổ đĩa.
    /// Chỉ ghi vào dung lượng trống, tệp thử được xóa khi kết thúc – không đụng tới dữ liệu có sẵn.
    /// </summary>
    public static class DriveTest
    {
        private const int Block = 4 * 1024 * 1024;       // 4 MB mỗi lần đọc/ghi
        private const long FileSize = 512L * 1024 * 1024; // mỗi tệp thử 512 MB (an toàn với FAT32)

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(SafeFileHandle h, IntPtr buf, int len, out int written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(SafeFileHandle h, IntPtr buf, int len, out int read, IntPtr overlapped);

        private const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        private const uint CREATE_ALWAYS = 2, OPEN_EXISTING = 3;
        private const uint NO_BUFFERING = 0x20000000, WRITE_THROUGH = 0x80000000, SEQUENTIAL = 0x08000000;

        public static string TestDir(string root) => Path.Combine(root, "DrAn_KiemTraO");

        /// <summary>Sinh dữ liệu duy nhất cho từng vị trí – nếu ổ "giả" ghi đè vòng lại, dữ liệu sẽ sai khi đọc lại.</summary>
        private static void Fill(long[] buf, long globalBlockIndex)
        {
            ulong seed = (ulong)globalBlockIndex * 0x9E3779B97F4A7C15UL + 0xD1B54A32D192ED03UL;
            for (int i = 0; i < buf.Length; i++)
            {
                seed ^= seed << 13; seed ^= seed >> 7; seed ^= seed << 17;
                buf[i] = (long)(seed + (ulong)i);
            }
        }

        public static Task<DriveTestResult> RunAsync(string root, long bytes, DriveTestProgress p, CancellationToken ct) => Task.Run(() =>
        {
            var res = new DriveTestResult();
            var dir = TestDir(root);
            IntPtr raw = IntPtr.Zero;
            var files = new List<string>();
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                bytes = bytes / Block * Block;
                if (bytes < Block) throw new IOException(L.T("Ổ đĩa không đủ dung lượng trống để kiểm tra."));
                p.Total = bytes;

                raw = Marshal.AllocHGlobal(Block + 4096);
                var buf = new IntPtr((raw.ToInt64() + 4095) / 4096 * 4096);
                var data = new long[Block / 8];
                var check = new long[Block / 8];

                // ===== Ghi =====
                p.Phase = "write";
                var sw = Stopwatch.StartNew();
                long written = 0, block = 0;
                int fileNo = 0;
                while (written < bytes && !ct.IsCancellationRequested)
                {
                    var path = Path.Combine(dir, $"test_{fileNo++:D4}.bin");
                    files.Add(path);
                    using (var h = CreateFile(path, GENERIC_WRITE, 0, IntPtr.Zero, CREATE_ALWAYS, NO_BUFFERING | WRITE_THROUGH | SEQUENTIAL, IntPtr.Zero))
                    {
                        if (h.IsInvalid) throw new IOException(L.T("Không tạo được tệp thử trên ổ đĩa."));
                        long inFile = 0;
                        while (inFile < FileSize && written < bytes && !ct.IsCancellationRequested)
                        {
                            Fill(data, block++);
                            Marshal.Copy(data, 0, buf, data.Length);
                            if (!WriteFile(h, buf, Block, out int w, IntPtr.Zero) || w != Block)
                            {
                                res.FirstErrorAt = written;
                                res.Error = L.F("Lỗi ghi tại vị trí {0}.", FileUtil.FormatSize(written));
                                goto WriteDone;
                            }
                            inFile += Block; written += Block;
                            p.Done = written;
                            p.WriteMBps = written / 1048576.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                        }
                    }
                }
            WriteDone:
                res.WriteMBps = p.WriteMBps;
                if (ct.IsCancellationRequested) { res.Cancelled = true; return res; }

                // ===== Đọc & kiểm tra =====
                p.Phase = "read";
                p.Done = 0;
                p.Total = written;
                sw.Restart();
                long read = 0;
                block = 0;
                foreach (var path in files)
                {
                    if (ct.IsCancellationRequested) break;
                    using (var h = CreateFile(path, GENERIC_READ, 0, IntPtr.Zero, OPEN_EXISTING, NO_BUFFERING | SEQUENTIAL, IntPtr.Zero))
                    {
                        if (h.IsInvalid) { res.BadBytes += Math.Min(FileSize, written - read); read += FileSize; continue; }
                        long inFile = 0;
                        while (inFile < FileSize && read < written && !ct.IsCancellationRequested)
                        {
                            Fill(data, block++);
                            bool ok = ReadFile(h, buf, Block, out int r, IntPtr.Zero) && r == Block;
                            if (ok)
                            {
                                Marshal.Copy(buf, check, 0, check.Length);
                                for (int i = 0; i < check.Length; i++) if (check[i] != data[i]) { ok = false; break; }
                            }
                            if (ok) res.GoodBytes += Block;
                            else
                            {
                                res.BadBytes += Block;
                                if (res.FirstErrorAt < 0) res.FirstErrorAt = read;
                            }
                            inFile += Block; read += Block;
                            p.Done = read;
                            p.GoodBytes = res.GoodBytes;
                            p.BadBytes = res.BadBytes;
                            p.ReadMBps = read / 1048576.0 / Math.Max(0.001, sw.Elapsed.TotalSeconds);
                        }
                    }
                }
                res.ReadMBps = p.ReadMBps;
                res.Tested = read;
                res.Cancelled = ct.IsCancellationRequested;
            }
            catch (Exception ex)
            {
                res.Error = ex.Message;
                Logger.Error("Kiểm tra ổ đĩa " + root, ex);
            }
            finally
            {
                if (raw != IntPtr.Zero) Marshal.FreeHGlobal(raw);
                try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch { }
            }
            Logger.Info($"Kiểm tra ổ {root}: ghi {res.WriteMBps:0} MB/s, đọc {res.ReadMBps:0} MB/s, tốt {res.GoodBytes}, lỗi {res.BadBytes}");
            return res;
        });
    }
}
