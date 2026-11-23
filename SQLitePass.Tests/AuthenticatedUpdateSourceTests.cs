using System.Security.Cryptography;
using System.Net;
using System.Text.Json;
using Velopack;
using Velopack.Logging;
using Velopack.Locators;
using Velopack.Sources;
using SQLitePass.Desktop.Services;

namespace SQLitePass.Tests;

public sealed class AuthenticatedUpdateSourceTests
{
    private const string Channel = "linux";

    [Fact]
    public void OfficialManifestAndSignatureUrisAreFixedHttpsReleaseAssets()
    {
        var version = SemanticVersion.Parse("1.2.4");

        Assert.Equal("https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.4/sqlitepass-update-auth.json",
            GitHubReleaseManifestProvider.BuildReleaseAssetUri(version, GitHubReleaseManifestProvider.ManifestFileName).AbsoluteUri);
        Assert.Equal("https://github.com/FuryGamingASD/SQLitePass/releases/download/v1.2.4/sqlitepass-update-auth.sig",
            GitHubReleaseManifestProvider.BuildReleaseAssetUri(version, GitHubReleaseManifestProvider.SignatureFileName).AbsoluteUri);
        Assert.Throws<InvalidDataException>(() => GitHubReleaseManifestProvider.BuildReleaseAssetUri(version, "../asset.nupkg"));
    }

    [Theory]
    [InlineData("https://github.com/path", true)]
    [InlineData("https://release-assets.githubusercontent.com/path", true)]
    [InlineData("http://github.com/path", false)]
    [InlineData("https://githubusercontent.com.attacker.invalid/path", false)]
    [InlineData("https://attacker.invalid/path", false)]
    public void ReleaseRedirectsStayOnGitHubHttpsHosts(string value, bool expected)
    {
        Assert.Equal(expected, GitHubReleaseManifestProvider.IsAllowedGitHubReleaseUri(new Uri(value)));
    }

    [Fact]
    public async Task OfficialPackageDownloadRejectsBytesLongerThanSignedSizeBeforeWriting()
    {
        using var client = new HttpClient(new FixedResponseHandler([1, 2, 3, 4, 5]));
        var provider = new GitHubReleaseManifestProvider(client);
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".nupkg");
        var asset = new UpdateManifestAsset(Channel, "SQLitePass", "1.2.4", "Full", "SQLitePass-1.2.4-linux-full.nupkg", 4, new string('0', 64));

        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                provider.DownloadAsync(asset, destination, _ => { }, CancellationToken.None));
            Assert.False(File.Exists(destination));
        }
        finally { if (File.Exists(destination)) File.Delete(destination); }
    }

    [Fact]
    public async Task SignedLatestPackageIsReturnedAndDownloadedBytesAreVerified()
    {
        using var fixture = new Fixture("1.2.4", [1, 2, 3, 4]);
        var feed = await fixture.Source.GetReleaseFeed(null!, "SQLitePass", Channel);
        var package = Assert.Single(feed.Assets);
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".nupkg");
        try
        {
            await fixture.Source.DownloadReleaseEntry(null!, package, destination, _ => { });
            Assert.Equal(fixture.PackageBytes, await File.ReadAllBytesAsync(destination));
        }
        finally { if (File.Exists(destination)) File.Delete(destination); }
    }

    [Fact]
    public async Task ModifiedVelopackFeedIsRejectedAgainstSignedManifest()
    {
        using var fixture = new Fixture("1.2.4", [1, 2, 3, 4]);
        fixture.Inner.Assets[0].SHA256 = new string('0', 64);

        await Assert.ThrowsAsync<CryptographicException>(() =>
            fixture.Source.GetReleaseFeed(null!, "SQLitePass", Channel));
    }

    [Fact]
    public async Task InvalidManifestSignatureFailsClosed()
    {
        using var fixture = new Fixture("1.2.4", [1, 2, 3, 4]);
        fixture.Manifest.Signature[^1] ^= 0x01;

        await Assert.ThrowsAsync<CryptographicException>(() =>
            fixture.Source.GetReleaseFeed(null!, "SQLitePass", Channel));
    }

    [Fact]
    public async Task ModifiedPackageIsRejectedBeforeItReachesVelopackDestination()
    {
        using var fixture = new Fixture("1.2.4", [1, 2, 3, 4]);
        fixture.Inner.DownloadBytes = [1, 2, 3, 5];
        var package = Assert.Single((await fixture.Source.GetReleaseFeed(null!, "SQLitePass", Channel)).Assets);
        string destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".nupkg");

        try
        {
            await Assert.ThrowsAsync<CryptographicException>(() =>
                fixture.Source.DownloadReleaseEntry(null!, package, destination, _ => { }));
            Assert.False(File.Exists(destination));
        }
        finally { if (File.Exists(destination)) File.Delete(destination); }
    }

    [Fact]
    public async Task ModifiedCachedPackageIsRejectedUnderVelopackLockBeforeCacheEarlyReturn()
    {
        using var fixture = new Fixture("1.2.4", [1, 2, 3, 4]);
        var package = Assert.Single((await fixture.Source.GetReleaseFeed(null!, "SQLitePass", Channel)).Assets);
        string packagesDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packagesDirectory);
        string cachedPackage = Path.Combine(packagesDirectory, package.FileName);
        try
        {
            await File.WriteAllBytesAsync(cachedPackage, [1, 2, 3, 9]);
            var locator = new TestVelopackLocator("SQLitePass", "1.2.3", packagesDirectory);
            var manager = new AuthenticatedUpdateManager(fixture.Source,
                new UpdateOptions { ExplicitChannel = Channel, MaximumDeltasBeforeFallback = 0 }, locator);

            await Assert.ThrowsAsync<CryptographicException>(() =>
                manager.DownloadUpdatesAsync(new UpdateInfo(package, isDowngrade: false)));
        }
        finally { Directory.Delete(packagesDirectory, recursive: true); }
    }

    [Fact]
    public async Task ReplayedOlderSignedReleaseIsNotOfferedToInstalledVersion()
    {
        using var fixture = new Fixture("1.2.3", [1, 2, 3, 4]);
        var local = new VelopackAsset { PackageId = "SQLitePass", Version = SemanticVersion.Parse("1.2.4"), Type = VelopackAssetType.Full };

        var feed = await fixture.Source.GetReleaseFeed(null!, "SQLitePass", Channel, latestLocalRelease: local);

        Assert.Empty(feed.Assets);
        Assert.Equal(0, fixture.Provider.ReadCount);
    }

    [Fact]
    public async Task SignedFreshnessRejectsStaleFeedsAndManifestSubstitution()
    {
        using var fixture = new Fixture("1.2.4", [1, 2, 3], verifyFreshness: true);
        var source = fixture.Source;
        var release = Assert.Single((await source.GetReleaseFeed(null!, "SQLitePass", Channel)).Assets);
        fixture.Inner.Assets[0] = new VelopackAsset { PackageId = "SQLitePass", Type = VelopackAssetType.Full,
            Version = SemanticVersion.Parse("1.2.3") };
        await Assert.ThrowsAsync<CryptographicException>(() => source.GetReleaseFeed(null!, "SQLitePass", Channel));
        fixture.Inner.Assets[0] = release;
        fixture.SetFreshness("1.2.4", new string('0', 64), fixture.Clock.Now, fixture.Clock.Now.AddDays(30));
        await Assert.ThrowsAsync<CryptographicException>(() => source.GetReleaseFeed(null!, "SQLitePass", Channel));
    }

    [Fact]
    public async Task FreshnessExpiryRejectsCurrentFeedAndPreviouslyApprovedPackage()
    {
        using var fixture = new Fixture("1.2.4", [1, 2, 3], verifyFreshness: true);
        var asset = Assert.Single((await fixture.Source.GetReleaseFeed(null!, "SQLitePass", Channel)).Assets);
        var local = new VelopackAsset { PackageId = "SQLitePass", Type = VelopackAssetType.Full, Version = asset.Version };
        Assert.Empty((await fixture.Source.GetReleaseFeed(null!, "SQLitePass", Channel, latestLocalRelease: local)).Assets);
        Assert.Single((await fixture.Source.GetReleaseFeed(null!, "SQLitePass", Channel)).Assets);
        fixture.Clock.Now = fixture.Clock.Now.AddDays(30);
        var error = Assert.Throws<CryptographicException>(() => fixture.Source.VerifyPackageFile(asset, "unused"));
        Assert.Contains("no current", error.Message);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Source.GetReleaseFeed(null!, "SQLitePass", Channel, latestLocalRelease: local));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly RSA _signer = RSA.Create(3072);
        internal byte[] PackageBytes { get; }
        internal SignedManifestBytes Manifest { get; }
        internal FakeUpdateSource Inner { get; }
        internal FakeManifestProvider Provider { get; }
        internal AuthenticatedUpdateSource Source { get; }
        internal TestClock Clock { get; } = new();
        private readonly FakeFreshnessProvider _freshness;

        internal Fixture(string version, byte[] packageBytes, bool verifyFreshness = false)
        {
            PackageBytes = packageBytes;
            string sha256 = Convert.ToHexString(SHA256.HashData(packageBytes));
            const string fileName = "SQLitePass-1.2.4-linux-full.nupkg";
            var asset = new VelopackAsset
            {
                PackageId = "SQLitePass", Version = SemanticVersion.Parse(version), Type = VelopackAssetType.Full,
                FileName = fileName, SHA1 = "synthetic-sha1", SHA256 = sha256, Size = packageBytes.Length
            };
            Inner = new FakeUpdateSource(asset) { DownloadBytes = packageBytes };
            var manifest = new UpdateManifestDocument(1, "SQLitePass", version, new string('a', 40),
            [
                new UpdateManifestAsset("win", "SQLitePass", version, "Full", "SQLitePass-1.2.4-win-full.nupkg", 1, new string('0', 64)),
                new UpdateManifestAsset(Channel, "SQLitePass", version, "Full", fileName, packageBytes.Length, sha256)
            ]);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
            Manifest = new SignedManifestBytes(bytes, _signer.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
            Provider = new FakeManifestProvider(Manifest);
            _freshness = new FakeFreshnessProvider(Provider);
            SetFreshness(version, Convert.ToHexString(SHA256.HashData(bytes)), Clock.Now, Clock.Now.AddDays(30));
            Source = new AuthenticatedUpdateSource(Inner, verifyFreshness ? _freshness : Provider,
                new UpdateManifestVerifier(Convert.ToBase64String(_signer.ExportSubjectPublicKeyInfo())), Clock);
        }

        internal void SetFreshness(string version, string hash, DateTimeOffset issuedAt, DateTimeOffset expiresAt)
        {
            byte[] checkpoint = JsonSerializer.SerializeToUtf8Bytes(new UpdateFreshnessDocument(1, "SQLitePass", version, hash, issuedAt, expiresAt));
            _freshness.Envelope = JsonSerializer.SerializeToUtf8Bytes(new SignedUpdateFreshness(Convert.ToBase64String(checkpoint),
                Convert.ToBase64String(_signer.SignData(checkpoint, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))));
        }

        public void Dispose() => _signer.Dispose();
    }

    private sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 11, 23, 13, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FakeFreshnessProvider(FakeManifestProvider provider) : IUpdateManifestProvider, IUpdateFreshnessProvider
    {
        internal byte[] Envelope { get; set; } = [];
        public Task<SignedManifestBytes> GetAsync(SemanticVersion version) => provider.GetAsync(version);
        public Task<byte[]> GetFreshnessAsync() => Task.FromResult(Envelope);
    }

    private sealed class FakeManifestProvider(SignedManifestBytes signed) : IUpdateManifestProvider
    {
        internal int ReadCount { get; private set; }
        public Task<SignedManifestBytes> GetAsync(SemanticVersion version)
        {
            ReadCount++;
            return Task.FromResult(signed);
        }
    }

    private sealed class FakeUpdateSource(VelopackAsset asset) : IUpdateSource
    {
        internal VelopackAsset[] Assets { get; } = [asset];
        internal byte[] DownloadBytes { get; set; } = [];

        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
            Guid? stagingId = null, VelopackAsset? latestLocalRelease = null) =>
            Task.FromResult(new VelopackAssetFeed { Assets = Assets });

        public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
            Action<int> progress, CancellationToken cancelToken = default)
        {
            cancelToken.ThrowIfCancellationRequested();
            await File.WriteAllBytesAsync(localFile, DownloadBytes, cancelToken);
            progress(100);
        }
    }

    private sealed class FixedResponseHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = new HttpRequestMessage(request.Method, request.RequestUri),
                Content = new ByteArrayContent(bytes)
            });
    }
}
