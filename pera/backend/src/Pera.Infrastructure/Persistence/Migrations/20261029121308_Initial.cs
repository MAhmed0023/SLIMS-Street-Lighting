using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Pera.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Result = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ActorId = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    ActorName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    ActingForId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    PrevJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NextJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    Ip = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Designations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Designations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DocSequences",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Next = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocSequences", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "DocumentTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AllowedExtensions = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MaxSizeMb = table.Column<int>(type: "int", nullable: false),
                    RequireAttachment = table.Column<bool>(type: "bit", nullable: false),
                    EnforceAuthority = table.Column<bool>(type: "bit", nullable: false),
                    AllowSelfApproval = table.Column<bool>(type: "bit", nullable: false),
                    AllowParallel = table.Column<bool>(type: "bit", nullable: false),
                    AmendmentNeedsApproval = table.Column<bool>(type: "bit", nullable: false),
                    RequireApprovalStage = table.Column<bool>(type: "bit", nullable: false),
                    ApprovalCommentRequired = table.Column<bool>(type: "bit", nullable: false),
                    DefaultClassification = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    StageServiceHours = table.Column<int>(type: "int", nullable: false),
                    DefaultTemplateId = table.Column<int>(type: "int", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EscalationLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EntityType = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    EntityId = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Reference = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Level = table.Column<int>(type: "int", nullable: false),
                    Recipients = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EscalationLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Files",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UploadedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Files", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EntityType = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    EntityId = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    Channel = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrgUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    HeadUserId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrgUnits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrgUnits_OrgUnits_ParentId",
                        column: x => x.ParentId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Perms = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "TaskCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthorityRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: true),
                    DesignationId = table.Column<int>(type: "int", nullable: false),
                    Authority = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorityRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuthorityRules_Designations_DesignationId",
                        column: x => x.DesignationId,
                        principalTable: "Designations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AuthorityRules_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    EffectiveFrom = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EffectiveTo = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OwnerId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    AllowCustomization = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTemplates_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    EmployeeId = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    RoleKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DesignationId = table.Column<int>(type: "int", nullable: true),
                    OrgUnitId = table.Column<int>(type: "int", nullable: true),
                    ReportsToId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    PasswordSalt = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    PasswordSetAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MustChangePassword = table.Column<bool>(type: "bit", nullable: false),
                    FailedAttempts = table.Column<int>(type: "int", nullable: false),
                    LockedUntil = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLogin = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RefreshTokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    RefreshTokenExpires = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Designations_DesignationId",
                        column: x => x.DesignationId,
                        principalTable: "Designations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_OrgUnits_OrgUnitId",
                        column: x => x.OrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleKey",
                        column: x => x.RoleKey,
                        principalTable: "Roles",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Users_Users_ReportsToId",
                        column: x => x.ReportsToId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkflowTemplateStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Mandatory = table.Column<bool>(type: "bit", nullable: false),
                    AssigneeMode = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    DesignationId = table.Column<int>(type: "int", nullable: true),
                    ServiceHours = table.Column<int>(type: "int", nullable: true),
                    InitiatorMayChangeAssignee = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkflowTemplateStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkflowTemplateStages_WorkflowTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "WorkflowTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Delegations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromUserId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    ToUserId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: true),
                    StartsOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    EndsOn = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RevokedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Delegations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Delegations_Users_FromUserId",
                        column: x => x.FromUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Delegations_Users_ToUserId",
                        column: x => x.ToUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(240)", maxLength: 240, nullable: false),
                    Purpose = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: true),
                    Priority = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    OwnerId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    AssignedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    ClosureAuthorityId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    ParentId = table.Column<int>(type: "int", nullable: true),
                    OrgUnitId = table.Column<int>(type: "int", nullable: true),
                    DueOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Deliverable = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Instructions = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RequireDocumentsComplete = table.Column<bool>(type: "bit", nullable: false),
                    Progress = table.Column<int>(type: "int", nullable: false),
                    ProgressNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    NextAction = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ForecastOn = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Blocker = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    BlockedSince = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    ClosureNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReturnCount = table.Column<int>(type: "int", nullable: false),
                    EscalationLevel = table.Column<int>(type: "int", nullable: false),
                    RemindedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tasks_TaskCategories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "TaskCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Tasks_ParentId",
                        column: x => x.ParentId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Users_AssignedById",
                        column: x => x.AssignedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Users_ClosureAuthorityId",
                        column: x => x.ClosureAuthorityId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Tasks_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Documents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    DocumentTypeId = table.Column<int>(type: "int", nullable: false),
                    OriginOrgUnitId = table.Column<int>(type: "int", nullable: true),
                    Classification = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    TaskId = table.Column<int>(type: "int", nullable: true),
                    ExternalRef = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    InitiatorId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ApprovalState = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    SignatureState = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ActiveVersionId = table.Column<int>(type: "int", nullable: true),
                    TemplateId = table.Column<int>(type: "int", nullable: true),
                    TemplateVersion = table.Column<int>(type: "int", nullable: true),
                    RouteSnapshotJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReturnCount = table.Column<int>(type: "int", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Documents_DocumentTypes_DocumentTypeId",
                        column: x => x.DocumentTypeId,
                        principalTable: "DocumentTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_OrgUnits_OriginOrgUnitId",
                        column: x => x.OriginOrgUnitId,
                        principalTable: "OrgUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Documents_Users_InitiatorId",
                        column: x => x.InitiatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TaskActivities",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    ActingForId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    Event = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    OldValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ReplyToId = table.Column<long>(type: "bigint", nullable: true),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Mentions = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskActivities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskActivities_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Stage = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskAttachments_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskAttachments_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TaskDependencies",
                columns: table => new
                {
                    TaskId = table.Column<int>(type: "int", nullable: false),
                    DependsOnId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskDependencies", x => new { x.TaskId, x.DependsOnId });
                    table.ForeignKey(
                        name: "FK_TaskDependencies_Tasks_DependsOnId",
                        column: x => x.DependsOnId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TaskDependencies_Tasks_TaskId",
                        column: x => x.TaskId,
                        principalTable: "Tasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    No = table.Column<int>(type: "int", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    State = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Note = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true),
                    CreatedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentVersions_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentVersions_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Movements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FromUserId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    ToUserId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    ActingForId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    RouteStageId = table.Column<int>(type: "int", nullable: true),
                    StageName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Action = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ActionRequested = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    VersionNo = table.Column<int>(type: "int", nullable: true),
                    HeldHours = table.Column<double>(type: "float", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Movements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Movements_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RouteAmendments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    RequestedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    ProposalJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BeforeJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DecidedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteAmendments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteAmendments_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RouteStages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    Step = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Type = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    Mandatory = table.Column<bool>(type: "bit", nullable: false),
                    TemplateStageId = table.Column<int>(type: "int", nullable: true),
                    UserId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    AdHoc = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    ServiceHours = table.Column<int>(type: "int", nullable: true),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DueAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    ActingForId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: true),
                    Comment = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    VersionId = table.Column<int>(type: "int", nullable: true),
                    EscalationLevel = table.Column<int>(type: "int", nullable: false),
                    RemindedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RouteStages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RouteStages_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RouteStages_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SignatureFields",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DocumentId = table.Column<int>(type: "int", nullable: false),
                    RouteStageId = table.Column<int>(type: "int", nullable: true),
                    SignerUserId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false),
                    Page = table.Column<int>(type: "int", nullable: false),
                    X = table.Column<double>(type: "float", nullable: false),
                    Y = table.Column<double>(type: "float", nullable: false),
                    W = table.Column<double>(type: "float", nullable: false),
                    H = table.Column<double>(type: "float", nullable: false),
                    ShowName = table.Column<bool>(type: "bit", nullable: false),
                    ShowDate = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(24)", maxLength: 24, nullable: false),
                    VersionId = table.Column<int>(type: "int", nullable: true),
                    DocumentHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SignedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SignedById = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    ActingForId = table.Column<string>(type: "nvarchar(48)", maxLength: 48, nullable: true),
                    AuthMethod = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: true),
                    SignatureCode = table.Column<string>(type: "nvarchar(16)", maxLength: 16, nullable: true),
                    SignatureMac = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    SignedFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InvalidatedReason = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SignatureFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SignatureFields_Documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "Documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_ActorId",
                table: "AuditLogs",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_At",
                table: "AuditLogs",
                column: "At",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityType_EntityId",
                table: "AuditLogs",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthorityRules_DesignationId",
                table: "AuthorityRules",
                column: "DesignationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorityRules_DocumentTypeId",
                table: "AuthorityRules",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_FromUserId",
                table: "Delegations",
                column: "FromUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Delegations_ToUserId_EndsOn",
                table: "Delegations",
                columns: new[] { "ToUserId", "EndsOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Designations_Code",
                table: "Designations",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_DocumentTypeId",
                table: "Documents",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_InitiatorId_Status",
                table: "Documents",
                columns: new[] { "InitiatorId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Documents_Number",
                table: "Documents",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Documents_OriginOrgUnitId",
                table: "Documents",
                column: "OriginOrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Documents_TaskId",
                table: "Documents",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentTypes_Code",
                table: "DocumentTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_DocumentId_No",
                table: "DocumentVersions",
                columns: new[] { "DocumentId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentVersions_FileId",
                table: "DocumentVersions",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_EscalationLogs_At",
                table: "EscalationLogs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_At",
                table: "Movements",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_Movements_DocumentId",
                table: "Movements",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadAt",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnits_Code",
                table: "OrgUnits",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrgUnits_ParentId",
                table: "OrgUnits",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteAmendments_DocumentId",
                table: "RouteAmendments",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStages_DocumentId",
                table: "RouteStages",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_RouteStages_UserId_Status",
                table: "RouteStages",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SignatureFields_DocumentId",
                table: "SignatureFields",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_SignatureFields_SignatureCode",
                table: "SignatureFields",
                column: "SignatureCode");

            migrationBuilder.CreateIndex(
                name: "IX_TaskActivities_TaskId",
                table: "TaskActivities",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAttachments_FileId",
                table: "TaskAttachments",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskAttachments_TaskId",
                table: "TaskAttachments",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskDependencies_DependsOnId",
                table: "TaskDependencies",
                column: "DependsOnId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AssignedById",
                table: "Tasks",
                column: "AssignedById");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_CategoryId",
                table: "Tasks",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ClosureAuthorityId",
                table: "Tasks",
                column: "ClosureAuthorityId");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_DueOn",
                table: "Tasks",
                column: "DueOn");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Number",
                table: "Tasks",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_OwnerId_Status",
                table: "Tasks",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ParentId",
                table: "Tasks",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_DesignationId",
                table: "Users",
                column: "DesignationId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true,
                filter: "[Email] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_OrgUnitId",
                table: "Users",
                column: "OrgUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ReportsToId",
                table: "Users",
                column: "ReportsToId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleKey",
                table: "Users",
                column: "RoleKey");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplates_Code_Version",
                table: "WorkflowTemplates",
                columns: new[] { "Code", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplates_DocumentTypeId",
                table: "WorkflowTemplates",
                column: "DocumentTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkflowTemplateStages_TemplateId",
                table: "WorkflowTemplateStages",
                column: "TemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "AuthorityRules");

            migrationBuilder.DropTable(
                name: "Delegations");

            migrationBuilder.DropTable(
                name: "DocSequences");

            migrationBuilder.DropTable(
                name: "DocumentVersions");

            migrationBuilder.DropTable(
                name: "EscalationLogs");

            migrationBuilder.DropTable(
                name: "Movements");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "RouteAmendments");

            migrationBuilder.DropTable(
                name: "RouteStages");

            migrationBuilder.DropTable(
                name: "SignatureFields");

            migrationBuilder.DropTable(
                name: "TaskActivities");

            migrationBuilder.DropTable(
                name: "TaskAttachments");

            migrationBuilder.DropTable(
                name: "TaskDependencies");

            migrationBuilder.DropTable(
                name: "WorkflowTemplateStages");

            migrationBuilder.DropTable(
                name: "Documents");

            migrationBuilder.DropTable(
                name: "Files");

            migrationBuilder.DropTable(
                name: "WorkflowTemplates");

            migrationBuilder.DropTable(
                name: "Tasks");

            migrationBuilder.DropTable(
                name: "DocumentTypes");

            migrationBuilder.DropTable(
                name: "TaskCategories");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Designations");

            migrationBuilder.DropTable(
                name: "OrgUnits");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
