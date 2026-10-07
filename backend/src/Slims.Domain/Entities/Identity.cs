namespace Slims.Domain.Entities;

/// <summary>What a role may do. Checked on the server for every write — never by hiding buttons.</summary>
public static class Permissions
{
    public const string View = "view", Receive = "receive", Requisition = "requisition", Approve = "approve", Issue = "issue",
        Install = "install", Verify = "verify", Return = "return", Procure = "procure", Configure = "configure", Export = "export", Audit = "audit";
    public static readonly string[] All = [View, Receive, Requisition, Approve, Issue, Install, Verify, Return, Procure, Configure, Export, Audit];
}

public class Role
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Comma-separated permission keys, e.g. "view,receive,issue".</summary>
    public string Perms { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Has(string perm) => Perms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains(perm);
    public string[] PermList => Perms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class User
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? EmployeeId { get; set; }
    public string? Designation { get; set; }
    public string? Phone { get; set; }
    public string RoleKey { get; set; } = "";
    /// <summary>null = every zone. Otherwise the user only sees stores and sites in this zone.</summary>
    public string? Zone { get; set; }
    /// <summary>Comma-separated store ids that narrow the user further; empty = all stores in zone.</summary>
    public string StoreIds { get; set; } = "";
    /// <summary>Comma-separated site ids that narrow the user further; empty = all sites in zone.</summary>
    public string SiteIds { get; set; } = "";
    public bool Active { get; set; } = true;

    public string? PasswordHash { get; set; }
    public string? PasswordSalt { get; set; }
    public DateTime? PasswordSetAt { get; set; }
    public bool MustChangePassword { get; set; }
    public int FailedAttempts { get; set; }
    public DateTime? LockedUntil { get; set; }
    public DateTime? LastLogin { get; set; }
    public string? RefreshTokenHash { get; set; }
    public DateTime? RefreshTokenExpires { get; set; }
    /// <summary>JSON: {"muted":["NTF-14"],"email":true,"sms":true}. Assigned-work notices cannot be muted.</summary>
    public string NotifyPrefs { get; set; } = "{}";

    public Role? Role { get; set; }
}

/// <summary>Append-only audit trail. The API only ever inserts here.</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Kind { get; set; } = "stock";          // stock | auth | master | access | config
    public string Action { get; set; } = "";
    public string? EntityType { get; set; }              // item | grn | requisition | issue | installation | return | site | store | article | user
    public string? EntityId { get; set; }
    public string? ActorId { get; set; }
    public string? ActorName { get; set; }
    public string? Role { get; set; }
    public string? PrevJson { get; set; }
    public string? NextJson { get; set; }
    public string? Reason { get; set; }
    public string? UserAgent { get; set; }
    public string? Ip { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Json { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Gap-free document numbering: one row per prefix, e.g. "GRN/CEN01/26-27".</summary>
public class DocSequence
{
    public string Key { get; set; } = "";
    public int Next { get; set; } = 1;
}
