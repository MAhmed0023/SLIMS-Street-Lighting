using System.Text;
using Microsoft.EntityFrameworkCore;
using Slims.Application.Common;
using Slims.Application.Interfaces;
using Slims.Domain.Entities;
using Slims.Domain.Rules;

namespace Slims.Application.Services;

public record ScheduleUpsert(string ReportKey, string? Title, string Frequency, int DayOfWeek, int DayOfMonth, int Hour, string Period, string? Zone, int? StoreId, string[]? RecipientUserIds, string? RecipientEmails, bool Active);
public record ScheduleDto(int Id, string ReportKey, string Title, string Frequency, int DayOfWeek, int DayOfMonth, int Hour, string Period, string? Zone, int? StoreId, string[] RecipientUserIds, string RecipientEmails,
    bool Active, string CreatedById, DateTime CreatedAt, DateTime? LastRunAt, DateTime? NextRunAt, List<ReportRunDto> Runs);
public record ReportRunDto(int Id, int? ScheduleId, string ReportKey, string Title, DateTime RanAt, DateTime? From, DateTime? To, int Rows, string Status, string? Error, string DownloadUrl);

/// <summary>FR-RPT-010: reports delivered automatically. Each run is produced in the scope of the schedule's owner, saved as
/// CSV, announced in-app to the nominated users and e-mailed as an attachment where e-mail is configured.</summary>
public class ScheduleService(IAppDbContext db, AuditService audit, UserContext users, ReportService reports, IFileStorage files, NotificationService notify, NotifySettingsService channels, IMessageSender sender)
{
    static readonly TimeSpan Pkt = TimeSpan.FromHours(5);

    public async Task<List<ScheduleDto>> ListAsync(CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var all = AccessPolicy.Can(me, Permissions.Configure);
        var rows = await db.ReportSchedules.AsNoTracking().OrderBy(s => s.Id).ToListAsync(ct);
        var runs = await db.ReportRuns.AsNoTracking().OrderByDescending(r => r.Id).Take(300).ToListAsync(ct);
        return rows.Where(s => all || s.CreatedById == me.Id || s.RecipientUserIds.Split(',').Contains(me.Id)).Select(s => ToDto(s, runs.Where(r => r.ScheduleId == s.Id).Take(10))).ToList();
    }

    public async Task<ScheduleDto> UpsertAsync(int? id, ScheduleUpsert r, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Export, Permissions.Configure);
        var cat = ReportService.Catalogue.FirstOrDefault(c => c.Key == r.ReportKey);
        if (cat.Key is null) throw AppException.Bad("Unknown report.");
        if (r.Frequency is not ("Daily" or "Weekly" or "Monthly")) throw AppException.Bad("Frequency must be Daily, Weekly or Monthly.");
        if (r.Period is not ("previous" or "todate")) throw AppException.Bad("Period must be previous or todate.");
        var ids = (r.RecipientUserIds ?? []).Select(x => x.Trim()).Where(x => x != "").Distinct().ToArray();
        if (ids.Length > 0 && await db.Users.CountAsync(u => ids.Contains(u.Id) && u.Active, ct) != ids.Length) throw AppException.Bad("One of the recipients is not an active user.");
        var emails = (r.RecipientEmails ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim().ToLowerInvariant()).Distinct().ToArray();
        if (emails.Any(e => !e.Contains('@') || e.Length > 120)) throw AppException.Bad("Check the e-mail addresses.");
        if (ids.Length == 0 && emails.Length == 0) throw AppException.Bad("Nominate at least one recipient.");
        var s = id is null ? null : await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (id is not null && s is null) throw AppException.NotFound("Schedule");
        if (s is not null && s.CreatedById != me.Id && !AccessPolicy.Can(me, Permissions.Configure)) throw AppException.Forbidden("Only the owner or the administrator can change this schedule.");
        if (s is null) { s = new ReportSchedule { CreatedById = me.Id }; db.ReportSchedules.Add(s); }
        s.ReportKey = r.ReportKey; s.Title = string.IsNullOrWhiteSpace(r.Title) ? cat.Title : r.Title.Trim(); s.Frequency = r.Frequency; s.DayOfWeek = Math.Clamp(r.DayOfWeek, 0, 6); s.DayOfMonth = Math.Clamp(r.DayOfMonth, 1, 28);
        s.Hour = Math.Clamp(r.Hour, 0, 23); s.Period = r.Period; s.Zone = string.IsNullOrWhiteSpace(r.Zone) ? null : r.Zone; s.StoreId = r.StoreId; s.RecipientUserIds = string.Join(',', ids); s.RecipientEmails = string.Join(',', emails); s.Active = r.Active;
        s.NextRunAt = s.Active ? NextRun(s, DateTime.UtcNow) : null;
        audit.Log("config", id is null ? "schedule-created" : "schedule-updated", me, "schedule", s.Title, next: new { s.ReportKey, s.Frequency, s.Hour, s.RecipientUserIds, s.RecipientEmails, s.Active });
        await db.SaveChangesAsync(ct);
        return ToDto(s, []);
    }

    /// <summary>Next occurrence after <paramref name="after"/>, in Pakistan time, returned in UTC.</summary>
    public static DateTime NextRun(ReportSchedule s, DateTime after)
    {
        var local = after + Pkt;
        var candidate = local.Date.AddHours(s.Hour);
        for (var i = 0; i < 400; i++, candidate = candidate.AddDays(1))
        {
            if (candidate <= local) continue;
            var ok = s.Frequency switch { "Daily" => true, "Weekly" => (int)candidate.DayOfWeek == s.DayOfWeek, _ => candidate.Day == s.DayOfMonth };
            if (ok) return candidate - Pkt;
        }
        return after.AddDays(1);
    }

    /// <summary>The period the run covers, as report filter dates (local calendar dates).</summary>
    public static (DateTime From, DateTime To) PeriodFor(ReportSchedule s, DateTime runUtc)
    {
        var today = (runUtc + Pkt).Date;
        return (s.Frequency, s.Period) switch
        {
            ("Daily", "previous") => (today.AddDays(-1), today.AddDays(-1)),
            ("Daily", _) => (today, today),
            ("Weekly", "previous") => (today.AddDays(-7), today.AddDays(-1)),
            ("Weekly", _) => (today.AddDays(-6), today),
            ("Monthly", "previous") => (new DateTime(today.Year, today.Month, 1).AddMonths(-1), new DateTime(today.Year, today.Month, 1).AddDays(-1)),
            _ => (new DateTime(today.Year, today.Month, 1), today),
        };
    }

    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow; var n = 0;
        foreach (var s in await db.ReportSchedules.Where(x => x.Active && x.NextRunAt != null && x.NextRunAt <= now).ToListAsync(ct))
        {
            await RunAsync(s, ct);
            s.LastRunAt = now; s.NextRunAt = NextRun(s, now);
            await db.SaveChangesAsync(ct); n++;
        }
        return n;
    }

    public async Task<ReportRunDto> RunNowAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAnyAsync(ct, Permissions.Export, Permissions.Configure);
        var s = await db.ReportSchedules.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw AppException.NotFound("Schedule");
        var run = await RunAsync(s, ct);
        s.LastRunAt = DateTime.UtcNow;
        audit.Log("config", "schedule-run", me, "schedule", s.Title, next: new { run.Id, run.Rows, run.Status });
        await db.SaveChangesAsync(ct);
        return RunDto(run);
    }

    async Task<ReportRun> RunAsync(ReportSchedule s, CancellationToken ct)
    {
        var (from, to) = PeriodFor(s, DateTime.UtcNow);
        var run = new ReportRun { ScheduleId = s.Id, ReportKey = s.ReportKey, Title = $"{s.Title} {from:dd-MM-yyyy}{(to != from ? " – " + to.ToString("dd-MM-yyyy") : "")}", From = from, To = to, RunAsUserId = s.CreatedById };
        try
        {
            var owner = await db.Users.Include(u => u.Role).FirstOrDefaultAsync(u => u.Id == s.CreatedById && u.Active, ct) ?? throw new InvalidOperationException("The schedule's owner is no longer active.");
            var rep = await reports.RunForAsync(owner, s.ReportKey, new ReportFilters(from, to, s.Zone, s.StoreId), ct);
            var csv = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(ReportService.ToCsv(rep))).ToArray();
            var name = $"{s.ReportKey}_{from:yyyyMMdd}_{to:yyyyMMdd}.csv";
            run.FilePath = await files.SaveAsync(Path.Combine("reports", DateTime.UtcNow.ToString("yyyy-MM")), name, new MemoryStream(csv), ct);
            run.Rows = rep.Rows.Count;
            db.ReportRuns.Add(run); await db.SaveChangesAsync(ct);
            var ids = s.RecipientUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries);
            var who = await notify.ByIdsAsync(ct, ids);
            await notify.NotifyAsync(who, "NTF-RPT", run.Title, $"{rep.Rows.Count} row(s). Download it from Reports ▸ Schedules.", $"/api/report-runs/{run.Id}/download", "report", run.Id.ToString(), ct);
            // the report goes out once, as an attachment, to every recipient with an address
            foreach (var x in db.Notifications.Local.Where(x => x.Code == "NTF-RPT" && x.EntityRef == run.Id.ToString())) x.EmailStatus = "None";
            var ch = await channels.GetAsync(ct);
            var to2 = who.Select(u => u.Email).Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => e!).Concat(s.RecipientEmails.Split(',', StringSplitOptions.RemoveEmptyEntries)).Distinct().ToList();
            if (ch.EmailEnabled && to2.Count > 0) await sender.SendEmailAsync(ch, to2, "[SLIMS] " + run.Title, $"{rep.Title}\n{rep.Rows.Count} row(s), attached as CSV (opens in Excel).\n\n— SLIMS scheduled report", [(name, csv)], ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            run.Status = "Failed"; run.Error = e.Message.Length > 400 ? e.Message[..400] : e.Message;
            if (run.Id == 0) db.ReportRuns.Add(run);
        }
        return run;
    }

    public async Task<(Stream, string)?> OpenRunAsync(int id, CancellationToken ct)
    {
        var me = await users.RequireAsync(Permissions.View, ct);
        var run = await db.ReportRuns.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null || run.Status != "Done") return null;
        var s = run.ScheduleId is null ? null : await db.ReportSchedules.AsNoTracking().FirstOrDefaultAsync(x => x.Id == run.ScheduleId, ct);
        if (!AccessPolicy.Can(me, Permissions.Configure) && run.RunAsUserId != me.Id && (s is null || !s.RecipientUserIds.Split(',').Contains(me.Id))) throw AppException.Forbidden("This report was not sent to you.");
        var stream = files.Open(run.FilePath);
        return stream is null ? null : (stream, Path.GetFileName(run.FilePath));
    }

    static ScheduleDto ToDto(ReportSchedule s, IEnumerable<ReportRun> runs) => new(s.Id, s.ReportKey, s.Title, s.Frequency, s.DayOfWeek, s.DayOfMonth, s.Hour, s.Period, s.Zone, s.StoreId,
        s.RecipientUserIds.Split(',', StringSplitOptions.RemoveEmptyEntries), s.RecipientEmails, s.Active, s.CreatedById, s.CreatedAt, s.LastRunAt, s.NextRunAt, runs.Select(RunDto).ToList());
    static ReportRunDto RunDto(ReportRun r) => new(r.Id, r.ScheduleId, r.ReportKey, r.Title, r.RanAt, r.From, r.To, r.Rows, r.Status, r.Error, $"/api/report-runs/{r.Id}/download");
}
