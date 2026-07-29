using HR.Application.Common.Interfaces;
using HR.Domain.Engines.Attendance;
using HR.Domain.Enums;
using HR.Modules.Attendance.DTOs;
using HR.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Attendance.Services;

/// <summary>Filters shared by the daily / weekly / monthly / export endpoints.</summary>
public sealed class AttendanceFilter
{
    public Guid? EmployeeId { get; set; }
    public Guid? DepartmentId { get; set; }
    public Guid? BranchId { get; set; }
    public Guid? JobTitleId { get; set; }
    public Guid? ShiftId { get; set; }
    public string? Status { get; set; }
}

public interface IAttendanceService
{
    Task<AttendanceDailyResponse> GetDailyAsync(AttendanceFilter filter, DateTime date, CancellationToken ct);
    Task<List<AttendanceDayDto>> GetRangeRowsAsync(AttendanceFilter filter, DateTime from, DateTime to, CancellationToken ct);
    Task<AttendanceSummaryResponse> GetSummaryAsync(AttendanceFilter filter, DateTime from, DateTime to, CancellationToken ct);
    Task<AttendanceDetailDto?> GetDetailAsync(Guid recordId, CancellationToken ct);
    Task<Guid> AddManualPunchAsync(ManualPunchRequest req, CancellationToken ct);
    Task CorrectAsync(Guid recordId, CorrectAttendanceRequest req, CancellationToken ct);
}

public sealed class AttendanceService : IAttendanceService
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IAttendanceCalculationService _calc;
    private readonly IShiftResolver _resolver;

    public AttendanceService(ApplicationDbContext db, ICurrentUserService user,
        IAttendanceCalculationService calc, IShiftResolver resolver)
    {
        _db = db; _user = user; _calc = calc; _resolver = resolver;
    }

    private sealed class EmpRow
    {
        public Guid Id; public string? Name; public string? Number;
        public Guid? DepartmentId; public string? DepartmentName;
        public Guid? BranchId; public string? BranchName;
        public Guid? JobTitleId; public string? JobTitleName;
        public EmployeeScope Scope => new(Id, DepartmentId, BranchId, JobTitleId);
    }

    // ── Reads ───────────────────────────────────────────────────────────────

    public async Task<AttendanceDailyResponse> GetDailyAsync(AttendanceFilter filter, DateTime date, CancellationToken ct)
    {
        var rows = await GetRangeRowsAsync(filter, date, date, ct);
        return new AttendanceDailyResponse { Date = date.Date, Rows = rows, Kpis = BuildKpis(rows) };
    }

    public async Task<List<AttendanceDayDto>> GetRangeRowsAsync(AttendanceFilter filter, DateTime from, DateTime to, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;
        from = from.Date; to = to.Date;
        if (to < from) (from, to) = (to, from);

        var emps = await LoadEmployeesAsync(filter, ct);
        var empIds = emps.Select(e => e.Id).ToList();

        var assignments = await _db.ShiftAssignments.AsNoTracking().ToListAsync(ct);
        var shifts = await _db.Shifts.AsNoTracking().ToListAsync(ct);
        var shiftsById = shifts.ToDictionary(s => s.Id);

        var records = await _db.AttendanceRecords.AsNoTracking()
            .Where(a => a.Date >= from && a.Date <= to && empIds.Contains(a.EmployeeId))
            .ToListAsync(ct);
        var recByKey = records
            .GroupBy(r => (r.EmployeeId, r.Date.Date))
            .ToDictionary(g => g.Key, g => g.First());

        var holidays = await LoadHolidayDatesAsync(from, to, ct);
        var policy = await LoadPolicyAsync(ct);

        var permissions = await _db.AttendancePermissions.AsNoTracking()
            .Where(p => p.Date >= from && p.Date <= to && empIds.Contains(p.EmployeeId))
            .ToListAsync(ct);
        var permByKey = permissions
            .GroupBy(p => (p.EmployeeId, p.Date.Date))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<PermissionWindow>)g
                .Select(p => new PermissionWindow(p.FromMinutes, p.ToMinutes)).ToList());

        var rows = new List<AttendanceDayDto>();
        for (var d = from; d <= to; d = d.AddDays(1))
        {
            foreach (var e in emps)
            {
                var shift = _resolver.Resolve(assignments, shiftsById, e.Scope, d);
                if (filter.ShiftId is { } sid && shift?.Id != sid) continue;

                recByKey.TryGetValue((e.Id, d), out var rec);
                permByKey.TryGetValue((e.Id, d), out var pw);
                var dto = BuildDay(e, d, shift, rec, today, holidays.Contains(d.Date), policy, pw ?? Array.Empty<PermissionWindow>());

                if (filter.Status is { Length: > 0 } st && !string.Equals(dto.Status, st, StringComparison.OrdinalIgnoreCase))
                    continue;

                rows.Add(dto);
            }
        }
        return rows;
    }

    private AttendanceDayDto BuildDay(EmpRow e, DateTime date, Shift? shift, AttendanceRecord? rec, DateTime today,
        bool isHolidayDate, AttendancePolicySettings policy, IReadOnlyList<PermissionWindow>? permissions = null)
    {
        var isLeave = rec is not null && (rec.Status == AttendanceStatus.OnLeave || rec.Source == AttendanceSources.LeaveRequest);
        var isHoliday = isHolidayDate || (rec is not null && rec.Status == AttendanceStatus.Holiday);
        var isWfh = rec is not null && (rec.Status == AttendanceStatus.WorkFromHome || rec.Status == AttendanceStatus.Remote);

        var ci = isLeave ? null : rec?.CheckIn;
        var co = isLeave ? null : rec?.CheckOut;

        var calc = _calc.Calculate(shift, date, ci, co, isLeave, isHoliday, isWfh, policy, permissions);
        var statusStr = calc.Status.ToString();

        // Don't penalise future working days, or — when the policy disables auto-absent — past days with
        // no punches, as "Absent".
        if (rec is null && calc.Status == AttendanceStatus.Absent && (date > today || !policy.AutoMarkAbsent))
        {
            statusStr = "Scheduled";
            calc.ShortageMinutes = 0;
            calc.RequiredMinutes = shift?.RequiredMinutes ?? calc.RequiredMinutes;
        }

        return new AttendanceDayDto
        {
            RecordId = rec?.Id,
            EmployeeId = e.Id,
            EmployeeName = e.Name,
            EmployeeNumber = e.Number,
            DepartmentName = e.DepartmentName,
            BranchName = e.BranchName,
            JobTitleName = e.JobTitleName,
            Date = date,
            ShiftId = shift?.Id,
            ShiftName = shift is null ? null : (shift.NameAr ?? shift.NameEn),
            IsFlexible = calc.IsFlexible,
            CheckIn = ci,
            CheckOut = co,
            RequiredMinutes = calc.RequiredMinutes,
            WorkedMinutes = calc.WorkedMinutes,
            LateMinutes = calc.LateMinutes,
            ShortageMinutes = calc.ShortageMinutes,
            OvertimeMinutes = calc.OvertimeMinutes,
            BreakMinutes = calc.BreakMinutes,
            ExcusedMinutes = calc.ExcusedMinutes,
            Status = statusStr,
            Source = rec?.Source,
            ReferenceId = rec?.ReferenceId,
            Notes = rec?.Notes,
        };
    }

    public async Task<AttendanceSummaryResponse> GetSummaryAsync(AttendanceFilter filter, DateTime from, DateTime to, CancellationToken ct)
    {
        var rows = await GetRangeRowsAsync(filter, from, to, ct);
        var groups = rows.GroupBy(r => r.EmployeeId).Select(g =>
        {
            var first = g.First();
            return new AttendanceSummaryDto
            {
                EmployeeId = g.Key,
                EmployeeName = first.EmployeeName,
                EmployeeNumber = first.EmployeeNumber,
                DepartmentName = first.DepartmentName,
                BranchName = first.BranchName,
                PresentDays = g.Count(x => IsPresentStatus(x.Status)),
                AbsentDays = g.Count(x => x.Status == nameof(AttendanceStatus.Absent)),
                LeaveDays = g.Count(x => x.Status == nameof(AttendanceStatus.OnLeave)),
                LateDays = g.Count(x => x.Status == nameof(AttendanceStatus.Late)),
                ShortDays = g.Count(x => x.Status == nameof(AttendanceStatus.ShortHours)),
                OvertimeDays = g.Count(x => x.Status == nameof(AttendanceStatus.Overtime)),
                WeekendDays = g.Count(x => x.Status == nameof(AttendanceStatus.Weekend)),
                HolidayDays = g.Count(x => x.Status == nameof(AttendanceStatus.Holiday)),
                MissingPunchDays = g.Count(x => x.Status == nameof(AttendanceStatus.MissingCheckIn) || x.Status == nameof(AttendanceStatus.MissingCheckOut)),
                WorkedMinutes = g.Sum(x => x.WorkedMinutes),
                RequiredMinutes = g.Sum(x => x.RequiredMinutes),
                LateMinutes = g.Sum(x => x.LateMinutes),
                ShortageMinutes = g.Sum(x => x.ShortageMinutes),
                OvertimeMinutes = g.Sum(x => x.OvertimeMinutes),
            };
        }).OrderBy(x => x.EmployeeNumber).ToList();

        return new AttendanceSummaryResponse { From = from.Date, To = to.Date, Rows = groups, Kpis = BuildKpis(rows) };
    }

    public async Task<AttendanceDetailDto?> GetDetailAsync(Guid recordId, CancellationToken ct)
    {
        var rec = await _db.AttendanceRecords.AsNoTracking().FirstOrDefaultAsync(a => a.Id == recordId, ct);
        if (rec is null) return null;

        var emps = await LoadEmployeesAsync(new AttendanceFilter { EmployeeId = rec.EmployeeId }, ct);
        var e = emps.FirstOrDefault() ?? new EmpRow { Id = rec.EmployeeId };

        var assignments = await _db.ShiftAssignments.AsNoTracking().ToListAsync(ct);
        var shifts = await _db.Shifts.AsNoTracking().ToListAsync(ct);
        var shift = _resolver.Resolve(assignments, shifts.ToDictionary(s => s.Id), e.Scope, rec.Date);

        var policy = await LoadPolicyAsync(ct);
        var isHolidayDate = (await LoadHolidayDatesAsync(rec.Date, rec.Date, ct)).Contains(rec.Date.Date);
        var perms = await LoadPermissionWindowsAsync(rec.EmployeeId, rec.Date, ct);
        var day = BuildDay(e, rec.Date, shift, rec, DateTime.UtcNow.Date, isHolidayDate, policy, perms);

        var punches = await _db.AttendancePunches.AsNoTracking()
            .Where(p => p.EmployeeId == rec.EmployeeId && p.PunchTime >= rec.Date && p.PunchTime < rec.Date.AddDays(1))
            .OrderBy(p => p.PunchTime)
            .Select(p => new AttendancePunchDto
            {
                Id = p.Id, PunchTime = p.PunchTime, Direction = p.Direction.ToString(),
                Source = p.Source, Latitude = p.Latitude, Longitude = p.Longitude, Notes = p.Notes,
            }).ToListAsync(ct);

        var audit = await _db.AttendanceAuditLogs.AsNoTracking()
            .Where(a => a.AttendanceRecordId == rec.Id)
            .OrderByDescending(a => a.At)
            .Select(a => new AttendanceAuditDto { Action = a.Action, Details = a.DetailsAr ?? a.DetailsEn, At = a.At })
            .ToListAsync(ct);

        string? reqNumber = null;
        if (rec.ReferenceId is { } refId)
            reqNumber = await _db.RequestInstances.Where(r => r.Id == refId).Select(r => r.RequestNumber).FirstOrDefaultAsync(ct);

        return new AttendanceDetailDto
        {
            Day = day, Punches = punches, Audit = audit,
            RelatedRequestId = rec.ReferenceId, RelatedRequestNumber = reqNumber,
        };
    }

    // ── Writes ──────────────────────────────────────────────────────────────

    public async Task<Guid> AddManualPunchAsync(ManualPunchRequest req, CancellationToken ct)
    {
        var date = DateTime.SpecifyKind(req.Date.Date, DateTimeKind.Utc);
        var checkIn = CombineTime(date, req.CheckIn);
        var checkOut = CombineTime(date, req.CheckOut);

        var rec = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.EmployeeId == req.EmployeeId && a.Date == date, ct);
        if (rec is null)
        {
            rec = new AttendanceRecord { EmployeeId = req.EmployeeId, Date = date };
            _db.AttendanceRecords.Add(rec);
            await _db.SaveChangesAsync(ct); // materialise Id for punch FK
        }

        if (checkIn is not null)
            _db.AttendancePunches.Add(new AttendancePunch { EmployeeId = req.EmployeeId, AttendanceRecordId = rec.Id, PunchTime = checkIn.Value, Direction = PunchDirection.In, Source = AttendanceSources.ManualEntry, Notes = req.Notes });
        if (checkOut is not null)
            _db.AttendancePunches.Add(new AttendancePunch { EmployeeId = req.EmployeeId, AttendanceRecordId = rec.Id, PunchTime = checkOut.Value, Direction = PunchDirection.Out, Source = AttendanceSources.ManualEntry, Notes = req.Notes });

        rec.CheckIn = checkIn ?? rec.CheckIn;
        rec.CheckOut = checkOut ?? rec.CheckOut;
        rec.Source = AttendanceSources.ManualEntry;
        if (req.Notes is not null) rec.Notes = req.Notes;

        await RecalcAsync(rec, ct);
        AddAudit(rec, "ManualPunch", $"تسجيل يدوي: حضور {req.CheckIn ?? "—"} انصراف {req.CheckOut ?? "—"}", $"Manual punch in {req.CheckIn ?? "—"} out {req.CheckOut ?? "—"}");
        await _db.SaveChangesAsync(ct);
        return rec.Id;
    }

    public async Task CorrectAsync(Guid recordId, CorrectAttendanceRequest req, CancellationToken ct)
    {
        var rec = await _db.AttendanceRecords.FirstOrDefaultAsync(a => a.Id == recordId, ct)
            ?? throw new KeyNotFoundException("Attendance record not found");

        var oldIn = rec.CheckIn; var oldOut = rec.CheckOut;
        var newIn = CombineTime(rec.Date, req.CheckIn) ?? rec.CheckIn;
        var newOut = CombineTime(rec.Date, req.CheckOut) ?? rec.CheckOut;

        _db.AttendanceCorrections.Add(new AttendanceCorrection
        {
            AttendanceRecordId = rec.Id, EmployeeId = rec.EmployeeId, Date = rec.Date,
            OldCheckIn = oldIn, OldCheckOut = oldOut, NewCheckIn = newIn, NewCheckOut = newOut, Reason = req.Reason,
        });

        rec.CheckIn = newIn; rec.CheckOut = newOut;
        rec.Source = AttendanceSources.AttendanceCorrection;
        if (req.Reason is not null) rec.Notes = req.Reason;

        await RecalcAsync(rec, ct);
        AddAudit(rec, "Correction", $"تصحيح الحضور: {req.Reason}", $"Correction: {req.Reason}");
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Recompute the calculation fields + status of a persisted record from its punches/shift.</summary>
    private async Task RecalcAsync(AttendanceRecord rec, CancellationToken ct)
    {
        var emp = await _db.Employees.Where(e => e.Id == rec.EmployeeId)
            .Select(e => new { e.DepartmentId, e.BranchId, e.JobTitleId }).FirstOrDefaultAsync(ct);
        var scope = new EmployeeScope(rec.EmployeeId, emp?.DepartmentId, emp?.BranchId, emp?.JobTitleId);

        var assignments = await _db.ShiftAssignments.AsNoTracking().ToListAsync(ct);
        var shifts = await _db.Shifts.AsNoTracking().ToListAsync(ct);
        var shift = _resolver.Resolve(assignments, shifts.ToDictionary(s => s.Id), scope, rec.Date);

        var isLeave = rec.Status == AttendanceStatus.OnLeave || rec.Source == AttendanceSources.LeaveRequest;
        var policy = await LoadPolicyAsync(ct);
        var isHolidayDate = (await LoadHolidayDatesAsync(rec.Date, rec.Date, ct)).Contains(rec.Date.Date);
        var windows = await LoadPermissionWindowsAsync(rec.EmployeeId, rec.Date, ct);
        var calc = _calc.Calculate(shift, rec.Date, isLeave ? null : rec.CheckIn, isLeave ? null : rec.CheckOut,
            isLeave, rec.Status == AttendanceStatus.Holiday || isHolidayDate, false, policy, windows);

        rec.ShiftId = shift?.Id;
        rec.IsFlexible = calc.IsFlexible;
        rec.RequiredMinutes = calc.RequiredMinutes;
        rec.WorkedMinutes = calc.WorkedMinutes;
        rec.LateMinutes = calc.LateMinutes;
        rec.ShortageMinutes = calc.ShortageMinutes;
        rec.OvertimeMinutes = calc.OvertimeMinutes;
        rec.BreakMinutes = calc.BreakMinutes;
        rec.ExcusedMinutes = calc.ExcusedMinutes;
        if (!isLeave) rec.Status = calc.Status;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<List<EmpRow>> LoadEmployeesAsync(AttendanceFilter filter, CancellationToken ct)
    {
        var q = _db.Employees.AsNoTracking().Where(e => e.Status == EmployeeStatus.Active);
        if (filter.EmployeeId is { } eid) q = q.Where(e => e.Id == eid);
        if (filter.DepartmentId is { } dep) q = q.Where(e => e.DepartmentId == dep);
        if (filter.BranchId is { } br) q = q.Where(e => e.BranchId == br);
        if (filter.JobTitleId is { } jt) q = q.Where(e => e.JobTitleId == jt);

        return await (from e in q
                      join d in _db.Departments on e.DepartmentId equals d.Id into dj
                      from d in dj.DefaultIfEmpty()
                      join b in _db.Branch on e.BranchId equals b.Id into bj
                      from b in bj.DefaultIfEmpty()
                      join p in _db.Positions on e.JobTitleId equals p.Id into pj
                      from p in pj.DefaultIfEmpty()
                      orderby e.EmployeeNumber
                      select new EmpRow
                      {
                          Id = e.Id,
                          Name = (e.FirstNameAr ?? e.FirstName) + " " + (e.LastNameAr ?? e.LastName),
                          Number = e.EmployeeNumber,
                          DepartmentId = e.DepartmentId, DepartmentName = d != null ? (d.NameAr ?? d.Name) : null,
                          BranchId = e.BranchId, BranchName = b != null ? (b.NameAr ?? b.Name) : null,
                          JobTitleId = e.JobTitleId, JobTitleName = p != null ? (p.NameAr ?? p.NameEn) : null,
                      }).ToListAsync(ct);
    }

    /// <summary>Resolve which dates in [from,to] are official holidays (exact + recurring month/day).</summary>
    private async Task<HashSet<DateTime>> LoadHolidayDatesAsync(DateTime from, DateTime to, CancellationToken ct)
    {
        var holidays = await _db.AttendanceHolidays.AsNoTracking().Where(h => h.IsActive).ToListAsync(ct);
        var set = new HashSet<DateTime>();
        if (holidays.Count == 0) return set;
        for (var d = from.Date; d <= to.Date; d = d.AddDays(1))
            foreach (var h in holidays)
                if (h.IsRecurring ? (h.Date.Month == d.Month && h.Date.Day == d.Day) : h.Date.Date == d.Date)
                {
                    set.Add(d);
                    break;
                }
        return set;
    }

    private async Task<AttendancePolicySettings> LoadPolicyAsync(CancellationToken ct)
    {
        var p = await _db.AttendancePolicies.AsNoTracking()
            .Where(x => x.IsActive).OrderByDescending(x => x.IsDefault).FirstOrDefaultAsync(ct);
        return p is null
            ? new AttendancePolicySettings(0, 0, true, true)
            : new AttendancePolicySettings(p.DefaultGraceMinutes, p.RoundingMinutes, p.CountOvertime, p.AutoMarkAbsent);
    }

    /// <summary>Approved permission windows for one employee/day, as minutes-from-midnight.</summary>
    private async Task<List<PermissionWindow>> LoadPermissionWindowsAsync(Guid employeeId, DateTime date, CancellationToken ct)
    {
        var d = date.Date;
        return await _db.AttendancePermissions.AsNoTracking()
            .Where(p => p.EmployeeId == employeeId && p.Date == d)
            .Select(p => new PermissionWindow(p.FromMinutes, p.ToMinutes))
            .ToListAsync(ct);
    }

    private void AddAudit(AttendanceRecord rec, string action, string ar, string en)
        => _db.AttendanceAuditLogs.Add(new AttendanceAuditLog
        {
            AttendanceRecordId = rec.Id, EmployeeId = rec.EmployeeId, Date = rec.Date,
            Action = action, DetailsAr = ar, DetailsEn = en, ActorUserId = _user.UserId, At = DateTime.UtcNow,
        });

    private static bool IsPresentStatus(string s) =>
        s is nameof(AttendanceStatus.Present) or nameof(AttendanceStatus.Late)
          or nameof(AttendanceStatus.Overtime) or nameof(AttendanceStatus.ShortHours)
          or nameof(AttendanceStatus.WorkFromHome);

    private static AttendanceKpiDto BuildKpis(List<AttendanceDayDto> rows) => new()
    {
        Total = rows.Count,
        Present = rows.Count(r => IsPresentStatus(r.Status)),
        Absent = rows.Count(r => r.Status == nameof(AttendanceStatus.Absent)),
        Late = rows.Count(r => r.Status == nameof(AttendanceStatus.Late)),
        OnLeave = rows.Count(r => r.Status == nameof(AttendanceStatus.OnLeave)),
        MissingPunches = rows.Count(r => r.Status == nameof(AttendanceStatus.MissingCheckIn) || r.Status == nameof(AttendanceStatus.MissingCheckOut)),
        ShortHours = rows.Count(r => r.Status == nameof(AttendanceStatus.ShortHours)),
        Overtime = rows.Count(r => r.Status == nameof(AttendanceStatus.Overtime)),
        Weekend = rows.Count(r => r.Status == nameof(AttendanceStatus.Weekend)),
        Holiday = rows.Count(r => r.Status == nameof(AttendanceStatus.Holiday)),
    };

    /// <summary>Combine a UTC date with "HH:mm" (or "HH:mm:ss") into a UTC DateTime; null when blank.</summary>
    private static DateTime? CombineTime(DateTime date, string? time)
    {
        if (string.IsNullOrWhiteSpace(time)) return null;
        return TimeSpan.TryParse(time, out var ts)
            ? DateTime.SpecifyKind(date.Date.Add(ts), DateTimeKind.Utc) : null;
    }
}
