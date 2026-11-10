using System;
using System.IO;
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
    }
}
