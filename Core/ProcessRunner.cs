using System.Diagnostics;

namespace BacSiMayTinh.Core
{
    public class RunResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public bool Ok => ExitCode == 0;
    }

    /// <summary>Chạy lệnh nền (ẩn cửa sổ), đọc kết quả theo từng dòng.</summary>
    public static class ProcessRunner
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static Encoding _oem;
        private static Encoding Oem
        {
            get
            {
                if (_oem == null)
                {
                    try { _oem = Encoding.GetEncoding((int)Native.GetOEMCP()); }
                    catch { _oem = Encoding.Default; }
                }
                return _oem;
            }
        }

        /// <param name="onLine">(dòng, laDongTienTrinh) – dòng tiến trình kết thúc bằng \r, nên ghi đè dòng trước.</param>
        /// <param name="encoding">null = tự nhận dạng từng dòng (UTF-8, nếu không hợp lệ thì dùng bảng mã OEM của Windows).</param>
        public static Task<RunResult> RunAsync(string file, string args, Action<string, bool> onLine = null,
            Encoding encoding = null, CancellationToken ct = default, string workDir = null)
        {
            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo(file, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    WorkingDirectory = workDir ?? Environment.SystemDirectory,
                };
                var all = new StringBuilder();
                try
                {
                    using (var p = Process.Start(psi))
                    {
                        using (ct.Register(() => { try { if (!p.HasExited) p.Kill(); } catch { } }))
                        {
                            var errTask = Task.Run(() => Pump(p.StandardError.BaseStream, encoding, all, onLine));
                            Pump(p.StandardOutput.BaseStream, encoding, all, onLine);
                            errTask.Wait();
                            p.WaitForExit();
                            return new RunResult { ExitCode = p.ExitCode, Output = all.ToString() };
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("Chạy " + file + " " + args, ex);
                    onLine?.Invoke(L.T("Lỗi: ") + ex.Message, false);
                    return new RunResult { ExitCode = -1, Output = ex.Message };
                }
            });
        }

        private static void Pump(Stream s, Encoding enc, StringBuilder all, Action<string, bool> onLine)
        {
            // UTF-16 (vd. sfc.exe): đọc theo ký tự.
            if (enc is UnicodeEncoding)
            {
                using (var r = new StreamReader(s, enc))
                {
                    var sb = new StringBuilder();
                    bool cr = false;
                    int c;
                    while ((c = r.Read()) != -1)
                    {
                        if (cr) { cr = false; if (c == '\n') { Emit(sb.ToString(), all, onLine, false); sb.Clear(); continue; } Emit(sb.ToString(), all, onLine, true); sb.Clear(); }
                        if (c == '\r') { cr = true; continue; }
                        if (c == '\n') { Emit(sb.ToString(), all, onLine, false); sb.Clear(); continue; }
                        if (c != 0) sb.Append((char)c);
                    }
                    Emit(sb.ToString(), all, onLine, cr);
                }
                return;
            }

            // Byte: tách dòng rồi giải mã từng dòng.
            var bs = new BufferedStream(s);
            var buf = new List<byte>(256);
            bool pendingCr = false;
            int b;
            void Flush(bool progress)
            {
                var bytes = buf.ToArray();
                buf.Clear();
                Emit(Decode(bytes, enc), all, onLine, progress);
            }
            while ((b = bs.ReadByte()) != -1)
            {
                if (pendingCr)
                {
                    pendingCr = false;
                    if (b == '\n') { Flush(false); continue; }
                    Flush(true);
                }
                if (b == '\r') { pendingCr = true; continue; }
                if (b == '\n') { Flush(false); continue; }
                if (b != 0) buf.Add((byte)b);
            }
            Flush(pendingCr);
        }

        private static string Decode(byte[] bytes, Encoding enc)
        {
            if (bytes.Length == 0) return "";
            if (enc != null) return enc.GetString(bytes);
            try { return StrictUtf8.GetString(bytes); }
            catch { return Oem.GetString(bytes); }
        }

        private static void Emit(string line, StringBuilder all, Action<string, bool> onLine, bool progress)
        {
            if (line.Trim().Length == 0) return;
            lock (all) { if (!progress) all.AppendLine(line); }
            onLine?.Invoke(line, progress);
        }

        /// <summary>Chạy một lệnh cmd (bảng mã UTF-8, tự nhận dạng nếu chương trình xuất theo bảng mã OEM).</summary>
        public static Task<RunResult> CmdAsync(string command, Action<string, bool> onLine = null, CancellationToken ct = default)
            => RunAsync(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /c chcp 65001 >nul & " + command, onLine, null, ct);

        /// <summary>Chạy lệnh PowerShell (dùng -EncodedCommand để tránh lỗi dấu nháy).</summary>
        public static Task<RunResult> PowerShellAsync(string script, Action<string, bool> onLine = null, CancellationToken ct = default)
        {
            var full = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $ProgressPreference='SilentlyContinue'; $ErrorActionPreference='Continue';\n" + script;
            var b64 = Convert.ToBase64String(Encoding.Unicode.GetBytes(full));
            var ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            return RunAsync(ps, "-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand " + b64, onLine, Encoding.UTF8, ct);
        }

        /// <summary>Mở file/URL/thư mục bằng chương trình mặc định.</summary>
        public static void Open(string target, string args = null)
        {
            try
            {
                var psi = new ProcessStartInfo(target) { UseShellExecute = true };
                if (args != null) psi.Arguments = args;
                Process.Start(psi);
            }
            catch (Exception ex) { Logger.Error("Mở " + target, ex); }
        }

        public static void ShowInExplorer(string path)
        {
            try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
        }
    }
}
