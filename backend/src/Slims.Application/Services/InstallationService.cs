using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>One photograph as it arrives from the browser (legacy path, off unless Parameters allow web capture).</summary>
public record EvidenceUpload(string Kind, string FileName, string ContentType, long Size, Stream Content, double? Lat, double? Lng, double? AccuracyM, DateTime CapturedAt);
public record InstallationSubmit(string Qr, string? PoleId, double Lat, double Lng, double AccuracyM, DateTime CapturedAt, string? Remarks, bool Offline, string? ReplacesQr, EvidenceUpload Before, EvidenceUpload After);

/// <summary>Phase 4 — geo-tagged installation evidence validated against the site geo-fence (FR-FLD, FR-EXC, section 13).
/// Evidence comes from the SLIMS Field app on the officer's bound phone: live capture only, queued offline with the original
/// capture time, synchronised with a client transaction id, integrity-checked by SHA-256 and stamped on the server.</summary>
public class InstallationService(IAppDbContext db, AuditService audit, UserContext users, ConfigService configs, NumberingService numbers, IFileStorage files, ICurrentUser current,
    DeviceService devices, EvidenceService evidence, NotificationService notify)
{
    /// <summary>The field app's submission (FR-FLD-004 … 013).</summary>
    public async Task<InstallationDto> SubmitMobileAsync(MobileInstallationRequest s, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Install, ct);
        var dev = await devices.RequireActiveDeviceAsync(me, ct);
        // FR-FLD-013: a retried or repeated synchronisation returns the installation created the first time
        var done = await db.Installations.AsNoTracking().FirstOrDefaultAsync(x => x.ClientTxnId == s.ClientTxnId, ct);
        if (done is not null) return await GetAsync(done.Id, ct);
        var cfg = await configs.GetAsync(ct);
        var now = DateTime.UtcNow;
        async Task<AppException> Refuse(string rule, string reason, string message)
        {
            audit.Log("stock", "evidence-refused", me, "item", null, next: new { s.Qr, s.ClientTxnId, Device = dev.Id }, reason: reason);
            await db.SaveChangesAsync(ct);
            return AppException.Rule(rule, message);
        }
        // FR-FLD-011: mock location and clock drift are refused and logged
        if (s.Before.IsMock || s.After.IsMock) throw await Refuse("FR-FLD-011", "mock location", "The phone reported a mock (fake) location. Remove any location-faking app and capture again.");
        var drift = Math.Abs((now - s.DeviceTime.ToUniversalTime()).TotalMinutes);
        if (drift > cfg.Geo.MaxClockDriftMinutes) throw await Refuse("FR-FLD-011", $"clock drift {drift:0} min", $"The phone clock is {drift:0} minutes off server time (limit {cfg.Geo.MaxClockDriftMinutes}). Switch on automatic date and time.");
        if (s.QueuedAt is DateTime q && (now - q.ToUniversalTime()).TotalDays > cfg.Field.OfflineRetentionDays) throw await Refuse("FR-FLD-012", "offline too long", $"This capture was queued more than {cfg.Field.OfflineRetentionDays} days ago and can no longer be accepted. Capture it again.");
        if (s.After.CapturedAt < s.Before.CapturedAt) throw AppException.Rule("FR-IN-06", "The AFTER photograph was taken before the BEFORE photograph.");

        if (!Codes.TryParse(s.Qr, out _, out _)) throw AppException.Rule("BR-002", "Not a SLIMS label.");
        var item = await Map.Items(db).FirstOrDefaultAsync(i => i.QrCode == s.Qr.Trim(), ct) ?? throw AppException.NotFound("Item");
        var site = item.Site ?? throw AppException.Rule("BR-012", "This item has not been issued to any site.");
        UserContext.RequireSite(me, site);
        if (item.Status is not (ItemStatus.Issued or ItemStatus.AtSite or ItemStatus.PendingVerification)) throw AppException.Rule("BR-013", $"Item is {item.Status}; only material issued to a site can be installed.");

        var bytesBefore = SafeDecode(s.Before.Base64); var bytesAfter = SafeDecode(s.After.Base64);
        foreach (var (b, kind) in new[] { (bytesBefore, "BEFORE"), (bytesAfter, "AFTER") })
        {
            if (b.Length == 0) throw AppException.Rule("BR-013", $"The {kind} photograph is missing.");
            if (b.Length > cfg.Controls.EvidenceMaxMB * 1024L * 1024) throw AppException.Bad($"{kind} photograph exceeds {cfg.Controls.EvidenceMaxMB} MB.");
        }
        // FR-FLD-004 / 009: a photograph whose bytes do not match the hash taken on the phone is Missing Evidence
        var intact = EvidenceService.Sha256Hex(bytesBefore) == (s.Before.Sha256 ?? "").ToLowerInvariant() && EvidenceService.Sha256Hex(bytesAfter) == (s.After.Sha256 ?? "").ToLowerInvariant();
        var accuracy = Math.Max(s.Before.AccuracyM, s.After.AccuracyM);
        if (intact && s.After.AccuracyM > cfg.Geo.MinAccuracyM * 3) throw AppException.Rule("FR-FLD-010", $"GPS accuracy ±{s.After.AccuracyM:0} m is too poor (limit ±{cfg.Geo.MinAccuracyM * 3} m). Wait for a better fix and capture again.");

        await SupersedeAsync(me, item, ct);
        var number = await numbers.NextAsync("INST", site.Code, ct, 5, fy: false);
        var folder = Path.Combine("sites", site.Code);
        var context = new[] { $"Site {site.Code} · {Short(site.Name, 34)}", $"{item.Article!.ItemCode} #{item.Serial}{(string.IsNullOrWhiteSpace(s.PoleId) ? "" : " · Pole " + s.PoleId)}", $"{me.Name} · {number}" };
        var before = await evidence.SaveAsync(me, "before", folder, bytesBefore, s.Before, dev.Id, ["SLIMS · BEFORE INSTALLATION", .. context], ct);
        var after = await evidence.SaveAsync(me, "after", folder, bytesAfter, s.After, dev.Id, ["SLIMS · AFTER INSTALLATION", .. context], ct);
        var distance = Geo.DistanceM(s.After.Lat, s.After.Lng, site.Lat, site.Lng);
        var inside = distance <= site.GeofenceM + cfg.Geo.ToleranceM;
        var inst = new Installation
        {
            Number = number, ItemId = item.Id, SiteId = site.Id, OfficerId = me.Id, Officer = me.Name, PoleId = s.PoleId, BeforeEvidenceId = before.Id, AfterEvidenceId = after.Id,
            Lat = s.After.Lat, Lng = s.After.Lng, AccuracyM = accuracy, DistanceM = distance, Remarks = s.Remarks, CapturedAt = s.After.CapturedAt.ToUniversalTime(), SubmittedAt = now, Offline = s.Offline,
            ClientTxnId = s.ClientTxnId, DeviceId = dev.Id, QueuedAt = s.QueuedAt?.ToUniversalTime(), DeviceTime = s.DeviceTime.ToUniversalTime(),
            GeoStatus = !intact ? GeoStatus.MissingEvidence : inside ? GeoStatus.Confirmed : GeoStatus.PendingVerification,
        };
        if (!string.IsNullOrWhiteSpace(s.ReplacesQr)) inst.ReplacesItemId = (await db.Items.FirstOrDefaultAsync(i => i.QrCode == s.ReplacesQr.Trim(), ct))?.Id;
        db.Installations.Add(inst);
        await db.SaveChangesAsync(ct);
        await ApplyOutcomeAsync(me, item, site, inst, cfg.Geo.ToleranceM, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(inst.Id, ct);
    }

    /// <summary>Browser upload (multipart). Kept for installations recorded at the office from photographs taken elsewhere, but
    /// refused unless Parameters ▸ Field allows it, because a browser cannot guarantee live capture (FR-FLD-007).</summary>
    public async Task<InstallationDto> SubmitAsync(InstallationSubmit s, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Install, ct);
        var cfg = await configs.GetAsync(ct);
        if (!cfg.Field.AllowWebCapture) throw AppException.Rule("FR-FLD-007", "Installation photographs are accepted only from the SLIMS Field app on your registered phone (live capture). Open Mobile setup to install it.");
        if (!Codes.TryParse(s.Qr, out _, out _)) throw AppException.Rule("BR-002", "Not a SLIMS label.");
        var item = await Map.Items(db).FirstOrDefaultAsync(i => i.QrCode == s.Qr.Trim(), ct) ?? throw AppException.NotFound("Item");
        var site = item.Site ?? throw AppException.Rule("BR-012", "This item has not been issued to any site.");
        UserContext.RequireSite(me, site);
        if (item.Status is not (ItemStatus.Issued or ItemStatus.AtSite or ItemStatus.PendingVerification)) throw AppException.Rule("BR-013", $"Item is {item.Status}; only material issued to a site can be installed.");
        foreach (var e in new[] { s.Before, s.After })
        {
            if (e.Size <= 0) throw AppException.Rule("BR-013", $"The {e.Kind.ToUpperInvariant()} photograph is missing.");
            if (e.Size > cfg.Controls.EvidenceMaxMB * 1024L * 1024) throw AppException.Bad($"{e.Kind} photograph exceeds {cfg.Controls.EvidenceMaxMB} MB.");
            if (!e.ContentType.StartsWith("image/")) throw AppException.Bad("Evidence must be a photograph.");
            if (e.Lat is null || e.Lng is null) throw AppException.Rule("BR-016", $"The {e.Kind.ToUpperInvariant()} photograph carries no coordinates. Enable location and capture it again.");
        }
        if (s.AccuracyM > cfg.Geo.MinAccuracyM * 3) throw AppException.Rule("FR-IN-13", $"GPS accuracy ±{s.AccuracyM:0} m is too poor (limit ±{cfg.Geo.MinAccuracyM * 3} m). Wait for a better fix and capture again.");
        // DF-02: the permitted drift is the configured limit, not the limit plus an hour
        if (!s.Offline && Math.Abs((DateTime.UtcNow - s.CapturedAt.ToUniversalTime()).TotalMinutes) > cfg.Geo.MaxClockDriftMinutes) throw AppException.Rule("FR-IN-13", "The device clock differs from server time by more than the permitted drift. Set the phone to automatic time.");
        if (s.After.CapturedAt < s.Before.CapturedAt) throw AppException.Rule("FR-IN-06", "The AFTER photograph was taken before the BEFORE photograph.");

        await SupersedeAsync(me, item, ct);
        var number = await numbers.NextAsync("INST", site.Code, ct, 5, fy: false);
        var folder = Path.Combine("sites", site.Code);
        var context = new[] { $"Site {site.Code} · {Short(site.Name, 34)}", $"{item.Article!.ItemCode} #{item.Serial}", $"{me.Name} · {number} · web upload" };
        async Task<Evidence> Save(EvidenceUpload e)
        {
            using var ms = new MemoryStream(); await e.Content.CopyToAsync(ms, ct);
            return await evidence.SaveAsync(me, e.Kind, folder, ms.ToArray(), new MobilePhoto("", "", e.Lat ?? s.Lat, e.Lng ?? s.Lng, e.AccuracyM ?? s.AccuracyM, e.CapturedAt, false), null, ["SLIMS · " + e.Kind.ToUpperInvariant(), .. context], ct);
        }
        var before = await Save(s.Before); var after = await Save(s.After);
        var distance = Geo.DistanceM(s.After.Lat ?? s.Lat, s.After.Lng ?? s.Lng, site.Lat, site.Lng);
        var inside = distance <= site.GeofenceM + cfg.Geo.ToleranceM;
        var inst = new Installation
        {
            Number = number, ItemId = item.Id, SiteId = site.Id, OfficerId = me.Id, Officer = me.Name, PoleId = s.PoleId, BeforeEvidenceId = before.Id, AfterEvidenceId = after.Id,
            Lat = s.After.Lat ?? s.Lat, Lng = s.After.Lng ?? s.Lng, AccuracyM = s.AccuracyM, DistanceM = distance, GeoStatus = inside ? GeoStatus.Confirmed : GeoStatus.PendingVerification, Remarks = s.Remarks, CapturedAt = s.CapturedAt, Offline = s.Offline,
        };
        if (!string.IsNullOrWhiteSpace(s.ReplacesQr)) inst.ReplacesItemId = (await db.Items.FirstOrDefaultAsync(i => i.QrCode == s.ReplacesQr.Trim(), ct))?.Id;
        db.Installations.Add(inst);
        await db.SaveChangesAsync(ct);
        await ApplyOutcomeAsync(me, item, site, inst, cfg.Geo.ToleranceM, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(inst.Id, ct);
    }

    static byte[] SafeDecode(string? b64) { try { return EvidenceService.Decode(b64 ?? ""); } catch (FormatException) { return []; } }
    static string Short(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    /// <summary>An earlier attempt still awaiting verification is superseded by the new capture.</summary>
    async Task SupersedeAsync(User me, Item item, CancellationToken ct)
    {
        var earlier = await db.Installations.Where(x => x.ItemId == item.Id && (x.GeoStatus == GeoStatus.PendingVerification || x.GeoStatus == GeoStatus.MissingEvidence)).ToListAsync(ct);
        foreach (var old in earlier) { old.GeoStatus = GeoStatus.Rejected; old.Justification = "Superseded by re-captured evidence"; old.VerifiedAt = DateTime.UtcNow; old.VerifiedById = me.Id; old.VerifiedBy = me.Name; }
    }

    async Task ApplyOutcomeAsync(User me, Item item, Site site, Installation inst, int tolerance, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        item.InstallationId = inst.Id; item.UpdatedAt = now; item.UpdatedBy = me.Id;
        var distance = Math.Round(inst.DistanceM, 1);
        switch (inst.GeoStatus)
        {
            case GeoStatus.Confirmed:
                item.Status = ItemStatus.Installed; item.InstalledAt = inst.CapturedAt;
                audit.Log("stock", "installed", me, "item", item.Id.ToString(), next: new { inst.Number, Site = site.Code, inst.PoleId, Distance = distance, inst.Lat, inst.Lng, inst.Offline });
                break;
            case GeoStatus.PendingVerification:
                item.Status = ItemStatus.PendingVerification;
                audit.Log("stock", "geo-exception", me, "item", item.Id.ToString(), next: new { inst.Number, Site = site.Code, Distance = distance, Allowed = site.GeofenceM + tolerance }, reason: "evidence outside site geo-fence");
                // NTF-08: the Monitoring Officer reviews, the site supervisor is copied
                await notify.NotifyAsync([.. await notify.RoleAsync("pdoffice", ct, site: site), .. await notify.SiteRoleAsync(site, "supervisor", ct)], "NTF-08", $"GPS exception at {site.Code}",
                    $"{inst.Number}: {item.Article!.ItemCode} #{item.Serial} captured {distance:0} m from the site point (allowed {site.GeofenceM + tolerance} m) by {me.Name}.", "/field?inst=" + inst.Id, "installation", inst.Number, ct);
                break;
            case GeoStatus.MissingEvidence:
                item.Status = ItemStatus.AtSite; item.InstallationId = null;
                audit.Log("stock", "missing-evidence", me, "item", item.Id.ToString(), next: new { inst.Number, Site = site.Code }, reason: "photograph failed its integrity check after synchronisation");
                await notify.NotifyAsync(await notify.ByIdsAsync(ct, me.Id), "NTF-09", $"Re-capture required at {site.Code}", $"{inst.Number}: the photographs of {item.Article!.ItemCode} #{item.Serial} failed the integrity check. Capture them again.", "/field", "installation", inst.Number, ct);
                break;
        }
        audit.Log("stock", inst.GeoStatus switch { GeoStatus.Confirmed => "installation-confirmed", GeoStatus.MissingEvidence => "installation-missing-evidence", _ => "installation-pending" }, me, "installation", inst.Number,
            next: new { Item = item.Article!.ItemCode, item.Serial, Site = site.Code, Distance = distance, inst.Offline, inst.QueuedAt, SyncedAt = inst.SubmittedAt, inst.ClientTxnId });
    }

    public async Task<InstallationDto> GetAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var x = await Map.Installations(db).AsNoTracking().FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw AppException.NotFound("Installation");
        UserContext.RequireSite(me, x.Site!);
        return Map.Installation(x);
    }

    public async Task<List<InstallationDto>> ListAsync(int? siteId, string? geoStatus, string? officerId, DateTime? from, DateTime? to, string? zone, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var q = Map.Installations(db).AsNoTracking().AsQueryable();
        if (siteId is not null) q = q.Where(i => i.SiteId == siteId);
        if (!string.IsNullOrEmpty(geoStatus) && Enum.TryParse<GeoStatus>(geoStatus, true, out var gs)) q = q.Where(i => i.GeoStatus == gs);
        if (!string.IsNullOrEmpty(officerId)) q = q.Where(i => i.OfficerId == officerId);
        if (from is not null) q = q.Where(i => i.SubmittedAt >= from);
        if (to is not null) q = q.Where(i => i.SubmittedAt < to.Value.AddDays(1));
        if (!string.IsNullOrEmpty(zone)) q = q.Where(i => i.Site!.Zone == zone);
        var rows = await q.OrderByDescending(i => i.Id).Take(1000).ToListAsync(ct);
        return rows.Where(x => AccessPolicy.SiteVisible(me, x.Site!)).Select(Map.Installation).ToList();
    }

    /// <summary>FR-IN-10 / BR-017: approve the deviation with a written justification, or send it back for re-capture.</summary>
    public async Task<InstallationDto> VerifyAsync(int id, VerifyRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Verify, ct);
        var x = await Map.Installations(db).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw AppException.NotFound("Installation");
        UserContext.RequireSite(me, x.Site!);
        if (x.GeoStatus != GeoStatus.PendingVerification) throw AppException.Bad($"Installation is {x.GeoStatus}; nothing to verify.");
        if (x.OfficerId == me.Id) throw AppException.Rule("BR-026", "You cannot verify your own installation.");
        var item = x.Item!;
        x.VerifiedById = me.Id; x.VerifiedBy = me.Name; x.VerifiedAt = DateTime.UtcNow; x.Justification = r.Justification;
        if (r.Approve)
        {
            if (string.IsNullOrWhiteSpace(r.Justification) || r.Justification.Trim().Length < 10) throw AppException.Rule("BR-017", "A written justification (at least 10 characters) is required to accept evidence outside the geo-fence.");
            x.GeoStatus = GeoStatus.ApprovedException; item.Status = ItemStatus.Installed; item.InstalledAt = x.CapturedAt;
            audit.Log("stock", "installed", me, "item", item.Id.ToString(), next: new { x.Number, Exception = true, Distance = Math.Round(x.DistanceM, 1) }, reason: r.Justification);
            audit.Log("stock", "exception-approved", me, "installation", x.Number, reason: r.Justification);
        }
        else
        {
            x.GeoStatus = GeoStatus.Rejected; item.Status = ItemStatus.AtSite; item.InstallationId = null;
            audit.Log("stock", "exception-rejected", me, "installation", x.Number, reason: r.Justification ?? "re-capture required");
            audit.Log("stock", "recapture-required", me, "item", item.Id.ToString(), reason: r.Justification);
            await notify.NotifyAsync(await notify.ByIdsAsync(ct, x.OfficerId), "NTF-09", $"Re-capture required at {x.Site!.Code}", $"{x.Number} ({item.Article!.ItemCode} #{item.Serial}) was sent back by {me.Name}: {r.Justification}", "/field", "installation", x.Number, ct);
        }
        item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = me.Id;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>FR-EXC-007: the reviewer asks the Lighting Supervisor to verify the installation on the ground.</summary>
    public async Task<InstallationDto> RequestFieldVerificationAsync(int id, FieldVerifyRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Verify, ct);
        var x = await Map.Installations(db).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw AppException.NotFound("Installation");
        UserContext.RequireSite(me, x.Site!);
        if (x.GeoStatus != GeoStatus.PendingVerification) throw AppException.Bad($"Installation is {x.GeoStatus}; field verification applies to open exceptions.");
        if (x.FvRequestedAt is not null && x.FvFindingsAt is null) throw AppException.Bad("Field verification has already been requested and is awaiting findings.");
        var sup = (await notify.SiteRoleAsync(x.Site!, "supervisor", ct)).Where(u => u.Id != x.OfficerId).ToList();
        if (sup.Count == 0) throw AppException.Bad($"No Lighting Supervisor covers site {x.Site!.Code}.");
        x.FvRequestedById = me.Id; x.FvRequestedBy = me.Name; x.FvRequestedAt = DateTime.UtcNow; x.FvRequestNote = r.Note; x.FvAssignedToId = sup[0].Id;
        x.FvFindings = null; x.FvFindingsAt = null; x.FvFindingsBy = null; x.FvEvidenceIds = null;
        audit.Log("stock", "field-verification-requested", me, "installation", x.Number, next: new { AssignedTo = sup[0].Id }, reason: r.Note);
        await notify.NotifyAsync(sup, "NTF-10", $"Field verification requested at {x.Site!.Code}", $"{me.Name} asks you to verify {x.Number} ({x.Item!.Article!.ItemCode} #{x.Item.Serial}, {x.DistanceM:0} m from the site point). {r.Note}", "/field?inst=" + x.Id, "installation", x.Number, ct);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>The supervisor's findings, with live photographs from their bound phone, recorded on the exception.</summary>
    public async Task<InstallationDto> SubmitFindingsAsync(int id, FindingsRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Verify, ct);
        var x = await Map.Installations(db).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw AppException.NotFound("Installation");
        UserContext.RequireSite(me, x.Site!);
        if (x.FvRequestedAt is null) throw AppException.Bad("No field verification was requested for this installation.");
        if (x.FvFindingsAt is not null) throw AppException.Bad("Findings have already been recorded.");
        if (x.OfficerId == me.Id) throw AppException.Rule("BR-026", "You cannot verify your own installation.");
        if (string.IsNullOrWhiteSpace(r.Findings) || r.Findings.Trim().Length < 10) throw AppException.Bad("Describe what you found (at least 10 characters).");
        var ids = new List<Guid>();
        if (r.Photos is { Count: > 0 })
        {
            var dev = await devices.RequireActiveDeviceAsync(me, ct);
            var cfg = await configs.GetAsync(ct);
            if (Math.Abs((DateTime.UtcNow - r.DeviceTime.ToUniversalTime()).TotalMinutes) > cfg.Geo.MaxClockDriftMinutes) throw AppException.Rule("FR-FLD-011", "The phone clock is off server time. Switch on automatic date and time.");
            foreach (var p in r.Photos.Take(4))
            {
                if (p.IsMock) throw AppException.Rule("FR-FLD-011", "The phone reported a mock location.");
                var bytes = SafeDecode(p.Base64);
                if (bytes.Length == 0 || EvidenceService.Sha256Hex(bytes) != (p.Sha256 ?? "").ToLowerInvariant()) throw AppException.Rule("FR-FLD-009", "A verification photograph failed its integrity check. Take it again.");
                var d = Geo.DistanceM(p.Lat, p.Lng, x.Site!.Lat, x.Site.Lng);
                var ev = await evidence.SaveAsync(me, "verification", Path.Combine("sites", x.Site.Code), bytes, p, dev.Id, ["SLIMS · FIELD VERIFICATION", $"Site {x.Site.Code} · {x.Number}", $"{me.Name} · {d:0} m from site point"], ct);
                ids.Add(ev.Id);
            }
        }
        x.FvFindings = r.Findings.Trim(); x.FvFindingsBy = me.Name; x.FvFindingsAt = DateTime.UtcNow; x.FvEvidenceIds = ids.Count == 0 ? null : string.Join(',', ids);
        audit.Log("stock", "field-verification-findings", me, "installation", x.Number, next: new { Photos = ids.Count }, reason: x.FvFindings);
        await notify.NotifyAsync(await notify.ByIdsAsync(ct, x.FvRequestedById), "NTF-10", $"Field verification done at {x.Site!.Code}", $"{me.Name}: {x.FvFindings}", "/field?inst=" + x.Id, "installation", x.Number, ct, level: 9);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<(Stream stream, string contentType, string name)?> OpenEvidenceAsync(Guid id, bool stamped, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var e = await db.Evidence.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (e is null) return null;
        var path = stamped && e.StampedPath is not null ? e.StampedPath : e.StoragePath;
        var s = files.Open(path);
        return s is null ? null : (s, e.ContentType, (stamped && e.StampedPath is not null ? "stamped_" : "") + e.Name);
    }
}
