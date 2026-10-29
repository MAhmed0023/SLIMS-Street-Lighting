using Pera.Domain.Config;
using Pera.Domain.Entities;
using Pera.Domain.Rules;

namespace Pera.Application.Dtos;

// ---- identity -------------------------------------------------------------------------------------------
public record LoginRequest(string Id, string? Password);
public record ChangePasswordRequest(string? Old, string New);
public record SetPasswordRequest(string Password);
public record TokenResponse(string AccessToken, DateTime ExpiresAt, UserDto User, bool MustChangePassword);
public record UserDto(string Id, string Name, string? Email, string? EmployeeId, string? Phone, string RoleKey, string RoleLabel,
    int? DesignationId, string? Designation, int? OrgUnitId, string? OrgUnit, string? ReportsToId, string? ReportsTo,
    bool Active, bool HasPassword, bool MustChangePassword, DateTime? LastLogin, DateTime? LockedUntil, Dictionary<string, bool> Permissions);
public record UserPublicDto(string Id, string Name, string RoleKey, string RoleLabel, string? Designation, bool HasPassword, bool Active);
/// <summary>The short form used in pickers: route builder, assignment, delegation.</summary>
public record UserRefDto(string Id, string Name, string? Designation, string? OrgUnit, string? ReportsToId, bool Active);
public record UserUpsertRequest(string Id, string Name, string? Email, string? EmployeeId, string? Phone, string RoleKey, int? DesignationId, int? OrgUnitId, string? ReportsToId, bool Active);
public record RoleDto(string Key, string Label, string[] Perms, int SortOrder);
public record MetaDto(AppInfo App, bool LoginRequired, bool ShowUserPicker, bool FirstRun, DateTime ServerTime);
public record AuditDto(long Id, DateTime At, string Kind, string Action, string Result, string? EntityType, string? EntityId, string? ActorId, string? ActorName, string? ActingForId,
    string? Role, string? PrevJson, string? NextJson, string? Reason, string? CorrelationId, string? Ip);
public record ReasonRequest(string? Reason);

// ---- organisation ---------------------------------------------------------------------------------------
public record OrgUnitDto(int Id, string Code, string Name, string Kind, int? ParentId, string? ParentName, string? HeadUserId, string? HeadName, DateTime EffectiveFrom, DateTime? EffectiveTo, bool Active, int Members);
public record OrgUnitUpsert(int? Id, string Code, string Name, string Kind, int? ParentId, string? HeadUserId, DateTime? EffectiveFrom, DateTime? EffectiveTo, bool Active);
public record DesignationDto(int Id, string Code, string Title, int Rank, bool Active, int Holders);
public record DesignationUpsert(int? Id, string Code, string Title, int Rank, bool Active);
public record DelegationDto(int Id, string FromUserId, string FromName, string ToUserId, string ToName, string Scope, int? DocumentTypeId, string? DocumentType,
    DateTime StartsOn, DateTime EndsOn, string Reason, string CreatedById, DateTime CreatedAt, DateTime? RevokedAt, string State);
public record DelegationRequest(string FromUserId, string ToUserId, DelegationScope Scope, int? DocumentTypeId, DateTime StartsOn, DateTime EndsOn, string Reason);
public record CategoryDto(int Id, string Name, bool Active);

// ---- tasks ----------------------------------------------------------------------------------------------
public record TaskDto(int Id, string Number, string Title, string Priority, string Status, string? OwnerId, string? OwnerName, string AssignedById, string? AssignedByName,
    int? ParentId, string? ParentNumber, DateTime? DueOn, int Progress, bool Overdue, int OverdueDays, int? BlockedDays, int Children, int ChildrenDone,
    string? Category, string? OrgUnit, DateTime CreatedAt, DateTime? SubmittedAt, DateTime? ClosedAt, int EscalationLevel);
public record TaskActivityDto(long Id, DateTime At, string ActorId, string ActorName, string? ActingForId, string? ActingForName, string Event, string Stage,
    string? OldValue, string? NewValue, string? Comment, long? ReplyToId, Guid? FileId, string? FileName, string[] Mentions);
public record TaskAttachmentDto(int Id, Guid FileId, string FileName, string ContentType, long Size, string Sha256, string Kind, string? Note, string Stage, string UploadedBy, DateTime UploadedAt);
public record TaskRefDto(int Id, string Number, string Title, string Status);
public record DocumentRefDto(int Id, string Number, string Subject, string Status, string? CurrentHolder);
public record TaskDetailDto(TaskDto Task, string? Purpose, string? Instructions, string? Deliverable, string? ProgressNote, string? NextAction, DateTime? ForecastOn,
    string? Blocker, DateTime? BlockedSince, string? ClosureAuthorityId, string? ClosureAuthorityName, string? ClosedById, string? ClosedByName, string? ClosureNote,
    int ReturnCount, bool RequireDocumentsComplete, int? CategoryId, List<TaskRefDto> Ancestors, List<TaskDto> SubTasks, List<TaskRefDto> Dependencies,
    List<TaskActivityDto> Activity, List<TaskAttachmentDto> Attachments, List<DocumentRefDto> Documents, string[] AllowedActions, int OpenDependencies);
public record TaskUpsert(string Title, string? Purpose, int? CategoryId, TaskPriority Priority, string? OwnerId, string? ClosureAuthorityId, DateTime? DueOn,
    string? Deliverable, string? Instructions, int? ParentId, bool RequireDocumentsComplete, bool Assign);
public record TaskActionRequest(string? Reason, string? OwnerId);
public record TaskProgressRequest(int Progress, string? ProgressNote, string? NextAction, DateTime? ForecastOn);
public record CommentRequest(string Text, long? ReplyToId);
public record DependencyRequest(int DependsOnId);
public record SubTaskLine(string Title, string OwnerId, DateTime DueOn, string Deliverable, TaskPriority Priority, string? Instructions);
public record SubTasksRequest(List<SubTaskLine> Lines);

// ---- documents ------------------------------------------------------------------------------------------
public record DocumentTypeDto(int Id, string Code, string Name, string AllowedExtensions, int MaxSizeMb, bool RequireAttachment, bool EnforceAuthority, bool AllowSelfApproval,
    bool AllowParallel, bool AmendmentNeedsApproval, bool RequireApprovalStage, bool ApprovalCommentRequired, string DefaultClassification, int StageServiceHours, int? DefaultTemplateId, bool Active);
public record DocumentTypeUpsert(int? Id, string Code, string Name, string AllowedExtensions, int MaxSizeMb, bool RequireAttachment, bool EnforceAuthority, bool AllowSelfApproval,
    bool AllowParallel, bool AmendmentNeedsApproval, bool RequireApprovalStage, bool ApprovalCommentRequired, Classification DefaultClassification, int StageServiceHours, int? DefaultTemplateId, bool Active);
public record TemplateStageDto(int Id, int Step, string Name, string Type, bool Mandatory, string AssigneeMode, string? UserId, string? UserName, int? DesignationId, string? Designation, int? ServiceHours, bool InitiatorMayChangeAssignee);
public record TemplateStageUpsert(int Step, string Name, StageType Type, bool Mandatory, AssigneeMode AssigneeMode, string? UserId, int? DesignationId, int? ServiceHours, bool InitiatorMayChangeAssignee);
public record TemplateDto(int Id, string Code, string Name, int Version, int DocumentTypeId, string DocumentType, string Status, DateTime EffectiveFrom, DateTime? EffectiveTo,
    string OwnerId, string OwnerName, bool AllowCustomization, List<TemplateStageDto> Stages);
public record TemplateUpsert(int? Id, string Code, string Name, int DocumentTypeId, bool AllowCustomization, DateTime? EffectiveFrom, List<TemplateStageUpsert> Stages);
public record AuthorityRuleDto(int Id, int? DocumentTypeId, string? DocumentType, int DesignationId, string Designation, string Authority, DateTime EffectiveFrom, DateTime? EffectiveTo, bool Active, string? Note);
public record AuthorityRuleUpsert(int? Id, int? DocumentTypeId, int DesignationId, AuthorityKind Authority, DateTime? EffectiveFrom, DateTime? EffectiveTo, bool Active, string? Note);

public record DocumentDto(int Id, string Number, string Subject, int DocumentTypeId, string DocumentType, string Classification, string Status, string ApprovalState, string SignatureState,
    string InitiatorId, string InitiatorName, string? OriginOrgUnit, int? TaskId, string? TaskNumber, string? ExternalRef, string? CurrentHolders, string? CurrentStage,
    DateTime? HeldSince, DateTime? DueAt, bool Overdue, int ReturnCount, int? ActiveVersionNo, DateTime CreatedAt, DateTime? SubmittedAt, DateTime? CompletedAt, string? Outcome, bool AwaitingMe);
public record VersionDto(int Id, int No, string State, bool IsActive, Guid FileId, string FileName, string ContentType, long Size, string Sha256, string? Note, string CreatedBy, DateTime CreatedAt, int? Pages);
public record RouteStageDto(int Id, int Step, string Name, string Type, bool Mandatory, bool FromTemplate, bool AdHoc, string? UserId, string? UserName, string? Designation, string Status,
    DateTime? ReceivedAt, DateTime? DueAt, DateTime? ActedAt, string? ActedByName, string? ActingForName, string? Outcome, string? Comment, int? VersionNo, double? HeldHours, bool Overdue, bool CanChangeAssignee);
public record MovementDto(long Id, DateTime At, string? FromUserId, string? FromName, string? ToUserId, string? ToName, string? ActingForName, string? StageName, string Action,
    string? ActionRequested, string? Comment, int? VersionNo, double? HeldHours);
public record SignatureFieldDto(int Id, int? RouteStageId, string? StageName, string SignerUserId, string SignerName, int Order, int Page, double X, double Y, double W, double H,
    bool ShowName, bool ShowDate, string Status, int? VersionNo, string? DocumentHash, DateTime? SignedAt, string? SignedByName, string? ActingForName, string? AuthMethod,
    string? SignatureCode, string? InvalidatedReason);
public record AmendmentDto(int Id, string RequestedById, string RequestedBy, DateTime RequestedAt, string Reason, string Status, List<AmendmentOp> Ops, string? DecidedBy, DateTime? DecidedAt, string? DecisionNote, bool CanDecide);
public record StageActionsDto(int StageId, string StageName, string StageType, string[] Actions, string? ActingFor);
public record DocumentPermissionsDto(bool Edit, bool Submit, bool Withdraw, bool Revise, bool Inform, bool Amend, bool Verify, List<StageActionsDto> Stages);
public record DocumentDetailDto(DocumentDto Document, string? Notes, int? OriginOrgUnitId, int? TemplateId, string? TemplateName, int? TemplateVersion, List<VersionDto> Versions,
    List<RouteStageDto> Route, List<MovementDto> Movements, List<SignatureFieldDto> SignatureFields, List<AmendmentDto> Amendments, List<RouteIssue> RouteIssues,
    DocumentPermissionsDto Can, DocumentTypeDto Type);
public record DocumentUpsert(string Subject, int DocumentTypeId, int? OriginOrgUnitId, Classification Classification, int? TaskId, string? ExternalRef, string? Notes, int? TemplateId);
public record RouteLineRequest(int? Id, int Step, string Name, StageType Type, string? UserId);
public record RouteRequest(List<RouteLineRequest> Lines);
public record TemplateChoice(int TemplateId);
public record SignatureFieldRequest(int? Id, int RouteStageId, int Page, double X, double Y, double W, double H, bool ShowName, bool ShowDate);
public record SignatureFieldsRequest(List<SignatureFieldRequest> Fields);
public record SubmitRequest(bool AcceptWarnings);
public record StageActionRequest(StageAction Action, string? Comment, int VersionId);
public record SignRequest(string Password, int VersionId, bool Consent);
public record InformRequest(List<string> UserIds, string? Note);
public record AmendmentOp(string Op, int? StageId, int? Step, string? Name, StageType? Type, string? UserId);
public record AmendmentRequest(string Reason, List<AmendmentOp> Ops);
public record AmendmentDecision(bool Approve, string? Note);
public record SignatureCheckDto(int FieldId, string Signer, string? ActingFor, DateTime? SignedAt, string? Code, bool SealValid, bool DocumentUnchanged, bool SignedFileIntact, string Verdict);
public record VerificationDto(int DocumentId, string Number, DateTime CheckedAt, string Overall, List<SignatureCheckDto> Signatures, string Mechanism);

// ---- notifications, dashboards, reports -------------------------------------------------------------------
public record NotificationDto(long Id, DateTime At, string Kind, string Title, string? Body, string? EntityType, string? EntityId, DateTime? ReadAt);
public record CountDto(string Key, string Label, int Count);
public record OwnerLoadDto(string UserId, string Name, string? Designation, int Open, int Overdue, int Blocked, int Submitted, int Completed);
public record HolderLoadDto(string UserId, string Name, string? Designation, int Pending, double OldestHours, int Overdue);
public record StageTurnaroundDto(string StageType, int Completed, double AvgHours, double MaxHours);
public record QueueItemDto(string Kind, int Id, string Number, string Title, string Action, string? Priority, DateTime? DueAt, DateTime? ReceivedAt, bool Overdue, string? ActingFor);
public record DashboardDto(DateTime GeneratedAt, string Scope, List<QueueItemDto> MyQueue, List<CountDto> MyCounts, List<CountDto> TaskStatus, List<CountDto> TaskRisk,
    List<CountDto> Aging, List<OwnerLoadDto> OwnerLoad, List<CountDto> DocumentStatus, List<CountDto> DocumentRisk, List<HolderLoadDto> Bottlenecks,
    List<StageTurnaroundDto> Turnaround, List<TaskDto> BlockedTasks, int ReturnedDocuments, int ReworkCycles);
public record ReportColumn(string Key, string Label, string Kind = "text");
public record ReportResult(string Key, string Title, DateTime GeneratedAt, string GeneratedBy, Dictionary<string, string> Filters, List<ReportColumn> Columns, List<Dictionary<string, object?>> Rows);
public record ReportInfo(string Key, string Title, string Description, string[] Filters);
public record ReportQuery(DateTime? From, DateTime? To, string? UserId, int? OrgUnitId, string? Status, int? DocumentTypeId, string? Kind);
