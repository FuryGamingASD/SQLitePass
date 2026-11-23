using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;

namespace SQLitePass.Desktop.Services;

internal sealed record UpdateManifestAsset(
    string Channel,
    string PackageId,
    string Version,
    string Type,
    string FileName,
    long Size,
    string Sha256);

internal sealed record UpdateManifestDocument(
    int SchemaVersion,
    string PackageId,
    string Version,
    string SourceCommit,
    UpdateManifestAsset[] Assets);

internal sealed record SignedManifestBytes(byte[] Manifest, byte[] Signature);
internal sealed record UpdateFreshnessDocument(int SchemaVersion, string PackageId, string Version,
    string ManifestSha256, DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt);
internal sealed record SignedUpdateFreshness(string Checkpoint, string Signature);

internal interface IUpdateManifestProvider
{
    Task<SignedManifestBytes> GetAsync(SemanticVersion version);
}

internal interface IUpdateFreshnessProvider
{
    Task<byte[]> GetFreshnessAsync();
}

internal interface IUpdatePackageDownloader
{
    Task DownloadAsync(UpdateManifestAsset asset, string localFile, Action<int> progress, CancellationToken cancelToken);
}

internal sealed class GitHubReleaseManifestProvider : IUpdateManifestProvider, IUpdatePackageDownloader, IUpdateFreshnessProvider
{
    internal const string ManifestFileName = "sqlitepass-update-auth.json";
    internal const string SignatureFileName = "sqlitepass-update-auth.sig";
    internal const string FreshnessFileName = "sqlitepass-update-freshness.json";
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true })
    {
        Timeout = TimeSpan.FromSeconds(20)
    };
    private readonly HttpClient _http;

    internal GitHubReleaseManifestProvider(HttpClient? httpClient = null) => _http = httpClient ?? Http;

    public Task<byte[]> GetFreshnessAsync() => DownloadBoundedAsync(_http,
        new Uri($"https://github.com/FuryGamingASD/SQLitePass/releases/latest/download/{FreshnessFileName}"), 16 * 1024);

    public async Task<SignedManifestBytes> GetAsync(SemanticVersion version)
    {
        byte[] manifest = await DownloadBoundedAsync(_http, BuildReleaseAssetUri(version, ManifestFileName), 64 * 1024).ConfigureAwait(false);
        byte[] signature = await DownloadBoundedAsync(_http, BuildReleaseAssetUri(version, SignatureFileName), 4096).ConfigureAwait(false);
        if (signature.Length > 1024) throw new InvalidDataException("The release signature file is invalid.");
        return new(manifest, signature);
    }

    public Task DownloadAsync(UpdateManifestAsset asset, string localFile, Action<int> progress, CancellationToken cancelToken)
    {
        if (!UpdateManifestVerifier.IsStableVersion(asset.Version) || asset.Size < 1)
            throw new InvalidDataException("The signed update package metadata is invalid.");
        return DownloadBoundedToFileAsync(_http, BuildReleaseAssetUri(SemanticVersion.Parse(asset.Version), asset.FileName),
            localFile, asset.Size, progress, cancelToken);
    }

    internal static Uri BuildReleaseAssetUri(SemanticVersion version, string assetName)
    {
        string versionText = version.ToString();
        if (!UpdateManifestVerifier.IsStableVersion(versionText) || string.IsNullOrWhiteSpace(assetName) ||
            Path.GetFileName(assetName) != assetName || assetName.Contains('/') || assetName.Contains('\\'))
            throw new InvalidDataException("The update release URL contains invalid metadata.");
        return new Uri($"https://github.com/FuryGamingASD/SQLitePass/releases/download/v{versionText}/{Uri.EscapeDataString(assetName)}");
    }

    private static async Task<byte[]> DownloadBoundedAsync(HttpClient http, Uri uri, int maximumBytes)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        EnsureGitHubReleaseUri(response.RequestMessage?.RequestUri);
        if (response.Content.Headers.ContentLength is long length && (length < 1 || length > maximumBytes))
            throw new InvalidDataException("Release metadata exceeds its size limit.");

        await using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var output = new MemoryStream();
        byte[] buffer = new byte[4096];
        while (true)
        {
            int read = await input.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes) throw new InvalidDataException("Release metadata exceeds its size limit.");
            output.Write(buffer, 0, read);
        }
        if (output.Length == 0) throw new InvalidDataException("Release metadata is empty.");
        return output.ToArray();
    }

    private static async Task DownloadBoundedToFileAsync(HttpClient http, Uri uri, string localFile, long expectedBytes,
        Action<int> progress, CancellationToken cancelToken)
    {
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancelToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        EnsureGitHubReleaseUri(response.RequestMessage?.RequestUri);
        if (response.Content.Headers.ContentLength is long length && length != expectedBytes)
            throw new InvalidDataException("The release package size differs from its signed size.");

        await using var input = await response.Content.ReadAsStreamAsync(cancelToken).ConfigureAwait(false);
        await using var output = new FileStream(localFile, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 81920, useAsync: true);
        byte[] buffer = new byte[81920];
        long total = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancelToken).ConfigureAwait(false);
            if (read == 0) break;
            total += read;
            if (total > expectedBytes) throw new InvalidDataException("The release package exceeds its signed size.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancelToken).ConfigureAwait(false);
            progress((int)Math.Min(99, total / (double)expectedBytes * 100));
        }
        if (total != expectedBytes) throw new InvalidDataException("The release package is shorter than its signed size.");
        await output.FlushAsync(cancelToken).ConfigureAwait(false);
        progress(100);
    }

    private static void EnsureGitHubReleaseUri(Uri? uri)
    {
        if (!IsAllowedGitHubReleaseUri(uri))
            throw new InvalidDataException("Release assets must remain on GitHub HTTPS hosts.");
    }

    internal static bool IsAllowedGitHubReleaseUri(Uri? uri) =>
        uri != null && uri.Scheme == Uri.UriSchemeHttps &&
        (uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase));
}

internal sealed class UpdateManifestVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        MaxDepth = 8
    };
    private readonly byte[] _publicKey;
    private readonly string? _configurationError;

    internal UpdateManifestVerifier(string subjectPublicKeyInfoBase64)
    {
        if (string.IsNullOrWhiteSpace(subjectPublicKeyInfoBase64))
        {
            _publicKey = [];
            _configurationError = "Verified updates are disabled until UPDATE_SIGNING_PUBLIC_KEY is provisioned in the release build.";
            return;
        }

        try { _publicKey = Convert.FromBase64String(subjectPublicKeyInfoBase64); }
        catch (FormatException)
        {
            _publicKey = [];
            _configurationError = "The pinned update verification key is malformed.";
            return;
        }

        using var rsa = RSA.Create();
        try
        {
            rsa.ImportSubjectPublicKeyInfo(_publicKey, out int read);
            if (read != _publicKey.Length || rsa.KeySize < 3072 || rsa.KeySize > 8192)
                throw new CryptographicException("The pinned update verification key has an unsupported size.");
        }
        catch (CryptographicException)
        {
            _configurationError = "The pinned update verification key is invalid.";
        }
    }

    private void VerifySignature(SignedManifestBytes signed)
    {
        if (_configurationError != null) throw new InvalidOperationException(_configurationError);
        if (signed.Manifest.Length is < 1 or > 64 * 1024 || signed.Signature.Length is < 1 or > 1024)
            throw new InvalidDataException("The signed update manifest has an invalid size.");

        using var rsa = RSA.Create();
        rsa.ImportSubjectPublicKeyInfo(_publicKey, out _);
        if (!rsa.VerifyData(signed.Manifest, signed.Signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new CryptographicException("The update manifest signature is invalid.");
    }

    internal UpdateFreshnessDocument VerifyFreshness(byte[] envelope, DateTimeOffset now)
    {
        if (envelope.Length is < 1 or > 16 * 1024)
            throw new InvalidDataException("The update freshness envelope has an invalid size.");
        var signed = JsonSerializer.Deserialize<SignedUpdateFreshness>(envelope, JsonOptions)
            ?? throw new InvalidDataException("The update freshness envelope is empty.");
        if (string.IsNullOrEmpty(signed.Checkpoint) || string.IsNullOrEmpty(signed.Signature))
            throw new InvalidDataException("The update freshness envelope is incomplete.");
        byte[] checkpoint = Convert.FromBase64String(signed.Checkpoint);
        VerifySignature(new(checkpoint, Convert.FromBase64String(signed.Signature)));
        var freshness = JsonSerializer.Deserialize<UpdateFreshnessDocument>(checkpoint, JsonOptions)
            ?? throw new InvalidDataException("The update freshness checkpoint is empty.");
        if (freshness.SchemaVersion != 1 || freshness.PackageId != "SQLitePass" ||
            string.IsNullOrEmpty(freshness.Version) || !IsStableVersion(freshness.Version) ||
            freshness.ManifestSha256 is not { Length: 64 } || !freshness.ManifestSha256.All(Uri.IsHexDigit) ||
            freshness.IssuedAt > now.AddMinutes(5) || freshness.ExpiresAt <= now ||
            freshness.ExpiresAt <= freshness.IssuedAt || freshness.ExpiresAt - freshness.IssuedAt > TimeSpan.FromDays(30))
            throw new InvalidDataException("The signed update freshness checkpoint is invalid or expired.");
        return freshness;
    }

    internal UpdateManifestDocument Verify(SignedManifestBytes signed)
    {
        VerifySignature(signed);
        var manifest = JsonSerializer.Deserialize<UpdateManifestDocument>(signed.Manifest, JsonOptions)
            ?? throw new InvalidDataException("The signed update manifest is empty.");
        if (manifest.SchemaVersion != 1 || manifest.PackageId != "SQLitePass" ||
            !IsStableVersion(manifest.Version) ||
            !System.Text.RegularExpressions.Regex.IsMatch(manifest.SourceCommit, "^[0-9a-f]{40}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
            manifest.Assets is not { Length: 2 })
            throw new InvalidDataException("The signed update manifest has unsupported metadata.");

        var channels = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asset in manifest.Assets)
        {
            if (asset.Channel is not ("win" or "linux") || !channels.Add(asset.Channel) ||
                asset.PackageId != manifest.PackageId || asset.Version != manifest.Version || asset.Type != "Full" ||
                string.IsNullOrWhiteSpace(asset.FileName) || Path.GetFileName(asset.FileName) != asset.FileName ||
                asset.FileName.Contains('/') || asset.FileName.Contains('\\') ||
                asset.Size < 1 || !System.Text.RegularExpressions.Regex.IsMatch(asset.Sha256, "^[0-9a-fA-F]{64}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                throw new InvalidDataException("The signed update manifest contains an invalid package entry.");
        }
        return manifest;
    }

    internal static bool IsStableVersion(string value) =>
        System.Text.RegularExpressions.Regex.IsMatch(value, "^(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
}

/// <summary>
/// Restricts Velopack to the signed latest full package and authenticates its bytes before
/// they enter Velopack's package cache or updater apply path.
/// </summary>
internal sealed class AuthenticatedUpdateSource : IUpdateSource
{
    private sealed record AuthorizedAsset(UpdateManifestAsset Asset, DateTimeOffset ExpiresAt);
    private readonly IUpdateSource _inner;
    private readonly IUpdateManifestProvider _manifestProvider;
    private readonly UpdateManifestVerifier _verifier;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<string, AuthorizedAsset> _authorized = new(StringComparer.Ordinal);

    internal AuthenticatedUpdateSource(IUpdateSource inner, IUpdateManifestProvider manifestProvider, UpdateManifestVerifier verifier,
        TimeProvider? timeProvider = null)
    {
        _inner = inner;
        _manifestProvider = manifestProvider;
        _verifier = verifier;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    internal static AuthenticatedUpdateSource ForOfficialReleases(IUpdateSource inner)
    {
        string pinnedKey = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(attribute => attribute.Key == "SQLitePass.UpdateSigningPublicKey")?.Value ?? string.Empty;
        return new(inner, new GitHubReleaseManifestProvider(), new UpdateManifestVerifier(pinnedKey));
    }

    public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
        Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        if (appId != "SQLitePass" || channel is not ("win" or "linux"))
            throw new InvalidDataException("The signed update feed has an unexpected application or channel.");

        var freshnessProvider = _manifestProvider as IUpdateFreshnessProvider;
        var feed = await _inner.GetReleaseFeed(logger, appId, channel, stagingId,
            freshnessProvider == null ? latestLocalRelease : null).ConfigureAwait(false);
        var candidate = feed.Assets
            .Where(asset => asset.PackageId == appId && asset.Type == VelopackAssetType.Full)
            .MaxBy(asset => asset.Version);
        UpdateFreshnessDocument? freshness = null;
        if (freshnessProvider != null)
        {
            _authorized.Clear();
            byte[] envelope = await freshnessProvider.GetFreshnessAsync().ConfigureAwait(false);
            freshness = _verifier.VerifyFreshness(envelope, _timeProvider.GetUtcNow());
            if (candidate == null || candidate.Version.ToString() != freshness.Version ||
                latestLocalRelease != null && candidate.Version < latestLocalRelease.Version)
                throw new CryptographicException("The update feed differs from the signed latest-release checkpoint.");
        }
        if (candidate == null || latestLocalRelease != null && candidate.Version <= latestLocalRelease.Version)
            return new VelopackAssetFeed { Assets = [] };

        var manifest = await _manifestProvider.GetAsync(candidate.Version).ConfigureAwait(false);
        if (freshness != null && !Convert.ToHexString(SHA256.HashData(manifest.Manifest)).Equals(freshness.ManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("The update manifest differs from the signed latest-release checkpoint.");
        var signed = _verifier.Verify(manifest);
        if (signed.Version != candidate.Version.ToString())
            throw new InvalidDataException("The signed update version does not match the Velopack feed.");
        var entry = signed.Assets.SingleOrDefault(asset => asset.Channel == channel)
            ?? throw new InvalidDataException("The signed manifest does not cover this update channel.");
        if (entry.PackageId != candidate.PackageId || entry.Version != candidate.Version.ToString() ||
            entry.Type != "Full" || entry.FileName != candidate.FileName || entry.Size != candidate.Size ||
            !entry.Sha256.Equals(candidate.SHA256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("The Velopack feed differs from the signed release manifest.");

        _authorized[Key(candidate)] = new(entry, freshness?.ExpiresAt ?? DateTimeOffset.MaxValue);
        return new VelopackAssetFeed { Assets = [candidate] };
    }

    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
        Action<int> progress, CancellationToken cancelToken = default)
    {
        var expected = GetExpected(releaseEntry);
        string destination = Path.GetFullPath(localFile);
        string temporary = destination + ".auth-" + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (_manifestProvider is IUpdatePackageDownloader packageDownloader)
                await packageDownloader.DownloadAsync(expected, temporary, progress, cancelToken).ConfigureAwait(false);
            else
                await _inner.DownloadReleaseEntry(logger, releaseEntry, temporary, progress, cancelToken).ConfigureAwait(false);
            VerifyPackageFile(releaseEntry, temporary);
            cancelToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal void VerifyPackageFile(VelopackAsset releaseEntry, string path)
    {
        var expected = GetExpected(releaseEntry);
        var file = new FileInfo(path);
        if (!file.Exists || file.Length != expected.Size)
            throw new CryptographicException("The downloaded update package does not match the signed size.");
        using var input = file.OpenRead();
        string hash = Convert.ToHexString(SHA256.HashData(input));
        if (!hash.Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("The downloaded update package does not match the signed SHA-256 digest.");
    }

    private UpdateManifestAsset GetExpected(VelopackAsset asset) =>
        _authorized.TryGetValue(Key(asset), out var expected) && expected.ExpiresAt > _timeProvider.GetUtcNow()
            ? expected.Asset
            : throw new CryptographicException("The Velopack package has no current verified release manifest.");

    private static string Key(VelopackAsset asset) =>
        $"{asset.PackageId}\n{asset.Version}\n{asset.Type}\n{asset.FileName}\n{asset.Size}\n{asset.SHA256}";
}

internal sealed class AuthenticatedUpdateManager : UpdateManager
{
    private readonly AuthenticatedUpdateSource _source;
    private readonly AsyncLocal<VelopackAsset?> _downloadRelease = new();

    internal AuthenticatedUpdateManager(AuthenticatedUpdateSource source, UpdateOptions options, IVelopackLocator? locator = null)
        : base(source, options, locator) => _source = source;

    public override async Task DownloadUpdatesAsync(UpdateInfo updates, Action<int>? progress = null, CancellationToken cancelToken = default)
    {
        var release = updates.TargetFullRelease;
        if (release == null) throw new InvalidDataException("The update has no full package.");
        string packagePath = LocalPackagePath(release);
        _downloadRelease.Value = release;
        try
        {
            await base.DownloadUpdatesAsync(updates, progress, cancelToken).ConfigureAwait(false);
            _source.VerifyPackageFile(release, packagePath);
        }
        finally
        {
            _downloadRelease.Value = null;
        }
    }

    protected override async Task<IDisposable> AcquireUpdateLock()
    {
        var updateLock = await base.AcquireUpdateLock().ConfigureAwait(false);
        try
        {
            // Velopack 1.2.161 returns early for cached files and extracts Update.exe
            // from the package in its finally block. Verify cache only after acquiring
            // Velopack's lock, so another Velopack update cannot swap it after the check.
            if (_downloadRelease.Value is { } release)
                VerifyCachedPackage(_source, release, LocalPackagePath(release));
            return updateLock;
        }
        catch
        {
            updateLock.Dispose();
            throw;
        }
    }

    internal static void VerifyCachedPackage(AuthenticatedUpdateSource source, VelopackAsset release, string packagePath)
    {
        if (File.Exists(packagePath)) source.VerifyPackageFile(release, packagePath);
    }

    protected override async Task VerifyPackageChecksumAsync(VelopackAsset release, string? filePathOverride = null)
    {
        await base.VerifyPackageChecksumAsync(release, filePathOverride).ConfigureAwait(false);
        _source.VerifyPackageFile(release, filePathOverride ?? LocalPackagePath(release));
    }

    private string LocalPackagePath(VelopackAsset release) =>
        Path.Combine(Locator.PackagesDir ?? throw new InvalidOperationException("Velopack package cache is unavailable."), release.FileName);
}
