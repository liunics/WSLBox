using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace WSLBox
{
    [DataContract]
    internal sealed class OpenRequest
    {
        [DataMember(EmitDefaultValue = false)] public string Command;
        [DataMember(EmitDefaultValue = false)] public string Path;
        [DataMember(EmitDefaultValue = false)] public string Application;
        [DataMember(EmitDefaultValue = false)] public bool SetDefault;
        [DataMember(EmitDefaultValue = false)] public bool ShowHidden;
    }
    internal sealed class OpenWorker : IDisposable
    {
        private readonly object gate = new object(), lifetime = new object();
        private readonly string distro, script, mode, linuxUser;
        private Process process;
        private Task<string> errors;
        private volatile bool disposed;
        private long lastActivity;
        private int linuxPid;
        public int BackendPid { get { lock (lifetime) return linuxPid; } }
        public OpenWorker(string distro, string script, string mode = "serve-open", string linuxUser = null)
        {
            this.distro = distro;
            this.script = script;
            this.mode = mode;
            this.linuxUser = linuxUser;
        }
        public void Warmup() { lock (gate) EnsureStarted(); }
        private Reply ReadReply(Process current, Task<string> stderr, int timeout)
        {
            Task<string> read = current.StandardOutput.ReadLineAsync();
            if (!read.Wait(timeout)) { Stop(); throw new IOException(L10n.T("ui.the_linux_request_timed_out_it_may_already_have_been_submitted_c")); }
            string line = read.GetAwaiter().GetResult();
            if (line == null)
            {
                string error = stderr != null && stderr.Status == TaskStatus.RanToCompletion ? stderr.Result : "";
                Stop(); throw new IOException(L10n.T("ui.the_linux_request_channel_exited") + error);
            }
            Reply reply = Json.Read<Reply>(line);
            if (!String.IsNullOrEmpty(reply.Error)) throw new IOException(reply.Error);
            return reply;
        }
        private void EnsureStarted()
        {
            lock (lifetime)
            {
                if (disposed) throw new ObjectDisposedException("OpenWorker");
                if (process != null && !process.HasExited) return;
            }
            Stop();
            string arguments = WslClient.LaunchOptions(distro, linuxUser);
            arguments += "--exec /usr/bin/python3 -u -X utf8 -c " + Paths.Quote(WslClient.Bootstrap) + " " + mode;
            Process started = Process.Start(new ProcessStartInfo("wsl.exe", arguments) {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            });
            Task<string> stderr = started.StandardError.ReadToEndAsync();
            bool rejected;
            lock (lifetime)
            {
                rejected = disposed;
                if (!rejected) { process = started; errors = stderr; }
            }
            if (rejected) { StopProcess(started); throw new ObjectDisposedException("OpenWorker"); }
            try
            {
                WslClient.WriteSource(started.StandardInput.BaseStream, script);
                Reply ready = ReadReply(started, stderr, 20000);
                if (!ready.Ready) throw new IOException(L10n.T("ui.the_linux_request_channel_is_not_ready"));
                lock (lifetime) { if (process == started) linuxPid = ready.Pid; }
            }
            catch { Stop(); throw; }
        }
        public Reply Open(string path, string application = null, bool setDefault = false)
        { return Request("open", path, application, setDefault); }
        public Reply Request(string command, string path, string application = null, bool setDefault = false, bool showHidden = false)
        {
            lock (gate)
            {
                try
                {
                    EnsureStarted();
                    Process current; Task<string> stderr;
                    lock (lifetime)
                    {
                        if (disposed) throw new ObjectDisposedException("OpenWorker");
                        current = process; stderr = errors;
                    }
                    try
                    {
                        byte[] request = Encoding.UTF8.GetBytes(Json.Write(new OpenRequest { Command = command, Path = path, Application = application, SetDefault = setDefault, ShowHidden = showHidden }) + "\n");
                        current.StandardInput.BaseStream.Write(request, 0, request.Length);
                        current.StandardInput.BaseStream.Flush();
                        return ReadReply(current, stderr, (command == "trash" || command == "restore-trash" || command == "purge-trash") ? 120000 : mode == "serve-open" ? 16000 : 20000);
                    }
                    catch
                    {
                        // Never replay a request: lost acknowledgements can also
                        // follow a successful file operation or GUI activation.
                        bool alive = false;
                        try { alive = !current.HasExited; } catch (InvalidOperationException) { }
                        if (!alive) Stop();
                        throw;
                    }
                }
                finally { lastActivity = Stopwatch.GetTimestamp(); }
            }
        }
        public void ReleaseIfIdle(TimeSpan idle)
        {
            if (mode == "serve-open" || !System.Threading.Monitor.TryEnter(gate)) return;
            try
            {
                if (!disposed && lastActivity != 0 && Stopwatch.GetTimestamp() - lastActivity >= idle.TotalSeconds * Stopwatch.Frequency) Stop();
            }
            finally { System.Threading.Monitor.Exit(gate); }
        }
        private void Stop()
        {
            Process stopped;
            lock (lifetime) { stopped = process; process = null; errors = null; linuxPid = 0; }
            if (stopped != null) StopProcess(stopped);
        }
        private static void StopProcess(Process current)
        {
            try
            {
                if (!current.HasExited)
                {
                    current.StandardInput.Close();
                    if (!current.WaitForExit(800)) current.Kill();
                }
            }
            catch (InvalidOperationException) { }
            catch (IOException) { }
            catch (System.ComponentModel.Win32Exception) { }
            finally { current.Dispose(); }
        }
        public void Dispose()
        {
            // Do not wait for gate: a thumbnailer or prewarm may still be
            // running. Closing the private channel interrupts its pending IO.
            lock (lifetime) disposed = true;
            Stop();
        }
    }
}
