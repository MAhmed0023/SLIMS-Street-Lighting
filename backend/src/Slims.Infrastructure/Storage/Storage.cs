using QRCoder;
using Slims.Application.Interfaces;

namespace Slims.Infrastructure.Storage;

/// <summary>Evidence files on local disk (or a mounted share): &lt;root&gt;/&lt;folder&gt;/&lt;guid&gt;_&lt;name&gt;. Back up this folder together with the database.</summary>
public sealed class LocalFileStorage(string root) : IFileStorage
{
    public async Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken ct = default)
    {
        var rel = Path.Combine(string.Join(Path.DirectorySeparatorChar, folder.Split('/', '\\').Select(Safe)), Guid.NewGuid().ToString("N") + "_" + Safe(fileName));
        var full = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await using var fs = File.Create(full);
        await content.CopyToAsync(fs, ct);
        return rel.Replace('\\', '/');
    }
    public Stream? Open(string storagePath) { var full = Path.Combine(root, storagePath); return File.Exists(full) ? File.OpenRead(full) : null; }
    public void Delete(string storagePath) { var full = Path.Combine(root, storagePath); if (File.Exists(full)) File.Delete(full); }
    static string Safe(string s) => string.Concat(Path.GetFileName(s).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}

/// <summary>QR Code model 2, error-correction M (BRD 9.4) rendered as PNG.</summary>
public sealed class QrRenderer : IQrRenderer
{
    public byte[] Png(string payload, int pixelsPerModule = 6)
    {
        using var gen = new QRCodeGenerator();
        using var data = gen.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        return new PngByteQRCode(data).GetGraphic(pixelsPerModule);
    }
}
