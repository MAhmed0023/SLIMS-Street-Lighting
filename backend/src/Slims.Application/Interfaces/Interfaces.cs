using Microsoft.EntityFrameworkCore;
using Slims.Domain.Entities;

namespace Slims.Application.Interfaces;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<AppSetting> AppSettings { get; }
    DbSet<DocSequence> DocSequences { get; }
    DbSet<Vendor> Vendors { get; }
    DbSet<PurchaseOrder> PurchaseOrders { get; }
    DbSet<PurchaseOrderLine> PurchaseOrderLines { get; }
    DbSet<Article> Articles { get; }
    DbSet<Store> Stores { get; }
    DbSet<StoreLocation> StoreLocations { get; }
    DbSet<Site> Sites { get; }
    DbSet<Item> Items { get; }
    DbSet<Grn> Grns { get; }
    DbSet<DiscrepancyNote> DiscrepancyNotes { get; }
    DbSet<Requisition> Requisitions { get; }
    DbSet<RequisitionLine> RequisitionLines { get; }
    DbSet<Issue> Issues { get; }
    DbSet<Evidence> Evidence { get; }
    DbSet<Installation> Installations { get; }
    DbSet<ReturnNote> ReturnNotes { get; }
    DbSet<StockMovement> StockMovements { get; }
    DbSet<StockTransfer> StockTransfers { get; }
    DbSet<StockAdjustment> StockAdjustments { get; }
    DbSet<PhysicalCount> PhysicalCounts { get; }
    DbSet<AmendmentRequest> AmendmentRequests { get; }
    DbSet<Device> Devices { get; }
    DbSet<DeviceEnrolment> DeviceEnrolments { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<ReportSchedule> ReportSchedules { get; }
    DbSet<ReportRun> ReportRuns { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

/// <summary>Who is calling (from the validated JWT) and from where.</summary>
public interface ICurrentUser
{
    string? UserId { get; }
    string? UserAgent { get; }
    string? Ip { get; }
    /// <summary>The bound phone the session was opened from (claim "dev"), or null for a browser session.</summary>
    Guid? DeviceId { get; }
}

public interface IPasswordHasher
{
    (string Hash, string Salt) Hash(string password);
    bool Verify(string password, string? hash, string? salt);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateAccessToken(User user, Guid? deviceId = null);
    (string Token, DateTime ExpiresAt) CreateRefreshToken();
    string HashToken(string token);
}

public interface IFileStorage
{
    Task<string> SaveAsync(string folder, string fileName, Stream content, CancellationToken ct = default);
    Stream? Open(string storagePath);
    void Delete(string storagePath);
}

/// <summary>Draws the evidence stamp (FR-FLD-009) onto a copy of a photograph. The original bytes are never touched.</summary>
public interface IImageStamper
{
    /// <returns>JPEG bytes of the stamped copy, or null when the image cannot be decoded.</returns>
    byte[]? Stamp(byte[] image, IReadOnlyList<string> lines);
}

/// <summary>Outbound e-mail and SMS for notifications and scheduled reports (FR-NTF-002, FR-RPT-010).</summary>
public interface IMessageSender
{
    Task SendEmailAsync(Services.NotifySettings s, IEnumerable<string> to, string subject, string body, IEnumerable<(string Name, byte[] Content)>? attachments, CancellationToken ct);
    Task SendSmsAsync(Services.NotifySettings s, string phone, string text, CancellationToken ct);
}

/// <summary>Renders a QR symbol as PNG bytes.</summary>
public interface IQrRenderer
{
    byte[] Png(string payload, int pixelsPerModule = 6);
}
