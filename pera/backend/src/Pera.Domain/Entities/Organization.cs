namespace Pera.Domain.Entities;

/// <summary>Office, wing or section. Units nest through ParentId (FR-002).</summary>
public class OrgUnit
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Section";        // Office | Wing | Section
    public int? ParentId { get; set; }
    public string? HeadUserId { get; set; }
    public DateTime EffectiveFrom { get; set; } = DateTime.UtcNow.Date;
    public DateTime? EffectiveTo { get; set; }
    public bool Active { get; set; } = true;
    public OrgUnit? Parent { get; set; }
}

public class Designation
{
    public int Id { get; set; }
    public string Code { get; set; } = "";
    public string Title { get; set; } = "";
    /// <summary>Seniority, 1 = most senior. Used for ordering only.</summary>
    public int Rank { get; set; } = 50;
    public bool Active { get; set; } = true;
}

public enum DelegationScope { All, Tasks, Documents, Approvals, Signatures }

/// <summary>Temporary acting arrangement: ToUser may act for FromUser between the dates, within the scope (FR-027).
/// Every action taken this way records both the acting officer and the officer acted for.</summary>
public class Delegation
{
    public int Id { get; set; }
    public string FromUserId { get; set; } = "";
    public string ToUserId { get; set; } = "";
    public DelegationScope Scope { get; set; } = DelegationScope.All;
    /// <summary>null = every document type.</summary>
    public int? DocumentTypeId { get; set; }
    public DateTime StartsOn { get; set; }
    public DateTime EndsOn { get; set; }
    public string Reason { get; set; } = "";
    public string CreatedById { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? RevokedAt { get; set; }
    public string? RevokedById { get; set; }

    public User? FromUser { get; set; }
    public User? ToUser { get; set; }

    public bool CoversDate(DateTime utc) => RevokedAt is null && utc.Date >= StartsOn.Date && utc.Date <= EndsOn.Date;
    public bool Covers(DelegationScope needed) => Scope == DelegationScope.All || Scope == needed
        // approval and signature powers include the document handling around them
        || (needed == DelegationScope.Documents && Scope is DelegationScope.Approvals or DelegationScope.Signatures);
}
