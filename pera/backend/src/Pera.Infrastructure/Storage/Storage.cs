using Microsoft.Extensions.Logging;
using Pera.Application.Interfaces;

namespace Pera.Infrastructure.Storage;

/// <summary>Uploaded files on local disk (or a mounted share): &lt;root&gt;/&lt;folder&gt;/&lt;guid&gt;_&lt;name&gt;. Back up this folder together with the database.
/// Files are written once and never overwritten; a signed copy is always a new file.</summary>
public sealed class LocalFileStorage(string root) : IFileStorage
{
    public async Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken ct = default)
    {
        var rel = Path.Combine(string.Join(Path.DirectorySeparatorChar, folder.Split('/', '\\').Select(Safe)), Guid.NewGuid().ToString("N") + "_" + Safe(fileName));
        var full = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write);
        await content.CopyToAsync(fs, ct);
        return rel.Replace('\\', '/');
    }
    public Stream? Open(string storagePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, storagePath));
        if (!full.StartsWith(Path.GetFullPath(root), StringComparison.Ordinal)) return null;
        return File.Exists(full) ? File.OpenRead(full) : null;
    }
    static string Safe(string s) => string.Concat(Path.GetFileName(s).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

/// <summary>Placeholder for the approved malware scanner (SR-SEC-05). Logs once that scanning is not configured and passes the file.
/// Replace with an implementation that calls the scanner PERA IT selects (e.g. a ClamAV daemon or the endpoint product's API).</summary>
public sealed class UnconfiguredFileScanner(ILogger<UnconfiguredFileScanner> log) : IFileScanner
{
    static bool _warned;
    public Task<string?> ScanAsync(byte[] content, string fileName, CancellationToken ct = default)
    {
        if (!_warned) { _warned = true; log.LogWarning("No malware scanner is configured; uploads are checked for type, size and content signature only (SR-SEC-05)."); }
        return Task.FromResult<string?>(null);
    }
}
