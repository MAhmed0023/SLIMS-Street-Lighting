namespace Pera.Domain.Entities;

/// <summary>What a role may do. Checked on the server for every write — never by hiding buttons.
/// Approval and signing are NOT role permissions: they come from the authority matrix (designation × document type)
/// and from being the officer the route names, so a System Administrator gains no business authority (SR-SEC-08).</summary>
public static class Permissions
{
    public const string View = "view", CreateTask = "task", CreateDocument = "document", Workflow = "workflow",
        Configure = "configure", Report = "report", Export = "export", Audit = "audit";
    public static readonly string[] All = [View, CreateTask, CreateDocument, Workflow, Configure, Report, Export, Audit];
}

public class Role
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    /// <summary>Comma-separated permission keys, e.g. "view,task,document".</summary>
    public string Perms { get; set; } = "";
    public int SortOrder { get; set; }
    public bool Has(string perm) => PermList.Contains(perm);
    public string[] PermList => Perms.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public class User
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Email { get; set; }
    public string? EmployeeId { get; set; }
    public string? Phone { get; set; }
    public string RoleKey { get; set; } = "";
    public int? DesignationId { get; set; }
    public int? OrgUnitId { get; set; }
    /// <summary>Reporting officer. The chain upwards decides default routing, visibility and escalation (FR-002).</summary>
    public string? ReportsToId { get; set; }
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

    public Role? Role { get; set; }
    public Designation? Designation { get; set; }
    public OrgUnit? OrgUnit { get; set; }
}

/// <summary>Append-only audit trail. The API only ever inserts here (FR-025).</summary>
public class AuditLog
{
    public long Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Kind { get; set; } = "task";           // task | document | workflow | signature | auth | access | config | escalation
    public string Action { get; set; } = "";
    public string Result { get; set; } = "ok";           // ok | denied | failed
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? ActorId { get; set; }
    public string? ActorName { get; set; }
    /// <summary>Set when an acting officer performed the action under a delegation (FR-027).</summary>
    public string? ActingForId { get; set; }
    public string? Role { get; set; }
    public string? PrevJson { get; set; }
    public string? NextJson { get; set; }
    public string? Reason { get; set; }
    public string? CorrelationId { get; set; }
    public string? UserAgent { get; set; }
    public string? Ip { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Json { get; set; } = "{}";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>Gap-free reference numbering: one row per prefix, e.g. "PERA/TSK/26-27".</summary>
public class DocSequence
{
    public string Key { get; set; } = "";
    public int Next { get; set; } = 1;
}

/// <summary>In-app notification. Read state is recorded (FR-023); e-mail/SMS depend on approved integrations.</summary>
public class Notification
{
    public long Id { get; set; }
    public string UserId { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Kind { get; set; } = "";               // assigned | returned | comment | decision | deadline | escalation | signature | route
    public string Title { get; set; } = "";
    public string? Body { get; set; }
    public string? EntityType { get; set; }              // task | document
    public string? EntityId { get; set; }
    public string Channel { get; set; } = "inapp";
    public DateTime? ReadAt { get; set; }
}

/// <summary>An uploaded file. Bytes live on disk under uploads/; the database keeps the path and SHA-256.</summary>
public class StoredFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long Size { get; set; }
    public string Sha256 { get; set; } = "";
    public string Path { get; set; } = "";
    public string UploadedById { get; set; } = "";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
