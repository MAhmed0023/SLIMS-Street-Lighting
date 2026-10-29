using Microsoft.EntityFrameworkCore;
using Pera.Application.Interfaces;
using Pera.Domain.Entities;

namespace Pera.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<DocSequence> DocSequences => Set<DocSequence>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<StoredFile> Files => Set<StoredFile>();
    public DbSet<OrgUnit> OrgUnits => Set<OrgUnit>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<Delegation> Delegations => Set<Delegation>();
    public DbSet<TaskCategory> TaskCategories => Set<TaskCategory>();
    public DbSet<WorkTask> Tasks => Set<WorkTask>();
    public DbSet<TaskActivity> TaskActivities => Set<TaskActivity>();
    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();
    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();
    public DbSet<DocumentType> DocumentTypes => Set<DocumentType>();
    public DbSet<WorkflowTemplate> WorkflowTemplates => Set<WorkflowTemplate>();
    public DbSet<WorkflowTemplateStage> WorkflowTemplateStages => Set<WorkflowTemplateStage>();
    public DbSet<AuthorityRule> AuthorityRules => Set<AuthorityRule>();
    public DbSet<DocumentCase> Documents => Set<DocumentCase>();
    public DbSet<DocumentVersion> DocumentVersions => Set<DocumentVersion>();
    public DbSet<RouteStage> RouteStages => Set<RouteStage>();
    public DbSet<Movement> Movements => Set<Movement>();
    public DbSet<RouteAmendment> RouteAmendments => Set<RouteAmendment>();
    public DbSet<SignatureField> SignatureFields => Set<SignatureField>();
    public DbSet<EscalationLog> EscalationLogs => Set<EscalationLog>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // enums are stored by name so the database reads on its own and survives re-ordering
        c.Properties<Enum>().HaveConversion<string>().HaveMaxLength(24);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        const int Uid = 48;
        b.Entity<Role>(e => { e.ToTable("Roles"); e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(40); e.Property(x => x.Label).HasMaxLength(80); e.Property(x => x.Perms).HasMaxLength(300); });
        b.Entity<User>(e =>
        {
            e.ToTable("Users"); e.HasKey(x => x.Id); e.Property(x => x.Id).HasMaxLength(Uid); e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.Email).HasMaxLength(160);
            e.HasIndex(x => x.Email).IsUnique().HasFilter("[Email] IS NOT NULL");
            e.Property(x => x.RoleKey).HasMaxLength(40); e.Property(x => x.EmployeeId).HasMaxLength(40); e.Property(x => x.Phone).HasMaxLength(40); e.Property(x => x.ReportsToId).HasMaxLength(Uid);
            e.Property(x => x.PasswordHash).HasMaxLength(64); e.Property(x => x.PasswordSalt).HasMaxLength(32); e.Property(x => x.RefreshTokenHash).HasMaxLength(64);
            e.HasOne(x => x.Role).WithMany().HasForeignKey(x => x.RoleKey).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Designation).WithMany().HasForeignKey(x => x.DesignationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OrgUnit).WithMany().HasForeignKey(x => x.OrgUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ReportsToId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.ReportsToId);
        });
        b.Entity<AuditLog>(e =>
        {
            e.ToTable("AuditLogs"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(16); e.Property(x => x.Action).HasMaxLength(64); e.Property(x => x.Result).HasMaxLength(12);
            e.Property(x => x.EntityType).HasMaxLength(24); e.Property(x => x.EntityId).HasMaxLength(64); e.Property(x => x.ActorId).HasMaxLength(160); e.Property(x => x.ActorName).HasMaxLength(120);
            e.Property(x => x.ActingForId).HasMaxLength(Uid); e.Property(x => x.Role).HasMaxLength(40); e.Property(x => x.CorrelationId).HasMaxLength(64); e.Property(x => x.UserAgent).HasMaxLength(160); e.Property(x => x.Ip).HasMaxLength(64);
            e.HasIndex(x => x.At).IsDescending(); e.HasIndex(x => new { x.EntityType, x.EntityId }); e.HasIndex(x => x.ActorId);
        });
        b.Entity<AppSetting>(e => { e.ToTable("AppSettings"); e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(64); });
        b.Entity<DocSequence>(e => { e.ToTable("DocSequences"); e.HasKey(x => x.Key); e.Property(x => x.Key).HasMaxLength(64); });
        b.Entity<Notification>(e =>
        {
            e.ToTable("Notifications"); e.HasKey(x => x.Id); e.Property(x => x.UserId).HasMaxLength(Uid); e.Property(x => x.Kind).HasMaxLength(24); e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Body).HasMaxLength(1000); e.Property(x => x.EntityType).HasMaxLength(24); e.Property(x => x.EntityId).HasMaxLength(24); e.Property(x => x.Channel).HasMaxLength(12);
            e.HasIndex(x => new { x.UserId, x.ReadAt });
        });
        b.Entity<StoredFile>(e =>
        {
            e.ToTable("Files"); e.HasKey(x => x.Id); e.Property(x => x.FileName).HasMaxLength(200); e.Property(x => x.ContentType).HasMaxLength(120); e.Property(x => x.Sha256).HasMaxLength(64);
            e.Property(x => x.Path).HasMaxLength(400); e.Property(x => x.UploadedById).HasMaxLength(Uid);
        });

        b.Entity<OrgUnit>(e =>
        {
            e.ToTable("OrgUnits"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(24); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Name).HasMaxLength(160); e.Property(x => x.Kind).HasMaxLength(12);
            e.Property(x => x.HeadUserId).HasMaxLength(Uid);
            e.HasOne(x => x.Parent).WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<Designation>(e => { e.ToTable("Designations"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(24); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Title).HasMaxLength(120); });
        b.Entity<Delegation>(e =>
        {
            e.ToTable("Delegations"); e.HasKey(x => x.Id); e.Property(x => x.FromUserId).HasMaxLength(Uid); e.Property(x => x.ToUserId).HasMaxLength(Uid); e.Property(x => x.Reason).HasMaxLength(400);
            e.Property(x => x.CreatedById).HasMaxLength(Uid); e.Property(x => x.RevokedById).HasMaxLength(Uid);
            e.HasOne(x => x.FromUser).WithMany().HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ToUser).WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ToUserId, x.EndsOn });
        });

        b.Entity<TaskCategory>(e => { e.ToTable("TaskCategories"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(80); });
        b.Entity<WorkTask>(e =>
        {
            e.ToTable("Tasks"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.HasIndex(x => x.Number).IsUnique(); e.Property(x => x.Title).HasMaxLength(240);
            e.Property(x => x.Purpose).HasMaxLength(2000); e.Property(x => x.Deliverable).HasMaxLength(1000); e.Property(x => x.ProgressNote).HasMaxLength(2000); e.Property(x => x.NextAction).HasMaxLength(400);
            e.Property(x => x.Blocker).HasMaxLength(1000); e.Property(x => x.ClosureNote).HasMaxLength(1000);
            e.Property(x => x.OwnerId).HasMaxLength(Uid); e.Property(x => x.AssignedById).HasMaxLength(Uid); e.Property(x => x.ClosureAuthorityId).HasMaxLength(Uid); e.Property(x => x.CreatedById).HasMaxLength(Uid); e.Property(x => x.ClosedById).HasMaxLength(Uid);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedBy).WithMany().HasForeignKey(x => x.AssignedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ClosureAuthority).WithMany().HasForeignKey(x => x.ClosureAuthorityId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Parent).WithMany(p => p.Children).HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Activities).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Attachments).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Dependencies).WithOne().HasForeignKey(a => a.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.OwnerId, x.Status }); e.HasIndex(x => x.AssignedById); e.HasIndex(x => x.DueOn);
        });
        b.Entity<TaskActivity>(e =>
        {
            e.ToTable("TaskActivities"); e.HasKey(x => x.Id); e.Property(x => x.ActorId).HasMaxLength(Uid); e.Property(x => x.ActingForId).HasMaxLength(Uid); e.Property(x => x.Event).HasMaxLength(24);
            e.Property(x => x.OldValue).HasMaxLength(1000); e.Property(x => x.NewValue).HasMaxLength(1000); e.Property(x => x.Comment).HasMaxLength(4000); e.Property(x => x.Mentions).HasMaxLength(400);
            e.HasIndex(x => x.TaskId);
        });
        b.Entity<TaskAttachment>(e =>
        {
            e.ToTable("TaskAttachments"); e.HasKey(x => x.Id); e.Property(x => x.Kind).HasMaxLength(16); e.Property(x => x.Note).HasMaxLength(400);
            e.HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<TaskDependency>(e =>
        {
            e.ToTable("TaskDependencies"); e.HasKey(x => new { x.TaskId, x.DependsOnId });
            e.HasOne(x => x.DependsOn).WithMany().HasForeignKey(x => x.DependsOnId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DocumentType>(e =>
        {
            e.ToTable("DocumentTypes"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(16); e.HasIndex(x => x.Code).IsUnique(); e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.AllowedExtensions).HasMaxLength(200);
        });
        b.Entity<WorkflowTemplate>(e =>
        {
            e.ToTable("WorkflowTemplates"); e.HasKey(x => x.Id); e.Property(x => x.Code).HasMaxLength(24); e.HasIndex(x => new { x.Code, x.Version }).IsUnique(); e.Property(x => x.Name).HasMaxLength(160);
            e.Property(x => x.Status).HasMaxLength(12); e.Property(x => x.OwnerId).HasMaxLength(Uid);
            e.HasOne(x => x.DocumentType).WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Stages).WithOne().HasForeignKey(s => s.TemplateId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<WorkflowTemplateStage>(e => { e.ToTable("WorkflowTemplateStages"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.UserId).HasMaxLength(Uid); });
        b.Entity<AuthorityRule>(e =>
        {
            e.ToTable("AuthorityRules"); e.HasKey(x => x.Id); e.Property(x => x.Note).HasMaxLength(400);
            e.HasOne(x => x.Designation).WithMany().HasForeignKey(x => x.DesignationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.DocumentType).WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<DocumentCase>(e =>
        {
            e.ToTable("Documents"); e.HasKey(x => x.Id); e.Property(x => x.Number).HasMaxLength(40); e.HasIndex(x => x.Number).IsUnique(); e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.ExternalRef).HasMaxLength(120); e.Property(x => x.Notes).HasMaxLength(4000); e.Property(x => x.InitiatorId).HasMaxLength(Uid); e.Property(x => x.Outcome).HasMaxLength(24);
            e.HasOne(x => x.DocumentType).WithMany().HasForeignKey(x => x.DocumentTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Initiator).WithMany().HasForeignKey(x => x.InitiatorId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.OriginOrgUnit).WithMany().HasForeignKey(x => x.OriginOrgUnitId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Task).WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Versions).WithOne().HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Route).WithOne().HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Movements).WithOne().HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.SignatureFields).WithOne().HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(x => x.Amendments).WithOne().HasForeignKey(v => v.DocumentId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.InitiatorId, x.Status }); e.HasIndex(x => x.TaskId);
        });
        b.Entity<DocumentVersion>(e =>
        {
            e.ToTable("DocumentVersions"); e.HasKey(x => x.Id); e.Property(x => x.Note).HasMaxLength(600); e.Property(x => x.CreatedById).HasMaxLength(Uid);
            e.HasOne(x => x.File).WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.DocumentId, x.No }).IsUnique();
        });
        b.Entity<RouteStage>(e =>
        {
            e.ToTable("RouteStages"); e.HasKey(x => x.Id); e.Property(x => x.Name).HasMaxLength(120); e.Property(x => x.UserId).HasMaxLength(Uid); e.Property(x => x.ActedById).HasMaxLength(Uid);
            e.Property(x => x.ActingForId).HasMaxLength(Uid); e.Property(x => x.Comment).HasMaxLength(4000);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.Ignore(x => x.Blocking);
            e.HasIndex(x => new { x.UserId, x.Status });
        });
        b.Entity<Movement>(e =>
        {
            e.ToTable("Movements"); e.HasKey(x => x.Id); e.Property(x => x.FromUserId).HasMaxLength(Uid); e.Property(x => x.ToUserId).HasMaxLength(Uid); e.Property(x => x.ActingForId).HasMaxLength(Uid);
            e.Property(x => x.StageName).HasMaxLength(120); e.Property(x => x.Action).HasMaxLength(60); e.Property(x => x.ActionRequested).HasMaxLength(80); e.Property(x => x.Comment).HasMaxLength(4000);
            e.HasIndex(x => x.DocumentId); e.HasIndex(x => x.At);
        });
        b.Entity<RouteAmendment>(e =>
        {
            e.ToTable("RouteAmendments"); e.HasKey(x => x.Id); e.Property(x => x.RequestedById).HasMaxLength(Uid); e.Property(x => x.Reason).HasMaxLength(1000); e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.DecidedById).HasMaxLength(Uid); e.Property(x => x.DecisionNote).HasMaxLength(1000);
        });
        b.Entity<SignatureField>(e =>
        {
            e.ToTable("SignatureFields"); e.HasKey(x => x.Id); e.Property(x => x.SignerUserId).HasMaxLength(Uid); e.Property(x => x.DocumentHash).HasMaxLength(64); e.Property(x => x.SignedById).HasMaxLength(Uid);
            e.Property(x => x.ActingForId).HasMaxLength(Uid); e.Property(x => x.AuthMethod).HasMaxLength(60); e.Property(x => x.SignatureCode).HasMaxLength(16); e.Property(x => x.SignatureMac).HasMaxLength(64);
            e.Property(x => x.InvalidatedReason).HasMaxLength(600);
            e.HasIndex(x => x.SignatureCode);
        });
        b.Entity<EscalationLog>(e =>
        {
            e.ToTable("EscalationLogs"); e.HasKey(x => x.Id); e.Property(x => x.EntityType).HasMaxLength(12); e.Property(x => x.EntityId).HasMaxLength(24); e.Property(x => x.Reference).HasMaxLength(200);
            e.Property(x => x.Recipients).HasMaxLength(400); e.Property(x => x.Reason).HasMaxLength(600); e.HasIndex(x => x.At);
        });
    }
}
