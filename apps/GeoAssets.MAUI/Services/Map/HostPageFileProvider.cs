using System.Text;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace GeoAssets.MAUI.Services.Map;

/// <summary>
/// The BlazorWebView's static files (XD01-460) with its host page rewritten once by a transform — MapLibre mode's
/// <see cref="MapLibreHostPage.WidenContentSecurityPolicy"/> — and every other file served by <paramref name="inner"/> as
/// it is. The page is read from <paramref name="inner"/> (UTF-8) on first request and kept.
/// </summary>
internal sealed class HostPageFileProvider(IFileProvider inner, string hostPagePath, Func<string, string> transform) : IFileProvider
{
    private readonly Lazy<IFileInfo> _hostPage = new(() => Rewrite(inner, hostPagePath, transform), LazyThreadSafetyMode.ExecutionAndPublication);

    public IFileInfo GetFileInfo(string subpath) =>
        string.Equals(Normalize(subpath), Normalize(hostPagePath), StringComparison.OrdinalIgnoreCase)
            ? _hostPage.Value
            : inner.GetFileInfo(subpath);

    public IDirectoryContents GetDirectoryContents(string subpath) => inner.GetDirectoryContents(subpath);

    public IChangeToken Watch(string filter) => inner.Watch(filter);

    private static string Normalize(string path) => (path ?? string.Empty).Replace('\\', '/').TrimStart('/');

    private static IFileInfo Rewrite(IFileProvider inner, string hostPagePath, Func<string, string> transform)
    {
        var original = inner.GetFileInfo(hostPagePath);
        if (!original.Exists)
            return original;

        string html;
        using (var stream = original.CreateReadStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
            html = reader.ReadToEnd();

        return new RewrittenFile(original, Encoding.UTF8.GetBytes(transform(html)));
    }

    private sealed class RewrittenFile(IFileInfo original, byte[] content) : IFileInfo
    {
        public bool Exists => true;

        public long Length => content.Length;

        public string? PhysicalPath => null;

        public string Name => original.Name;

        public DateTimeOffset LastModified => original.LastModified;

        public bool IsDirectory => false;

        public Stream CreateReadStream() => new MemoryStream(content, writable: false);
    }
}
