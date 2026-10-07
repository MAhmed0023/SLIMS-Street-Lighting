using System.Security.Cryptography;
using System.Text;
using Slims.Domain.Entities;

namespace Slims.Domain.Rules;

/// <summary>The permitted status transitions (BRD section 12.2). Anything not listed is refused.</summary>
public static class ItemLifecycle
{
    static readonly Dictionary<ItemStatus, ItemStatus[]> Next = new()
    {
        [ItemStatus.Created] = [ItemStatus.Received, ItemStatus.Rejected, ItemStatus.Cancelled],
        [ItemStatus.Received] = [ItemStatus.Reserved, ItemStatus.Issued, ItemStatus.Rejected, ItemStatus.Scrap, ItemStatus.InTransit, ItemStatus.WrittenOff],
        [ItemStatus.Rejected] = [ItemStatus.Received, ItemStatus.Cancelled],
        [ItemStatus.Reserved] = [ItemStatus.Issued, ItemStatus.Received],
        [ItemStatus.Issued] = [ItemStatus.AtSite, ItemStatus.Received, ItemStatus.PendingVerification, ItemStatus.Installed],
        [ItemStatus.AtSite] = [ItemStatus.PendingVerification, ItemStatus.Installed, ItemStatus.Received, ItemStatus.Dismantled],
        [ItemStatus.PendingVerification] = [ItemStatus.Installed, ItemStatus.AtSite],
        [ItemStatus.Installed] = [ItemStatus.OutOfOrder, ItemStatus.Dismantled],
        [ItemStatus.OutOfOrder] = [ItemStatus.Returned],
        [ItemStatus.Dismantled] = [ItemStatus.Returned],
        [ItemStatus.Returned] = [ItemStatus.UnderRepair, ItemStatus.WarrantyClaim, ItemStatus.Scrap, ItemStatus.Received],
        [ItemStatus.UnderRepair] = [ItemStatus.Received, ItemStatus.Scrap],
        [ItemStatus.WarrantyClaim] = [ItemStatus.Received, ItemStatus.Scrap],
        [ItemStatus.Scrap] = [ItemStatus.Disposed],
        [ItemStatus.Disposed] = [],
        [ItemStatus.Cancelled] = [],
        [ItemStatus.InTransit] = [ItemStatus.Received],
        [ItemStatus.WrittenOff] = [ItemStatus.Received],
    };
    public static bool Allowed(ItemStatus from, ItemStatus to) => Next.TryGetValue(from, out var n) && n.Contains(to);
    public static bool IsIssuable(ItemStatus s) => s == ItemStatus.Received;
    public static bool InField(ItemStatus s) => s is ItemStatus.Issued or ItemStatus.AtSite or ItemStatus.PendingVerification or ItemStatus.Installed;
}

public static class Geo
{
    /// <summary>Great-circle distance in metres.</summary>
    public static double DistanceM(double lat1, double lng1, double lat2, double lng2)
    {
        const double R = 6371000;
        double d2r(double d) => d * Math.PI / 180;
        var dLat = d2r(lat2 - lat1); var dLng = d2r(lng2 - lng1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(d2r(lat1)) * Math.Cos(d2r(lat2)) * Math.Sin(dLng / 2) * Math.Sin(dLng / 2);
        return 2 * R * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }
}

/// <summary>Item code and QR payload conventions (BRD 9.3 and 9.4).</summary>
public static class Codes
{
    public static string PoTag(string poNumber) => "PO" + new string(poNumber.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant().Replace("PO", "");
    public static string Clean(string s, int max) { var t = new string(s.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant(); return t.Length > max ? t[..max] : t; }
    /// <summary>CAT-SUBCAT-SPEC-POnnnn-LLL (the trailing block is the PO line, the per-piece serial lives in the QR).</summary>
    public static string ItemCode(string cat, string sub, string spec, string poNumber, int lineNo) => $"{Clean(cat, 4)}-{Clean(sub, 4)}-{Clean(spec, 6)}-{PoTag(poNumber)}-{lineNo:D3}";
    public static string Check(string itemCode, int serial)
    {
        var h = SHA256.HashData(Encoding.UTF8.GetBytes(itemCode + "|" + serial + "|SLIMS"));
        return Convert.ToHexString(h, 0, 3);
    }
    /// <summary>The string encoded in the QR symbol: SLIMS|LUM-LED-090W-PO2611-001|000482|A1B2C3</summary>
    public static string QrPayload(string itemCode, int serial) => $"SLIMS|{itemCode}|{serial:D6}|{Check(itemCode, serial)}";
    public static bool TryParse(string? qr, out string itemCode, out int serial)
    {
        itemCode = ""; serial = 0;
        if (string.IsNullOrWhiteSpace(qr)) return false;
        var p = qr.Trim().Split('|');
        if (p.Length != 4 || p[0] != "SLIMS" || !int.TryParse(p[2], out serial)) return false;
        itemCode = p[1];
        return string.Equals(Check(itemCode, serial), p[3], StringComparison.OrdinalIgnoreCase);
    }
    /// <summary>Bin label payload: SLIMSLOC|CEN01-LUM-R01-B01. Scanned with the item to put away or move stock (FR-INV-012).</summary>
    public static string LocationPayload(string fullCode) => $"SLIMSLOC|{fullCode}";
    public static string? ParseLocation(string? qr) { var p = (qr ?? "").Trim().Split('|'); return p.Length == 2 && p[0] == "SLIMSLOC" && p[1] != "" ? p[1] : null; }
    /// <summary>Pakistan financial year, July to June: "26-27".</summary>
    public static string FY(DateTime d) { var y = d.Month >= 7 ? d.Year : d.Year - 1; return $"{y % 100:D2}-{(y + 1) % 100:D2}"; }
}

public static class AccessPolicy
{
    public static bool Can(User u, string perm) => u.Role?.Has(perm) ?? false;
    static HashSet<int> Ids(string csv) => csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToHashSet();
    public static HashSet<int> StoreIds(User u) => Ids(u.StoreIds);
    public static HashSet<int> SiteIds(User u) => Ids(u.SiteIds);
    public static bool AllZones(User u) => string.IsNullOrEmpty(u.Zone);
    public static bool StoreVisible(User u, Store s) { var ids = StoreIds(u); return (AllZones(u) || s.Zone == u.Zone) && (ids.Count == 0 || ids.Contains(s.Id)); }
    public static bool SiteVisible(User u, Site s) { var ids = SiteIds(u); return (AllZones(u) || s.Zone == u.Zone) && (ids.Count == 0 || ids.Contains(s.Id)); }
    public static string DescribeScope(User u)
    {
        var parts = new List<string> { AllZones(u) ? "All zones" : $"Zone {u.Zone}" };
        if (StoreIds(u).Count > 0) parts.Add($"{StoreIds(u).Count} store(s)");
        if (SiteIds(u).Count > 0) parts.Add($"{SiteIds(u).Count} site(s)");
        return string.Join(" · ", parts);
    }
}
