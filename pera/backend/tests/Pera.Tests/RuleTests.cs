using Pera.Domain.Entities;
using Pera.Domain.Rules;

namespace Pera.Tests;

public class RuleTests
{
    static RouteContext Ctx(Action<RouteContextBuilder>? tweak = null)
    {
        var b = new RouteContextBuilder();
        tweak?.Invoke(b);
        return b.Build();
    }

    public sealed class RouteContextBuilder
    {
        public HashSet<string> Active = ["init", "rev", "boss", "legal", "clerk"];
        public HashSet<(string, AuthorityKind)> Powers = [("boss", AuthorityKind.Approve), ("boss", AuthorityKind.Sign)];
        public List<MandatoryStage> Mandatory = [];
        public bool AllowParallel = true, AllowCustomization = true, RequireApproval = true;
        public RouteContext Build() => new()
        {
            InitiatorId = "init", ActiveUserIds = Active, HasAuthority = (u, k) => Powers.Contains((u, k)), MandatoryStages = Mandatory,
            AllowParallel = AllowParallel, AllowCustomization = AllowCustomization, RequireApprovalStage = RequireApproval,
        };
    }

    static List<RouteLine> Good() =>
    [
        new(1, "Review", StageType.Review, "rev", false, 11),
        new(2, "Approve", StageType.Approval, "boss", true, 12),
        new(3, "Sign", StageType.Signature, "boss", true, 13),
        new(4, "Dispatch", StageType.Dispatch, "clerk", false, 14),
    ];

    [Fact]
    public void A_complete_route_has_no_issues()
        => Assert.Empty(RouteValidator.Validate(Good(), Ctx(c => c.Mandatory = [new(12, "Approve", StageType.Approval), new(13, "Sign", StageType.Signature)])));

    [Fact]
    public void Removing_a_mandatory_approval_stage_is_refused_with_an_explanation()
    {
        var route = Good().Where(l => l.Type != StageType.Approval).ToList();
        var issues = RouteValidator.Validate(route, Ctx(c => c.Mandatory = [new(12, "Approve", StageType.Approval)]));
        var e = Assert.Single(issues, i => i.Code == "MANDATORY_MISSING");
        Assert.True(e.IsError);
        Assert.Contains("cannot be removed", e.Message);
        Assert.Contains(issues, i => i.Code == "NO_COMPETENT_AUTHORITY");
    }

    [Fact]
    public void Self_approval_missing_authority_and_inactive_recipients_are_errors()
    {
        var route = Good();
        route[1] = route[1] with { UserId = "init" };
        route[0] = route[0] with { UserId = "ghost" };
        var issues = RouteValidator.Validate(route, Ctx());
        Assert.Contains(issues, i => i.Code == "SELF_APPROVAL");
        Assert.Contains(issues, i => i.Code == "NO_AUTHORITY");
        Assert.Contains(issues, i => i.Code == "INVALID_RECIPIENT");
        Assert.All(issues.Where(i => i.Code is "SELF_APPROVAL" or "NO_AUTHORITY" or "INVALID_RECIPIENT"), i => Assert.True(i.IsError));
    }

    [Fact]
    public void Signing_before_approval_is_an_error()
    {
        var route = Good();
        route[2] = route[2] with { Step = 2 };
        Assert.Contains(RouteValidator.Validate(route, Ctx()), i => i.Code == "SIGN_BEFORE_APPROVAL");
    }

    [Fact]
    public void Duplicate_and_circular_routes_are_warnings()
    {
        var route = Good();
        route.Insert(2, new RouteLine(2, "Review again", StageType.Review, "rev", false, null));
        route[2] = route[2] with { Step = 3 };
        route[3] = route[3] with { Step = 4 }; route[4] = route[4] with { Step = 5 };
        var issues = RouteValidator.Validate(route, Ctx());
        Assert.Contains(issues, i => i.Code == "DUPLICATE_STAGE" && !i.IsError);
        Assert.Contains(issues, i => i.Code == "CIRCULAR_ROUTE" && !i.IsError);
        Assert.DoesNotContain(issues, i => i.IsError);
    }

    [Fact]
    public void Parallel_steps_and_additions_respect_the_configuration()
    {
        var route = Good();
        route.Add(new RouteLine(1, "Second review", StageType.Review, "legal", false, null));
        var issues = RouteValidator.Validate(route, Ctx(c => { c.AllowParallel = false; c.AllowCustomization = false; }));
        Assert.Contains(issues, i => i.Code == "PARALLEL_NOT_ALLOWED");
        Assert.Contains(issues, i => i.Code == "CUSTOMIZATION_NOT_ALLOWED");
    }

    [Fact]
    public void Task_lifecycle_allows_only_listed_transitions()
    {
        Assert.True(TaskLifecycle.Allowed(WorkTaskStatus.Draft, WorkTaskStatus.Assigned));
        Assert.True(TaskLifecycle.Allowed(WorkTaskStatus.SubmittedForReview, WorkTaskStatus.Returned));
        Assert.True(TaskLifecycle.Allowed(WorkTaskStatus.Completed, WorkTaskStatus.InProgress));
        Assert.False(TaskLifecycle.Allowed(WorkTaskStatus.InProgress, WorkTaskStatus.Completed));   // closure needs review
        Assert.False(TaskLifecycle.Allowed(WorkTaskStatus.Cancelled, WorkTaskStatus.InProgress));
        Assert.False(TaskLifecycle.Allowed(WorkTaskStatus.Assigned, WorkTaskStatus.Completed));
    }

    [Fact]
    public void Seal_detects_any_change_to_the_signed_facts()
    {
        var key = new byte[32];
        var payload = Integrity.SealPayload("PERA/DOC/1", 7, "abc", "adg", null, new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc), "AAAA-BBBB");
        var mac = Integrity.Seal(key, payload);
        Assert.True(Integrity.VerifySeal(key, payload, mac));
        Assert.False(Integrity.VerifySeal(key, payload.Replace("abc", "abd"), mac));
        Assert.False(Integrity.VerifySeal(key, Integrity.SealPayload("PERA/DOC/1", 7, "abc", "adg", "dg", new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc), "AAAA-BBBB"), mac));
    }

    [Fact]
    public void Delegation_scopes()
    {
        var d = new Delegation { Scope = DelegationScope.Approvals, StartsOn = new DateTime(2026, 10, 1), EndsOn = new DateTime(2026, 10, 31) };
        Assert.True(d.Covers(DelegationScope.Approvals));
        Assert.True(d.Covers(DelegationScope.Documents));
        Assert.False(d.Covers(DelegationScope.Signatures));
        Assert.True(d.CoversDate(new DateTime(2026, 10, 31, 23, 0, 0)));
        Assert.False(d.CoversDate(new DateTime(2026, 11, 1)));
    }
}
