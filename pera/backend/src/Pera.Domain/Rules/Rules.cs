using System.Security.Cryptography;
using System.Text;
using Pera.Domain.Entities;

namespace Pera.Domain.Rules;

/// <summary>The permitted task status transitions (FRD section 3). Anything not listed is refused.</summary>
public static class TaskLifecycle
{
    static readonly Dictionary<WorkTaskStatus, WorkTaskStatus[]> Next = new()
    {
        [WorkTaskStatus.Draft] = [WorkTaskStatus.Assigned, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.Assigned] = [WorkTaskStatus.InProgress, WorkTaskStatus.Blocked, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.InProgress] = [WorkTaskStatus.Blocked, WorkTaskStatus.SubmittedForReview, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.Blocked] = [WorkTaskStatus.InProgress, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.SubmittedForReview] = [WorkTaskStatus.Completed, WorkTaskStatus.Returned],
        [WorkTaskStatus.Returned] = [WorkTaskStatus.InProgress, WorkTaskStatus.SubmittedForReview, WorkTaskStatus.Blocked, WorkTaskStatus.Cancelled],
        [WorkTaskStatus.Completed] = [WorkTaskStatus.InProgress],
        [WorkTaskStatus.Cancelled] = [],
    };
    public static bool Allowed(WorkTaskStatus from, WorkTaskStatus to) => Next.TryGetValue(from, out var n) && n.Contains(to);
    public static bool IsFinal(WorkTaskStatus s) => s is WorkTaskStatus.Completed or WorkTaskStatus.Cancelled;
}

/// <summary>Which actions each stage type offers (FRD section 4). Comment is open at every active stage.</summary>
public static class StageRules
{
    public static StageAction[] Actions(StageType t) => t switch
    {
        StageType.Information => [StageAction.Acknowledge, StageAction.Comment],
        StageType.Review => [StageAction.Recommend, StageAction.Return, StageAction.Comment],
        StageType.Concurrence => [StageAction.Concur, StageAction.NotConcur, StageAction.Return, StageAction.Comment],
        StageType.Approval => [StageAction.Approve, StageAction.Reject, StageAction.Return, StageAction.Comment],
        StageType.Signature => [StageAction.Sign, StageAction.Return, StageAction.Comment],
        StageType.Dispatch => [StageAction.Dispatch, StageAction.Record, StageAction.Comment],
        _ => [StageAction.Comment],
    };
    /// <summary>Actions that close the stage and move the route on.</summary>
    public static bool Completes(StageAction a) => a is StageAction.Acknowledge or StageAction.Recommend or StageAction.Concur or StageAction.NotConcur
        or StageAction.Approve or StageAction.Sign or StageAction.Dispatch or StageAction.Record;
    /// <summary>Reasons are mandatory for these (FRD 4: return, reject; non-concurrence must say why).</summary>
    public static bool NeedsReason(StageAction a) => a is StageAction.Return or StageAction.Reject or StageAction.NotConcur or StageAction.Comment;
    public static AuthorityKind? AuthorityFor(StageType t) => t switch
    {
        StageType.Approval => AuthorityKind.Approve,
        StageType.Signature => AuthorityKind.Sign,
        _ => null,
    };
    public static DelegationScope ScopeFor(StageType t) => t switch
    {
        StageType.Approval or StageType.Concurrence => DelegationScope.Approvals,
        StageType.Signature => DelegationScope.Signatures,
        _ => DelegationScope.Documents,
    };
    public static string Requested(StageType t) => t switch
    {
        StageType.Information => "For information",
        StageType.Review => "Review and comment",
        StageType.Concurrence => "Concurrence",
        StageType.Approval => "Approval",
        StageType.Signature => "Signature",
        StageType.Dispatch => "Dispatch / place on record",
        _ => t.ToString(),
    };
}

public record RouteLine(int Step, string Name, StageType Type, string? UserId, bool Mandatory, int? TemplateStageId, bool AdHoc = false);
public record MandatoryStage(int TemplateStageId, string Name, StageType Type);
public record RouteIssue(string Severity, string Code, string Message, int? Index = null)
{
    public bool IsError => Severity == "error";
}

public class RouteContext
{
    public string InitiatorId { get; init; } = "";
    public IReadOnlySet<string> ActiveUserIds { get; init; } = new HashSet<string>();
    public Func<string, AuthorityKind, bool> HasAuthority { get; init; } = (_, _) => true;
    public Func<string, string> NameOf { get; init; } = id => id;
    public IReadOnlyList<MandatoryStage> MandatoryStages { get; init; } = [];
    public bool AllowParallel { get; init; } = true;
    public bool AllowCustomization { get; init; } = true;
    public bool RequireApprovalStage { get; init; } = true;
    public bool AllowSelfApproval { get; init; }
    public bool EnforceAuthority { get; init; } = true;
}

/// <summary>Validates an initiator's proposed route before submission (FR-013, FRD section 6).
/// Errors block submission; warnings are shown and the initiator may proceed.</summary>
public static class RouteValidator
{
    public static List<RouteIssue> Validate(IReadOnlyList<RouteLine> route, RouteContext ctx)
    {
        var issues = new List<RouteIssue>();
        var lines = route.Where(l => !l.AdHoc).ToList();
        if (lines.Count == 0) { issues.Add(new("error", "ROUTE_EMPTY", "The route has no stages.")); return issues; }

        foreach (var m in ctx.MandatoryStages)
            if (!lines.Any(l => l.TemplateStageId == m.TemplateStageId && l.Type == m.Type))
                issues.Add(new("error", "MANDATORY_MISSING", $"Mandatory {m.Type.ToString().ToLower()} stage \"{m.Name}\" cannot be removed from this route."));

        for (var i = 0; i < route.Count; i++)
        {
            var l = route[i];
            if (l.AdHoc) continue;
            if (l.Step < 1) issues.Add(new("error", "BAD_STEP", $"Stage \"{l.Name}\" needs a step number of 1 or more.", i));
            if (string.IsNullOrWhiteSpace(l.UserId)) { issues.Add(new("error", "NO_RECIPIENT", $"Stage \"{l.Name}\" has no officer.", i)); continue; }
            if (!ctx.ActiveUserIds.Contains(l.UserId)) issues.Add(new("error", "INVALID_RECIPIENT", $"Stage \"{l.Name}\": {ctx.NameOf(l.UserId)} is not an active user.", i));
            if (l.Type is StageType.Approval or StageType.Signature)
            {
                if (l.UserId == ctx.InitiatorId && !ctx.AllowSelfApproval)
                    issues.Add(new("error", "SELF_APPROVAL", $"Stage \"{l.Name}\": the initiator cannot {(l.Type == StageType.Approval ? "approve" : "sign")} their own submission.", i));
                var kind = l.Type == StageType.Approval ? AuthorityKind.Approve : AuthorityKind.Sign;
                if (ctx.EnforceAuthority && !ctx.HasAuthority(l.UserId, kind))
                    issues.Add(new("error", "NO_AUTHORITY", $"Stage \"{l.Name}\": {ctx.NameOf(l.UserId)} holds no {kind.ToString().ToLower()} authority for this document type.", i));
            }
            if (!ctx.AllowCustomization && l.TemplateStageId is null)
                issues.Add(new("error", "CUSTOMIZATION_NOT_ALLOWED", $"Stage \"{l.Name}\" is not in the approved template and this template does not allow additions.", i));
        }

        if (ctx.RequireApprovalStage && !lines.Any(l => l.Type == StageType.Approval))
            issues.Add(new("error", "NO_COMPETENT_AUTHORITY", "The route has no approval stage. A competent authority must approve this document type."));

        var approvalSteps = lines.Where(l => l.Type == StageType.Approval).Select(l => l.Step).ToList();
        if (approvalSteps.Count > 0)
            foreach (var s in lines.Where(l => l.Type == StageType.Signature && l.Step <= approvalSteps.Max()))
                issues.Add(new("error", "SIGN_BEFORE_APPROVAL", $"Signature stage \"{s.Name}\" must come after every approval stage."));

        var lastStep = lines.Max(l => l.Step);
        foreach (var d in lines.Where(l => l.Type == StageType.Dispatch && l.Step < lastStep))
            issues.Add(new("warning", "DISPATCH_NOT_LAST", $"Dispatch stage \"{d.Name}\" is not the last step."));

        if (!ctx.AllowParallel)
            foreach (var g in lines.Where(l => l.Type != StageType.Information).GroupBy(l => l.Step).Where(g => g.Count() > 1))
                issues.Add(new("error", "PARALLEL_NOT_ALLOWED", $"Step {g.Key} has {g.Count()} stages in parallel; this document type allows sequential stages only."));

        foreach (var g in lines.Where(l => l.UserId != null).GroupBy(l => (l.UserId, l.Type)).Where(g => g.Count() > 1))
            issues.Add(new("warning", "DUPLICATE_STAGE", $"{ctx.NameOf(g.Key.UserId!)} appears {g.Count()} times for {g.Key.Type.ToString().ToLower()}."));

        // the same officer receiving the file again after someone else has handled it
        var ordered = lines.Where(l => l.UserId != null && l.Type != StageType.Information).OrderBy(l => l.Step).ToList();
        var seen = new HashSet<string>();
        string? prev = null;
        var circular = new HashSet<string>();
        foreach (var l in ordered)
        {
            if (l.UserId != prev && seen.Contains(l.UserId!)) circular.Add(l.UserId!);
            seen.Add(l.UserId!); prev = l.UserId;
        }
        foreach (var u in circular) issues.Add(new("warning", "CIRCULAR_ROUTE", $"The route goes back to {ctx.NameOf(u)} after other officers have acted."));

        return issues;
    }
}

/// <summary>Document integrity and signature seal (SRD 5). The seal is an HMAC over the facts of the signing,
/// keyed with a server secret held outside the source tree (SR-SEC-04).</summary>
public static class Integrity
{
    public static string Sha256Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    public static string SealPayload(string documentNumber, int versionId, string documentHash, string signerId, string? actingForId, DateTime signedAtUtc, string code)
        => string.Join('|', "PERA-SIG-1", documentNumber, versionId, documentHash, signerId, actingForId ?? "", signedAtUtc.ToString("O"), code);
    public static string Seal(byte[] key, string payload) => Convert.ToHexString(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    public static bool VerifySeal(byte[] key, string payload, string? mac)
        => mac is not null && CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Seal(key, payload)), Encoding.ASCII.GetBytes(mac));
    /// <summary>Short human-readable code printed on the stamp, e.g. "7KQ4-M2XD".</summary>
    public static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var b = RandomNumberGenerator.GetBytes(8);
        var s = new string(b.Select(x => alphabet[x % alphabet.Length]).ToArray());
        return s[..4] + "-" + s[4..];
    }
}

public static class Codes
{
    /// <summary>Pakistan financial year, July to June: "26-27".</summary>
    public static string FY(DateTime d) { var y = d.Month >= 7 ? d.Year : d.Year - 1; return $"{y % 100:D2}-{(y + 1) % 100:D2}"; }
}

public static class AccessPolicy
{
    public static bool Can(User u, string perm) => u.Role?.Has(perm) ?? false;
}
