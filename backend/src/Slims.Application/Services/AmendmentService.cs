using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Dtos;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

/// <summary>FR-EXC-011: completed records are corrected only through an amendment request — a reason, approval by somebody
/// else, and the before and after values. The record itself stays visible with its amendment history. Photographs,
/// coordinates and stock quantities are never amendable (FR-FLD-014; quantities go through adjustments).</summary>
public class AmendmentService(IAppDbContext db, AuditService audit, UserContext users, NumberingService numbers, NotificationService notify)
{
    public static readonly AmendableFieldDto[] Fields =
    [
        new("installation", "PoleId", "Pole / point identifier"), new("installation", "Remarks", "Installation remarks"),
        new("issue", "IssuedTo", "Collected by"), new("issue", "Vehicle", "Vehicle"), new("issue", "Remarks", "Issue remarks"),
        new("grn", "ChallanNo", "Delivery challan no."), new("grn", "InvoiceNo", "Invoice no."), new("grn", "InvoiceDate", "Invoice date (yyyy-mm-dd)"), new("grn", "Remarks", "GRN remarks"),
        new("return", "Reason", "Failure / removal reason"), new("return", "Remarks", "Return remarks"),
    ];

    /// <summary>Loads the record, checks the caller may see it, and returns getter / setter for the field.</summary>
    async Task<(Func<string?> get, Action<string?> set, string perm, Store? store, Site? site)> TargetAsync(User me, string type, string reference, string field, CancellationToken ct)
    {
        if (!Fields.Any(f => f.EntityType == type && f.Field == field)) throw AppException.Bad($"{type}.{field} cannot be amended.");
        switch (type)
        {
            case "installation":
                {
                    var x = await db.Installations.Include(i => i.Site).FirstOrDefaultAsync(i => i.Number == reference, ct) ?? throw AppException.NotFound("Installation " + reference);
                    if (x.GeoStatus is not (GeoStatus.Confirmed or GeoStatus.ApprovedException)) throw AppException.Bad("Only a completed installation is amended. An open one is re-captured instead.");
                    return field == "PoleId" ? (() => x.PoleId, v => x.PoleId = v, Permissions.Verify, null, x.Site) : (() => x.Remarks, v => x.Remarks = v, Permissions.Verify, null, x.Site);
                }
            case "issue":
                {
                    var x = await db.Issues.Include(i => i.Store).Include(i => i.Site).FirstOrDefaultAsync(i => i.Number == reference, ct) ?? throw AppException.NotFound("Issue " + reference);
                    return field switch
                    {
                        "IssuedTo" => (() => x.IssuedTo, v => x.IssuedTo = v, Permissions.Approve, x.Store, null),
                        "Vehicle" => (() => x.Vehicle, v => x.Vehicle = v, Permissions.Approve, x.Store, null),
                        _ => (() => x.Remarks, v => x.Remarks = v, Permissions.Approve, x.Store, null),
                    };
                }
            case "grn":
                {
                    var x = await db.Grns.Include(g => g.Store).FirstOrDefaultAsync(g => g.Number == reference, ct) ?? throw AppException.NotFound("GRN " + reference);
                    return field switch
                    {
                        "ChallanNo" => (() => x.ChallanNo, v => x.ChallanNo = v, Permissions.Approve, x.Store, null),
                        "InvoiceNo" => (() => x.InvoiceNo, v => x.InvoiceNo = v, Permissions.Approve, x.Store, null),
                        "InvoiceDate" => (() => x.InvoiceDate?.ToString("yyyy-MM-dd"), v => x.InvoiceDate = string.IsNullOrWhiteSpace(v) ? null : DateOnly.TryParse(v, out var d) ? d : throw AppException.Bad("Invoice date must be yyyy-mm-dd."), Permissions.Approve, x.Store, null),
                        _ => (() => x.Remarks, v => x.Remarks = v, Permissions.Approve, x.Store, null),
                    };
                }
            case "return":
                {
                    var x = await db.ReturnNotes.Include(r => r.Store).Include(r => r.Site).FirstOrDefaultAsync(r => r.Number == reference, ct) ?? throw AppException.NotFound("Return " + reference);
                    return field == "Reason" ? (() => x.Reason, v => x.Reason = v ?? "", Permissions.Approve, x.Store, x.Site) : (() => x.Remarks, v => x.Remarks = v, Permissions.Approve, x.Store, x.Site);
                }
            default: throw AppException.Bad("Unknown record type.");
        }
    }

    static void Scope(User me, Store? store, Site? site)
    {
        if (store is not null && !AccessPolicy.StoreVisible(me, store) && (site is null || !AccessPolicy.SiteVisible(me, site))) throw AppException.Forbidden($"Store {store.Code} is outside your scope.", "scope");
        if (store is null && site is not null) UserContext.RequireSite(me, site);
    }

    public async Task<AmendmentDto> CreateAsync(AmendmentCreate r, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        if (string.IsNullOrWhiteSpace(r.Reason) || r.Reason.Trim().Length < 10) throw AppException.Bad("Give the reason for the correction (at least 10 characters).");
        var (get, _, perm, store, site) = await TargetAsync(me, r.EntityType, r.EntityRef, r.Field, ct);
        Scope(me, store, site);
        var old = get();
        var nv = string.IsNullOrWhiteSpace(r.NewValue) ? null : r.NewValue.Trim();
        if (old == nv) throw AppException.Bad("The new value is the same as the current one.");
        if (await db.AmendmentRequests.AnyAsync(a => a.EntityType == r.EntityType && a.EntityRef == r.EntityRef && a.Field == r.Field && a.Status == "Pending", ct)) throw AppException.Bad("An amendment of this field is already awaiting approval.");
        var a = new AmendmentRequest { Number = await numbers.NextAsync("AMD", null, ct), EntityType = r.EntityType, EntityRef = r.EntityRef, Field = r.Field, OldValue = old, NewValue = nv, Reason = r.Reason.Trim(), RaisedById = me.Id, RaisedBy = me.Name };
        db.AmendmentRequests.Add(a);
        audit.Log("master", "amendment-requested", me, r.EntityType, r.EntityRef, new { Field = r.Field, Value = old }, new { Field = r.Field, Value = nv }, a.Reason);
        var approvers = (await notify.ActiveUsersAsync(ct)).Where(u => u.Id != me.Id && AccessPolicy.Can(u, perm) && (store is null || AccessPolicy.StoreVisible(u, store)) && (site is null || store is not null || AccessPolicy.SiteVisible(u, site)));
        await notify.NotifyAsync(approvers, "NTF-13", $"Amendment {a.Number} awaiting approval", $"{me.Name} asks to change {r.Field} of {r.EntityType} {r.EntityRef} from \"{old}\" to \"{nv}\": {a.Reason}", "/amendments", "amendment", a.Number, ct);
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<AmendmentDto> DecideAsync(int id, DecisionRequest d, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var a = await db.AmendmentRequests.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Amendment");
        if (a.Status != "Pending") throw AppException.Bad($"Amendment is already {a.Status}.");
        if (a.RaisedById == me.Id) throw AppException.Rule("BR-026", "An amendment must be approved by somebody other than the person who asked for it.");
        var (get, set, perm, store, site) = await TargetAsync(me, a.EntityType, a.EntityRef, a.Field, ct);
        if (!AccessPolicy.Can(me, perm)) throw AppException.Forbidden($"Approving this amendment needs the \"{perm}\" permission.", "permission");
        Scope(me, store, site);
        a.DecidedById = me.Id; a.DecidedBy = me.Name; a.DecidedAt = DateTime.UtcNow; a.DecisionNote = d.Note;
        if (d.Approve)
        {
            if (get() != a.OldValue) throw AppException.Bad("The record has changed since the amendment was requested. Reject it and ask again.");
            set(a.NewValue);
            a.Status = "Approved";
            audit.Log("master", "amended", me, a.EntityType, a.EntityRef, new { a.Field, Value = a.OldValue }, new { a.Field, Value = a.NewValue, Amendment = a.Number }, a.Reason);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(d.Note)) throw AppException.Bad("A reason is required to reject.");
            a.Status = "Rejected";
            audit.Log("master", "amendment-rejected", me, a.EntityType, a.EntityRef, reason: d.Note);
        }
        await notify.NotifyAsync(await notify.ByIdsAsync(ct, a.RaisedById), "NTF-13", $"Amendment {a.Number} {a.Status.ToLowerInvariant()}", $"{a.Field} of {a.EntityType} {a.EntityRef}: {a.Status.ToLowerInvariant()} by {me.Name}. {d.Note}", "/amendments", "amendment", a.Number, ct, level: 9);
        await db.SaveChangesAsync(ct);
        return ToDto(a);
    }

    public async Task<List<AmendmentDto>> ListAsync(string? status, string? entityType, string? entityRef, CancellationToken ct)
    {
        await users.RequireAsync(Permissions.View, ct);
        var q = db.AmendmentRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status)) q = q.Where(a => a.Status == status);
        if (!string.IsNullOrEmpty(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (!string.IsNullOrEmpty(entityRef)) q = q.Where(a => a.EntityRef == entityRef);
        return (await q.OrderByDescending(a => a.Id).Take(500).ToListAsync(ct)).Select(ToDto).ToList();
    }

    static AmendmentDto ToDto(AmendmentRequest a) => new(a.Id, a.Number, a.EntityType, a.EntityRef, a.Field, a.OldValue, a.NewValue, a.Reason, a.Status, a.RaisedBy, a.RaisedById, a.RaisedAt, a.DecidedBy, a.DecidedAt, a.DecisionNote);
}
