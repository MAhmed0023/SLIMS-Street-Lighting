// Mirrors Pera.Application.Dtos. Enums arrive as their names.

export interface AppInfo { name: string; short: string; client: string; agency: string }
export interface AppConfig {
  version: number; app: AppInfo; loginRequired: boolean; showUserPicker: boolean;
  alerts: { reminderHours: number; escalate1Hours: number; escalate2Hours: number; workerMinutes: number; dueSoonDays: number };
  files: { blockedExtensions: string; taskAttachmentMaxMb: number; taskAttachmentExtensions: string };
}
export interface MetaDto { app: AppInfo; loginRequired: boolean; showUserPicker: boolean; firstRun: boolean; serverTime: string }
export interface UserDto {
  id: string; name: string; email: string | null; employeeId: string | null; phone: string | null; roleKey: string; roleLabel: string;
  designationId: number | null; designation: string | null; orgUnitId: number | null; orgUnit: string | null; reportsToId: string | null; reportsTo: string | null;
  active: boolean; hasPassword: boolean; mustChangePassword: boolean; lastLogin: string | null; lockedUntil: string | null; permissions: Record<string, boolean>;
}
export interface UserPublicDto { id: string; name: string; roleKey: string; roleLabel: string; designation: string | null; hasPassword: boolean; active: boolean }
export interface UserRefDto { id: string; name: string; designation: string | null; orgUnit: string | null; reportsToId: string | null; active: boolean }
export interface TokenResponse { accessToken: string; expiresAt: string; user: UserDto; mustChangePassword: boolean }
export interface RoleDto { key: string; label: string; perms: string[]; sortOrder: number }
export interface AuditDto { id: number; at: string; kind: string; action: string; result: string; entityType: string | null; entityId: string | null; actorId: string | null; actorName: string | null; actingForId: string | null; role: string | null; prevJson: string | null; nextJson: string | null; reason: string | null; correlationId: string | null; ip: string | null }

export interface OrgUnitDto { id: number; code: string; name: string; kind: string; parentId: number | null; parentName: string | null; headUserId: string | null; headName: string | null; effectiveFrom: string; effectiveTo: string | null; active: boolean; members: number }
export interface DesignationDto { id: number; code: string; title: string; rank: number; active: boolean; holders: number }
export interface DelegationDto { id: number; fromUserId: string; fromName: string; toUserId: string; toName: string; scope: string; documentTypeId: number | null; documentType: string | null; startsOn: string; endsOn: string; reason: string; createdById: string; createdAt: string; revokedAt: string | null; state: string }
export interface CategoryDto { id: number; name: string; active: boolean }

export interface TaskDto {
  id: number; number: string; title: string; priority: string; status: string; ownerId: string | null; ownerName: string | null; assignedById: string; assignedByName: string | null;
  parentId: number | null; parentNumber: string | null; dueOn: string | null; progress: number; overdue: boolean; overdueDays: number; blockedDays: number | null; children: number; childrenDone: number;
  category: string | null; orgUnit: string | null; createdAt: string; submittedAt: string | null; closedAt: string | null; escalationLevel: number;
}
export interface TaskActivityDto { id: number; at: string; actorId: string; actorName: string; actingForId: string | null; actingForName: string | null; event: string; stage: string; oldValue: string | null; newValue: string | null; comment: string | null; replyToId: number | null; fileId: string | null; fileName: string | null; mentions: string[] }
export interface TaskAttachmentDto { id: number; fileId: string; fileName: string; contentType: string; size: number; sha256: string; kind: string; note: string | null; stage: string; uploadedBy: string; uploadedAt: string }
export interface TaskRefDto { id: number; number: string; title: string; status: string }
export interface DocumentRefDto { id: number; number: string; subject: string; status: string; currentHolder: string | null }
export interface TaskDetailDto {
  task: TaskDto; purpose: string | null; instructions: string | null; deliverable: string | null; progressNote: string | null; nextAction: string | null; forecastOn: string | null;
  blocker: string | null; blockedSince: string | null; closureAuthorityId: string | null; closureAuthorityName: string | null; closedById: string | null; closedByName: string | null; closureNote: string | null;
  returnCount: number; requireDocumentsComplete: boolean; categoryId: number | null; ancestors: TaskRefDto[]; subTasks: TaskDto[]; dependencies: TaskRefDto[]; activity: TaskActivityDto[];
  attachments: TaskAttachmentDto[]; documents: DocumentRefDto[]; allowedActions: string[]; openDependencies: number;
}

export interface DocumentTypeDto { id: number; code: string; name: string; allowedExtensions: string; maxSizeMb: number; requireAttachment: boolean; enforceAuthority: boolean; allowSelfApproval: boolean; allowParallel: boolean; amendmentNeedsApproval: boolean; requireApprovalStage: boolean; approvalCommentRequired: boolean; defaultClassification: string; stageServiceHours: number; defaultTemplateId: number | null; active: boolean }
export interface TemplateStageDto { id: number; step: number; name: string; type: string; mandatory: boolean; assigneeMode: string; userId: string | null; userName: string | null; designationId: number | null; designation: string | null; serviceHours: number | null; initiatorMayChangeAssignee: boolean }
export interface TemplateDto { id: number; code: string; name: string; version: number; documentTypeId: number; documentType: string; status: string; effectiveFrom: string; effectiveTo: string | null; ownerId: string; ownerName: string; allowCustomization: boolean; stages: TemplateStageDto[] }
export interface AuthorityRuleDto { id: number; documentTypeId: number | null; documentType: string | null; designationId: number; designation: string; authority: string; effectiveFrom: string; effectiveTo: string | null; active: boolean; note: string | null }

export interface DocumentDto {
  id: number; number: string; subject: string; documentTypeId: number; documentType: string; classification: string; status: string; approvalState: string; signatureState: string;
  initiatorId: string; initiatorName: string; originOrgUnit: string | null; taskId: number | null; taskNumber: string | null; externalRef: string | null; currentHolders: string | null; currentStage: string | null;
  heldSince: string | null; dueAt: string | null; overdue: boolean; returnCount: number; activeVersionNo: number | null; createdAt: string; submittedAt: string | null; completedAt: string | null; outcome: string | null; awaitingMe: boolean;
}
export interface VersionDto { id: number; no: number; state: string; isActive: boolean; fileId: string; fileName: string; contentType: string; size: number; sha256: string; note: string | null; createdBy: string; createdAt: string; pages: number | null }
export interface RouteStageDto { id: number; step: number; name: string; type: string; mandatory: boolean; fromTemplate: boolean; adHoc: boolean; userId: string | null; userName: string | null; designation: string | null; status: string; receivedAt: string | null; dueAt: string | null; actedAt: string | null; actedByName: string | null; actingForName: string | null; outcome: string | null; comment: string | null; versionNo: number | null; heldHours: number | null; overdue: boolean; canChangeAssignee: boolean }
export interface MovementDto { id: number; at: string; fromUserId: string | null; fromName: string | null; toUserId: string | null; toName: string | null; actingForName: string | null; stageName: string | null; action: string; actionRequested: string | null; comment: string | null; versionNo: number | null; heldHours: number | null }
export interface SignatureFieldDto { id: number; routeStageId: number | null; stageName: string | null; signerUserId: string; signerName: string; order: number; page: number; x: number; y: number; w: number; h: number; showName: boolean; showDate: boolean; status: string; versionNo: number | null; documentHash: string | null; signedAt: string | null; signedByName: string | null; actingForName: string | null; authMethod: string | null; signatureCode: string | null; invalidatedReason: string | null }
export interface AmendmentOp { op: string; stageId: number | null; step: number | null; name: string | null; type: string | null; userId: string | null }
export interface AmendmentDto { id: number; requestedById: string; requestedBy: string; requestedAt: string; reason: string; status: string; ops: AmendmentOp[]; decidedBy: string | null; decidedAt: string | null; decisionNote: string | null; canDecide: boolean }
export interface RouteIssue { severity: string; code: string; message: string; index: number | null; isError: boolean }
export interface StageActionsDto { stageId: number; stageName: string; stageType: string; actions: string[]; actingFor: string | null }
export interface DocumentPermissionsDto { edit: boolean; submit: boolean; withdraw: boolean; revise: boolean; inform: boolean; amend: boolean; verify: boolean; stages: StageActionsDto[] }
export interface DocumentDetailDto {
  document: DocumentDto; notes: string | null; originOrgUnitId: number | null; templateId: number | null; templateName: string | null; templateVersion: number | null; versions: VersionDto[];
  route: RouteStageDto[]; movements: MovementDto[]; signatureFields: SignatureFieldDto[]; amendments: AmendmentDto[]; routeIssues: RouteIssue[]; can: DocumentPermissionsDto; type: DocumentTypeDto;
}
export interface SignatureCheckDto { fieldId: number; signer: string; actingFor: string | null; signedAt: string | null; code: string | null; sealValid: boolean; documentUnchanged: boolean; signedFileIntact: boolean; verdict: string }
export interface VerificationDto { documentId: number; number: string; checkedAt: string; overall: string; signatures: SignatureCheckDto[]; mechanism: string }

export interface NotificationDto { id: number; at: string; kind: string; title: string; body: string | null; entityType: string | null; entityId: string | null; readAt: string | null }
export interface CountDto { key: string; label: string; count: number }
export interface OwnerLoadDto { userId: string; name: string; designation: string | null; open: number; overdue: number; blocked: number; submitted: number; completed: number }
export interface HolderLoadDto { userId: string; name: string; designation: string | null; pending: number; oldestHours: number; overdue: number }
export interface StageTurnaroundDto { stageType: string; completed: number; avgHours: number; maxHours: number }
export interface QueueItemDto { kind: string; id: number; number: string; title: string; action: string; priority: string | null; dueAt: string | null; receivedAt: string | null; overdue: boolean; actingFor: string | null }
export interface DashboardDto {
  generatedAt: string; scope: string; myQueue: QueueItemDto[]; myCounts: CountDto[]; taskStatus: CountDto[]; taskRisk: CountDto[]; aging: CountDto[]; ownerLoad: OwnerLoadDto[];
  documentStatus: CountDto[]; documentRisk: CountDto[]; bottlenecks: HolderLoadDto[]; turnaround: StageTurnaroundDto[]; blockedTasks: TaskDto[]; returnedDocuments: number; reworkCycles: number;
}
export interface ReportColumn { key: string; label: string; kind: string }
export interface ReportResult { key: string; title: string; generatedAt: string; generatedBy: string; filters: Record<string, string>; columns: ReportColumn[]; rows: Record<string, unknown>[] }
export interface ReportInfo { key: string; title: string; description: string; filters: string[] }
