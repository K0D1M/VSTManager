using System.Net;
using System.Text;
using VstManager.Core.Services;

namespace VstManager.Core.Tests;

public class LogoCacheTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "vstmgr-logocache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_cacheDir))
        {
            Directory.Delete(_cacheDir, recursive: true);
        }
    }

    private static byte[] WebpBytes()
    {
        // "RIFF" + 4 size bytes + "WEBP" is enough for the signature sniffer.
        var bytes = new byte[16];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] PngBytes() => new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00 };

    private LogoCache CacheReturning(byte[] payload, string? contentType = null)
    {
        var handler = new StubHandler(payload, contentType);
        return new LogoCache(new HttpClient(handler), _cacheDir);
    }

    [Fact]
    public async Task ManualLogo_UrlWithoutExtension_WebpBytes_SavedAsWebp()
    {
        var cache = CacheReturning(WebpBytes());

        var path = await cache.GetManualLogoPathAsync("Serum", "https://cdn.example.com/img?id=123");

        Assert.NotNull(path);
        Assert.Equal(".webp", Path.GetExtension(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task ManualLogo_UrlSaysPngButBytesAreWebp_CorrectedToWebp()
    {
        var cache = CacheReturning(WebpBytes());

        var path = await cache.GetManualLogoPathAsync("Massive", "https://cdn.example.com/logo.png");

        Assert.NotNull(path);
        Assert.Equal(".webp", Path.GetExtension(path));
        // The wrongly-named .png must not be left behind.
        Assert.False(File.Exists(Path.ChangeExtension(path, ".png")));
    }

    [Fact]
    public async Task ManualLogo_WebpUrl_KeepsWebpExtension()
    {
        var cache = CacheReturning(WebpBytes());

        var path = await cache.GetManualLogoPathAsync("Phase Plant", "https://cdn.example.com/logo.webp?w=300");

        Assert.NotNull(path);
        Assert.Equal(".webp", Path.GetExtension(path));
    }

    [Fact]
    public async Task ManualLogo_ContentTypeOnly_WhenNoRecognizableSignature()
    {
        // Unknown leading bytes, but the server declares WebP.
        var cache = CacheReturning(new byte[] { 0x01, 0x02, 0x03, 0x04 }, contentType: "image/webp");

        var path = await cache.GetManualLogoPathAsync("Vital", "https://cdn.example.com/img");

        Assert.NotNull(path);
        Assert.Equal(".webp", Path.GetExtension(path));
    }

    [Fact]
    public async Task ManualLogo_CachedResult_ReusedRegardlessOfExtension()
    {
        var cache = CacheReturning(WebpBytes());
        var first = await cache.GetManualLogoPathAsync("Diva", "https://cdn.example.com/img?id=1");

        // Second call (no forceRefresh) must return the same cached .webp without re-downloading.
        var second = await cache.GetManualLogoPathAsync("Diva", "https://cdn.example.com/img?id=1");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ManualLogo_ForceRefresh_ReplacesStaleFileAndExtension()
    {
        var pngCache = CacheReturning(PngBytes());
        var pngPath = await pngCache.GetManualLogoPathAsync("Pigments", "https://cdn.example.com/logo.png");
        Assert.Equal(".png", Path.GetExtension(pngPath));

        // A corrected URL now resolves to WebP bytes; forceRefresh must drop the old .png.
        var webpCache = CacheReturning(WebpBytes());
        var webpPath = await webpCache.GetManualLogoPathAsync("Pigments", "https://cdn.example.com/new", forceRefresh: true);

        Assert.Equal(".webp", Path.GetExtension(webpPath));
        Assert.False(File.Exists(pngPath));
    }

    [Fact]
    public async Task SaveLocalLogo_PngBytes_SavedWithPngExtension()
    {
        var cache = new LogoCache(new HttpClient(new StubHandler(Array.Empty<byte>(), null)), _cacheDir);
        var sourceFile = Path.Combine(_cacheDir, "source-image.tmp");
        Directory.CreateDirectory(_cacheDir);
        await File.WriteAllBytesAsync(sourceFile, PngBytes());

        var path = await cache.SaveLocalLogoAsync("Serum", sourceFile);

        Assert.NotNull(path);
        Assert.Equal(".png", Path.GetExtension(path));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task SaveLocalLogo_ThenFindManualCachedFile_ReturnsSamePath()
    {
        var cache = new LogoCache(new HttpClient(new StubHandler(Array.Empty<byte>(), null)), _cacheDir);
        var sourceFile = Path.Combine(_cacheDir, "source-image.tmp");
        Directory.CreateDirectory(_cacheDir);
        await File.WriteAllBytesAsync(sourceFile, WebpBytes());

        var saved = await cache.SaveLocalLogoAsync("Massive", sourceFile);
        var found = cache.FindManualCachedFile("Massive");

        Assert.Equal(saved, found);
        Assert.Equal(".webp", Path.GetExtension(found));
    }

    [Fact]
    public async Task SaveLocalLogo_ReplacesPreviouslyCachedFile()
    {
        var cache = new LogoCache(new HttpClient(new StubHandler(Array.Empty<byte>(), null)), _cacheDir);
        var pngSource = Path.Combine(_cacheDir, "a.tmp");
        var webpSource = Path.Combine(_cacheDir, "b.tmp");
        Directory.CreateDirectory(_cacheDir);
        await File.WriteAllBytesAsync(pngSource, PngBytes());
        await File.WriteAllBytesAsync(webpSource, WebpBytes());

        var firstPath = await cache.SaveLocalLogoAsync("Pigments", pngSource);
        var secondPath = await cache.SaveLocalLogoAsync("Pigments", webpSource);

        Assert.Equal(".webp", Path.GetExtension(secondPath));
        Assert.False(File.Exists(firstPath));
    }

    private sealed class StubHandler(byte[] payload, string? contentType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new ByteArrayContent(payload);
            if (contentType is not null)
            {
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
