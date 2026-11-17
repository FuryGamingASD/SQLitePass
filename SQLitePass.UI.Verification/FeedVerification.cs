using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace SQLitePass.UI.Verification;

internal static class FeedVerification
{
    public static async Task<int> RunAsync(string repository, string version, string output)
    {
        string folder = Path.Combine(Path.GetTempPath(), "SQLitePass-feed-verification-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder); Directory.CreateDirectory(output);
        try
        {
            if (!Uri.TryCreate(repository, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "github.com")
                throw new ArgumentException("Expected a public HTTPS GitHub repository.");
            var logger = new NullVelopackLogger();
            var source = new GithubSource(repository, null, false);
            var feed = await source.GetReleaseFeed(logger, "SQLitePass", "win", null, null);
            var asset = feed.Assets.Single(item => item.PackageId == "SQLitePass" && item.Type == VelopackAssetType.Full && item.Version.ToString() == version);
            if (asset.FileName != Path.GetFileName(asset.FileName)) throw new InvalidDataException("Unexpected package filename.");
            string path = Path.Combine(folder, asset.FileName);
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await source.DownloadReleaseEntry(logger, asset, path, _ => { }, timeout.Token);
            string sha256;
            using (var file = File.OpenRead(path)) sha256 = Convert.ToHexString(await SHA256.HashDataAsync(file, timeout.Token));
            if (new FileInfo(path).Length != asset.Size) throw new InvalidDataException("Public package length mismatch.");
            if (!string.IsNullOrEmpty(asset.SHA256))
            { if (!sha256.Equals(asset.SHA256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Public package SHA-256 mismatch."); }
            else
            {
                using var file = File.OpenRead(path);
                string sha1 = Convert.ToHexString(await SHA1.HashDataAsync(file, timeout.Token));
                if (!sha1.Equals(asset.SHA1, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Legacy public package SHA-1 mismatch.");
            }
            var evidence = new { Repository = repository, Version = version, Package = asset.FileName, asset.Size, Sha256 = sha256,
                ManifestProvidesSha256 = !string.IsNullOrEmpty(asset.SHA256), FeedReadWithApplicationLibrary = true,
                AnonymousPublicDownload = true, PackageDownloadedAndVerified = true, AppStarted = false, VaultOpened = false };
            File.WriteAllText(Path.Combine(output, "public-feed-results.json"), JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(evidence)); return 0;
        }
        catch (Exception exception) { Console.Error.WriteLine(exception); return 1; }
        finally
        {
            string resolved = Path.GetFullPath(folder);
            if (!resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(resolved).StartsWith("SQLitePass-feed-verification-", StringComparison.Ordinal)) throw new InvalidOperationException("Invalid feed cleanup path.");
            Directory.Delete(resolved, true);
        }
    }
}
