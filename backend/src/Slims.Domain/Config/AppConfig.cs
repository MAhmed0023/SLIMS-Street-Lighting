using Slims.Domain.Entities;

namespace Slims.Domain.Config;

/// <summary>Application parameters — editable by the administrator, stored as JSON in AppSetting "config".</summary>
public class AppConfig
{
    public int Version { get; set; } = 1;
    public AppInfo App { get; set; } = new();
    public GeoSettings Geo { get; set; } = new();
    public ControlSettings Controls { get; set; } = new();
    public List<CategoryDef> Categories { get; set; } = new();
    public List<string> Uoms { get; set; } = ["Nos", "Metre", "Set", "Kg", "Litre"];
    public List<string> Zones { get; set; } = new();
    public List<string> SiteTypes { get; set; } = ["Road stretch", "Junction", "Park", "Public building", "Pole cluster"];
    public List<string> FailureReasons { get; set; } = ["Driver failure", "LED module failure", "Physical damage", "Water ingress", "Cable fault", "Theft / vandalism", "Pole damage", "Replaced during upgrade", "Other"];
    public List<string> ReceiptConditions { get; set; } = ["Good", "Packaging damaged", "Partly damaged", "Wet / water damage", "Short-packed"];
    public List<string> RejectReasons { get; set; } = ["Physically damaged", "Wrong specification", "Wrong item", "Missing parts", "Failed inspection", "Other"];
    public List<string> AdjustmentReasons { get; set; } = ["Lost / not found", "Theft", "Damaged in store", "Found in store", "Count correction", "Pack quantity correction", "Other"];
    /// <summary>FR-REQ-010: delegated authority by requisition value. A requisition goes to the first level whose limit covers it;
    /// an approver below that level can only recommend, which passes it up. MaxValue null = no limit.</summary>
    public List<ApprovalLevel> ApprovalLevels { get; set; } = [new() { Level = 1, RoleKey = "storeincharge", Label = "Store In-Charge", MaxValue = 1_000_000 }, new() { Level = 2, RoleKey = "pdoffice", Label = "PD Office", MaxValue = null }];
    /// <summary>Who a notice escalates to when it is not actioned (FR-NTF-004): role → next role.</summary>
    public Dictionary<string, string> EscalateTo { get; set; } = new() { ["officer"] = "supervisor", ["supervisor"] = "pdoffice", ["storekeeper"] = "storeincharge", ["storeincharge"] = "pdoffice", ["procurement"] = "pdoffice", ["pdoffice"] = "sysadmin" };
    public AlertSettings Alerts { get; set; } = new();
    public FieldSettings Field { get; set; } = new();
    public bool LoginRequired { get; set; } = true;
    public bool ShowUserPicker { get; set; } = true;
}

public class AppInfo
{
    public string Name { get; set; } = "Street Lighting Store & Installation Manager";
    public string Short { get; set; } = "SLIMS";
    public string Client { get; set; } = "Government Street Lighting Project";
    public string Agency { get; set; } = "Project Director's Office";
    public string Currency { get; set; } = "Rs.";
}
public class GeoSettings
{
    public int DefaultGeofenceM { get; set; } = 50;
    public int ToleranceM { get; set; } = 20;
    public int MinAccuracyM { get; set; } = 25;
    public int MaxClockDriftMinutes { get; set; } = 5;
}
public class ControlSettings
{
    public int ReturnDays { get; set; } = 7;
    public int ReservationDays { get; set; } = 5;
    public int AckOverdueHours { get; set; } = 48;
    public double ReceiptTolerancePct { get; set; } = 0;
    public int AgeingDays { get; set; } = 180;
    public int EvidenceMaxMB { get; set; } = 8;
}
public class CategoryDef { public string Code { get; set; } = ""; public string Label { get; set; } = ""; public List<string> Subs { get; set; } = new(); }
public class ApprovalLevel { public int Level { get; set; } public string RoleKey { get; set; } = ""; public string Label { get; set; } = ""; public decimal? MaxValue { get; set; } }

/// <summary>Thresholds for the notification rules and configurable alerts (FRD Section 14, FR-NTF-003/004).</summary>
public class AlertSettings
{
    public int ApprovalEscalateHours { get; set; } = 24;       // NTF-01, NTF-13
    public int InstallPendingDays { get; set; } = 7;           // NTF-07
    public int ExceptionEscalateDays { get; set; } = 3;        // NTF-08
    public int RecaptureEscalateDays { get; set; } = 2;        // NTF-09
    public int GpsMismatchCount { get; set; } = 3;             // NTF-15: this many exceptions …
    public int GpsMismatchDays { get; set; } = 30;             // … within this many days, by one officer or at one site
    public double ReturnRatePct { get; set; } = 10;            // NTF-16: failures as % of installed, per article or vendor
    public int UnresolvedSiteDays { get; set; } = 14;          // NTF-17
    public int TurnaroundHours { get; set; } = 72;             // requisition submitted → issued
    public int WorkerMinutes { get; set; } = 15;               // how often the background checks run
}

/// <summary>Mobile field app (FR-FLD-007/012, FR-SEC-009).</summary>
public class FieldSettings
{
    public bool MobileSetupRequired { get; set; } = true;      // Lighting Officers must bind a phone before field work
    public bool AllowWebCapture { get; set; } = false;         // browser upload of installation photos (not live-only)
    public int EnrolmentMinutes { get; set; } = 15;            // how long a setup QR stays valid
    public int SetupMaxAccuracyM { get; set; } = 60;           // the setup test photo needs a GPS fix at least this good
    public int OfflineRetentionDays { get; set; } = 7;         // queued captures older than this are refused
    public string PhoneServerUrl { get; set; } = "";           // address phones use to reach SLIMS; blank = the portal's own LAN address
}

public static class DefaultConfig
{
    public static AppConfig Create()
    {
        var c = new AppConfig();
        c.Categories =
        [
            new() { Code = "LUM", Label = "Luminaire", Subs = ["LED", "SOD", "MH", "SOL"] },
            new() { Code = "POL", Label = "Pole", Subs = ["STL", "GI", "OCT", "CON"] },
            new() { Code = "BRK", Label = "Bracket / Arm", Subs = ["SGL", "DBL"] },
            new() { Code = "CBL", Label = "Cable", Subs = ["CU", "AL", "ARM"] },
            new() { Code = "PNL", Label = "Panel / Controller", Subs = ["FDR", "TMR", "PHC"] },
            new() { Code = "ACC", Label = "Accessory", Subs = ["CLP", "FUS", "MCB", "LUG"] },
        ];
        c.Zones = ["Central", "North", "South"];
        return c;
    }

    public static List<Role> DefaultRoles() =>
    [
        R("sysadmin", "System Administrator", string.Join(',', Permissions.All), 0),
        R("pdoffice", "PD Office / MIS", "view,approve,verify,export,configure,audit", 1),
        R("storeincharge", "Store In-Charge", "view,receive,approve,issue,return,export", 2),
        R("storekeeper", "Store Keeper", "view,receive,issue,return", 3),
        R("supervisor", "Lighting Supervisor", "view,requisition,verify,export", 4),
        R("officer", "Lighting Officer", "view,install,return", 5),
        R("procurement", "Procurement & Contracts", "view,procure,export", 6),
        R("audit", "Internal Audit", "view,export,audit", 7),
    ];
    static Role R(string key, string label, string perms, int order) => new() { Key = key, Label = label, Perms = perms, SortOrder = order };
}
