using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Config;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>Application parameters, cached per request.</summary>
public class ConfigService(IAppDbContext db)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
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

/// <summary>Append-only audit trail. Every write path calls Log(); nothing here updates or deletes.</summary>
public class AuditService(IAppDbContext db, ICurrentUser current)
{
    public AuditLog Log(string kind, string action, User? actor, string? entityType = null, string? entityId = null, object? prev = null, object? next = null, string? reason = null, string? actorIdOverride = null)
    {
        var e = new AuditLog
        {
            Kind = kind, Action = action, EntityType = entityType, EntityId = entityId,
            ActorId = actor?.Id ?? actorIdOverride, ActorName = actor?.Name, Role = actor?.RoleKey,
            PrevJson = prev is null ? null : JsonSerializer.Serialize(prev, ConfigService.Json), NextJson = next is null ? null : JsonSerializer.Serialize(next, ConfigService.Json),
            Reason = reason, UserAgent = Trunc(current.UserAgent, 120), Ip = Trunc(current.Ip, 64),
        };
        db.AuditLogs.Add(e);
        return e;
    }
    static string? Trunc(string? s, int n) => s is null ? null : s.Length <= n ? s : s[..n];

    public async Task<List<AuditDto>> ListAsync(string? kind, string? action, string? entityType, string? entityId, string? q, int limit, CancellationToken ct, string? actorId = null)
    {
        var query = db.AuditLogs.AsNoTracking().AsQueryable();
        if (actorId is not null) query = query.Where(a => a.ActorId == actorId);
        if (!string.IsNullOrEmpty(kind)) query = query.Where(a => a.Kind == kind);
        if (!string.IsNullOrEmpty(action)) query = query.Where(a => a.Action.StartsWith(action));
        if (!string.IsNullOrEmpty(entityType)) query = query.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityId)) query = query.Where(a => a.EntityId == entityId);
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(a => (a.ActorName ?? "").Contains(q) || (a.Reason ?? "").Contains(q) || (a.NextJson ?? "").Contains(q) || (a.EntityId ?? "").Contains(q) || a.Action.Contains(q));
        var rows = await query.OrderByDescending(a => a.Id).Take(Math.Clamp(limit, 1, 2000)).ToListAsync(ct);
        return rows.Select(a => new AuditDto(a.Id, DateTime.SpecifyKind(a.At, DateTimeKind.Utc), a.Kind, a.Action, a.EntityType, a.EntityId, a.ActorId, a.ActorName, a.Role, a.PrevJson, a.NextJson, a.Reason, a.Ip)).ToList();
    }
}

/// <summary>Loads the signed-in user (with role) once per request and maps users to DTOs.</summary>
public class UserContext(IAppDbContext db, ICurrentUser current)
{
    private User? _me; private bool _loaded;

    public async Task<User?> TryGetAsync(CancellationToken ct = default)
    {
        if (_loaded) return _me;
        _loaded = true;
        if (string.IsNullOrEmpty(current.UserId)) return null;
        _me = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == current.UserId, ct);
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
    /// <summary>Any one of the permissions is enough (e.g. a Store Keeper or a Store In-Charge may post a GRN).</summary>
    public async Task<User> RequireAnyAsync(CancellationToken ct, params string[] permissions)
    {
        var me = await RequireAsync(ct);
        if (!permissions.Any(p => AccessPolicy.Can(me, p))) throw AppException.Forbidden($"Your role ({me.Role?.Label ?? me.RoleKey}) cannot do this (needs {string.Join(" or ", permissions)}).", "permission");
        return me;
    }
    public static void RequireStore(User me, Store s) { if (!AccessPolicy.StoreVisible(me, s)) throw AppException.Forbidden($"Store {s.Code} is outside your scope.", "scope"); }
    public static void RequireSite(User me, Site s) { if (!AccessPolicy.SiteVisible(me, s)) throw AppException.Forbidden($"Site {s.Code} is outside your scope.", "scope"); }

    public static UserDto ToDto(User u) => new(u.Id, u.Name, u.Email, u.EmployeeId, u.Designation, u.Phone, u.RoleKey, u.Role?.Label ?? u.RoleKey, u.Zone,
        AccessPolicy.StoreIds(u).ToArray(), AccessPolicy.SiteIds(u).ToArray(), u.Active, u.PasswordHash is not null, u.MustChangePassword, u.LastLogin, u.LockedUntil,
        Permissions.All.ToDictionary(p => p, p => AccessPolicy.Can(u, p)), AccessPolicy.DescribeScope(u));
    public static UserPublicDto ToPublic(User u) => new(u.Id, u.Name, u.RoleKey, u.Role?.Label ?? u.RoleKey, u.Designation, u.PasswordHash is not null, u.Active);
}

/// <summary>Sequential, gap-free document numbers: PREFIX/STORE/FY/nnnnn.</summary>
public class NumberingService(IAppDbContext db)
{
    public async Task<string> NextAsync(string prefix, string? scope, CancellationToken ct, int width = 5, bool fy = true)
    {
        var key = string.Join('/', new[] { prefix, scope, fy ? Codes.FY(DateTime.UtcNow) : null }.Where(x => !string.IsNullOrEmpty(x)));
        var seq = await db.DocSequences.FirstOrDefaultAsync(s => s.Key == key, ct);
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

    public async Task<LoginResult> LoginAsync(string idOrEmail, string? password, CancellationToken ct)
    {
        const string generic = "Invalid user ID or password.";
        var cfg = await configs.GetAsync(ct);
        var k = (idOrEmail ?? "").Trim().ToLowerInvariant();
        var u = await db.Users.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id.ToLower() == k || (x.Email != null && x.Email.ToLower() == k), ct);
        if (u is null) { audit.Log("auth", "login-failed", null, reason: "unknown user", actorIdOverride: idOrEmail); await db.SaveChangesAsync(ct); throw new AppException(401, generic); }
        if (!u.Active) { audit.Log("auth", "login-failed", u, reason: "inactive"); await db.SaveChangesAsync(ct); throw new AppException(403, "This account is inactive. Contact the System Administrator."); }
        if (u.LockedUntil is DateTime until && until > DateTime.UtcNow) { audit.Log("auth", "login-failed", u, reason: "locked"); await db.SaveChangesAsync(ct); throw new AppException(423, $"Account locked after repeated failures. Try again in {Math.Ceiling((until - DateTime.UtcNow).TotalMinutes)} minute(s) or ask the administrator."); }
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
            else { audit.Log("auth", "login-failed", u, reason: "no password issued"); await db.SaveChangesAsync(ct); throw new AppException(403, "No password has been issued for this account yet. Ask the System Administrator to set an initial password."); }
        }
        else if (!hasher.Verify(password, u.PasswordHash, u.PasswordSalt))
        {
            u.FailedAttempts += 1; var reason = "wrong password";
            if (u.FailedAttempts >= MaxFailed) { u.LockedUntil = DateTime.UtcNow.AddMinutes(LockMinutes); u.FailedAttempts = 0; reason = "wrong password — account locked"; }
            audit.Log("auth", "login-failed", u, reason: reason); await db.SaveChangesAsync(ct);
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
        var u = await db.Users.Include(x => x.Role).FirstOrDefaultAsync(x => x.RefreshTokenHash == hash, ct);
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
        if (oldPassword is not null && !hasher.Verify(oldPassword, me.PasswordHash, me.PasswordSalt)) { audit.Log("auth", "password-change-failed", me, reason: "wrong current password"); await db.SaveChangesAsync(ct); throw new AppException(400, "Current password is incorrect."); }
        var problems = Policy(newPassword, me); if (problems.Count > 0) throw new AppException(400, "Password must have " + string.Join(", ", problems) + ".");
        if (hasPw && hasher.Verify(newPassword, me.PasswordHash, me.PasswordSalt)) throw new AppException(400, "Choose a password you have not used before.");
        var h = hasher.Hash(newPassword); me.PasswordHash = h.Hash; me.PasswordSalt = h.Salt; me.PasswordSetAt = DateTime.UtcNow; me.MustChangePassword = false; me.FailedAttempts = 0; me.LockedUntil = null;
        audit.Log("auth", "password-changed", me);
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<UserPublicDto>> PickerAsync(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct);
        if (!cfg.ShowUserPicker) return new();
        return (await db.Users.AsNoTracking().Include(u => u.Role).Where(u => u.Active).OrderBy(u => u.Role!.SortOrder).ThenBy(u => u.Name).ToListAsync(ct)).Select(UserContext.ToPublic).ToList();
    }
}

/// <summary>User administration (System Administrator only).</summary>
public class UsersService(IAppDbContext db, IPasswordHasher hasher, AuditService audit, UserContext users)
{
    public async Task<List<UserDto>> ListAsync(CancellationToken ct)
    {
        await users.RequireAsync(Permissions.Configure, ct);
        return (await db.Users.AsNoTracking().Include(u => u.Role).OrderBy(u => u.Role!.SortOrder).ThenBy(u => u.Name).ToListAsync(ct)).Select(UserContext.ToDto).ToList();
    }
    public async Task<List<RoleDto>> RolesAsync(CancellationToken ct)
        => (await db.Roles.AsNoTracking().OrderBy(r => r.SortOrder).ToListAsync(ct)).Select(r => new RoleDto(r.Key, r.Label, r.PermList, r.SortOrder)).ToList();

    public async Task<UserDto> UpsertAsync(UserUpsertRequest r, CancellationToken ct)
    {
        var admin = await users.RequireAsync(Permissions.Configure, ct);
        var id = (r.Id ?? "").Trim().ToLowerInvariant();
        if (id.Length < 3 || !id.All(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_')) throw AppException.Bad("User ID must be at least 3 characters: letters, digits, dot, dash or underscore.");
        if (!await db.Roles.AnyAsync(x => x.Key == r.RoleKey, ct)) throw AppException.Bad("Unknown role.");
        var u = await db.Users.Include(x => x.Role).FirstOrDefaultAsync(x => x.Id == id, ct);
        var created = u is null;
        if (u is null) { u = new User { Id = id }; db.Users.Add(u); }
        var prev = created ? null : UserContext.ToDto(u);
        if (u.Id == admin.Id && (!r.Active || r.RoleKey != "sysadmin")) throw AppException.Bad("You cannot deactivate or demote your own account.");
        u.Name = r.Name.Trim(); u.Email = string.IsNullOrWhiteSpace(r.Email) ? null : r.Email.Trim().ToLowerInvariant(); u.EmployeeId = r.EmployeeId; u.Designation = r.Designation; u.Phone = r.Phone;
        u.RoleKey = r.RoleKey; u.Zone = string.IsNullOrWhiteSpace(r.Zone) ? null : r.Zone; u.StoreIds = string.Join(',', r.StoreIds ?? []); u.SiteIds = string.Join(',', r.SiteIds ?? []); u.Active = r.Active;
        if (!r.Active) { u.RefreshTokenHash = null; u.RefreshTokenExpires = null; }
        audit.Log("access", created ? "user-created" : "user-updated", admin, "user", u.Id, prev, new { u.Name, u.RoleKey, u.Zone, u.StoreIds, u.SiteIds, u.Active });
        await db.SaveChangesAsync(ct);
        u.Role = await db.Roles.FirstAsync(x => x.Key == u.RoleKey, ct);
        return UserContext.ToDto(u);
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
