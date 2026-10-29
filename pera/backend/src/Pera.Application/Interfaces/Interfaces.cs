using Microsoft.EntityFrameworkCore;
using Pera.Domain.Entities;

namespace Pera.Application.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<AppSetting> AppSettings { get; }
    DbSet<DocSequence> DocSequences { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<StoredFile> Files { get; }
    DbSet<OrgUnit> OrgUnits { get; }
    DbSet<Designation> Designations { get; }
    DbSet<Delegation> Delegations { get; }
    DbSet<TaskCategory> TaskCategories { get; }
    DbSet<WorkTask> Tasks { get; }
    DbSet<TaskActivity> TaskActivities { get; }
    DbSet<TaskAttachment> TaskAttachments { get; }
    DbSet<TaskDependency> TaskDependencies { get; }
    DbSet<DocumentType> DocumentTypes { get; }
    DbSet<WorkflowTemplate> WorkflowTemplates { get; }
    DbSet<WorkflowTemplateStage> WorkflowTemplateStages { get; }
    DbSet<AuthorityRule> AuthorityRules { get; }
    DbSet<DocumentCase> Documents { get; }
    DbSet<DocumentVersion> DocumentVersions { get; }
    DbSet<RouteStage> RouteStages { get; }
    DbSet<Movement> Movements { get; }
    DbSet<RouteAmendment> RouteAmendments { get; }
    DbSet<SignatureField> SignatureFields { get; }
    DbSet<EscalationLog> EscalationLogs { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Who is calling (from the validated JWT) and from where.</summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? UserAgent { get; }
    string? Ip { get; }
    /// <summary>One id per HTTP request, written on every audit row of that request.</summary>
    string? CorrelationId { get; }
}

public interface IPasswordHasher
{
    (string Hash, string Salt) Hash(string password);
    bool Verify(string password, string? hash, string? salt);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user);
    (string Token, DateTime ExpiresAt) CreateRefreshToken();
    string HashToken(string token);
}

public interface IFileStorage
{
    Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken ct = default);
    Stream? Open(string storagePath);
}

/// <summary>Malware scanning hook for uploads (SR-SEC-05). Returns the threat name, or null when clean.
/// The default implementation reports "not configured" in the log and passes; PERA IT plugs in the approved scanner.</summary>
public interface IFileScanner
{
    Task<string?> ScanAsync(byte[] content, string fileName, CancellationToken ct = default);
}

/// <summary>Visible signature block drawn on a copy of a PDF. Coordinates are normalised 0–1 from the page's top-left.</summary>
public record StampSpec(int Page, double X, double Y, double W, double H, IReadOnlyList<string> Lines);

public interface IPdfStamper
{
    /// <returns>The stamped copy, or null when the file is not a PDF this stamper can read.</returns>
    byte[]? Stamp(byte[] pdf, IReadOnlyList<StampSpec> stamps);
    int? PageCount(byte[] pdf);
}

/// <summary>Secret for the signature seal, held outside source control (SR-SEC-04).</summary>
public interface ISigningKey
{
    byte[] Key { get; }
}

public interface IReportWriter
{
    byte[] Xlsx(Dtos.ReportResult report);
}
