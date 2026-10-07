using Slims.Domain.Config;

namespace Slims.Application.Dtos;

// ---- mobile setup and device binding (FR-SEC-009) ---------------------------------------------------
public record EnrolmentRequest(string? ForUserId, string ServerUrl);
public record EnrolmentDto(string Code, string UserId, string UserName, DateTime ExpiresAt, string QrPayload, string ServerUrl);
public record EnrolRequest(string Code, string DeviceUid, string? Model, string? Manufacturer, string? OsVersion, string? AppVersion);
public record EnrolResultDto(Guid DeviceId, string DeviceToken, string UserId, string UserName, string RoleLabel, string Status, bool ReplacesAnotherPhone);
public record DeviceSessionRequest(string DeviceToken, string? AppVersion);
public record DeviceDto(Guid Id, string UserId, string UserName, string RoleLabel, string DeviceUid, string? Model, string? Manufacturer, string? OsVersion, string? AppVersion, string Status,
    DateTime EnrolledAt, DateTime? SetupCompletedAt, DateTime? ActivatedAt, DateTime? LastSeenAt, double? SetupLat, double? SetupLng, double? SetupAccuracyM, string? SetupPhotoUrl, DateTime? RevokedAt, string? Note);
public record DeviceSessionDto(string AccessToken, DateTime ExpiresAt, UserDto User, DeviceDto Device, GeoSettings Geo, FieldSettings Field, DateTime ServerTime);
public record SetupCheckRequest(MobilePhoto Photo, DateTime DeviceTime, bool AutoTimeEnabled);
public record SetupCheckItem(string Check, bool Ok, string Detail);
public record SetupCheckDto(bool Ok, List<SetupCheckItem> Checks, DeviceDto Device);
public record MyMobileDto(bool Required, DeviceDto? Active, List<DeviceDto> Devices, string DownloadUrl);
public record ReasonBody(string? Reason);

// ---- field capture -------------------------------------------------------------------------------------
/// <summary>A photograph as the field app captured it: JPEG bytes (base64), the SHA-256 the device computed over those bytes,
/// and the fix recorded at the moment of capture. IsMock is Android's own mock-location flag for that fix.</summary>
public record MobilePhoto(string Base64, string Sha256, double Lat, double Lng, double AccuracyM, DateTime CapturedAt, bool IsMock);
public record MobileInstallationRequest(Guid ClientTxnId, string Qr, string? PoleId, string? Remarks, string? ReplacesQr, bool Offline, DateTime? QueuedAt, DateTime DeviceTime, MobilePhoto Before, MobilePhoto After);
public record MobileAckRequest(int IssueId, double? Lat, double? Lng, double? AccuracyM);
public record FindingsRequest(string Findings, List<MobilePhoto>? Photos, DateTime DeviceTime);
public record MobileBootstrapDto(UserDto User, DeviceDto Device, GeoSettings Geo, FieldSettings Field, List<SiteDto> Sites, List<ItemDto> WorkList, List<IssueDto> ToAcknowledge,
    List<InstallationDto> Recapture, List<InstallationDto> VerificationTasks, List<string> FailureReasons, int Unread, DateTime ServerTime);
