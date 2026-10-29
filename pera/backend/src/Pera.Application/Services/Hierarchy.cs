using Microsoft.EntityFrameworkCore;
using Pera.Application.Common;
using Pera.Application.Dtos;
using Pera.Application.Interfaces;
using Pera.Domain.Entities;
using Pera.Domain.Rules;

namespace Pera.Application.Services;

/// <summary>The reporting hierarchy and acting arrangements, loaded once per request.
/// Everything that asks "may this person see / act on this?" goes through here (SR-SEC-02).</summary>
public class HierarchyService(IAppDbContext db)
{
    Dictionary<string, User>? _users;
    Dictionary<string, List<string>>? _children;
    List<Delegation>? _delegations;

    public async Task<Dictionary<string, User>> UsersAsync(CancellationToken ct = default)
    {
        if (_users is not null) return _users;
        _users = await db.Users.AsNoTracking().Include(u => u.Designation).Include(u => u.OrgUnit).ToDictionaryAsync(u => u.Id, ct);
        _children = _users.Values.Where(u => u.ReportsToId != null).GroupBy(u => u.ReportsToId!).ToDictionary(g => g.Key, g => g.Select(u => u.Id).ToList());
        return _users;
    }
    public void Invalidate() { _users = null; _children = null; _delegations = null; }

    public async Task<string> NameAsync(string? id, CancellationToken ct = default)
        => id is null ? "—" : (await UsersAsync(ct)).TryGetValue(id, out var u) ? u.Name : id;

    /// <summary>Everybody below the user, at every level.</summary>
    public async Task<HashSet<string>> SubordinatesAsync(string userId, CancellationToken ct = default)
    {
        await UsersAsync(ct);
        var result = new HashSet<string>();
        var stack = new Stack<string>(); stack.Push(userId);
        while (stack.Count > 0)
            foreach (var c in _children!.GetValueOrDefault(stack.Pop()) ?? [])
                if (c != userId && result.Add(c)) stack.Push(c);
        return result;
    }

    /// <summary>Reporting officer, their reporting officer, and so on upwards.</summary>
    public async Task<List<string>> ChainAboveAsync(string userId, CancellationToken ct = default)
    {
        var users = await UsersAsync(ct);
        var chain = new List<string>();
        var cur = users.GetValueOrDefault(userId)?.ReportsToId;
        while (cur is not null && !chain.Contains(cur) && chain.Count < 50) { chain.Add(cur); cur = users.GetValueOrDefault(cur)?.ReportsToId; }
        return chain;
    }

    public async Task<List<Delegation>> DelegationsAsync(CancellationToken ct = default)
        => _delegations ??= await db.Delegations.AsNoTracking().Where(d => d.RevokedAt == null).ToListAsync(ct);

    /// <summary>The user plus everyone who has delegated the needed power to them today (FR-027).</summary>
    public async Task<HashSet<string>> PrincipalsAsync(string userId, DelegationScope scope, int? documentTypeId = null, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var result = new HashSet<string> { userId };
        foreach (var d in await DelegationsAsync(ct))
            if (d.ToUserId == userId && d.CoversDate(now) && d.Covers(scope) && (d.DocumentTypeId is null || documentTypeId is null || d.DocumentTypeId == documentTypeId))
                result.Add(d.FromUserId);
        return result;
    }

    /// <summary>People whose work this user may see: themselves, the officers they act for, and everyone below any of them.</summary>
    public async Task<HashSet<string>> VisibleOwnersAsync(string userId, CancellationToken ct = default)
    {
        var principals = await PrincipalsAsync(userId, DelegationScope.All, null, ct);
        // any acting arrangement, whatever its scope, lets the acting officer see the principal's work
        foreach (var d in await DelegationsAsync(ct)) if (d.ToUserId == userId && d.CoversDate(DateTime.UtcNow)) principals.Add(d.FromUserId);
        var result = new HashSet<string>(principals);
        foreach (var p in principals) result.UnionWith(await SubordinatesAsync(p, ct));
        return result;
    }

    /// <summary>Does the officer's designation hold this power for this document type today (authority matrix)?</summary>
    public async Task<bool> HasAuthorityAsync(string userId, int documentTypeId, AuthorityKind kind, CancellationToken ct = default)
    {
        var users = await UsersAsync(ct);
        if (!users.TryGetValue(userId, out var u) || u.DesignationId is null || !u.Active) return false;
        var now = DateTime.UtcNow;
        var rules = await db.AuthorityRules.AsNoTracking().Where(r => r.DesignationId == u.DesignationId && r.Authority == kind && (r.DocumentTypeId == null || r.DocumentTypeId == documentTypeId)).ToListAsync(ct);
        return rules.Any(r => r.InForce(now));
    }

    /// <summary>The officer an approval would normally go to: the nearest holder of the designation, searching the
    /// initiator's own unit first and then each unit above it (default routing, FR-002).</summary>
    public async Task<string?> ResolveDesignationAsync(int designationId, string initiatorId, CancellationToken ct = default)
    {
        var users = await UsersAsync(ct);
        var holders = users.Values.Where(u => u.Active && u.DesignationId == designationId).ToList();
        if (holders.Count == 0) return null;
        if (holders.Count == 1) return holders[0].Id;
        var chain = await ChainAboveAsync(initiatorId, ct);
        var inChain = chain.FirstOrDefault(id => holders.Any(h => h.Id == id));
        if (inChain is not null) return inChain;
        var units = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(o => o.Id, o => o.ParentId, ct);
        var unit = users.GetValueOrDefault(initiatorId)?.OrgUnitId;
        var guard = 0;
        while (unit is int uid && guard++ < 50)
        {
            var h = holders.FirstOrDefault(x => x.OrgUnitId == uid);
            if (h is not null) return h.Id;
            unit = units.GetValueOrDefault(uid);
        }
        return null;
    }
}

/// <summary>Offices, designations, acting arrangements, task categories and the user directory.</summary>
public class OrganizationService(IAppDbContext db, UserContext users, AuditService audit, HierarchyService hierarchy)
{
    public async Task<List<UserRefDto>> DirectoryAsync(bool includeInactive, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var all = await hierarchy.UsersAsync(ct);
        return all.Values.Where(u => includeInactive || u.Active).OrderBy(u => u.Designation?.Rank ?? 99).ThenBy(u => u.Name)
            .Select(u => new UserRefDto(u.Id, u.Name, u.Designation?.Title, u.OrgUnit?.Name, u.ReportsToId, u.Active)).ToList();
    }

    /// <summary>The people this user may assign tasks to: themselves, everyone below them, and everyone below the officers they act for.</summary>
    public async Task<List<UserRefDto>> AssignableAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var ids = new HashSet<string> { me.Id };
        foreach (var p in await hierarchy.PrincipalsAsync(me.Id, DelegationScope.Tasks, null, ct)) { ids.Add(p); ids.UnionWith(await hierarchy.SubordinatesAsync(p, ct)); }
        var all = await hierarchy.UsersAsync(ct);
        return ids.Select(id => all.GetValueOrDefault(id)).Where(u => u is { Active: true }).OrderBy(u => u!.Designation?.Rank ?? 99).ThenBy(u => u!.Name)
            .Select(u => new UserRefDto(u!.Id, u.Name, u.Designation?.Title, u.OrgUnit?.Name, u.ReportsToId, u.Active)).ToList();
    }

    // ---- units --------------------------------------------------------------------------------------------
    public async Task<List<OrgUnitDto>> UnitsAsync(CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var units = await db.OrgUnits.AsNoTracking().OrderBy(o => o.Code).ToListAsync(ct);
        var all = await hierarchy.UsersAsync(ct);
        var byId = units.ToDictionary(u => u.Id);
        return units.Select(o => new OrgUnitDto(o.Id, o.Code, o.Name, o.Kind, o.ParentId, o.ParentId is int p && byId.TryGetValue(p, out var pu) ? pu.Name : null,
            o.HeadUserId, o.HeadUserId is null ? null : all.GetValueOrDefault(o.HeadUserId)?.Name, o.EffectiveFrom, o.EffectiveTo, o.Active, all.Values.Count(u => u.OrgUnitId == o.Id && u.Active))).ToList();
    }
    public async Task<OrgUnitDto> SaveUnitAsync(OrgUnitUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Configure, ct);
        if (string.IsNullOrWhiteSpace(r.Code) || string.IsNullOrWhiteSpace(r.Name)) throw AppException.Bad("Code and name are required.");
        if (r.Kind is not ("Office" or "Wing" or "Section")) throw AppException.Bad("Kind must be Office, Wing or Section.");
        var o = r.Id is int id ? await db.OrgUnits.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Unit") : new OrgUnit();
        if (r.ParentId is int pid)
        {
            var parents = await db.OrgUnits.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.ParentId, ct);
            if (!parents.ContainsKey(pid)) throw AppException.Bad("Unknown parent unit.");
            int? cur = pid; var guard = 0;
            while (cur is int c && guard++ < 100) { if (o.Id != 0 && c == o.Id) throw AppException.Bad("That parent would create a loop."); cur = parents.GetValueOrDefault(c); }
        }
        if (await db.OrgUnits.AnyAsync(x => x.Code == r.Code.Trim() && x.Id != o.Id, ct)) throw AppException.Bad($"Unit code {r.Code} is already used.");
        var prev = o.Id == 0 ? null : new { o.Code, o.Name, o.Kind, o.ParentId, o.HeadUserId, o.Active };
        o.Code = r.Code.Trim().ToUpperInvariant(); o.Name = r.Name.Trim(); o.Kind = r.Kind; o.ParentId = r.ParentId; o.HeadUserId = string.IsNullOrWhiteSpace(r.HeadUserId) ? null : r.HeadUserId;
        o.EffectiveFrom = (r.EffectiveFrom ?? o.EffectiveFrom).Date; o.EffectiveTo = r.EffectiveTo?.Date; o.Active = r.Active;
        if (o.Id == 0) db.OrgUnits.Add(o);
        await db.SaveChangesAsync(ct);
        audit.Log("config", prev is null ? "unit-created" : "unit-updated", me, "orgunit", o.Id.ToString(), prev, new { o.Code, o.Name, o.Kind, o.ParentId, o.HeadUserId, o.Active });
        await db.SaveChangesAsync(ct);
        hierarchy.Invalidate();
        return (await UnitsAsync(ct)).First(x => x.Id == o.Id);
    }

    // ---- designations -------------------------------------------------------------------------------------
    public async Task<List<DesignationDto>> DesignationsAsync(CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var all = await hierarchy.UsersAsync(ct);
        return (await db.Designations.AsNoTracking().OrderBy(d => d.Rank).ThenBy(d => d.Title).ToListAsync(ct))
            .Select(d => new DesignationDto(d.Id, d.Code, d.Title, d.Rank, d.Active, all.Values.Count(u => u.DesignationId == d.Id && u.Active))).ToList();
    }
    public async Task<DesignationDto> SaveDesignationAsync(DesignationUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Configure, ct);
        if (string.IsNullOrWhiteSpace(r.Code) || string.IsNullOrWhiteSpace(r.Title)) throw AppException.Bad("Code and title are required.");
        var d = r.Id is int id ? await db.Designations.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Designation") : new Designation();
        if (await db.Designations.AnyAsync(x => x.Code == r.Code.Trim() && x.Id != d.Id, ct)) throw AppException.Bad($"Designation code {r.Code} is already used.");
        var prev = d.Id == 0 ? null : new { d.Code, d.Title, d.Rank, d.Active };
        d.Code = r.Code.Trim().ToUpperInvariant(); d.Title = r.Title.Trim(); d.Rank = r.Rank; d.Active = r.Active;
        if (d.Id == 0) db.Designations.Add(d);
        await db.SaveChangesAsync(ct);
        audit.Log("config", prev is null ? "designation-created" : "designation-updated", me, "designation", d.Id.ToString(), prev, new { d.Code, d.Title, d.Rank, d.Active });
        await db.SaveChangesAsync(ct);
        return (await DesignationsAsync(ct)).First(x => x.Id == d.Id);
    }

    // ---- task categories ----------------------------------------------------------------------------------
    public async Task<List<CategoryDto>> CategoriesAsync(CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        return await db.TaskCategories.AsNoTracking().OrderBy(c => c.Name).Select(c => new CategoryDto(c.Id, c.Name, c.Active)).ToListAsync(ct);
    }
    public async Task<CategoryDto> SaveCategoryAsync(CategoryDto r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Configure, ct);
        if (string.IsNullOrWhiteSpace(r.Name)) throw AppException.Bad("Name is required.");
        var c = r.Id > 0 ? await db.TaskCategories.FirstOrDefaultAsync(x => x.Id == r.Id, ct) ?? throw AppException.NotFound("Category") : new TaskCategory();
        c.Name = r.Name.Trim(); c.Active = r.Active;
        if (c.Id == 0) db.TaskCategories.Add(c);
        await db.SaveChangesAsync(ct);
        audit.Log("config", "category-saved", me, "category", c.Id.ToString(), next: new { c.Name, c.Active });
        await db.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name, c.Active);
    }

    // ---- delegations (acting officers) -------------------------------------------------------------------
    public async Task<List<DelegationDto>> DelegationsAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var all = await hierarchy.UsersAsync(ct);
        var q = db.Delegations.AsNoTracking().AsQueryable();
        // administrators and auditors see every arrangement; others see the ones they are party to
        if (!AccessPolicy.Can(me, Permissions.Configure) && !AccessPolicy.Can(me, Permissions.Audit)) q = q.Where(d => d.FromUserId == me.Id || d.ToUserId == me.Id);
        var types = await db.DocumentTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => t.Name, ct);
        var now = DateTime.UtcNow;
        return (await q.OrderByDescending(d => d.Id).ToListAsync(ct)).Select(d => new DelegationDto(d.Id, d.FromUserId, all.GetValueOrDefault(d.FromUserId)?.Name ?? d.FromUserId,
            d.ToUserId, all.GetValueOrDefault(d.ToUserId)?.Name ?? d.ToUserId, d.Scope.ToString(), d.DocumentTypeId, d.DocumentTypeId is int t ? types.GetValueOrDefault(t) : null,
            d.StartsOn, d.EndsOn, d.Reason, d.CreatedById, d.CreatedAt, d.RevokedAt,
            d.RevokedAt is not null ? "Revoked" : now.Date > d.EndsOn.Date ? "Expired" : now.Date < d.StartsOn.Date ? "Scheduled" : "Active")).ToList();
    }

    public async Task<DelegationDto> CreateDelegationAsync(DelegationRequest r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Configure, ct);
        var all = await hierarchy.UsersAsync(ct);
        if (!all.TryGetValue(r.FromUserId, out var from) || !all.TryGetValue(r.ToUserId, out var to)) throw AppException.Bad("Choose both officers.");
        if (r.FromUserId == r.ToUserId) throw AppException.Bad("An officer cannot act for themselves.");
        if (!to.Active) throw AppException.Bad($"{to.Name} is not an active user.");
        if (string.IsNullOrWhiteSpace(r.Reason)) throw AppException.Bad("Give the basis of the arrangement (leave, tour, order number).");
        if (r.EndsOn.Date < r.StartsOn.Date) throw AppException.Bad("The end date is before the start date.");
        if (r.EndsOn.Date > r.StartsOn.Date.AddDays(366)) throw AppException.Bad("Acting arrangements are limited to one year.");
        // an acting officer acting for someone who is in turn acting for them is circular
        if ((await hierarchy.DelegationsAsync(ct)).Any(d => d.FromUserId == r.ToUserId && d.ToUserId == r.FromUserId && d.EndsOn.Date >= r.StartsOn.Date && d.StartsOn.Date <= r.EndsOn.Date))
            throw AppException.Bad($"{to.Name} has already delegated to {from.Name} for an overlapping period.");
        var d = new Delegation { FromUserId = r.FromUserId, ToUserId = r.ToUserId, Scope = r.Scope, DocumentTypeId = r.DocumentTypeId, StartsOn = r.StartsOn.Date, EndsOn = r.EndsOn.Date, Reason = r.Reason.Trim(), CreatedById = me.Id };
        db.Delegations.Add(d);
        await db.SaveChangesAsync(ct);
        audit.Log("access", "delegation-created", me, "delegation", d.Id.ToString(), next: new { d.FromUserId, d.ToUserId, Scope = d.Scope.ToString(), d.DocumentTypeId, d.StartsOn, d.EndsOn, d.Reason });
        db.Notifications.Add(new Notification { UserId = d.ToUserId, Kind = "delegation", Title = $"You are acting for {from.Name}", Body = $"{d.Scope} · {d.StartsOn:dd-MM-yyyy} to {d.EndsOn:dd-MM-yyyy}. {d.Reason}" });
        db.Notifications.Add(new Notification { UserId = d.FromUserId, Kind = "delegation", Title = $"{to.Name} will act for you", Body = $"{d.Scope} · {d.StartsOn:dd-MM-yyyy} to {d.EndsOn:dd-MM-yyyy}. {d.Reason}" });
        await db.SaveChangesAsync(ct);
        hierarchy.Invalidate();
        return (await DelegationsAsync(ct)).First(x => x.Id == d.Id);
    }

    public async Task RevokeDelegationAsync(int id, string? reason, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.Configure, ct);
        var d = await db.Delegations.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Delegation");
        if (d.RevokedAt is not null) return;
        d.RevokedAt = DateTime.UtcNow; d.RevokedById = me.Id;
        audit.Log("access", "delegation-revoked", me, "delegation", d.Id.ToString(), reason: reason);
        await db.SaveChangesAsync(ct);
        hierarchy.Invalidate();
    }
}

/// <summary>In-app notifications (FR-023). E-mail / SMS are not wired: they depend on approved integrations (BRD 8).</summary>
public class NotificationService(IAppDbContext db, UserContext users)
{
    /// <summary>Queue a notice for each recipient (deduplicated, never to the person who caused it). Saved with the caller's transaction.</summary>
    public void Notify(IEnumerable<string?> userIds, string kind, string title, string? body, string entityType, string entityId, string? exceptUserId = null)
    {
        foreach (var id in userIds.Where(x => !string.IsNullOrEmpty(x)).Distinct())
            if (id != exceptUserId)
                db.Notifications.Add(new Notification { UserId = id!, Kind = kind, Title = Trunc(title, 200), Body = body is null ? null : Trunc(body, 1000), EntityType = entityType, EntityId = entityId });
    }
    static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    public async Task<List<NotificationDto>> ListAsync(bool unreadOnly, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == me.Id);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        return await q.OrderByDescending(n => n.Id).Take(300).Select(n => new NotificationDto(n.Id, n.At, n.Kind, n.Title, n.Body, n.EntityType, n.EntityId, n.ReadAt)).ToListAsync(ct);
    }
    public async Task<int> UnreadAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        return await db.Notifications.CountAsync(n => n.UserId == me.Id && n.ReadAt == null, ct);
    }
    public async Task MarkReadAsync(long? id, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var rows = await db.Notifications.Where(n => n.UserId == me.Id && n.ReadAt == null && (id == null || n.Id == id)).ToListAsync(ct);
        foreach (var n in rows) n.ReadAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Upload validation and storage (SR-SEC-05): extension allow-list, block-list, size limit, content sniffing,
/// malware scan hook, SHA-256 of the stored bytes.</summary>
public class FileService(IAppDbContext db, IFileStorage storage, IFileScanner scanner, ConfigService configs)
{
    public async Task<StoredFile> StoreAsync(string folder, string fileName, string? contentType, byte[] bytes, string allowedCsv, int maxMb, string uploadedBy, CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct);
        fileName = Path.GetFileName(fileName ?? "").Trim();
        if (fileName.Length == 0) throw AppException.Bad("The file has no name.");
        if (fileName.Length > 180) fileName = fileName[^180..];
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        var blocked = Csv(cfg.Files.BlockedExtensions);
        var allowed = Csv(allowedCsv);
        if (ext.Length == 0 || blocked.Contains(ext)) throw AppException.Bad($"Files of type .{ext} are not accepted.", "file-type");
        if (allowed.Count > 0 && !allowed.Contains(ext)) throw AppException.Bad($"Files of type .{ext} are not accepted here. Allowed: {string.Join(", ", allowed)}.", "file-type");
        if (bytes.Length == 0) throw AppException.Bad("The file is empty.");
        if (bytes.LongLength > maxMb * 1024L * 1024L) throw AppException.Bad($"The file is {bytes.Length / 1024d / 1024d:0.0} MB; the limit is {maxMb} MB.", "file-size");
        if (!ContentMatches(ext, bytes)) throw AppException.Bad($"The file content does not match a .{ext} file.", "file-type");
        var threat = await scanner.ScanAsync(bytes, fileName, ct);
        if (threat is not null) throw AppException.Bad($"The file was rejected by the malware scanner ({threat}).", "file-scan");
        using var ms = new MemoryStream(bytes);
        var path = await storage.SaveAsync(folder, fileName, ms, ct);
        var f = new StoredFile { FileName = fileName, ContentType = MimeFor(ext, contentType), Size = bytes.LongLength, Sha256 = Integrity.Sha256Hex(bytes), Path = path, UploadedById = uploadedBy };
        db.Files.Add(f);
        return f;
    }

    /// <summary>Stores bytes the system produced itself (stamped PDFs): no allow-list, still hashed.</summary>
    public async Task<StoredFile> StoreGeneratedAsync(string folder, string fileName, string contentType, byte[] bytes, string by, CancellationToken ct)
    {
        using var ms = new MemoryStream(bytes);
        var path = await storage.SaveAsync(folder, fileName, ms, ct);
        var f = new StoredFile { FileName = fileName, ContentType = contentType, Size = bytes.LongLength, Sha256 = Integrity.Sha256Hex(bytes), Path = path, UploadedById = by };
        db.Files.Add(f);
        return f;
    }

    public async Task<byte[]> ReadAsync(StoredFile f, CancellationToken ct)
    {
        await using var s = storage.Open(f.Path) ?? throw AppException.Rule("file-missing", $"The stored file {f.FileName} is missing from the file store.");
        using var ms = new MemoryStream();
        await s.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    static HashSet<string> Csv(string? s) => (s ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.TrimStart('.').ToLowerInvariant()).ToHashSet();

    static bool Starts(byte[] b, params byte[] sig) => b.Length >= sig.Length && sig.Select((x, i) => b[i] == x).All(x => x);
    public static bool ContentMatches(string ext, byte[] b) => ext switch
    {
        "pdf" => Starts(b, 0x25, 0x50, 0x44, 0x46),                                            // %PDF
        "docx" or "xlsx" or "pptx" or "zip" or "odt" or "ods" => Starts(b, 0x50, 0x4B, 0x03, 0x04),
        "doc" or "xls" or "ppt" or "msg" => Starts(b, 0xD0, 0xCF, 0x11, 0xE0),
        "png" => Starts(b, 0x89, 0x50, 0x4E, 0x47),
        "jpg" or "jpeg" => Starts(b, 0xFF, 0xD8, 0xFF),
        "tif" or "tiff" => Starts(b, 0x49, 0x49, 0x2A, 0x00) || Starts(b, 0x4D, 0x4D, 0x00, 0x2A),
        "txt" or "csv" => !b.Take(4096).Contains((byte)0),
        _ => true,
    };
    public static string MimeFor(string ext, string? given) => ext switch
    {
        "pdf" => "application/pdf",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "doc" => "application/msword", "xls" => "application/vnd.ms-excel",
        "png" => "image/png", "jpg" or "jpeg" => "image/jpeg", "tif" or "tiff" => "image/tiff",
        "txt" => "text/plain", "csv" => "text/csv", "zip" => "application/zip",
        _ => string.IsNullOrWhiteSpace(given) ? "application/octet-stream" : given,
    };
}
