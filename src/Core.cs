using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace WSLBox
{
    [DataContract]
    public sealed class Entry
    {
        [DataMember] public string Name;
        [DataMember] public string DisplayName;
        [DataMember] public string Path;
        [DataMember] public bool IsDirectory;
        [DataMember] public bool IsLink;
        [DataMember] public string ContentType;
        [DataMember] public string IconKey;
        [DataMember] public long Size;
        [DataMember] public long Modified;
        [DataMember] public bool CanThumbnail;
    }

    [DataContract]
    public sealed class LinuxIcon
    {
        [DataMember] public string Key;
        [DataMember] public string Png;
    }
    [DataContract]
    public sealed class ThumbnailResult
    {
        [DataMember] public string Path;
        [DataMember] public string IconKey;
    }

    [DataContract]
    public sealed class Reply
    {
        [DataMember] public string Distro;
        [DataMember] public string Home;
        [DataMember] public string Path;
        [DataMember] public string Error;
        [DataMember] public string InputMethodWarning;
        [DataMember] public Entry[] Entries;
        [DataMember] public LinuxIcon[] Icons;
        [DataMember] public ThumbnailResult[] Thumbnails;
        [DataMember] public bool Ok;
        [DataMember] public bool Ready;
        [DataMember] public int Pid;
        [DataMember] public string Application;
        [DataMember] public int Completed;
        [DataMember] public FileFailure[] Failures;
        [DataMember] public LinuxApplication[] Applications;
        [DataMember] public string ContentType;
        [DataMember] public RuntimeCheck[] Checks;
    }

    [DataContract]
    public sealed class RuntimeCheck
    {
        [DataMember] public string Name;
        [DataMember] public bool Ok;
        [DataMember] public string Details;
        [DataMember] public bool Required;
    }

    [DataContract]
    public sealed class Settings
    {
        [DataMember] public string Distro = "";
        [DataMember] public string Folder = "";
        [DataMember] public int X = 100;
        [DataMember] public int Y = 100;
        [DataMember] public int Width = 460;
        [DataMember] public int Height = 620;
        [DataMember(EmitDefaultValue = false)] public SavedWindowBounds Bounds;
        [DataMember] public string Id = Guid.NewGuid().ToString("N");
        [DataMember] public string Title = "";
        [DataMember] public string ViewMode = "icons";
        [DataMember] public string Filter = "all";
        [DataMember] public int IconSize = 42;
        [DataMember] public string SortBy = "name";
        [DataMember] public bool SortDescending;
        [DataMember] public string EdgeLayout = "";
        [DataMember] public bool Collapsed;
        [DataMember] public bool Locked;
        [DataMember] public bool Maximized;
        [DataMember] public string MaximizeReturnEdge = "";
        [DataMember] public bool? SnapToEdges = true;
        [DataMember] public bool? ShowThumbnails = true;
        [DataMember] public double BackgroundOpacity = 0.64;

        public void Normalize()
        {
            if (String.IsNullOrEmpty(Id)) Id = Guid.NewGuid().ToString("N");
            if (Title == null) Title = "";
            if (Distro == null) Distro = "";
            if (Folder == null) Folder = "";
            if (ViewMode != "list") ViewMode = "icons";
            if (!new[] { "all", "folders", "documents", "images", "archives", "other" }.Contains(Filter)) Filter = "all";
            if (!ShowThumbnails.HasValue) ShowThumbnails = true;
            if (IconSize != 24 && IconSize != 42 && IconSize != 64) IconSize = 42;
            if (SortBy != "size" && SortBy != "type" && SortBy != "modified") SortBy = "name";
            if (EdgeLayout != "left" && EdgeLayout != "right") EdgeLayout = "";
            if (MaximizeReturnEdge != "left" && MaximizeReturnEdge != "right") MaximizeReturnEdge = "";
            if (!SnapToEdges.HasValue) SnapToEdges = true;
            if (BackgroundOpacity < 0.15 || BackgroundOpacity > 0.95) BackgroundOpacity = 0.64;
            Width = Math.Max(280, Width); Height = Math.Max(180, Height);
            if (Bounds == null || !Bounds.IsValid)
                Bounds = new SavedWindowBounds { Left = X, Top = Y, Width = Width, Height = Height };
        }

        public static string FilePath
        {
            get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WSLBox", "settings.json"); }
        }
        public static Settings Load()
        {
            try { return Json.Read<Settings>(File.ReadAllText(FilePath, Encoding.UTF8)); }
            catch (FileNotFoundException) { return new Settings(); }
            catch (DirectoryNotFoundException) { return new Settings(); }
        }
        public void Save()
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, Json.Write(this), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
    }

    [DataContract]
    public sealed class WorkspaceLayout
    {
        [DataMember] public int Version = 2;
        [DataMember] public string Language = "auto";
        [DataMember] public bool? DesktopDoubleClick = true;
        [DataMember] public List<Settings> Boxes = new List<Settings>();
        public static string FilePath { get { return Path.Combine(Path.GetDirectoryName(Settings.FilePath), "layout.json"); } }
        public static WorkspaceLayout Load()
        {
            WorkspaceLayout layout;
            if (File.Exists(FilePath)) layout = Json.Read<WorkspaceLayout>(File.ReadAllText(FilePath, Encoding.UTF8));
            else layout = new WorkspaceLayout { Boxes = new List<Settings> { Settings.Load() } };
            if (layout.Boxes == null || layout.Boxes.Count == 0) layout.Boxes = new List<Settings> { new Settings() };
            layout.Language = L10n.Normalize(layout.Language);
            foreach (Settings box in layout.Boxes) box.Normalize();
            return layout;
        }
        public void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, Json.Write(this), new UTF8Encoding(false));
            if (File.Exists(FilePath)) File.Replace(temp, FilePath, null);
            else File.Move(temp, FilePath);
        }
    }

    public sealed class FolderHistory
    {
        private readonly List<string> paths = new List<string>();
        private int index = -1;
        public string BackTarget { get { return index > 0 ? paths[index - 1] : null; } }
        public string ForwardTarget { get { return index + 1 < paths.Count ? paths[index + 1] : null; } }
        public void Reset(string path) { paths.Clear(); index = -1; Record(path); }
        public void Record(string path)
        {
            if (index >= 0 && paths[index] == path) return;
            if (index + 1 < paths.Count) paths.RemoveRange(index + 1, paths.Count - index - 1);
            paths.Add(path); index = paths.Count - 1;
            if (paths.Count > 100) { paths.RemoveAt(0); index--; }
        }
        public void CommitBack() { if (BackTarget != null) index--; }
        public void CommitForward() { if (ForwardTarget != null) index++; }
    }

    public static class Json
    {
        public static T Read<T>(string text)
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
        }
        public static string Write<T>(T value)
        {
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(T)).WriteObject(stream, value);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
    }

    public static class Paths
    {
        // ProcessStartInfo on .NET Framework has no ArgumentList. Quote each
        // argument using Windows argv rules, without cmd.exe or a Linux shell.
        public static string Quote(string value)
        {
            var result = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') result.Append('\\', slashes * 2 + 1);
                else result.Append('\\', slashes);
                slashes = 0;
                result.Append(c);
            }
            result.Append('\\', slashes * 2);
            return result.Append('"').ToString();
        }

        public static string Unc(string distro, string linuxPath)
        {
            if (String.IsNullOrWhiteSpace(distro) || distro.IndexOfAny(new[] { '\\', '/', '\0', '\r', '\n' }) >= 0)
                throw new ArgumentException(L10n.T("ui.invalid_wsl_distribution_name"));
            if (String.IsNullOrEmpty(linuxPath) || linuxPath[0] != '/')
                throw new ArgumentException(L10n.T("ui.enter_an_absolute_linux_path_starting_with"));
            return @"\\wsl.localhost\" + distro + "\\" + linuxPath.TrimStart('/').Replace('/', '\\');
        }

        public static string Canonical(string path)
        {
            string full = System.IO.Path.GetFullPath(path);
            const string oldPrefix = @"\\wsl$\";
            if (full.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                full = @"\\wsl.localhost\" + full.Substring(oldPrefix.Length);
            return full.TrimEnd('\\');
        }

        public static bool IsWsl(string path)
        {
            return path.StartsWith(@"\\wsl.localhost\", StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(@"\\wsl$\", StringComparison.OrdinalIgnoreCase);
        }

        public static bool Same(string a, string b)
        {
            return String.Equals(Canonical(a), Canonical(b), IsWsl(a) || IsWsl(b)
                ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        public static void ValidateTransfer(string[] sources, string destination, bool move)
        {
            string target = Canonical(destination);
            if (sources.Length == 0) throw new ArgumentException(L10n.T("ui.no_files_to_transfer"));
            foreach (string source in sources)
            {
                string src = Canonical(source);
                var comparison = IsWsl(source) || IsWsl(destination) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
                if (Same(src, target) || target.StartsWith(src + "\\", comparison))
                    throw new IOException(L10n.T("ui.cannot_copy_or_move_a_folder_into_itself_or_a_descendant"));
                if (move && Same(System.IO.Path.GetDirectoryName(src) ?? src, target))
                    throw new IOException(L10n.T("ui.these_items_are_already_in_the_destination_folder"));
                if (source.IndexOfAny(new[] { '\0', '*', '?' }) >= 0)
                    throw new IOException(L10n.T("ui.this_filename_cannot_be_transferred_through_windows_shell_drag_a"));
            }
        }
    }

    public sealed class WslClient
    {
        private readonly string script, scriptKey;
        internal const string Bootstrap = "import sys;n=int(sys.stdin.buffer.readline());exec(compile(sys.stdin.buffer.read(n),'<WSLBox>','exec'))";
        internal static void WriteSource(Stream input, string source)
        {
            string language = L10n.Language;
            source = "import os;os.environ['WSLBOX_LANGUAGE']='" + language + "'\n" + source;
            byte[] data = Encoding.UTF8.GetBytes(source);
            byte[] header = Encoding.ASCII.GetBytes(data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
            input.Write(header, 0, header.Length); input.Write(data, 0, data.Length); input.Flush();
        }
        private static readonly object workerLock = new object();
        private static readonly Dictionary<string, OpenWorker> workers = new Dictionary<string, OpenWorker>(StringComparer.Ordinal);
        private static bool workersClosing;
        private static System.Threading.Timer idleTimer;
        private static int sweeping;
        private static readonly Dictionary<string, Task<Reply>> reads = new Dictionary<string, Task<Reply>>(StringComparer.Ordinal);
        public WslClient(string backendFile)
        {
            string runtimePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backend.py");
            if (String.Equals(Path.GetFullPath(backendFile), Path.GetFullPath(runtimePath), StringComparison.OrdinalIgnoreCase))
            {
                using (Stream embedded = typeof(WslClient).Assembly.GetManifestResourceStream("WSLBox.Backend"))
                {
                    if (embedded != null) { using (var reader = new StreamReader(embedded, Encoding.UTF8)) script = reader.ReadToEnd(); }
                }
            }
            // Explicit custom paths remain useful to developers and isolated tests.
            if (script == null) script = File.ReadAllText(backendFile, Encoding.UTF8);
            using (var hash = System.Security.Cryptography.SHA256.Create())
                scriptKey = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(script)));
        }
        public Task<Reply> OpenWith(string distro, string path, string application, bool setDefault)
        {
            return Task.Run(() => Worker(distro).Open(path, application, setDefault));
        }

        public Task<Reply> Run(string distro, string command, string path)
        {
            if (command == "open") return Task.Run(() => Worker(distro).Open(path));
            if (command == "open-trash") return Task.Run(() => Worker(distro).Request(command, path));
            if (command == "delete-permanently" || (command == "info" && String.IsNullOrWhiteSpace(distro)))
                return RunOneShot(distro, command, path);
            string mode = command == "thumbnails" ? "serve-thumbnails" : "serve-metadata";
            bool readOnly = command == "list" || command == "list-icons" || command == "info"
                || command == "folder-info" || command == "applications" || command == "thumbnails";
            if (!readOnly) return Task.Run(() => Worker(distro, mode).Request(command, path));
            string key = distro + "\n" + scriptKey + "\n" + command + "\n" + path;
            lock (workerLock)
            {
                if (workersClosing) throw new ObjectDisposedException("WslClient");
                Task<Reply> pending;
                if (reads.TryGetValue(key, out pending) && !pending.IsCompleted) return pending;
                pending = Task.Run(() => Worker(distro, mode).Request(command, path));
                reads[key] = pending;
                pending.ContinueWith(done => {
                    lock (workerLock)
                    {
                        Task<Reply> current;
                        if (reads.TryGetValue(key, out current) && Object.ReferenceEquals(current, done)) reads.Remove(key);
                    }
                },
                    System.Threading.CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return pending;
            }
        }
        private OpenWorker Worker(string distro, string mode = "serve-open")
        {
            string key = (distro ?? "") + "\n" + mode + "\n" + scriptKey;
            lock (workerLock)
            {
                if (workersClosing) throw new ObjectDisposedException("WslClient");
                OpenWorker worker;
                if (!workers.TryGetValue(key, out worker)) { worker = new OpenWorker(distro, script, mode); workers.Add(key, worker); }
                if (mode != "serve-open" && idleTimer == null) idleTimer = new System.Threading.Timer(SweepIdleWorkers, null, 30000, 30000);
                return worker;
            }
        }
        private static void SweepIdleWorkers(object state)
        {
            if (System.Threading.Interlocked.CompareExchange(ref sweeping, 1, 0) != 0) return;
            try
            {
                OpenWorker[] snapshot;
                lock (workerLock) snapshot = workers.Values.ToArray();
                foreach (OpenWorker worker in snapshot) worker.ReleaseIfIdle(TimeSpan.FromSeconds(60));
            }
            finally { System.Threading.Interlocked.Exchange(ref sweeping, 0); }
        }
        public void PrepareOpen(string distro)
        {
            Task.Run(() => { try { Worker(distro).Warmup(); } catch { /* Opening will surface any startup failure. */ } });
        }
        public static void ShutdownOpenWorkers()
        {
            OpenWorker[] all; System.Threading.Timer timer;
            lock (workerLock) { workersClosing = true; all = workers.Values.ToArray(); workers.Clear(); timer = idleTimer; idleTimer = null; }
            if (timer != null) timer.Dispose();
            foreach (OpenWorker worker in all) worker.Dispose();
        }
        public Task<Reply> RunOneShot(string distro, string command, string path)
        {
            return Task.Run(() => {
                // WSL parses its leading switches before forwarding Linux argv;
                // unlike CRT argv parsing, it does not accept quoted switches.
                string distroArgument = distro ?? "";
                if (distroArgument.Any(c => Char.IsWhiteSpace(c) || c == '"')) distroArgument = Paths.Quote(distroArgument);
                string arguments = String.IsNullOrWhiteSpace(distro) ? ""
                    : "--distribution " + distroArgument + " ";
                string prefix = arguments + "--exec /usr/bin/python3 -X utf8 -c ";
                string suffix = " " + Paths.Quote(command) + (path == null ? "" : " " + Paths.Quote(path));
                arguments = prefix + Paths.Quote(Bootstrap) + suffix;
                var start = new ProcessStartInfo("wsl.exe", arguments) {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                using (var process = Process.Start(start))
                {
                    var output = ReadBytes(process.StandardOutput.BaseStream);
                    var errors = ReadBytes(process.StandardError.BaseStream);
                    try { WriteSource(process.StandardInput.BaseStream, script); process.StandardInput.Close(); }
                    catch (IOException ex) { throw new IOException(L10n.T("ui.could_not_send_a_startup_request_to_wsl_check_that_the_distribut"), ex); }
                    bool mutation = command == "trash" || command == "delete-permanently";
                    if (!process.WaitForExit(mutation ? 120000 : 20000))
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        throw new IOException(mutation ? L10n.T("ui.the_file_operation_timed_out_and_may_be_partially_complete_check") : L10n.T("ui.wsl_did_not_respond_within_20_seconds_check_the_distribution_and"));
                    }
                    if (!Task.WaitAll(new Task[] { output, errors }, 5000))
                        throw new IOException(L10n.T("ui.wsl_output_did_not_close_normally"));
                    string stdout = Decode(output.Result), stderr = Decode(errors.Result);
                    Reply reply;
                    try { reply = Json.Read<Reply>(stdout.Trim()); }
                    catch (Exception ex)
                    {
                        if (!(ex is SerializationException) && !(ex is System.Xml.XmlException)) throw;
                        throw new IOException(L10n.T("ui.wsl_returned_an_invalid_response") + stdout + "\n" + stderr, ex);
                    }
                    if (!String.IsNullOrEmpty(reply.Error)) throw new IOException(reply.Error);
                    if (process.ExitCode != 0) throw new IOException(L10n.T("ui.wsl_failed") + stderr);
                    return reply;
                }
            });
        }

        private static async Task<byte[]> ReadBytes(Stream stream)
        {
            using (var memory = new MemoryStream())
            {
                await stream.CopyToAsync(memory);
                return memory.ToArray();
            }
        }
        private static string Decode(byte[] bytes)
        {
            // Linux Python emits UTF-8; native wsl.exe errors can be UTF-16LE.
            bool unicode = bytes.Length >= 2 && ((bytes[0] == 0xff && bytes[1] == 0xfe)
                || bytes.Take(512).Count(b => b == 0) > 4);
            return (unicode ? Encoding.Unicode : Encoding.UTF8).GetString(bytes).TrimStart('\ufeff');
        }
    }
}
