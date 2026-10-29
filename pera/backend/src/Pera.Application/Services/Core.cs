using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Pera.Application.Common;
using Pera.Application.Dtos;
using Pera.Application.Interfaces;
using Pera.Domain.Config;
using Pera.Domain.Entities;
using Pera.Domain.Rules;

namespace Pera.Application.Services;

/// <summary>Application parameters, cached per request.</summary>
public class ConfigService(IAppDbContext db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false, Converters = { new JsonStringEnumConverter() } };
    private AppConfig? _cfg;
    public async Task<AppConfig> GetAsync(CancellationToken ct = default)
    {
        if (_cfg is not null) return _cfg;
        var row = await db.AppSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == "config", ct);
        _cfg = row is null ? DefaultConfig.Create() : JsonSerializer.Deserialize<AppConfig>(row.Json, Json) ?? DefaultConfig.Create();
        return _cfg;
    }
    public async Task SaveAsync(AppConfig cfg, CancellationToken ct = default)
    {
        var row = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == "config", ct);
        if (row is null) { row = new AppSetting { Key = "config" }; db.AppSettings.Add(row); }
        row.Json = JsonSerializer.Serialize(cfg, Json); row.UpdatedAt = DateTime.UtcNow; _cfg = cfg;
    }
}

/// <summary>Append-only audit trail (FR-025). Every write path calls Log(); nothing here updates or deletes.</summary>
public class AuditService(IAppDbContext db, ICurrentUser current)
{
    public AuditLog Log(string kind, string action, User? actor, string? entityType = null, string? entityId = null, object? prev = null, object? next = null,
        string? reason = null, string? actorIdOverride = null, string result = "ok", string? actingForId = null)
    {
        var e = new AuditLog
        {
            Kind = kind, Action = action, Result = result, EntityType = entityType, EntityId = entityId,
            ActorId = actor?.Id ?? actorIdOverride, ActorName = actor?.Name, Role = actor?.RoleKey, ActingForId = actingForId,
            PrevJson = prev is null ? null : JsonSerializer.Serialize(prev, ConfigService.Json), NextJson = next is null ? null : JsonSerializer.Serialize(next, ConfigService.Json),
            Reason = reason, CorrelationId = current.CorrelationId, UserAgent = Trunc(current.UserAgent, 160), Ip = Trunc(current.Ip, 64),
        };
        db.AuditLogs.Add(e);
        return e;
    }
    static string? Trunc(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];

    /// <summary>Records a refused attempt and commits it before the refusal is thrown, so the attempt survives the failed request (FRD 7: "denies the action and logs the event").</summary>
    public async Task<AppException> DenyAsync(string kind, string action, User actor, string entityType, string entityId, string message, CancellationToken ct, int status = 403, string? step = null, string? actingForId = null)
    {
        Log(kind, action, actor, entityType, entityId, reason: message, result: "denied", actingForId: actingForId);
        await db.SaveChangesAsync(ct);
        return new AppException(status, message, step ?? "authority");
    }

    public async Task<List<AuditDto>> ListAsync(string? kind, string? action, string? entityType, string? entityId, string? result, string? q, DateTime? from, DateTime? to, int limit, CancellationToken ct, string? actorId = null)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (actorId is not null) query = query.Where(a => a.ActorId == actorId);
        if (!string.IsNullOrEmpty(kind)) query = query.Where(a => a.Kind == kind);
        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action.StartsWith(action));
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId)) query = query.Where(a => a.EntityId == entityId);
        if (!string.IsNullOrEmpty(result)) query = query.Where(a => a.Result == result);
        if (from is DateTime f) query = query.Where(a => a.At >= f.Date);
        if (to is DateTime t) { var end = t.Date.AddDays(1); query = query.Where(a => a.At < end); }
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(a => (a.ActorName ?? "").Contains(q) || (a.Reason ?? "").Contains(q) || (a.NextJson ?? "").Contains(q) || (a.EntityId ?? "").Contains(q) || a.Action.Contains(q));
        var rows = await query.OrderByDescending(a => a.Id).Take(Math.Clamp(limit, 1, 5000)).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }
    public static AuditDto ToDto(AuditLog a) => new(a.Id, DateTime.SpecifyKind(a.At, DateTimeKind.Utc), a.Kind, a.Action, a.Result, a.EntityType, a.EntityId, a.ActorId, a.ActorName, a.ActingForId, a.Role, a.PrevJson, a.NextJson, a.Reason, a.CorrelationId, a.Ip);
}

/// <summary>Loads the signed-in user (with role, designation and unit) once per request and maps users to DTOs.</summary>
public class UserContext(IAppDbContext db, ICurrentUser current)
{
    private User? _me; private bool _loaded;

    public async Task<User?> TryGetAsync(CancellationToken ct = default)
    {
        if (_loaded) return _me;
        _loaded = true;
        if (string.IsNullOrEmpty(current.UserId)) return null;
        _me = await db.Users.Include(u => u.Role).Include(u => u.Designation).Include(u => u.OrgUnit).FirstOrDefaultAsync(u => u.Id == current.UserId, ct);
        if (_me is not null && !_me.Active) _me = null;
        return _me;
    }
    public async Task<User> RequireAsync(CancellationToken ct = default) => await TryGetAsync(ct) ?? throw new AppException(401, "Sign in first.");
    public async Task<User> RequireAsync(string permission, CancellationToken ct = default)
    {
        var me = await RequireAsync(ct);
        if (!AccessPolicy.Can(me, permission)) throw AppException.Forbidden($"Your role ({me.Role?.Label ?? me.RoleKey}) has no \"{permission}\" permission.", "permission");
        return me;
    }

    public static UserDto ToDto(User u, string? reportsToName = null) => new(u.Id, u.Name, u.Email, u.EmployeeId, u.Phone, u.RoleKey, u.Role?.Label ?? u.RoleKey,
        u.DesignationId, u.Designation?.Title, u.OrgUnitId, u.OrgUnit?.Name, u.ReportsToId, reportsToName,
        u.Active, u.PasswordHash is not null, u.MustChangePassword, u.LastLogin, u.LockedUntil, Permissions.All.ToDictionary(p => p, p => AccessPolicy.Can(u, p)));
    public static UserPublicDto ToPublic(User u) => new(u.Id, u.Name, u.RoleKey, u.Role?.Label ?? u.RoleKey, u.Designation?.Title, u.PasswordHash is not null, u.Active);
}

/// <summary>Sequential, gap-free reference numbers (BRD 6): PERA/TSK/26-27/00001.</summary>
public class NumberingService(IAppDbContext db)
{
    public async Task<string> NextAsync(string prefix, CancellationToken ct, int width = 5)
    {
        var key = $"PERA/{prefix}/{Codes.FY(DateTime.UtcNow)}";
        var seq = db.DocSequences.Local.FirstOrDefault(s => s.Key == key) ?? await db.DocSequences.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (seq is null) { seq = new DocSequence { Key = key, Next = 1 }; db.DocSequences.Add(seq); }
        var n = seq.Next; seq.Next += 1;
        return $"{key}/{n.ToString().PadLeft(width, '0')}";
    }
}

/// <summary>Sign-in, tokens, password lifecycle and lockout.</summary>
public class AuthService(IAppDbContext db, IPasswordHasher hasher, ITokenService tokens, AuditService audit, UserContext users, ConfigService configs)
{
    public const int MinLength = 8, MaxFailed = 5, LockMinutes = 15;

    public static List<string> Policy(string? pw, User? u)
    {
        var problems = new List<string>();
        if (pw is null || pw.Length < MinLength) problems.Add($"at least {MinLength} characters");
        if (pw is null || !pw.Any(char.IsLetter) || !pw.Any(char.IsDigit)) problems.Add("both letters and digits");
        if (u is not null && pw is not null && (pw.Equals(u.Id, StringComparison.OrdinalIgnoreCase) || pw.Equals(u.Name, StringComparison.OrdinalIgnoreCase))) problems.Add("not your user ID or name");
        return problems;
    }

    public record LoginResult(User User, bool MustChange, string AccessToken, DateTime AccessExpires, string RefreshToken, DateTime RefreshExpires);

    static IQueryable<User> WithRefs(IQueryable<User> q) => q.Include(x => x.Role).Include(x => x.Designation).Include(x => x.OrgUnit);

    public async Task<LoginResult> LoginAsync(string idOrEmail, string? password, CancellationToken ct)
    {
        const string generic = "Invalid user ID or password.";
        var cfg = await configs.GetAsync(ct);
        var k = (idOrEmail ?? "").Trim().ToLowerInvariant();
        var u = await WithRefs(db.Users).FirstOrDefaultAsync(x => x.Id.ToLower() == k || (x.Email != null && x.Email.ToLower() == k), ct);
        if (u is null) { audit.Log("auth", "login-failed", null, reason: "unknown user", actorIdOverride: idOrEmail, result: "denied"); await db.SaveChangesAsync(ct); throw new AppException(401, generic); }
        if (!u.Active) { audit.Log("auth", "login-failed", u, reason: "inactive", result: "denied"); await db.SaveChangesAsync(ct); throw new AppException(403, "This account is inactive. Contact the System Administrator."); }
        if (u.LockedUntil is DateTime until && until > DateTime.UtcNow) { audit.Log("auth", "login-failed", u, reason: "locked", result: "denied"); await db.SaveChangesAsync(ct); throw new AppException(423, $"Account locked after repeated failures. Try again in {Math.Ceiling((until - DateTime.UtcNow).TotalMinutes)} minute(s) or ask the administrator."); }
        password ??= "";
        if (u.PasswordHash is null && !cfg.LoginRequired)
            audit.Log("auth", "login-nopassword", u, reason: "password sign-in switched off; account has no password");
        else if (u.PasswordHash is null)
        {
            var bootstrap = !await db.Users.AnyAsync(x => x.PasswordHash != null, ct);
            if (bootstrap && u.RoleKey == "sysadmin")
            {
                var problems = Policy(password, u);
                if (problems.Count > 0) throw new AppException(400, "Choose the administrator password: " + string.Join(", ", problems) + ".");
                var h = hasher.Hash(password); u.PasswordHash = h.Hash; u.PasswordSalt = h.Salt; u.PasswordSetAt = DateTime.UtcNow; u.MustChangePassword = false;
                audit.Log("auth", "password-bootstrap", u, reason: "first administrator password set");
            }
            else { audit.Log("auth", "login-failed", u, reason: "no password issued", result: "denied"); await db.SaveChangesAsync(ct); throw new AppException(403, "No password has been issued for this account yet. Ask the System Administrator to set an initial password."); }
        }
        else if (!hasher.Verify(password, u.PasswordHash, u.PasswordSalt))
        {
            u.FailedAttempts += 1; var reason = "wrong password";
            if (u.FailedAttempts >= MaxFailed) { u.LockedUntil = DateTime.UtcNow.AddMinutes(LockMinutes); u.FailedAttempts = 0; reason = "wrong password — account locked"; }
            audit.Log("auth", "login-failed", u, reason: reason, result: "denied"); await db.SaveChangesAsync(ct);
            throw new AppException(401, reason.Contains("locked") ? $"Account locked for {LockMinutes} minutes after {MaxFailed} failed attempts." : generic);
        }
        else audit.Log("auth", "login", u);
        u.FailedAttempts = 0; u.LockedUntil = null; u.LastLogin = DateTime.UtcNow;
        var (rt, rtExp) = tokens.CreateRefreshToken(); u.RefreshTokenHash = tokens.HashToken(rt); u.RefreshTokenExpires = rtExp;
        await db.SaveChangesAsync(ct);
        var (at, atExp) = tokens.CreateAccessToken(u);
        return new LoginResult(u, u.MustChangePassword, at, atExp, rt, rtExp);
    }

    public async Task<LoginResult> RefreshAsync(string? refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken)) throw new AppException(401, "Sign in first.");
        var hash = tokens.HashToken(refreshToken);
        var u = await WithRefs(db.Users).FirstOrDefaultAsync(x => x.RefreshTokenHash == hash, ct);
        if (u is null || !u.Active || u.RefreshTokenExpires is null || u.RefreshTokenExpires < DateTime.UtcNow) throw new AppException(401, "Session expired. Sign in again.");
        var (rt, rtExp) = tokens.CreateRefreshToken(); u.RefreshTokenHash = tokens.HashToken(rt); u.RefreshTokenExpires = rtExp;
        await db.SaveChangesAsync(ct);
        var (at, atExp) = tokens.CreateAccessToken(u);
        return new LoginResult(u, u.MustChangePassword, at, atExp, rt, rtExp);
    }

    public async Task LogoutAsync(CancellationToken ct)
    {
        var me = await users.TryGetAsync(ct); if (me is null) return;
        me.RefreshTokenHash = null; me.RefreshTokenExpires = null;
        audit.Log("auth", "logout", me);
        await db.SaveChangesAsync(ct);
    }

    public async Task ChangePasswordAsync(string? oldPassword, string newPassword, CancellationToken ct)
    {
        var me = await users.RequireAsync(ct);
        var hasPw = me.PasswordHash is not null;
        if (oldPassword is null && hasPw && !me.MustChangePassword) throw new AppException(400, "Current password is required.");
        if (oldPassword is not null && !hasher.Verify(oldPassword, me.PasswordHash, me.PasswordSalt)) { audit.Log("auth", "password-change-failed", me, reason: "wrong current password", result: "denied"); await db.SaveChangesAsync(ct); throw new AppException(400, "Current password is incorrect."); }
        var problems = Policy(newPassword, me); if (problems.Count > 0) throw new AppException(400, "Password must have " + string.Join(", ", problems) + ".");
        if (hasPw && hasher.Verify(newPassword, me.PasswordHash, me.PasswordSalt)) throw new AppException(400, "Choose a password you have not used before.");
        var h = hasher.Hash(newPassword); me.PasswordHash = h.Hash; me.PasswordSalt = h.Salt; me.PasswordSetAt = DateTime.UtcNow; me.MustChangePassword = false; me.FailedAttempts = 0; me.LockedUntil = null;
        audit.Log("auth", "password-changed", me);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<UserPublicDto>> PickerAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct);
        if (!cfg.ShowUserPicker) return [];
        return (await WithRefs(db.Users.AsNoTracking()).Where(u => u.Active).OrderBy(u => u.Role!.SortOrder).ThenBy(u => u.Name).ToListAsync(ct)).Select(UserContext.ToPublic).ToList();
    }
}

/// <summary>User administration, including the reporting line that drives routing and visibility (FR-001/002).</summary>
public class UsersService(IAppDbContext db, IPasswordHasher hasher, AuditService audit, UserContext users)
{
    public async Task<List<UserDto>> ListAsync(CancellationToken ct)
    {
        await users.RequireAsync(Permissions.Configure, ct);
        var all = await db.Users.AsNoTracking().Include(u => u.Role).Include(u => u.Designation).Include(u => u.OrgUnit).OrderBy(u => u.Role!.SortOrder).ThenBy(u => u.Name).ToListAsync(ct);
        var names = all.ToDictionary(u => u.Id, u => u.Name);
        return all.Select(u => UserContext.ToDto(u, u.ReportsToId is null ? null : names.GetValueOrDefault(u.ReportsToId))).ToList();
    }
    public async Task<List<RoleDto>> RolesAsync(CancellationToken ct)
        => (await db.Roles.AsNoTracking().OrderBy(r => r.SortOrder).ToListAsync(ct)).Select(r => new RoleDto(r.Key, r.Label, r.PermList, r.SortOrder)).ToList();

    public async Task<UserDto> UpsertAsync(UserUpsertRequest r, CancellationToken ct)
    {
        var admin = await users.RequireAsync(Permissions.Configure, ct);
        var id = (r.Id ?? "").Trim().ToLowerInvariant();
        if (id.Length < 3 || !id.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_')) throw AppException.Bad("User ID must be at least 3 characters: letters, digits, dot, dash or underscore.");
        if (string.IsNullOrWhiteSpace(r.Name)) throw AppException.Bad("Name is required.");
        if (!await db.Roles.AnyAsync(x => x.Key == r.RoleKey, ct)) throw AppException.Bad("Unknown role.");
        if (r.DesignationId is int d && !await db.Designations.AnyAsync(x => x.Id == d, ct)) throw AppException.Bad("Unknown designation.");
        if (r.OrgUnitId is int o && !await db.OrgUnits.AnyAsync(x => x.Id == o, ct)) throw AppException.Bad("Unknown office / wing / section.");
        var reportsTo = string.IsNullOrWhiteSpace(r.ReportsToId) ? null : r.ReportsToId.Trim().ToLowerInvariant();
        if (reportsTo is not null)
        {
            if (reportsTo == id) throw AppException.Bad("A user cannot report to themselves.");
            if (!await db.Users.AnyAsync(x => x.Id == reportsTo, ct)) throw AppException.Bad("Unknown reporting officer.");
            // walk up from the new reporting officer; meeting this user again would make a loop
            var map = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.ReportsToId, ct);
            var cur = reportsTo; var guard = 0;
            while (cur is not null && guard++ < 200) { if (cur == id) throw AppException.Bad("That reporting line would create a loop in the hierarchy."); cur = map.GetValueOrDefault(cur); }
        }
        var u = await db.Users.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id, ct);
        var created = u is null;
        if (u is null) { u = new User { Id = id }; db.Users.Add(u); }
        var prev = created ? null : new { u.Name, u.RoleKey, u.DesignationId, u.OrgUnitId, u.ReportsToId, u.Active };
        if (u.Id == admin.Id && (!r.Active || r.RoleKey != "sysadmin") && admin.RoleKey == "sysadmin") throw AppException.Bad("You cannot deactivate or demote your own account.");
        u.Name = r.Name.Trim(); u.Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim().ToLowerInvariant(); u.EmployeeId = r.EmployeeId; u.Phone = r.Phone;
        u.RoleKey = r.RoleKey; u.DesignationId = r.DesignationId; u.OrgUnitId = r.OrgUnitId; u.ReportsToId = reportsTo; u.Active = r.Active;
        if (!r.Active) { u.RefreshTokenHash = null; u.RefreshTokenExpires = null; }
        var action = created ? "user-created" : prev!.RoleKey != r.RoleKey ? "role-changed" : "user-updated";
        audit.Log("access", action, admin, "user", u.Id, prev, new { u.Name, u.RoleKey, u.DesignationId, u.OrgUnitId, u.ReportsToId, u.Active });
        await db.SaveChangesAsync(ct);
        var fresh = await db.Users.AsNoTracking().Include(x => x.Role).Include(x => x.Designation).Include(x => x.OrgUnit).FirstAsync(x => x.Id == u.Id, ct);
        return UserContext.ToDto(fresh, reportsTo is null ? null : (await db.Users.AsNoTracking().FirstAsync(x => x.Id == reportsTo, ct)).Name);
    }

    public async Task SetPasswordAsync(string targetId, string tempPassword, CancellationToken ct)
    {
        var admin = await users.RequireAsync(Permissions.Configure, ct);
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == targetId, ct) ?? throw AppException.NotFound("User");
        var problems = AuthService.Policy(tempPassword, u); if (problems.Count > 0) throw new AppException(400, "Temporary password must have " + string.Join(", ", problems) + ".");
        var h = hasher.Hash(tempPassword); u.PasswordHash = h.Hash; u.PasswordSalt = h.Salt; u.PasswordSetAt = DateTime.UtcNow; u.MustChangePassword = true; u.FailedAttempts = 0; u.LockedUntil = null; u.RefreshTokenHash = null;
        audit.Log("auth", "password-reset", admin, "user", u.Id, reason: "initial/replacement password issued");
        await db.SaveChangesAsync(ct);
    }

    public async Task UnlockAsync(string targetId, CancellationToken ct)
    {
        var admin = await users.RequireAsync(Permissions.Configure, ct);
        var u = await db.Users.FirstOrDefaultAsync(x => x.Id == targetId, ct) ?? throw AppException.NotFound("User");
        u.LockedUntil = null; u.FailedAttempts = 0;
        audit.Log("auth", "account-unlocked", admin, "user", u.Id);
        await db.SaveChangesAsync(ct);
    }
}
