using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Net;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace WSLBox
{
    [DataContract]
    internal sealed class ReleaseInfo
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
    }
    internal static class UpdateService
    {
        public const string ProjectUrl = "https://github.com/liunics/WSLBox";
        public const string ReleasesUrl = ProjectUrl + "/releases/latest";
        internal static Version Parse(string json)
        {
            var release = Json.Read<ReleaseInfo>(json);
            if (release == null || release.Draft || release.Prerelease || release.Tag == null
                || !Regex.IsMatch(release.Tag, @"^v?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$"))
                throw new InvalidDataException("Invalid stable release.");
            return new Version(release.Tag.TrimStart('v'));
        }
        internal static async Task<Version> Check(CancellationToken cancellation)
        {
            var request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/liunics/WSLBox/releases/latest");
            request.UserAgent = "WSLBox/" + Branding.Version;
            request.Accept = "application/vnd.github+json";
            request.Headers["X-GitHub-Api-Version"] = "2022-11-28";
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(12000);
                using (timeout.Token.Register(request.Abort))
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var input = response.GetResponseStream())
                using (var output = new MemoryStream())
                {
                    byte[] buffer = new byte[4096];
                    int read;
                    while ((read = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        if (output.Length + read > 2 * 1024 * 1024) throw new InvalidDataException("Release response too large.");
                        output.Write(buffer, 0, read);
                    }
                    return Parse(Encoding.UTF8.GetString(output.ToArray()));
                }
            }
        }

        internal static async Task<string> Download(Version version, CancellationToken cancellation)
        {
            if (version <= new Version(Branding.Version)) throw new InvalidDataException("Update is not newer.");
            string target = Assembly.GetExecutingAssembly().Location;
            string staged = target + ".update-" + Guid.NewGuid().ToString("N");
            string name = "WSLBox-v" + version + ".exe";
            string release = ProjectUrl + "/releases/download/v" + version + "/";
            try
            {
                string checksum;
                using (var manifest = new MemoryStream())
                {
                    await Fetch(release + "SHA256SUMS.txt", manifest, 4096, cancellation).ConfigureAwait(false);
                    var entry = Regex.Match(Encoding.UTF8.GetString(manifest.ToArray()).Trim(),
                        @"\A([a-fA-F0-9]{64})[ 	]+\*?" + Regex.Escape(name) + @"\z");
                    if (!entry.Success) throw new InvalidDataException(L10n.T("about.update_invalid"));
                    checksum = entry.Groups[1].Value;
                }
                using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await Fetch(release + name, output, 64 * 1024 * 1024, cancellation).ConfigureAwait(false);
                using (var input = File.OpenRead(staged))
                using (var sha = SHA256.Create())
                {
                    string actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                    if (!String.Equals(actual, checksum, StringComparison.OrdinalIgnoreCase) ||
                        FileVersionInfo.GetVersionInfo(staged).FileVersion != version + ".0")
                        throw new InvalidDataException(L10n.T("about.update_invalid"));
                }
                cancellation.ThrowIfCancellationRequested();
                return staged;
            }
            catch
            {
                TryDelete(staged);
                throw;
            }
        }

        private static async Task Fetch(string url, Stream output, int limit, CancellationToken cancellation)
        {
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "WSLBox/" + Branding.Version;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation))
            {
                timeout.CancelAfter(TimeSpan.FromMinutes(2));
                using (timeout.Token.Register(request.Abort))
                using (var response = (HttpWebResponse)await request.GetResponseAsync().ConfigureAwait(false))
                using (var input = response.GetResponseStream())
                {
                    if (response.ResponseUri.Scheme != Uri.UriSchemeHttps || response.ContentLength > limit)
                        throw new InvalidDataException(L10n.T("about.update_invalid"));
                    byte[] buffer = new byte[16384];
                    int count;
                    while ((count = await input.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0)
                    {
                        if (output.Length + count > limit) throw new InvalidDataException(L10n.T("about.update_invalid"));
                        output.Write(buffer, 0, count);
                    }
                }
            }
        }

        internal static void StartInstaller(string staged)
        {
            string executable = Assembly.GetExecutingAssembly().Location;
            CheckDestination(executable, InstalledPath(executable));
            string helper = Path.Combine(Path.GetDirectoryName(WorkspaceLayout.FilePath), "WSLBox.UpdateHelper.exe");
            File.Copy(executable, helper, true);
            using (var parent = Process.GetCurrentProcess())
            using (var installer = Process.Start(new ProcessStartInfo(helper,
                "--apply-update " + parent.Id + " " + Paths.Quote(executable) + " " + Paths.Quote(staged))
                { UseShellExecute = false }))
            {
                if (installer == null) throw new IOException(L10n.T("about.update_invalid"));
            }
        }

        internal static void ApplyUpdate(int parentId, string executable, string staged)
        {
            // The staged file must have been downloaded alongside the original EXE.
            executable = Path.GetFullPath(executable);
            staged = Path.GetFullPath(staged);
            if (!Regex.IsMatch(staged, @"\A" + Regex.Escape(executable) + @"\.update-[0-9a-f]{32}\z", RegexOptions.IgnoreCase))
                throw new InvalidDataException(L10n.T("about.update_invalid"));
            try
            {
                using (var parent = Process.GetProcessById(parentId))
                {
                    if (!String.Equals(parent.MainModule.FileName, executable, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException(L10n.T("about.update_invalid"));
                    if (!parent.WaitForExit(30000)) throw new TimeoutException(L10n.T("about.update_exit_timeout"));
                }
            }
            catch (ArgumentException)
            {
                // The parent may already have exited before the helper started.
            }

            string backup = executable + ".previous";
            string destination = InstalledPath(executable);
            bool rename = !String.Equals(executable, destination, StringComparison.OrdinalIgnoreCase);
            bool replaced = false, renamed = false, shortcutRetargeted = false;
            try
            {
                CheckDestination(executable, destination);
                File.Replace(staged, executable, backup);
                replaced = true;
                if (rename)
                {
                    File.Move(executable, destination);
                    renamed = true;
                }
                if (StartupRegistration.IsEnabled)
                {
                    StartupRegistration.SetEnabled(true, StartupRegistration.DirectoryPath, destination);
                    shortcutRetargeted = true;
                }
                Restart(destination);
                TryDelete(backup);
            }
            catch
            {
                if (renamed)
                {
                    File.Move(backup, executable);
                    TryDelete(destination);
                }
                else if (replaced) File.Replace(backup, executable, null);
                if (shortcutRetargeted)
                    StartupRegistration.SetEnabled(true, StartupRegistration.DirectoryPath, executable);
                Restart(executable);
                throw;
            }
            finally { TryDelete(staged); }
        }

        private static string InstalledPath(string executable)
        {
            return Path.Combine(Path.GetDirectoryName(executable), "WSLBox.exe");
        }

        private static void CheckDestination(string executable, string destination)
        {
            if (!String.Equals(executable, destination, StringComparison.OrdinalIgnoreCase) &&
                (File.Exists(destination) || Directory.Exists(destination)))
                throw new IOException(L10n.F("about.update_destination_exists", Path.GetFileName(destination)));
        }

        private static void Restart(string executable)
        {
            using (var process = Process.Start(new ProcessStartInfo(executable) {
                UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)
            }))
            {
                if (process == null) throw new IOException(L10n.T("about.update_invalid"));
            }
        }

        internal static void TryDelete(string path)
        {
            if (path == null) return;
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
