using Microsoft.EntityFrameworkCore;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;

namespace Slims.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<DocSequence> DocSequences => Set<DocSequence>();
    public DbSet<Vendor> Vendors => Set<Vendor>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderLine> PurchaseOrderLines => Set<PurchaseOrderLine>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<StoreLocation> StoreLocations => Set<StoreLocation>();
    public DbSet<Site> Sites => Set<Site>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Grn> Grns => Set<Grn>();
    public DbSet<DiscrepancyNote> DiscrepancyNotes => Set<DiscrepancyNote>();
    public DbSet<Requisition> Requisitions => Set<Requisition>();
    public DbSet<RequisitionLine> RequisitionLines => Set<RequisitionLine>();
    public DbSet<Issue> Issues => Set<Issue>();
    public DbSet<Evidence> Evidence => Set<Evidence>();
    public DbSet<Installation> Installations => Set<Installation>();
    public DbSet<ReturnNote> ReturnNotes => Set<ReturnNote>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();
    public DbSet<StockTransfer> StockTransfers => Set<StockTransfer>();
    public DbSet<StockAdjustment> StockAdjustments => Set<StockAdjustment>();
    public DbSet<PhysicalCount> PhysicalCounts => Set<PhysicalCount>();
    public DbSet<AmendmentRequest> AmendmentRequests => Set<AmendmentRequest>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<DeviceEnrolment> DeviceEnrolments => Set<DeviceEnrolment>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<ReportRun> ReportRuns => Set<ReportRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Role>(e => { e.ToTable("Roles"); e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(40); e.Property(x => x.Label).HasMaxLength(80); e.Property(x => x.Perms).HasMaxLength(300); });
        b.Entity<User>(e =>
        {
            e.ToTable("Users"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasMaxLength(48); e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.Email).HasMaxLength(160); e.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
            e.Property(x => x.RoleKey).HasMaxLength(40); e.Property(x => x.Zone).HasMaxLength(40); e.Property(x => x.StoreIds).HasMaxLength(400); e.Property(x => x.SiteIds).HasMaxLength(2000);
            e.Property(x => x.EmployeeId).HasMaxLength(40); e.Property(x => x.Designation).HasMaxLength(120); e.Property(x => x.Phone).HasMaxLength(40);
            e.Property(x => x.PasswordHash).HasMaxLength(64); e.Property(x => x.PasswordSalt).HasMaxLength(32); e.Property(x => x.RefreshTokenHash).HasMaxLength(64); e.Property(x => x.NotifyPrefs).HasMaxLength(1000);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleKey).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<AuditLog>(e => { e.ToTable("AuditLogs"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(16); e.Property(x => x.Action).HasMaxLength(64); e.Property(x => x.EntityType).HasMaxLength(24); e.Property(x => x.EntityId).HasMaxLength(64); e.Property(x => x.ActorId).HasMaxLength(160); e.Property(x => x.ActorName).HasMaxLength(120); e.Property(x => x.Role).HasMaxLength(40); e.Property(x => x.UserAgent).HasMaxLength(120); e.Property(x => x.Ip).HasMaxLength(64); e.HasIndex(x => x.At).IsDescending(); e.HasIndex(x => new { x.EntityType, x.EntityId }); });
        b.Entity<AppSetting>(e => { e.ToTable("AppSettings"); e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(64); });
        b.Entity<DocSequence>(e => { e.ToTable("DocSequences"); e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(64); });

        b.Entity<Vendor>(e => { e.ToTable("Vendors"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(10); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Name).HasMaxLength(160); e.Property(x => x.Contact).HasMaxLength(120); e.Property(x => x.Phone).HasMaxLength(40); e.Property(x => x.Email).HasMaxLength(160); });
        b.Entity<PurchaseOrder>(e =>
        {
            e.ToTable("PurchaseOrders"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.HasIndex(x => x.Number).IsUnique(); e.Property(x => x.Status).HasMaxLength(12); e.Property(x => x.CreatedBy).HasMaxLength(48);
            e.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne(l => l.PurchaseOrder).HasForeignKey(l => l.PurchaseOrderId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<PurchaseOrderLine>(e =>
        {
            e.ToTable("PurchaseOrderLines"); e.HasKey(x => x.Id); e.Property(x => x.Category).HasMaxLength(4); e.Property(x => x.SubCategory).HasMaxLength(4); e.Property(x => x.Spec).HasMaxLength(6); e.Property(x => x.Description).HasMaxLength(200); e.Property(x => x.Uom).HasMaxLength(12);
            e.Property(x => x.Qty).HasPrecision(18, 3); e.Property(x => x.Rate).HasPrecision(18, 2);
            e.HasOne(x => x.Article).WithOne(a => a.PurchaseOrderLine).HasForeignKey<Article>(a => a.PurchaseOrderLineId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Article>(e =>
        {
            e.ToTable("Articles"); e.HasKey(x => x.Id); e.Property(x => x.ItemCode).HasMaxLength(48); e.HasIndex(x => x.ItemCode).IsUnique(); e.HasIndex(x => x.PurchaseOrderLineId).IsUnique();
            e.Property(x => x.Category).HasMaxLength(4); e.Property(x => x.SubCategory).HasMaxLength(4); e.Property(x => x.Spec).HasMaxLength(6); e.Property(x => x.Description).HasMaxLength(200); e.Property(x => x.Uom).HasMaxLength(12); e.Property(x => x.Status).HasMaxLength(12); e.Property(x => x.CreatedBy).HasMaxLength(48);
            e.Property(x => x.PackSize).HasPrecision(18, 3); e.Property(x => x.Rate).HasPrecision(18, 2); e.Property(x => x.ReorderLevel).HasPrecision(18, 3);
            e.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Vendor).WithMany().HasForeignKey(x => x.VendorId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Store>(e => { e.ToTable("Stores"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(8); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.Type).HasMaxLength(16); e.Property(x => x.Zone).HasMaxLength(40); e.Property(x => x.Address).HasMaxLength(240); e.Property(x => x.InChargeUserId).HasMaxLength(48); e.HasMany(x => x.Locations).WithOne(l => l.Store).HasForeignKey(l => l.StoreId).OnDelete(DeleteBehavior.Cascade); });
        b.Entity<StoreLocation>(e => { e.ToTable("StoreLocations"); e.HasKey(x => x.Id); e.Property(x => x.Section).HasMaxLength(24); e.Property(x => x.Rack).HasMaxLength(24); e.Property(x => x.Bin).HasMaxLength(24); e.Property(x => x.FullCode).HasMaxLength(40); e.HasIndex(x => x.FullCode).IsUnique(); e.Property(x => x.Kind).HasMaxLength(12); });
        b.Entity<Site>(e => { e.ToTable("Sites"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(32); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Name).HasMaxLength(200); e.Property(x => x.Zone).HasMaxLength(40); e.Property(x => x.Division).HasMaxLength(80); e.Property(x => x.Ward).HasMaxLength(80); e.Property(x => x.Type).HasMaxLength(24); e.Property(x => x.Status).HasMaxLength(16); e.Property(x => x.SupervisorUserId).HasMaxLength(48); e.Property(x => x.OfficerUserId).HasMaxLength(48); e.HasIndex(x => new { x.Zone, x.Division }); });

        b.Entity<Item>(e =>
        {
            e.ToTable("Items"); e.HasKey(x => x.Id); e.Property(x => x.QrCode).HasMaxLength(80); e.HasIndex(x => x.QrCode).IsUnique(); e.HasIndex(x => new { x.ArticleId, x.Serial }).IsUnique();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24); e.HasIndex(x => x.Status); e.HasIndex(x => x.StoreLocationId); e.HasIndex(x => x.SiteId); e.Property(x => x.Qty).HasPrecision(18, 3); e.Property(x => x.UpdatedBy).HasMaxLength(48);
            e.HasOne(x => x.Article).WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.StoreLocation).WithMany().HasForeignKey(x => x.StoreLocationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Grn).WithMany().HasForeignKey(x => x.GrnId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Grn>(e => { e.ToTable("Grns"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.HasIndex(x => x.Number).IsUnique(); e.Property(x => x.ChallanNo).HasMaxLength(60); e.Property(x => x.InvoiceNo).HasMaxLength(60); e.Property(x => x.PostedById).HasMaxLength(48); e.Property(x => x.PostedBy).HasMaxLength(120); e.Property(x => x.Qty).HasPrecision(18, 3); e.Property(x => x.Value).HasPrecision(18, 2); e.Property(x => x.Condition).HasMaxLength(60); e.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<DiscrepancyNote>(e => { e.ToTable("DiscrepancyNotes"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.Property(x => x.Kind).HasMaxLength(12); e.Property(x => x.Status).HasMaxLength(12); e.Property(x => x.Reason).HasMaxLength(400); e.Property(x => x.Resolution).HasMaxLength(400); e.Property(x => x.RaisedById).HasMaxLength(48); e.Property(x => x.Qty).HasPrecision(18, 3); e.HasOne(x => x.PurchaseOrder).WithMany().HasForeignKey(x => x.PurchaseOrderId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Article).WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<Requisition>(e =>
        {
            e.ToTable("Requisitions"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.HasIndex(x => x.Number).IsUnique(); e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16); e.HasIndex(x => x.Status);
            e.Property(x => x.RaisedById).HasMaxLength(48); e.Property(x => x.RaisedBy).HasMaxLength(120); e.Property(x => x.ApprovedById).HasMaxLength(48); e.Property(x => x.ApprovedBy).HasMaxLength(120); e.Property(x => x.Purpose).HasMaxLength(300); e.Property(x => x.DecisionNote).HasMaxLength(400); e.Property(x => x.IssueSlipNo).HasMaxLength(40); e.Property(x => x.Value).HasPrecision(18, 2); e.Property(x => x.RecommendedBy).HasMaxLength(600);
            e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Lines).WithOne().HasForeignKey(l => l.RequisitionId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<RequisitionLine>(e => { e.ToTable("RequisitionLines"); e.HasKey(x => x.Id); foreach (var p in new[] { "QtyRequested", "QtyApproved", "QtyIssued", "Shortfall" }) e.Property(p).HasPrecision(18, 3); e.HasOne(x => x.Article).WithMany().HasForeignKey(x => x.ArticleId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<Issue>(e =>
        {
            e.ToTable("Issues"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.HasIndex(x => x.Number).IsUnique(); e.Property(x => x.GatePassNo).HasMaxLength(40); e.Property(x => x.IssuedById).HasMaxLength(48); e.Property(x => x.IssuedBy).HasMaxLength(120); e.Property(x => x.IssuedTo).HasMaxLength(120); e.Property(x => x.Vehicle).HasMaxLength(60); e.Property(x => x.ReceivedById).HasMaxLength(48); e.Property(x => x.Qty).HasPrecision(18, 3);
            e.HasOne(x => x.Requisition).WithMany().HasForeignKey(x => x.RequisitionId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Evidence>(e => { e.ToTable("Evidence"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(12); e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.StoragePath).HasMaxLength(400); e.Property(x => x.ContentType).HasMaxLength(80); e.Property(x => x.UploadedById).HasMaxLength(48); e.Property(x => x.UploadedBy).HasMaxLength(120); e.Property(x => x.Ip).HasMaxLength(64); e.Property(x => x.Sha256).HasMaxLength(64); e.Property(x => x.StampedPath).HasMaxLength(400); });
        b.Entity<Installation>(e =>
        {
            e.ToTable("Installations"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(48); e.Property(x => x.GeoStatus).HasConversion<string>().HasMaxLength(24); e.HasIndex(x => x.GeoStatus); e.HasIndex(x => x.ItemId); e.HasIndex(x => x.SiteId);
            e.Property(x => x.OfficerId).HasMaxLength(48); e.Property(x => x.Officer).HasMaxLength(120); e.Property(x => x.PoleId).HasMaxLength(40); e.Property(x => x.VerifiedById).HasMaxLength(48); e.Property(x => x.VerifiedBy).HasMaxLength(120); e.Property(x => x.Justification).HasMaxLength(600); e.Property(x => x.Remarks).HasMaxLength(400);
            e.HasIndex(x => x.ClientTxnId).IsUnique().HasFilter("[ClientTxnId] IS NOT NULL"); e.Property(x => x.FvRequestedById).HasMaxLength(48); e.Property(x => x.FvRequestedBy).HasMaxLength(120); e.Property(x => x.FvRequestNote).HasMaxLength(400); e.Property(x => x.FvAssignedToId).HasMaxLength(48); e.Property(x => x.FvFindings).HasMaxLength(1000); e.Property(x => x.FvFindingsBy).HasMaxLength(120); e.Property(x => x.FvEvidenceIds).HasMaxLength(400);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Before).WithMany().HasForeignKey(x => x.BeforeEvidenceId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.After).WithMany().HasForeignKey(x => x.AfterEvidenceId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<ReturnNote>(e =>
        {
            e.ToTable("ReturnNotes"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.Property(x => x.Classification).HasConversion<string>().HasMaxLength(16); e.Property(x => x.Kind).HasMaxLength(16); e.Property(x => x.Reason).HasMaxLength(200); e.Property(x => x.Remarks).HasMaxLength(400);
            e.Property(x => x.ReportedById).HasMaxLength(48); e.Property(x => x.ReportedBy).HasMaxLength(120); e.Property(x => x.ReceivedById).HasMaxLength(48); e.Property(x => x.ReceivedBy).HasMaxLength(120); e.Property(x => x.ClassifiedById).HasMaxLength(48); e.Property(x => x.DisposalApprovedById).HasMaxLength(48); e.Property(x => x.ClaimRef).HasMaxLength(60); e.Property(x => x.ScrapValue).HasPrecision(18, 2);
            e.HasIndex(x => x.ItemId); e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Site).WithMany().HasForeignKey(x => x.SiteId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Evidence).WithMany().HasForeignKey(x => x.EvidenceId).OnDelete(DeleteBehavior.Restrict);
        });

        // ---- stock controls, amendments, devices, notifications, schedules --------------------------
        b.Entity<StockMovement>(e => { e.ToTable("StockMovements"); e.HasKey(x => x.Id); e.Property(x => x.Type).HasMaxLength(20); e.Property(x => x.Qty).HasPrecision(18, 3); e.Property(x => x.RefType).HasMaxLength(20); e.Property(x => x.RefNo).HasMaxLength(48); e.Property(x => x.UserId).HasMaxLength(48); e.HasIndex(x => new { x.StoreId, x.ArticleId, x.At }); e.HasIndex(x => x.ItemId); });
        b.Entity<StockTransfer>(e =>
        {
            e.ToTable("StockTransfers"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.HasIndex(x => x.Number).IsUnique(); e.Property(x => x.Status).HasMaxLength(16); e.Property(x => x.DespatchedById).HasMaxLength(48); e.Property(x => x.DespatchedBy).HasMaxLength(120); e.Property(x => x.ReceivedById).HasMaxLength(48); e.Property(x => x.ReceivedBy).HasMaxLength(120); e.Property(x => x.Vehicle).HasMaxLength(60); e.Property(x => x.Remarks).HasMaxLength(400); e.Property(x => x.Qty).HasPrecision(18, 3);
            e.HasOne(x => x.FromStore).WithMany().HasForeignKey(x => x.FromStoreId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.ToStore).WithMany().HasForeignKey(x => x.ToStoreId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<StockAdjustment>(e =>
        {
            e.ToTable("StockAdjustments"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.HasIndex(x => x.Number).IsUnique(); e.Property(x => x.Kind).HasMaxLength(16); e.Property(x => x.ReasonCode).HasMaxLength(60); e.Property(x => x.Remarks).HasMaxLength(400); e.Property(x => x.Status).HasMaxLength(12); e.HasIndex(x => x.Status);
            e.Property(x => x.QtyBefore).HasPrecision(18, 3); e.Property(x => x.QtyAfter).HasPrecision(18, 3); e.Property(x => x.RaisedById).HasMaxLength(48); e.Property(x => x.RaisedBy).HasMaxLength(120); e.Property(x => x.DecidedById).HasMaxLength(48); e.Property(x => x.DecidedBy).HasMaxLength(120); e.Property(x => x.DecisionNote).HasMaxLength(400);
            e.HasOne(x => x.Item).WithMany().HasForeignKey(x => x.ItemId).OnDelete(DeleteBehavior.Restrict); e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<PhysicalCount>(e => { e.ToTable("PhysicalCounts"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.HasIndex(x => x.Number).IsUnique(); e.Property(x => x.Status).HasMaxLength(12); e.Property(x => x.StartedById).HasMaxLength(48); e.Property(x => x.StartedBy).HasMaxLength(120); e.HasOne(x => x.Store).WithMany().HasForeignKey(x => x.StoreId).OnDelete(DeleteBehavior.Restrict); });
        b.Entity<AmendmentRequest>(e =>
        {
            e.ToTable("AmendmentRequests"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.HasIndex(x => x.Number).IsUnique(); e.Property(x => x.EntityType).HasMaxLength(20); e.Property(x => x.EntityRef).HasMaxLength(48); e.HasIndex(x => new { x.EntityType, x.EntityRef }); e.Property(x => x.Field).HasMaxLength(40); e.Property(x => x.OldValue).HasMaxLength(400); e.Property(x => x.NewValue).HasMaxLength(400); e.Property(x => x.Reason).HasMaxLength(400); e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.RaisedById).HasMaxLength(48); e.Property(x => x.RaisedBy).HasMaxLength(120); e.Property(x => x.DecidedById).HasMaxLength(48); e.Property(x => x.DecidedBy).HasMaxLength(120); e.Property(x => x.DecisionNote).HasMaxLength(400);
        });
        b.Entity<Device>(e =>
        {
            e.ToTable("Devices"); e.HasKey(x => x.Id); e.Property(x => x.UserId).HasMaxLength(48); e.HasIndex(x => x.UserId); e.Property(x => x.DeviceUid).HasMaxLength(80); e.Property(x => x.Model).HasMaxLength(80); e.Property(x => x.Manufacturer).HasMaxLength(80); e.Property(x => x.OsVersion).HasMaxLength(40); e.Property(x => x.AppVersion).HasMaxLength(20); e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.TokenHash).HasMaxLength(64); e.HasIndex(x => x.TokenHash); e.Property(x => x.ApprovedById).HasMaxLength(48); e.Property(x => x.RevokedById).HasMaxLength(48); e.Property(x => x.Note).HasMaxLength(300); e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<DeviceEnrolment>(e => { e.ToTable("DeviceEnrolments"); e.HasKey(x => x.Code); e.Property(x => x.Code).HasMaxLength(16); e.Property(x => x.UserId).HasMaxLength(48); e.Property(x => x.CreatedById).HasMaxLength(48); });
        b.Entity<Notification>(e =>
        {
            e.ToTable("Notifications"); e.HasKey(x => x.Id); e.Property(x => x.UserId).HasMaxLength(48); e.Property(x => x.Code).HasMaxLength(12); e.Property(x => x.Title).HasMaxLength(200); e.Property(x => x.Body).HasMaxLength(1000); e.Property(x => x.Link).HasMaxLength(200); e.Property(x => x.EntityType).HasMaxLength(20); e.Property(x => x.EntityRef).HasMaxLength(64);
            e.Property(x => x.DedupKey).HasMaxLength(160); e.Property(x => x.EmailStatus).HasMaxLength(10); e.Property(x => x.SmsStatus).HasMaxLength(10); e.Property(x => x.DeliveryError).HasMaxLength(400); e.HasIndex(x => new { x.UserId, x.ReadAt }); e.HasIndex(x => new { x.UserId, x.DedupKey });
        });
        b.Entity<ReportSchedule>(e => { e.ToTable("ReportSchedules"); e.HasKey(x => x.Id); e.Property(x => x.ReportKey).HasMaxLength(40); e.Property(x => x.Title).HasMaxLength(120); e.Property(x => x.Frequency).HasMaxLength(10); e.Property(x => x.Period).HasMaxLength(12); e.Property(x => x.Zone).HasMaxLength(40); e.Property(x => x.RecipientUserIds).HasMaxLength(600); e.Property(x => x.RecipientEmails).HasMaxLength(600); e.Property(x => x.CreatedById).HasMaxLength(48); });
        b.Entity<ReportRun>(e => { e.ToTable("ReportRuns"); e.HasKey(x => x.Id); e.Property(x => x.ReportKey).HasMaxLength(40); e.Property(x => x.Title).HasMaxLength(160); e.Property(x => x.FilePath).HasMaxLength(400); e.Property(x => x.Status).HasMaxLength(10); e.Property(x => x.Error).HasMaxLength(400); e.Property(x => x.RunAsUserId).HasMaxLength(48); });
    }
}
