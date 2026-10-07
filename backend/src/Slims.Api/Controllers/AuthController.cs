using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Slims.Application.Dtos;
using Slims.Application.Services;
using Slims.Domain.Config;

namespace Slims.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(AuthService auth, UserContext users, IConfiguration config) : ControllerBase
{
    const string RefreshCookie = "slims_refresh";
    bool Secure => config.GetValue("App:SecureCookies", false);

    void SetRefresh(string token, DateTime expires) => Response.Cookies.Append(RefreshCookie, token, new CookieOptions { HttpOnly = true, Secure = Secure, SameSite = SameSiteMode.Lax, Path = "/api/auth", Expires = expires });
    TokenResponse ToResponse(AuthService.LoginResult r) { SetRefresh(r.RefreshToken, r.RefreshExpires); return new TokenResponse(r.AccessToken, r.AccessExpires, UserContext.ToDto(r.User), r.MustChange); }

    /// <summary>Sign in with user ID (or email) and password. First sign-in as sysadmin sets the administrator password.</summary>
    [HttpPost("login"), AllowAnonymous]
    public async Task<TokenResponse> Login([FromBody] LoginRequest r, CancellationToken ct) => ToResponse(await auth.LoginAsync(r.Id, r.Password, ct));

    /// <summary>Exchange the refresh cookie for a new access token (rotates the refresh token).</summary>
    [HttpPost("refresh"), AllowAnonymous]
    public async Task<TokenResponse> Refresh(CancellationToken ct) => ToResponse(await auth.RefreshAsync(Request.Cookies[RefreshCookie], ct));

    [HttpPost("logout"), Authorize]
    public async Task<IActionResult> Logout(CancellationToken ct) { await auth.LogoutAsync(ct); Response.Cookies.Delete(RefreshCookie, new CookieOptions { Path = "/api/auth" }); return Ok(new { ok = true }); }

    [HttpGet("me"), Authorize]
    public async Task<UserDto> Me(CancellationToken ct) => UserContext.ToDto(await users.RequireAsync(ct));

    [HttpPost("change-password"), Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest r, CancellationToken ct) { await auth.ChangePasswordAsync(r.Old, r.New, ct); return Ok(new { ok = true }); }

    /// <summary>Names for the "Choose a user" picker on the sign-in card (empty when disabled in Parameters).</summary>
    [HttpGet("picker"), AllowAnonymous]
    public async Task<List<UserPublicDto>> Picker(CancellationToken ct) => await auth.PickerAsync(ct);
}

[ApiController]
[Route("api")]
public class MetaController(ConfigService configs, Application.Interfaces.IAppDbContext db, UserContext users) : ControllerBase
{
    [HttpGet("meta"), AllowAnonymous]
    public async Task<MetaDto> Meta(CancellationToken ct)
    {
        var cfg = await configs.GetAsync(ct);
        var firstRun = !await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(db.Users, u => u.PasswordHash != null, ct);
        return new MetaDto(cfg.App, cfg.LoginRequired, cfg.ShowUserPicker, firstRun, DateTime.UtcNow);
    }

    [HttpGet("config"), Authorize]
    public async Task<AppConfig> Config(CancellationToken ct) { await users.RequireAsync(ct); return await configs.GetAsync(ct); }

    [HttpPut("config"), Authorize]
    public async Task<AppConfig> Save([FromBody] AppConfig cfg, [FromServices] AuditService audit, CancellationToken ct)
    {
        var me = await users.RequireAsync(Domain.Entities.Permissions.Configure, ct);
        var prev = await configs.GetAsync(ct);
        cfg.Version = prev.Version + 1;
        await configs.SaveAsync(cfg, ct);
        audit.Log("config", "parameters-updated", me, "config", "config", new { prev.Geo, prev.Controls }, new { cfg.Geo, cfg.Controls });
        await db.SaveChangesAsync(ct);
        return cfg;
    }
}

[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(UsersService svc) : ControllerBase
{
    [HttpGet] public Task<List<UserDto>> List(CancellationToken ct) => svc.ListAsync(ct);
    [HttpGet("roles")] public Task<List<RoleDto>> Roles(CancellationToken ct) => svc.RolesAsync(ct);
    [HttpPut] public Task<UserDto> Upsert([FromBody] UserUpsertRequest r, CancellationToken ct) => svc.UpsertAsync(r, ct);
    [HttpPost("{id}/password")] public async Task<IActionResult> SetPassword(string id, [FromBody] SetPasswordRequest r, CancellationToken ct) { await svc.SetPasswordAsync(id, r.Password, ct); return Ok(new { ok = true }); }
    [HttpPost("{id}/unlock")] public async Task<IActionResult> Unlock(string id, CancellationToken ct) { await svc.UnlockAsync(id, ct); return Ok(new { ok = true }); }
}

[ApiController]
[Route("api/audit")]
[Authorize]
public class AuditController(AuditService audit, UserContext users) : ControllerBase
{
    [HttpGet]
    public async Task<List<AuditDto>> List([FromQuery] string? kind, [FromQuery] string? action, [FromQuery] string? entityType, [FromQuery] string? entityId, [FromQuery] string? q, [FromQuery] int limit = 300, CancellationToken ct = default)
    {
        // DF-01: the whole trail is for Audit, PD Office and the administrator; everybody else sees only their own actions
        var me = await users.RequireAsync(Domain.Entities.Permissions.View, ct);
        var own = Domain.Rules.AccessPolicy.Can(me, Domain.Entities.Permissions.Audit) ? null : me.Id;
        return await audit.ListAsync(kind, action, entityType, entityId, q, limit, ct, own);
    }
}
