using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;

namespace Slims.Application.Services;

/// <summary>Binding a phone to a named user (FR-SEC-009) and the mandatory mobile setup.
///
/// 1. The user, signed in on the portal, opens Mobile setup and gets a one-time setup QR (valid a few minutes).
/// 2. The SLIMS Field app scans it and enrols: the phone is bound to that user and gets its own device token.
///    The phone never holds the user's password.
/// 3. The app checks camera, GPS, mock location and the clock, and takes a live test photograph with its fix.
///    The server checks the same things again and, if they pass, activates the phone.
/// A second phone for the same user waits for re-authorisation by an administrator; the old one is then retired.</summary>
public class DeviceService(IAppDbContext db, AuditService audit, UserContext users, ConfigService configs, ITokenService tokens, ICurrentUser current, EvidenceService evidence, NotificationService notify)
{
    const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static string HashToken(string token) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes("device|" + token)));

    public async Task<EnrolmentDto> CreateEnrolmentAsync(EnrolmentRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var targetId = string.IsNullOrWhiteSpace(r.ForUserId) ? me.Id : r.ForUserId.Trim();
        if (targetId != me.Id && !Domain.Rules.AccessPolicy.Can(me, Permissions.Configure)) throw AppException.Forbidden("Only the System Administrator can set up a phone for somebody else.");
        var target = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == targetId && u.Active, ct) ?? throw AppException.NotFound("User");
        var cfg = await configs.GetAsync(ct);
        // the phone must reach the server by an address it can see; "localhost" on a phone is the phone itself
        var server = (string.IsNullOrWhiteSpace(cfg.Field.PhoneServerUrl) ? r.ServerUrl ?? "" : cfg.Field.PhoneServerUrl).Trim().TrimEnd('/');
        if (!Uri.TryCreate(server, UriKind.Absolute, out var su) || su.IsLoopback)
            throw AppException.Bad("Phones cannot reach \"" + server + "\". Open the portal by the office address (for example http://172.16.10.184:3001), or set the phone server address under Settings ▸ Parameters ▸ Mobile field app.");
        var code = new string(Enumerable.Range(0, 8).Select(_ => Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]).ToArray());
        var e = new DeviceEnrolment { Code = code, UserId = target.Id, CreatedById = me.Id, ExpiresAt = DateTime.UtcNow.AddMinutes(Math.Clamp(cfg.Field.EnrolmentMinutes, 2, 60)) };
        db.DeviceEnrolments.Add(e);
        audit.Log("access", "mobile-setup-code", me, "user", target.Id, next: new { e.ExpiresAt });
        await db.SaveChangesAsync(ct);
        var payload = JsonSerializer.Serialize(new { t = "slims-setup", s = server, c = code, u = target.Id });
        return new(code, target.Id, target.Name, DateTime.SpecifyKind(e.ExpiresAt, DateTimeKind.Utc), payload, server);
    }

    /// <summary>Called by the app with the code from the setup QR. No password travels to the phone.</summary>
    public async Task<EnrolResultDto> EnrolAsync(EnrolRequest r, CancellationToken ct)
    {
        var code = (r.Code ?? "").Trim().ToUpperInvariant();
        var e = await db.DeviceEnrolments.FirstOrDefaultAsync(x => x.Code == code, ct);
        if (e is null || e.UsedAt is not null || e.ExpiresAt < DateTime.UtcNow)
        {
            audit.Log("access", "mobile-enrol-refused", null, reason: e is null ? "unknown code" : e.UsedAt is not null ? "code already used" : "code expired", actorIdOverride: "device:" + r.DeviceUid);
            await db.SaveChangesAsync(ct);
            throw new AppException(400, "This setup code is not valid any more. Open Mobile setup on the portal and scan the new code.");
        }
        if (string.IsNullOrWhiteSpace(r.DeviceUid) || r.DeviceUid.Length > 80) throw AppException.Bad("The app did not send a device identifier.");
        var u = await db.Users.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == e.UserId && x.Active, ct) ?? throw AppException.NotFound("User");
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var existing = await db.Devices.FirstOrDefaultAsync(d => d.UserId == u.Id && d.DeviceUid == r.DeviceUid && (d.Status == "Active" || d.Status == "Setup" || d.Status == "AwaitingApproval"), ct);
        var other = await db.Devices.AnyAsync(d => d.UserId == u.Id && d.Status == "Active" && d.DeviceUid != r.DeviceUid, ct);
        var dev = existing ?? new Device { Id = Guid.NewGuid(), UserId = u.Id, DeviceUid = r.DeviceUid, Status = "Setup" };
        if (existing is null) db.Devices.Add(dev);
        dev.Model = r.Model; dev.Manufacturer = r.Manufacturer; dev.OsVersion = r.OsVersion; dev.AppVersion = r.AppVersion; dev.TokenHash = HashToken(token); dev.LastSeenAt = DateTime.UtcNow;
        e.UsedAt = DateTime.UtcNow; e.DeviceId = dev.Id;
        audit.Log("access", existing is null ? "mobile-enrolled" : "mobile-re-enrolled", u, "device", dev.Id.ToString(), next: new { dev.Model, dev.Manufacturer, dev.OsVersion, dev.AppVersion, ReplacesAnother = other });
        await db.SaveChangesAsync(ct);
        return new(dev.Id, token, u.Id, u.Name, u.Role?.Label ?? u.RoleKey, dev.Status, other);
    }

    /// <summary>The app exchanges its device token for a normal access token carrying the device id.</summary>
    public async Task<DeviceSessionDto> SessionAsync(DeviceSessionRequest r, CancellationToken ct)
    {
        var hash = HashToken(r.DeviceToken ?? "");
        var dev = await db.Devices.Include(d => d.User).ThenInclude(u => u!.Role).FirstOrDefaultAsync(d => d.TokenHash == hash, ct);
        if (dev is null || dev.Status is "Revoked" or "Replaced") throw new AppException(401, dev is null ? "This phone is not registered. Run mobile setup again." : $"This phone was {dev.Status.ToLowerInvariant()} by the administrator. Run mobile setup again.");
        if (dev.User is null || !dev.User.Active) throw new AppException(403, "The account bound to this phone is inactive.");
        dev.LastSeenAt = DateTime.UtcNow; if (!string.IsNullOrWhiteSpace(r.AppVersion)) dev.AppVersion = r.AppVersion;
        await db.SaveChangesAsync(ct);
        var (at, exp) = tokens.CreateAccessToken(dev.User, dev.Id);
        var cfg = await configs.GetAsync(ct);
        return new(at, exp, UserContext.ToDto(dev.User), ToDto(dev, dev.User), cfg.Geo, cfg.Field, DateTime.UtcNow);
    }

    public async Task<Device> CurrentDeviceAsync(User me, CancellationToken ct)
    {
        if (current.DeviceId is not Guid id) throw AppException.Forbidden("Use the SLIMS Field app on your registered phone for this.", "FR-SEC-009");
        var dev = await db.Devices.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (dev is null || dev.UserId != me.Id || dev.Status is "Revoked" or "Replaced") throw AppException.Forbidden("This phone is no longer registered to you. Run mobile setup again.", "FR-SEC-009");
        return dev;
    }

    /// <summary>FR-SEC-009 / FR-FLD-007: field evidence only from the user's active, bound phone.</summary>
    public async Task<Device> RequireActiveDeviceAsync(User me, CancellationToken ct)
    {
        var dev = await CurrentDeviceAsync(me, ct);
        if (dev.Status != "Active") throw AppException.Forbidden(dev.Status == "AwaitingApproval" ? "This phone is waiting for the administrator to approve it as your new phone." : "Finish mobile setup on this phone first (test photograph).", "FR-SEC-009");
        return dev;
    }

    /// <summary>Step 3 of setup: the live test photograph with its GPS fix. Every check is repeated on the server.</summary>
    public async Task<SetupCheckDto> SetupCheckAsync(SetupCheckRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var dev = await CurrentDeviceAsync(me, ct);
        var cfg = await configs.GetAsync(ct);
        var checks = new List<SetupCheckItem>();
        var drift = Math.Abs((DateTime.UtcNow - r.DeviceTime.ToUniversalTime()).TotalMinutes);
        checks.Add(new("Phone clock", drift <= cfg.Geo.MaxClockDriftMinutes, drift <= cfg.Geo.MaxClockDriftMinutes ? $"within {Math.Round(drift * 60)} s of server time" : $"{drift:0} minutes off server time — switch on automatic date and time"));
        checks.Add(new("Automatic time", r.AutoTimeEnabled, r.AutoTimeEnabled ? "on" : "switch on Settings ▸ Date & time ▸ Set automatically"));
        checks.Add(new("Real GPS (no mock location)", !r.Photo.IsMock, r.Photo.IsMock ? "a mock-location app is feeding the position — remove it" : "genuine fix"));
        var acc = r.Photo.AccuracyM;
        checks.Add(new("GPS accuracy", acc > 0 && acc <= cfg.Field.SetupMaxAccuracyM, acc > 0 ? $"±{acc:0} m (needs ±{cfg.Field.SetupMaxAccuracyM} m or better — step outside if indoors)" : "no fix"));
        byte[] bytes;
        try { bytes = EvidenceService.Decode(r.Photo.Base64); } catch (FormatException) { bytes = []; }
        var hashOk = bytes.Length > 0 && EvidenceService.Sha256Hex(bytes) == (r.Photo.Sha256 ?? "").ToLowerInvariant();
        checks.Add(new("Camera photograph", bytes.Length > 10_000, bytes.Length > 10_000 ? $"{bytes.Length / 1024} KB captured" : "no photograph received"));
        checks.Add(new("Photograph integrity (SHA-256)", hashOk, hashOk ? "hash matches" : "hash does not match the photograph"));
        var ok = checks.All(c => c.Ok);
        if (ok)
        {
            var ev = await evidence.SaveAsync(me, "setup", Path.Combine("devices", me.Id), bytes, r.Photo, dev.Id, ["SLIMS · MOBILE SETUP", $"{me.Name} ({me.Id})", $"{dev.Manufacturer} {dev.Model}"], ct);
            dev.SetupEvidenceId = ev.Id; dev.SetupLat = r.Photo.Lat; dev.SetupLng = r.Photo.Lng; dev.SetupAccuracyM = acc; dev.SetupCompletedAt = DateTime.UtcNow;
            var other = await db.Devices.Where(d => d.UserId == me.Id && d.Status == "Active" && d.Id != dev.Id).ToListAsync(ct);
            if (dev.Status == "Setup" && other.Count > 0)
            {
                dev.Status = "AwaitingApproval";
                var admins = (await notify.ActiveUsersAsync(ct)).Where(u => Domain.Rules.AccessPolicy.Can(u, Permissions.Configure)).ToList();
                await notify.NotifyAsync(admins, "NTF-DEV", $"New phone for {me.Name} awaiting approval", $"{me.Name} completed setup on a {dev.Manufacturer} {dev.Model}. Approving it retires the phone now in use.", "/access", "device", dev.Id.ToString(), ct);
            }
            else if (dev.Status == "Setup") { dev.Status = "Active"; dev.ActivatedAt = DateTime.UtcNow; }
        }
        audit.Log("access", ok ? "mobile-setup-passed" : "mobile-setup-failed", me, "device", dev.Id.ToString(), next: new { dev.Status, Checks = checks.Where(c => !c.Ok).Select(c => c.Check) });
        await db.SaveChangesAsync(ct);
        return new(ok, checks, ToDto(dev, me));
    }

    public async Task<DeviceDto> ApproveAsync(Guid id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Configure, ct);
        var dev = await db.Devices.Include(d => d.User).ThenInclude(u => u!.Role).FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw AppException.NotFound("Device");
        if (dev.Status != "AwaitingApproval") throw AppException.Bad($"The phone is {dev.Status}; nothing to approve.");
        foreach (var o in await db.Devices.Where(d => d.UserId == dev.UserId && d.Status == "Active" && d.Id != dev.Id).ToListAsync(ct)) { o.Status = "Replaced"; o.RevokedAt = DateTime.UtcNow; o.RevokedById = me.Id; o.Note = "replaced by " + (dev.Model ?? "new phone"); }
        dev.Status = "Active"; dev.ActivatedAt = DateTime.UtcNow; dev.ApprovedById = me.Id;
        audit.Log("access", "mobile-replacement-approved", me, "device", dev.Id.ToString(), next: new { dev.UserId, dev.Model });
        await notify.NotifyAsync(await notify.ByIdsAsync(ct, dev.UserId), "NTF-DEV", "Your new phone is approved", $"{dev.Manufacturer} {dev.Model} is now your SLIMS field phone. The previous phone no longer works.", "/mobile-setup", "device", dev.Id.ToString(), ct, level: 9);
        await db.SaveChangesAsync(ct);
        return ToDto(dev, dev.User!);
    }

    public async Task<DeviceDto> RevokeAsync(Guid id, string? reason, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var dev = await db.Devices.Include(d => d.User).ThenInclude(u => u!.Role).FirstOrDefaultAsync(d => d.Id == id, ct) ?? throw AppException.NotFound("Device");
        if (dev.UserId != me.Id && !Domain.Rules.AccessPolicy.Can(me, Permissions.Configure)) throw AppException.Forbidden("Only the owner or the System Administrator can remove a phone.");
        if (dev.Status is "Revoked" or "Replaced") throw AppException.Bad($"The phone is already {dev.Status}.");
        dev.Status = "Revoked"; dev.RevokedAt = DateTime.UtcNow; dev.RevokedById = me.Id; dev.Note = reason;
        audit.Log("access", "mobile-revoked", me, "device", dev.Id.ToString(), next: new { dev.UserId, dev.Model }, reason: reason);
        await db.SaveChangesAsync(ct);
        return ToDto(dev, dev.User!);
    }

    public async Task<MyMobileDto> MineAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var cfg = await configs.GetAsync(ct);
        var list = await db.Devices.AsNoTracking().Where(d => d.UserId == me.Id).OrderByDescending(d => d.EnrolledAt).ToListAsync(ct);
        var dtos = list.Select(d => ToDto(d, me)).ToList();
        return new(RequiredFor(me, cfg), dtos.FirstOrDefault(d => d.Status == "Active"), dtos, "/api/mobile/app.apk");
    }

    public async Task<List<DeviceDto>> AllAsync(CancellationToken ct)
    {
        await users.RequireAsync(Permissions.Configure, ct);
        var list = await db.Devices.AsNoTracking().Include(d => d.User).ThenInclude(u => u!.Role).OrderBy(d => d.UserId).ThenByDescending(d => d.EnrolledAt).ToListAsync(ct);
        return list.Select(d => ToDto(d, d.User!)).ToList();
    }

    /// <summary>Mobile setup is mandatory for roles that capture field evidence.</summary>
    public static bool RequiredFor(User u, Domain.Config.AppConfig cfg) => cfg.Field.MobileSetupRequired && Domain.Rules.AccessPolicy.Can(u, Permissions.Install) && u.RoleKey != "sysadmin";

    public static DeviceDto ToDto(Device d, User u) => new(d.Id, d.UserId, u.Name, u.Role?.Label ?? u.RoleKey, d.DeviceUid, d.Model, d.Manufacturer, d.OsVersion, d.AppVersion, d.Status,
        DateTime.SpecifyKind(d.EnrolledAt, DateTimeKind.Utc), Utc(d.SetupCompletedAt), Utc(d.ActivatedAt), Utc(d.LastSeenAt), d.SetupLat, d.SetupLng, d.SetupAccuracyM,
        d.SetupEvidenceId is null ? null : $"/api/files/{d.SetupEvidenceId}?variant=stamped", Utc(d.RevokedAt), d.Note);
    static DateTime? Utc(DateTime? d) => d is null ? null : DateTime.SpecifyKind(d.Value, DateTimeKind.Utc);
}
