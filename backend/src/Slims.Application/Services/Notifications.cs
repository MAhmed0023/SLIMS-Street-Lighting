using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>Writes the quantity ledger (FR-INV-016). Every service that changes what a store holds calls Add().</summary>
public class LedgerService(IAppDbContext db)
{
    public void Add(string type, Item item, int storeId, decimal qty, string userId, int? from = null, int? to = null, string? refType = null, string? refNo = null, DateTime? at = null)
        => db.StockMovements.Add(new StockMovement { Type = type, ItemId = item.Id, ArticleId = item.ArticleId, StoreId = storeId, Qty = qty, FromLocationId = from, ToLocationId = to, RefType = refType, RefNo = refNo, UserId = userId, At = at ?? DateTime.UtcNow });
}

/// <summary>E-mail and SMS channel settings. Kept apart from the parameters because the parameters are readable by every
/// signed-in user and these hold a password and an API key.</summary>
public class NotifySettings
{
    public bool EmailEnabled { get; set; }
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public bool SmtpSsl { get; set; } = true;
    public string? SmtpUser { get; set; }
    public string? SmtpPassword { get; set; }
    public string? FromAddress { get; set; }
    public string FromName { get; set; } = "SLIMS";
    public bool SmsEnabled { get; set; }
    /// <summary>Gateway URL with {phone} and {text} placeholders, e.g. https://sms.example.pk/send?to={phone}&amp;msg={text}&amp;key={key}</summary>
    public string? SmsUrl { get; set; }
    public string SmsMethod { get; set; } = "GET";
    public string? SmsApiKey { get; set; }
    public string? PortalUrl { get; set; }                // used in e-mail links, e.g. http://172.16.10.184:3001
}

public class NotifySettingsService(IAppDbContext db)
{
    const string Key = "notify";
    NotifySettings? _s;
    public async Task<NotifySettings> GetAsync(CancellationToken ct = default)
    {
        if (_s is not null) return _s;
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == Key, ct);
        return _s = row is null ? new NotifySettings() : JsonSerializer.Deserialize<NotifySettings>(row.Json, ConfigService.Json) ?? new NotifySettings();
    }
    public async Task SaveAsync(NotifySettings s, CancellationToken ct = default)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == Key, ct);
        if (row is null) { row = new AppSetting { Key = Key }; db.AppSettings.Add(row); }
        row.Json = JsonSerializer.Serialize(s, ConfigService.Json); row.UpdatedAt = DateTime.UtcNow; _s = s;
    }
    /// <summary>The settings as the UI sees them: secrets replaced by a marker.</summary>
    public static NotifySettings Masked(NotifySettings s) => new()
    {
        EmailEnabled = s.EmailEnabled, SmtpHost = s.SmtpHost, SmtpPort = s.SmtpPort, SmtpSsl = s.SmtpSsl, SmtpUser = s.SmtpUser, SmtpPassword = string.IsNullOrEmpty(s.SmtpPassword) ? null : "********",
        FromAddress = s.FromAddress, FromName = s.FromName, SmsEnabled = s.SmsEnabled, SmsUrl = s.SmsUrl, SmsMethod = s.SmsMethod, SmsApiKey = string.IsNullOrEmpty(s.SmsApiKey) ? null : "********", PortalUrl = s.PortalUrl,
    };
}

public record NotifyPrefs(List<string> Muted, bool Email = true, bool Sms = true)
{
    public static NotifyPrefs Parse(string? json)
    {
        try { var p = string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<NotifyPrefs>(json, ConfigService.Json); return p is null ? new NotifyPrefs(new List<string>()) : p with { Muted = p.Muted ?? new List<string>() }; }
        catch (JsonException) { return new NotifyPrefs(new List<string>()); }
    }
}

/// <summary>The notification rules of FRD Section 14: who is told what, through which channels, and which notices are
/// assigned work that cannot be switched off (FR-NTF-001 … 005).</summary>
public class NotificationService(IAppDbContext db, ConfigService configs, NotifySettingsService channels)
{
    public static readonly Dictionary<string, (string Label, bool Email, bool Sms, bool Mandatory)> Codes = new()
    {
        ["NTF-01"] = ("Requisition awaiting your approval", true, false, true),
        ["NTF-02"] = ("Requisition approved", false, true, false),
        ["NTF-03"] = ("Requisition rejected", false, true, false),
        ["NTF-04"] = ("Reservation lapsing / lapsed", false, false, false),
        ["NTF-05"] = ("Material issued to your site", false, true, false),
        ["NTF-06"] = ("Issue not acknowledged at site", false, true, true),
        ["NTF-07"] = ("Issued material not yet installed", false, false, true),
        ["NTF-08"] = ("GPS exception raised", true, false, true),
        ["NTF-09"] = ("Re-capture required", false, true, true),
        ["NTF-10"] = ("Field verification requested", false, true, true),
        ["NTF-11"] = ("Out-of-order return reported", false, false, false),
        ["NTF-12"] = ("Return overdue", true, false, false),
        ["NTF-13"] = ("Adjustment / disposal / amendment awaiting approval", true, false, true),
        ["NTF-14"] = ("Stock below re-order level", true, false, false),
        ["NTF-15"] = ("Repeated GPS mismatch", true, false, false),
        ["NTF-16"] = ("Unusual return rate", true, false, false),
        ["NTF-17"] = ("Unresolved site installations", false, false, false),
        ["NTF-DEV"] = ("Phone awaiting approval", true, false, true),
        ["NTF-RPT"] = ("Scheduled report ready", true, false, false),
        ["NTF-A1"] = ("Alert: requisition turnaround exceeded", false, false, false),
        ["NTF-A2"] = ("Alert: stock ageing", false, false, false),
    };

    List<User>? _users;
    public async Task<List<User>> ActiveUsersAsync(CancellationToken ct) => _users ??= await db.Users.Include(u => u.Role).Where(u => u.Active).ToListAsync(ct);

    public async Task<List<User>> RoleAsync(string roleKey, CancellationToken ct, Store? store = null, Site? site = null)
        => (await ActiveUsersAsync(ct)).Where(u => u.RoleKey == roleKey && (store is null || AccessPolicy.StoreVisible(u, store)) && (site is null || AccessPolicy.SiteVisible(u, site))).ToList();
    public async Task<List<User>> StoreStaffAsync(Store store, CancellationToken ct) => [.. await RoleAsync("storeincharge", ct, store), .. await RoleAsync("storekeeper", ct, store)];
    public async Task<List<User>> ByIdsAsync(CancellationToken ct, params string?[] ids) { var set = ids.Where(i => i is not null).ToHashSet(); return (await ActiveUsersAsync(ct)).Where(u => set.Contains(u.Id)).ToList(); }
    /// <summary>The site's own supervisor or officer, else everybody in that role who can see the site.</summary>
    public async Task<List<User>> SiteRoleAsync(Site site, string roleKey, CancellationToken ct)
    {
        var assigned = roleKey == "supervisor" ? site.SupervisorUserId : roleKey == "officer" ? site.OfficerUserId : null;
        var named = await ByIdsAsync(ct, assigned);
        return named.Count > 0 ? named : await RoleAsync(roleKey, ct, site: site);
    }
    public async Task<List<User>> ApproversAsync(int level, Store store, CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct);
        var role = cfg.ApprovalLevels.FirstOrDefault(l => l.Level == level)?.RoleKey ?? "storeincharge";
        return await RoleAsync(role, ct, store);
    }
    /// <summary>FR-NTF-004: the next level up for whoever did not act.</summary>
    public async Task<List<User>> EscalationAsync(string fromRole, CancellationToken ct, Store? store = null, Site? site = null)
    {
        var cfg = await configs.GetAsync(ct);
        var next = cfg.EscalateTo.GetValueOrDefault(fromRole) ?? "pdoffice";
        var list = await RoleAsync(next, ct, store, site);
        return list.Count > 0 ? list : await RoleAsync(next, ct);
    }

    public async Task<int> NotifyAsync(IEnumerable<User> to, string code, string title, string body, string? link, string? entityType, string? entityRef, CancellationToken ct, int level = 0, string? dedup = null)
    {
        var (_, email, sms, mandatory) = Codes.GetValueOrDefault(code, (code, false, false, false));
        var ch = await channels.GetAsync(ct);
        var key = dedup ?? $"{code}|{entityType}|{entityRef}|{level}";
        var n = 0;
        foreach (var u in to.DistinctBy(u => u.Id))
        {
            var prefs = NotifyPrefs.Parse(u.NotifyPrefs);
            if (!mandatory && prefs.Muted.Contains(code)) continue;
            if (db.Notifications.Local.Any(x => x.UserId == u.Id && x.DedupKey == key) || await db.Notifications.AnyAsync(x => x.UserId == u.Id && x.DedupKey == key, ct)) continue;
            db.Notifications.Add(new Notification
            {
                UserId = u.Id, Code = code, Title = Trunc(title, 200), Body = Trunc(body, 1000), Link = link, EntityType = entityType, EntityRef = entityRef, Mandatory = mandatory, Level = level, DedupKey = Trunc(key, 160),
                EmailStatus = email && ch.EmailEnabled && !string.IsNullOrWhiteSpace(u.Email) && (prefs.Email || mandatory) ? "Pending" : "None",
                SmsStatus = sms && ch.SmsEnabled && !string.IsNullOrWhiteSpace(u.Phone) && (prefs.Sms || mandatory) ? "Pending" : "None",
            });
            n++;
        }
        return n;
    }
    static string Trunc(string s, int max) => s.Length <= max ? s : s[..max];
}

public record NotificationDto(long Id, string Code, string Label, string Title, string Body, string? Link, string? EntityType, string? EntityRef, bool Mandatory, int Level, DateTime CreatedAt, DateTime? ReadAt, string EmailStatus, string SmsStatus);
public record NotifyPrefsDto(List<string> Muted, bool Email, bool Sms, List<NotifyCodeDto> Codes);
public record NotifyCodeDto(string Code, string Label, bool Mandatory, bool Email, bool Sms);

/// <summary>The signed-in user's own inbox and preferences.</summary>
public class InboxService(IAppDbContext db, UserContext users)
{
    public async Task<List<NotificationDto>> ListAsync(bool unreadOnly, int take, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var q = db.Notifications.AsNoTracking().Where(n => n.UserId == me.Id);
        if (unreadOnly) q = q.Where(n => n.ReadAt == null);
        return (await q.OrderByDescending(n => n.Id).Take(Math.Clamp(take, 1, 500)).ToListAsync(ct)).Select(ToDto).ToList();
    }
    public async Task<int> UnreadAsync(CancellationToken ct) { var me = await users.RequireAsync(ct); return await db.Notifications.CountAsync(n => n.UserId == me.Id && n.ReadAt == null, ct); }
    public async Task MarkReadAsync(long? id, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var rows = await db.Notifications.Where(n => n.UserId == me.Id && n.ReadAt == null && (id == null || n.Id == id)).ToListAsync(ct);
        foreach (var n in rows) n.ReadAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }
    public async Task<NotifyPrefsDto> PrefsAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var p = NotifyPrefs.Parse(me.NotifyPrefs);
        return new(p.Muted, p.Email, p.Sms, NotificationService.Codes.Select(c => new NotifyCodeDto(c.Key, c.Value.Label, c.Value.Mandatory, c.Value.Email, c.Value.Sms)).ToList());
    }
    /// <summary>FR-NTF-005: assigned-work notices are silently kept on whatever the user asks.</summary>
    public async Task<NotifyPrefsDto> SavePrefsAsync(NotifyPrefs p, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var muted = (p.Muted ?? new()).Where(c => NotificationService.Codes.TryGetValue(c, out var d) && !d.Mandatory).Distinct().ToList();
        me.NotifyPrefs = JsonSerializer.Serialize(new NotifyPrefs(muted, p.Email, p.Sms), ConfigService.Json);
        await db.SaveChangesAsync(ct);
        return await PrefsAsync(ct);
    }
    public static NotificationDto ToDto(Notification n) => new(n.Id, n.Code, NotificationService.Codes.GetValueOrDefault(n.Code).Label ?? n.Code, n.Title, n.Body, n.Link, n.EntityType, n.EntityRef, n.Mandatory, n.Level,
        DateTime.SpecifyKind(n.CreatedAt, DateTimeKind.Utc), n.ReadAt is null ? null : DateTime.SpecifyKind(n.ReadAt.Value, DateTimeKind.Utc), n.EmailStatus, n.SmsStatus);
}
