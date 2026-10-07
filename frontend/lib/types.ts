// Mirrors the API DTOs (backend/src/Slims.Application/Dtos/Dtos.cs). JSON is camelCase.

export type Permissions = Record<string, boolean>;

export interface AppInfo { name: string; short: string; client: string; agency: string; currency: string }
export interface GeoSettings { defaultGeofenceM: number; toleranceM: number; minAccuracyM: number; maxClockDriftMinutes: number }
export interface ControlSettings { returnDays: number; reservationDays: number; ackOverdueHours: number; receiptTolerancePct: number; ageingDays: number; evidenceMaxMB: number }
export interface CategoryDef { code: string; label: string; subs: string[] }
export interface ApprovalLevel { level: number; roleKey: string; label: string; maxValue: number | null }
export interface AlertSettings { approvalEscalateHours: number; installPendingDays: number; exceptionEscalateDays: number; recaptureEscalateDays: number; gpsMismatchCount: number; gpsMismatchDays: number; returnRatePct: number; unresolvedSiteDays: number; turnaroundHours: number; workerMinutes: number }
export interface FieldSettings { mobileSetupRequired: boolean; allowWebCapture: boolean; enrolmentMinutes: number; setupMaxAccuracyM: number; offlineRetentionDays: number; phoneServerUrl?: string }
export interface AppConfig {
  version: number; app: AppInfo; geo: GeoSettings; controls: ControlSettings;
  categories: CategoryDef[]; uoms: string[]; zones: string[]; siteTypes: string[]; failureReasons: string[];
  receiptConditions: string[]; rejectReasons: string[]; adjustmentReasons: string[]; approvalLevels: ApprovalLevel[]; escalateTo: Record<string, string>;
  alerts: AlertSettings; field: FieldSettings;
  loginRequired: boolean; showUserPicker: boolean;
}
export interface MetaDto { app: AppInfo; loginRequired: boolean; showUserPicker: boolean; firstRun: boolean; serverTime: string }

export interface UserDto {
  id: string; name: string; email?: string | null; employeeId?: string | null; designation?: string | null; phone?: string | null;
  roleKey: string; roleLabel: string; zone?: string | null; storeIds: number[]; siteIds: number[]; active: boolean;
  hasPassword: boolean; mustChangePassword: boolean; lastLogin?: string | null; lockedUntil?: string | null;
  permissions: Permissions; scopeDescription: string;
}
export interface UserPublicDto { id: string; name: string; roleKey: string; roleLabel: string; designation?: string | null; hasPassword: boolean; active: boolean }
export interface TokenResponse { accessToken: string; expiresAt: string; user: UserDto; mustChangePassword: boolean }
export interface UserUpsertRequest { id: string; name: string; email?: string | null; employeeId?: string | null; designation?: string | null; phone?: string | null; roleKey: string; zone?: string | null; storeIds?: number[]; siteIds?: number[]; active: boolean }
export interface RoleDto { key: string; label: string; perms: string[]; sortOrder: number }

export interface VendorDto { id: number; code: string; name: string; contact?: string | null; phone?: string | null; email?: string | null; active: boolean; purchaseOrders: number }
export interface PoLineDto { id: number; lineNo: number; category: string; subCategory: string; spec: string; description: string; uom: string; qty: number; rate: number; articleId?: number | null; itemCode?: string | null; received: number; issued: number }
export interface PoDto { id: number; number: string; vendorId: number; vendorName: string; date: string; deliveryDue?: string | null; warrantyMonths: number; status: string; remarks?: string | null; value: number; lines: PoLineDto[] }
export interface PoLineUpsert { category: string; subCategory: string; spec: string; description: string; uom: string; qty: number; rate: number }
export interface PoUpsertRequest { number: string; vendorId: number; date: string; deliveryDue?: string | null; warrantyMonths: number; remarks?: string | null; lines: PoLineUpsert[] }
export interface ArticleDto {
  id: number; itemCode: string; category: string; subCategory: string; spec: string; description: string; uom: string; serialised: boolean; packSize: number;
  purchaseOrderId: number; poNumber: string; vendorId: number; vendorName: string; rate: number; warrantyMonths: number; expectedLifeMonths?: number | null;
  reorderLevel: number; status: string; itemsGenerated: number; poQty: number; received: number; onHand: number; reserved: number; installed: number; quarantine: number; createdAt: string;
}
export interface LocationDto { id: number; storeId: number; storeCode: string; section: string; rack: string; bin: string; fullCode: string; kind: string; active: boolean; items: number }
export interface StoreDto { id: number; code: string; name: string; type: string; zone: string; address?: string | null; lat?: number | null; lng?: number | null; inChargeUserId?: string | null; inChargeName?: string | null; active: boolean; items: number; value: number; locations: LocationDto[] }
export interface StoreUpsert { code: string; name: string; type: string; zone: string; address?: string | null; lat?: number | null; lng?: number | null; inChargeUserId?: string | null; active: boolean }
export interface SiteDto {
  id: number; code: string; name: string; zone: string; division: string; ward?: string | null; type: string; lat: number; lng: number; geofenceM: number;
  supervisorUserId?: string | null; supervisorName?: string | null; officerUserId?: string | null; officerName?: string | null; status: string; remarks?: string | null;
  installed: number; atSite: number; pending: number; createdAt: string;
}
export interface SiteUpsert { code?: string | null; name: string; zone: string; division: string; ward?: string | null; type: string; lat: number; lng: number; geofenceM?: number | null; supervisorUserId?: string | null; officerUserId?: string | null; status: string; remarks?: string | null }
export interface BulkResultDto { created: number; updated: number; errors: string[] }

export interface ItemDto {
  id: string; qrCode: string; articleId: number; itemCode: string; description: string; category: string; serial: number; qty: number; uom: string; status: string;
  storeId?: number | null; storeCode?: string | null; locationId?: number | null; locationCode?: string | null; siteId?: number | null; siteCode?: string | null; siteName?: string | null;
  poNumber: string; vendorName: string; rate: number; warrantyMonths: number; warrantyExpires?: string | null;
  grnId?: number | null; requisitionId?: number | null; issueId?: number | null; installationId?: number | null; returnNoteId?: number | null;
  receivedAt?: string | null; issuedAt?: string | null; installedAt?: string | null; returnedAt?: string | null; labelPrints: number; updatedAt: string; updatedBy: string;
}
export interface ItemHistoryDto { at: string; action: string; by?: string | null; role?: string | null; reason?: string | null; nextJson?: string | null }
export interface ItemDetailDto { item: ItemDto; history: ItemHistoryDto[]; installation?: InstallationDto | null; return?: ReturnDto | null }
export interface ScanResultDto { ok: boolean; message: string; item?: ItemDto | null; step?: string | null }

export interface GrnDto { id: number; number: string; purchaseOrderId: number; poNumber: string; vendorName: string; storeId: number; storeCode: string; challanNo?: string | null; invoiceNo?: string | null; invoiceDate?: string | null; postedBy: string; postedAt: string; itemCount: number; qty: number; value: number; remarks?: string | null; reversed: boolean; condition?: string | null; rejectedCount: number }
export interface DiscrepancyDto { id: number; number: string; purchaseOrderId: number; poNumber: string; vendorName: string; articleId?: number | null; itemCode?: string | null; kind: string; qty: number; reason: string; status: string; raisedAt: string; resolution?: string | null; resolvedAt?: string | null }

export interface ReqLineDto { id: number; articleId: number; itemCode: string; description: string; uom: string; qtyRequested: number; qtyApproved: number; qtyIssued: number; shortfall: number; available: number; indentRaised: boolean }
export interface RequisitionDto {
  id: number; number: string; siteId: number; siteCode: string; siteName: string; zone: string; storeId: number; storeCode: string; raisedBy: string; raisedById: string; raisedAt: string; needBy?: string | null;
  status: string; purpose?: string | null; approvedBy?: string | null; decidedAt?: string | null; decisionNote?: string | null; issueSlipNo?: string | null; value: number; lines: ReqLineDto[]; issueIds: number[];
  requiredLevel: number; currentLevel: number; recommendedBy?: string | null; submittedAt?: string | null; lapsedAt?: string | null;
}
export interface RequisitionUpsert { siteId: number; storeId: number; needBy?: string | null; purpose?: string | null; lines: { articleId: number; qty: number }[]; submit: boolean }
export interface RequisitionDecision { approve: boolean; note?: string | null; lines?: { lineId: number; qtyApproved: number }[] | null }

export interface IssueDto {
  id: number; number: string; gatePassNo: string; requisitionId: number; requisitionNo: string; storeId: number; storeCode: string; siteId: number; siteCode: string; siteName: string;
  issuedBy: string; issuedAt: string; issuedTo?: string | null; vehicle?: string | null; receivedById?: string | null; receivedAt?: string | null; itemCount: number; qty: number; value: number; remarks?: string | null; items: ItemDto[];
}

export interface EvidenceDto { id: string; kind: string; name: string; contentType: string; size: number; lat?: number | null; lng?: number | null; accuracyM?: number | null; capturedAt: string; uploadedBy: string; uploadedAt: string; url: string; sha256?: string | null; stampedUrl?: string | null; deviceId?: string | null }
export interface InstallationDto {
  id: number; number: string; itemId: string; qrCode: string; itemCode: string; description: string; siteId: number; siteCode: string; siteName: string; zone: string; siteLat: number; siteLng: number; geofenceM: number;
  officer: string; officerId: string; poleId?: string | null; lat: number; lng: number; accuracyM: number; distanceM: number; geoStatus: string; remarks?: string | null; capturedAt: string; submittedAt: string; offline: boolean;
  verifiedBy?: string | null; verifiedAt?: string | null; justification?: string | null; replacesItemId?: string | null; before: EvidenceDto; after: EvidenceDto;
  clientTxnId?: string | null; deviceId?: string | null; queuedAt?: string | null; ageDays: number; escalationLevel: number; escalatedAt?: string | null;
  fvRequestedBy?: string | null; fvRequestedAt?: string | null; fvRequestNote?: string | null; fvAssignedToId?: string | null; fvFindings?: string | null; fvFindingsBy?: string | null; fvFindingsAt?: string | null; fvPhotoUrls: string[];
}

export interface ReturnDto {
  id: number; number: string; itemId: string; qrCode: string; itemCode: string; description: string; poNumber: string; vendorName: string; siteId?: number | null; siteCode?: string | null; siteName?: string | null;
  storeId?: number | null; storeCode?: string | null; locationCode?: string | null; reason: string; kind: string; reportedBy: string; reportedAt: string; receivedBy?: string | null; receivedAt?: string | null;
  classification: string; warrantyEligible: boolean; warrantyExpires?: string | null; claimRef?: string | null; classifiedAt?: string | null; disposalApprovedAt?: string | null; scrapValue?: number | null; closedAt?: string | null;
  remarks?: string | null; installedAt?: string | null; serviceDays?: number | null; itemStatus: string;
}

export interface StockRowDto { articleId: number; itemCode: string; description: string; category: string; uom: string; storeId: number; storeCode: string; locationId: number; locationCode: string; locationKind: string; onHand: number; reserved: number; available: number; value: number; oldestDays: number }
export interface StockSummaryDto { rows: StockRowDto[]; totalValue: number; totalQty: number; articles: number; items: number; belowReorder: ArticleDto[] }
export interface KpiDto { key: string; label: string; value: number; sub?: string | null; tone: string }
export interface SeriesPoint { label: string; value: number; value2?: number | null }
export interface GroupRow { key: string; label: string; a: number; b: number; c: number; d: number }
export interface DashboardDto {
  kpis: KpiDto[]; monthly: SeriesPoint[]; byZone: GroupRow[]; byCategory: GroupRow[]; exceptions: InstallationDto[]; pendingApprovals: RequisitionDto[];
  overdueReturns: ReturnDto[]; unacknowledgedIssues: IssueDto[]; itemsByStatus: Record<string, number>; app: AppInfo; serverTime: string;
}
export interface ReportDto { key: string; title: string; columns: string[]; rows: (string | number | boolean | null)[][]; filters: Record<string, string> }
export interface ReportMeta { key: string; title: string; audience: string }
export interface AuditDto { id: number; at: string; kind: string; action: string; entityType?: string | null; entityId?: string | null; actorId?: string | null; actorName?: string | null; role?: string | null; prevJson?: string | null; nextJson?: string | null; reason?: string | null; ip?: string | null }

export interface Filters { zone?: string; storeId?: number; siteId?: number; q?: string }

// ---- store controls ----------------------------------------------------------------------------
export interface ScanLookupDto { kind: "item" | "location" | "unknown"; item?: ItemDto | null; location?: LocationDto | null; message: string }
export interface MoveResultDto { moved: number; toLocation: string; items: ItemDto[] }
export interface TransferDto { id: number; number: string; fromStoreId: number; fromStoreCode: string; toStoreId: number; toStoreCode: string; status: string; despatchedBy: string; despatchedAt: string; vehicle?: string | null; remarks?: string | null; itemCount: number; qty: number; inTransit: number; receivedBy?: string | null; receivedAt?: string | null; items: ItemDto[] }
export interface AdjustmentDto { id: number; number: string; storeId: number; storeCode: string; itemId: string; qrCode: string; itemCode: string; description: string; serial: number; kind: string; reasonCode: string; remarks?: string | null; qtyBefore: number; qtyAfter: number; status: string; physicalCountId?: number | null; raisedBy: string; raisedById: string; raisedAt: string; decidedBy?: string | null; decidedAt?: string | null; decisionNote?: string | null; itemStatus: string }
export interface CountScanDto { qr: string; itemId?: string | null; itemCode?: string | null; serial?: number | null; qty: number; locationId?: number | null; locationCode?: string | null; at: string; result: string }
export interface VarianceLineDto { kind: string; itemId?: string | null; qr: string; itemCode?: string | null; serial?: number | null; bookLocation?: string | null; countedLocation?: string | null; bookQty: number; countedQty: number; adjustmentId?: number | null; adjustmentStatus?: string | null; clearance: string }
export interface CountDto { id: number; number: string; storeId: number; storeCode: string; locationId?: number | null; locationCode?: string | null; status: string; startedBy: string; startedAt: string; submittedAt?: string | null; closedAt?: string | null; bookItems: number; scanned: number; scans: CountScanDto[]; variances: VarianceLineDto[] }
export interface AmendmentDto { id: number; number: string; entityType: string; entityRef: string; field: string; oldValue?: string | null; newValue?: string | null; reason: string; status: string; raisedBy: string; raisedById: string; raisedAt: string; decidedBy?: string | null; decidedAt?: string | null; decisionNote?: string | null }
export interface AmendableFieldDto { entityType: string; field: string; label: string }
export interface BinLabel { id: number; fullCode: string; kind: string; payload: string }

// ---- notifications -------------------------------------------------------------------------------
export interface NotificationDto { id: number; code: string; label: string; title: string; body: string; link?: string | null; entityType?: string | null; entityRef?: string | null; mandatory: boolean; level: number; createdAt: string; readAt?: string | null; emailStatus: string; smsStatus: string }
export interface NotifyCodeDto { code: string; label: string; mandatory: boolean; email: boolean; sms: boolean }
export interface NotifyPrefsDto { muted: string[]; email: boolean; sms: boolean; codes: NotifyCodeDto[] }
export interface NotifySettings { emailEnabled: boolean; smtpHost?: string | null; smtpPort: number; smtpSsl: boolean; smtpUser?: string | null; smtpPassword?: string | null; fromAddress?: string | null; fromName: string; smsEnabled: boolean; smsUrl?: string | null; smsMethod: string; smsApiKey?: string | null; portalUrl?: string | null }

// ---- mobile devices --------------------------------------------------------------------------------
export interface DeviceDto { id: string; userId: string; userName: string; roleLabel: string; deviceUid: string; model?: string | null; manufacturer?: string | null; osVersion?: string | null; appVersion?: string | null; status: string; enrolledAt: string; setupCompletedAt?: string | null; activatedAt?: string | null; lastSeenAt?: string | null; setupLat?: number | null; setupLng?: number | null; setupAccuracyM?: number | null; setupPhotoUrl?: string | null; revokedAt?: string | null; note?: string | null }
export interface MyMobileDto { required: boolean; active?: DeviceDto | null; devices: DeviceDto[]; downloadUrl: string }
export interface EnrolmentDto { code: string; userId: string; userName: string; expiresAt: string; qrPayload: string; serverUrl: string }
export interface AppInfoDto { available: boolean; size: number; updated?: string | null; version?: string | null; url: string; phoneServerUrl?: string | null; lanAddresses?: string[] }

// ---- reporting ------------------------------------------------------------------------------------------
export interface DrillCrumb { label: string; level: string; key?: string | null }
export interface DrillRow { cells: (string | number | null)[]; level?: string | null; key?: string | null }
export interface DrillDto { level: string; title: string; path: DrillCrumb[]; columns: string[]; rows: DrillRow[]; item?: ItemDetailDto | null; validation?: InstallationDto | null }
export interface MapSiteDto { id: number; code: string; name: string; zone: string; division: string; lat: number; lng: number; geofenceM: number; status: string; issued: number; installed: number; pending: number; exceptions: number; lastInstallation?: string | null; marker: string }
export interface MapPointDto { id: number; number: string; siteId: number; siteCode: string; lat: number; lng: number; geoStatus: string; itemCode: string; serial: number; officer: string; capturedAt: string; distanceM: number }
export interface MapDto { sites: MapSiteDto[]; installations: MapPointDto[]; zones: string[] }
export interface ReportRunDto { id: number; scheduleId?: number | null; reportKey: string; title: string; ranAt: string; from?: string | null; to?: string | null; rows: number; status: string; error?: string | null; downloadUrl: string }
export interface ScheduleDto { id: number; reportKey: string; title: string; frequency: string; dayOfWeek: number; dayOfMonth: number; hour: number; period: string; zone?: string | null; storeId?: number | null; recipientUserIds: string[]; recipientEmails: string; active: boolean; createdById: string; createdAt: string; lastRunAt?: string | null; nextRunAt?: string | null; runs: ReportRunDto[] }

export interface IssueAvailableDto { articleId: number; itemCode: string; uom: string; packSize: number; reserved: number; inStock: number; inReceiving: number }
