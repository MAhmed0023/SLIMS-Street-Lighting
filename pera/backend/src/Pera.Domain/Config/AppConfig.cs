using Pera.Domain.Entities;

namespace Pera.Domain.Config;

/// <summary>Application parameters, stored as one JSON row in AppSettings and edited under Settings ▸ Parameters (FR-028).</summary>
public class AppConfig
{
    public int Version { get; set; } = 1;
    public AppInfo App { get; set; } = new();
    public bool LoginRequired { get; set; } = true;
    public bool ShowUserPicker { get; set; } = true;
    public AlertSettings Alerts { get; set; } = new();
    public FileSettings Files { get; set; } = new();
}

public class AppInfo
{
    public string Name { get; set; } = "Task, Document Movement & Approval System";
    public string Short { get; set; } = "PERA Workflow";
    public string Client { get; set; } = "Punjab Enforcement & Regulatory Authority";
    public string Agency { get; set; } = "ADG Implementation";
}

/// <summary>Reminder and escalation thresholds (BRD 8, FR-024).</summary>
public class AlertSettings
{
    /// <summary>Remind the holder this many hours before a due date.</summary>
    public int ReminderHours { get; set; } = 24;
    /// <summary>Escalate to the holder's reporting officer this many hours after the due date.</summary>
    public int Escalate1Hours { get; set; } = 24;
    /// <summary>Escalate one more level up after this many hours overdue.</summary>
    public int Escalate2Hours { get; set; } = 72;
    public int WorkerMinutes { get; set; } = 15;
    /// <summary>"Due soon" window on dashboards, days.</summary>
    public int DueSoonDays { get; set; } = 3;
}

public class FileSettings
{
    /// <summary>Never accepted, whatever a document type allows (SR-SEC-05).</summary>
    public string BlockedExtensions { get; set; } = "exe,dll,bat,cmd,com,msi,ps1,vbs,js,jar,scr,sh,php,html,htm,svg";
    public int TaskAttachmentMaxMb { get; set; } = 20;
    public string TaskAttachmentExtensions { get; set; } = "pdf,docx,doc,xlsx,xls,pptx,jpg,jpeg,png,txt,csv,zip";
}

public static class DefaultConfig
{
    public static AppConfig Create() => new();

    public static List<Role> DefaultRoles() =>
    [
        new() { Key = "adg", Label = "ADG Implementation", Perms = "view,task,document,report,export", SortOrder = 1 },
        new() { Key = "manager", Label = "Manager / Wing or Section Head", Perms = "view,task,document,report,export", SortOrder = 2 },
        new() { Key = "officer", Label = "Officer / Official", Perms = "view,task,document", SortOrder = 3 },
        new() { Key = "workflowadmin", Label = "Workflow Administrator", Perms = "view,document,workflow,report", SortOrder = 4 },
        new() { Key = "auditor", Label = "Auditor (read-only)", Perms = "view,report,export,audit", SortOrder = 5 },
        // technical configuration only — no approval or signing power is implied (SR-SEC-08)
        new() { Key = "sysadmin", Label = "System Administrator", Perms = "view,configure,workflow", SortOrder = 6 },
    ];
}
