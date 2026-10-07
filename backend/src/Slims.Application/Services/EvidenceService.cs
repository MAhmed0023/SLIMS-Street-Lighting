using System.Security.Cryptography;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;

namespace Slims.Application.Services;

/// <summary>FR-FLD-009: the original photograph is stored byte for byte with its SHA-256; a separate stamped copy carries
/// date and time, coordinates, accuracy and the context lines (site, item, officer). Nothing ever rewrites the original.</summary>
public class EvidenceService(IAppDbContext db, IFileStorage files, IImageStamper stamper, ICurrentUser current)
{
    public static byte[] Decode(string b64) => Convert.FromBase64String((b64 ?? "").Contains(',') ? b64[(b64.IndexOf(',') + 1)..] : b64 ?? "");
    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string Pkt(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).AddHours(5).ToString("dd-MM-yyyy HH:mm:ss") + " PKT";

    public async Task<Evidence> SaveAsync(User me, string kind, string folder, byte[] bytes, MobilePhoto meta, Guid? deviceId, IReadOnlyList<string> context, CancellationToken ct)
    {
        var at = meta.CapturedAt.ToUniversalTime();
        var name = $"{kind}_{at:yyyyMMdd_HHmmss}.jpg";
        var path = await files.SaveAsync(folder, name, new MemoryStream(bytes, writable: false), ct);
        var lines = new List<string> { context.Count > 0 ? context[0] : "SLIMS · " + kind.ToUpperInvariant(), Pkt(at), $"{meta.Lat:F6}, {meta.Lng:F6}   ±{meta.AccuracyM:0} m" };
        lines.AddRange(context.Skip(1));
        string? stampedPath = null;
        var stamped = stamper.Stamp(bytes, lines);
        if (stamped is not null) stampedPath = await files.SaveAsync(folder, "stamped_" + name, new MemoryStream(stamped, writable: false), ct);
        var ev = new Evidence
        {
            Id = Guid.NewGuid(), Kind = kind, Name = name, StoragePath = path, StampedPath = stampedPath, ContentType = "image/jpeg", Size = bytes.Length, Lat = meta.Lat, Lng = meta.Lng, AccuracyM = meta.AccuracyM,
            CapturedAt = at, UploadedById = me.Id, UploadedBy = me.Name, Ip = current.Ip, Sha256 = Sha256Hex(bytes), DeviceId = deviceId, IsMock = meta.IsMock,
        };
        db.Evidence.Add(ev);
        return ev;
    }
}
