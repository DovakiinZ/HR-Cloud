using HR.Application.Common.Interfaces;
using HR.Domain.Engines.ObjectRegistry;
using HR.Domain.Engines.Reports;
using HR.Domain.Enums;
using HR.Infrastructure.Persistence;
using HR.Modules.Platform.Services.Catalog;
using Microsoft.EntityFrameworkCore;

namespace HR.Modules.Platform.Services.Reports;

/// <summary>
/// Provisions the built-in report definitions. Like <see cref="Dashboards.DashboardSeeder"/> this is
/// object-driven: every field, filter and sort is checked against the live catalog before it is
/// written, so the seeder ships only what this tenant's model actually supports rather than creating
/// definitions that throw at run time.
///
/// Idempotency is keyed on <see cref="ReportDefinition.Code"/> within the tenant (the global query
/// filter scopes the lookup). An existing report is left exactly as it is — never patched, never
/// deleted — so a tenant that customised a built-in report keeps their edits across restarts.
/// </summary>
public sealed class ReportSeeder : IReportSeeder
{
    private readonly ApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IObjectCatalogService _catalog;

    public ReportSeeder(ApplicationDbContext db, ICurrentUserService user, IObjectCatalogService catalog)
    {
        _db = db; _user = user; _catalog = catalog;
    }

    // ── Spec model ────────────────────────────────────────────────────────────

    /// <param name="Field">Catalog field code. Skipped silently when absent from the object.</param>
    /// <param name="IsParameter">True → the viewer renders an input and the caller may override it.</param>
    /// <param name="Value">
    /// The stored default. A parameter filter left blank here resolves to "no constraint" — see the
    /// blank-parameter handling in <see cref="ReportObjectResolver"/>. A non-parameter filter's value
    /// is the fixed predicate that defines the report (e.g. Status = Absent) and cannot be overridden.
    /// </param>
    private sealed record F(
        string Field,
        ReportFilterOperator Op,
        bool IsParameter,
        string? Value = null,
        string? ValueTo = null);

    private sealed record Col(string Field, string Ar, string En, AggregationType? Agg = null, string? Format = null);

    /// <param name="Formula">Set for a CalculatedField; compiled to an AST via ReportFormulaCompiler.</param>
    private sealed record Calc(string Code, string Ar, string En, string Formula, string? Format = null);

    private sealed record Spec(
        string Code,
        string NameAr,
        string NameEn,
        string Description,
        string ObjectCode,
        ReportType Type,
        Col[] Columns,
        F[] Filters,
        (string Field, SortDirection Dir)[] Sorts,
        string[] GroupBy,
        Calc[] Calculated);

    /// <summary>A Between default wide enough to mean "everything". A literal window (this month,
    /// this year) would silently go stale and start hiding rows the moment the calendar moved past it.</summary>
    private const string DateFloor = "1900-01-01";
    private const string DateCeiling = "2999-12-31";

    private static Spec[] Specs() => new[]
    {
        // ── Attendance ────────────────────────────────────────────────────────
        new Spec("attendance-daily", "تقرير الحضور اليومي", "Daily Attendance Report",
            "سجل الحضور والانصراف التفصيلي لكل موظف في فترة محددة",
            "AttendanceRecord", ReportType.Tabular,
            new[]
            {
                new Col("EmployeeId", "الموظف", "Employee"),
                new Col("Date", "التاريخ", "Date"),
                new Col("Status", "الحالة", "Status"),
                new Col("CheckIn", "وقت الحضور", "Check In"),
                new Col("CheckOut", "وقت الانصراف", "Check Out"),
                new Col("WorkedMinutes", "دقائق العمل", "Worked Minutes"),
                new Col("LateMinutes", "دقائق التأخير", "Late Minutes"),
                new Col("OvertimeMinutes", "دقائق العمل الإضافي", "Overtime Minutes"),
            },
            new[]
            {
                new F("Date", ReportFilterOperator.Between, true, DateFloor, DateCeiling),
                new F("EmployeeId", ReportFilterOperator.Equals, true),
                new F("Status", ReportFilterOperator.Equals, true),
            },
            new[] { ("Date", SortDirection.Descending) },
            Array.Empty<string>(), Array.Empty<Calc>()),

        new Spec("attendance-summary", "ملخص الحضور", "Attendance Summary",
            "إجماليات الحضور لكل موظف خلال الفترة المحددة",
            "AttendanceRecord", ReportType.Summary,
            new[]
            {
                new Col("EmployeeId", "الموظف", "Employee"),
                new Col("WorkedMinutes", "إجمالي دقائق العمل", "Total Worked", AggregationType.Sum),
                new Col("LateMinutes", "إجمالي دقائق التأخير", "Total Late", AggregationType.Sum),
                new Col("OvertimeMinutes", "إجمالي العمل الإضافي", "Total Overtime", AggregationType.Sum),
                new Col("ShortageMinutes", "إجمالي دقائق النقص", "Total Shortage", AggregationType.Sum),
            },
            new[]
            {
                new F("Date", ReportFilterOperator.Between, true, DateFloor, DateCeiling),
                new F("EmployeeId", ReportFilterOperator.Equals, true),
            },
            Array.Empty<(string, SortDirection)>(),
            new[] { "EmployeeId" }, Array.Empty<Calc>()),

        new Spec("attendance-late", "تقرير التأخير", "Late Attendance Report",
            "أيام التأخير لكل موظف مرتبة تنازلياً حسب مدة التأخير",
            "AttendanceRecord", ReportType.Tabular,
            new[]
            {
                new Col("EmployeeId", "الموظف", "Employee"),
                new Col("Date", "التاريخ", "Date"),
                new Col("CheckIn", "وقت الحضور", "Check In"),
                new Col("LateMinutes", "دقائق التأخير", "Late Minutes"),
                new Col("Status", "الحالة", "Status"),
            },
            new[]
            {
                // Not a parameter: "> 0" is what makes this the late report rather than a generic one.
                new F("LateMinutes", ReportFilterOperator.GreaterThan, false, "0"),
                new F("Date", ReportFilterOperator.Between, true, DateFloor, DateCeiling),
                new F("EmployeeId", ReportFilterOperator.Equals, true),
            },
            new[] { ("LateMinutes", SortDirection.Descending) },
            Array.Empty<string>(), Array.Empty<Calc>()),

        new Spec("attendance-absence", "تقرير الغياب", "Absence Report",
            "أيام الغياب المسجلة لكل موظف خلال الفترة المحددة",
            "AttendanceRecord", ReportType.Tabular,
            new[]
            {
                new Col("EmployeeId", "الموظف", "Employee"),
                new Col("Date", "التاريخ", "Date"),
                new Col("Status", "الحالة", "Status"),
                new Col("Notes", "ملاحظات", "Notes"),
            },
            new[]
            {
                // Fixed at Absent (AttendanceStatus.Absent = 2) — the defining predicate of this report.
                new F("Status", ReportFilterOperator.Equals, false, ((int)AttendanceStatus.Absent).ToString()),
                new F("Date", ReportFilterOperator.Between, true, DateFloor, DateCeiling),
                new F("EmployeeId", ReportFilterOperator.Equals, true),
            },
            new[] { ("Date", SortDirection.Descending) },
            Array.Empty<string>(), Array.Empty<Calc>()),

        // ── Leave ─────────────────────────────────────────────────────────────
        new Spec("leave-balance", "تقرير أرصدة الإجازات", "Leave Balance Report",
            "أرصدة الإجازات المستحقة والمستخدمة والمتبقية لكل موظف",
            "LeaveBalance", ReportType.Tabular,
            new[]
            {
                new Col("EmployeeId", "الموظف", "Employee"),
                new Col("LeaveTypeId", "نوع الإجازة", "Leave Type"),
                new Col("Year", "السنة", "Year"),
                new Col("EntitledDays", "الأيام المستحقة", "Entitled Days", Format: "N2"),
                new Col("CarriedForwardDays", "المرحّلة", "Carried Forward", Format: "N2"),
                new Col("UsedDays", "الأيام المستخدمة", "Used Days", Format: "N2"),
            },
            new[]
            {
                new F("Year", ReportFilterOperator.Equals, true, DateTime.UtcNow.Year.ToString()),
                new F("LeaveTypeId", ReportFilterOperator.Equals, true),
                new F("EmployeeId", ReportFilterOperator.Equals, true),
            },
            new[] { ("Year", SortDirection.Descending) },
            Array.Empty<string>(),
            // RemainingDays is a C# computed property and therefore absent from the catalog. It is
            // reproduced here through the existing CalculatedField path — the same arithmetic, over
            // three columns the report already selects.
            new[] { new Calc("RemainingDays", "الرصيد المتبقي", "Remaining Days",
                "EntitledDays + CarriedForwardDays - UsedDays", "N2") }),

        // ── Payroll ───────────────────────────────────────────────────────────
        new Spec("payroll-register", "سجل الرواتب", "Payroll Register",
            "سجل الرواتب التفصيلي: الإجمالي والاستقطاعات والصافي لكل موظف",
            "PayrollPayslip", ReportType.Tabular,
            new[]
            {
                new Col("EmployeeNumber", "الرقم الوظيفي", "Employee Number"),
                new Col("EmployeeName", "اسم الموظف", "Employee Name"),
                new Col("Currency", "العملة", "Currency"),
                new Col("GrossEarnings", "إجمالي الاستحقاق", "Gross Earnings", Format: "N2"),
                new Col("TotalDeductions", "إجمالي الاستقطاعات", "Total Deductions", Format: "N2"),
                new Col("NetAmount", "الصافي", "Net Amount", Format: "N2"),
            },
            new[]
            {
                new F("PayrollRunId", ReportFilterOperator.Equals, true),
                new F("EmployeeId", ReportFilterOperator.Equals, true),
                new F("CreatedAt", ReportFilterOperator.Between, true, DateFloor, DateCeiling),
            },
            new[] { ("EmployeeNumber", SortDirection.Ascending) },
            Array.Empty<string>(), Array.Empty<Calc>()),

        // ── Employees ─────────────────────────────────────────────────────────
        new Spec("employee-directory", "دليل الموظفين", "Employee Directory",
            "بيانات الموظفين الأساسية والتنظيمية",
            "Employee", ReportType.Tabular,
            new[]
            {
                new Col("EmployeeNumber", "الرقم الوظيفي", "Employee Number"),
                new Col("FirstName", "الاسم الأول", "First Name"),
                new Col("LastName", "اسم العائلة", "Last Name"),
                new Col("Email", "البريد الإلكتروني", "Email"),
                new Col("Phone", "الهاتف", "Phone"),
                new Col("DepartmentId", "الإدارة", "Department"),
                new Col("JobTitleId", "المسمى الوظيفي", "Job Title"),
                new Col("BranchId", "الفرع", "Branch"),
                new Col("HireDate", "تاريخ التعيين", "Hire Date"),
                new Col("Status", "الحالة", "Status"),
            },
            new[]
            {
                new F("DepartmentId", ReportFilterOperator.Equals, true),
                new F("BranchId", ReportFilterOperator.Equals, true),
                new F("Status", ReportFilterOperator.Equals, true),
                new F("HireDate", ReportFilterOperator.Between, true, DateFloor, DateCeiling),
            },
            new[] { ("EmployeeNumber", SortDirection.Ascending) },
            Array.Empty<string>(), Array.Empty<Calc>()),
    };

    public IReadOnlyList<string> AvailableCodes() => Specs().Select(s => s.Code).ToList();

    // ── Seeding ───────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ReportSeedOutcome>> SeedDefaultsAsync(CancellationToken ct)
    {
        var specs = Specs();
        var outcomes = new List<ReportSeedOutcome>(specs.Length);

        // One round-trip for existing codes rather than one per spec.
        var codes = specs.Select(s => s.Code).ToList();
        var existing = await _db.Set<ReportDefinition>().AsNoTracking()
            .Where(r => codes.Contains(r.Code))
            .Select(r => new { r.Code, r.Id })
            .ToListAsync(ct);
        var existingByCode = existing.ToDictionary(x => x.Code, x => x.Id, StringComparer.OrdinalIgnoreCase);

        var anyWritten = false;

        foreach (var spec in specs)
        {
            if (existingByCode.TryGetValue(spec.Code, out var presentId))
            {
                outcomes.Add(new ReportSeedOutcome(spec.Code, spec.NameAr, ReportSeedStatus.AlreadyPresent, presentId, null));
                continue;
            }

            var resolved = _catalog.Resolve(spec.ObjectCode);
            if (resolved is null)
            {
                outcomes.Add(new ReportSeedOutcome(spec.Code, spec.NameAr, ReportSeedStatus.Unsupported, null,
                    $"Object '{spec.ObjectCode}' is not discoverable in this model."));
                continue;
            }

            var columns = spec.Columns.Where(c => resolved.Field(c.Field) is not null).ToList();
            if (columns.Count == 0)
            {
                outcomes.Add(new ReportSeedOutcome(spec.Code, spec.NameAr, ReportSeedStatus.Unsupported, null,
                    $"None of the specified fields exist on '{spec.ObjectCode}'."));
                continue;
            }

            var objectDefId = await EnsureObjectDefinitionAsync(spec.ObjectCode, resolved, ct);
            var report = BuildReport(spec, resolved, columns, objectDefId);
            _db.Set<ReportDefinition>().Add(report);
            anyWritten = true;

            outcomes.Add(new ReportSeedOutcome(spec.Code, spec.NameAr, ReportSeedStatus.Created, report.Id, null));
        }

        // Single commit: a partially-seeded set on a mid-way failure would be re-attempted on the next
        // call anyway, but committing once keeps the catalogue consistent for concurrent readers.
        if (anyWritten) await _db.SaveChangesAsync(ct);
        return outcomes;
    }

    /// <summary>ObjectDefinition rows are the registry handle a report stores (reports reference objects
    /// by Guid, not by code). Created on demand and reused — the catalog itself stays reflective.</summary>
    private async Task<Guid> EnsureObjectDefinitionAsync(string code, ResolvedObject resolved, CancellationToken ct)
    {
        var existing = await _db.Set<ObjectDefinition>()
            .FirstOrDefaultAsync(o => o.Code == code, ct);
        if (existing is not null) return existing.Id;

        // A local add from an earlier spec in this same pass is not yet queryable — check the tracker
        // too, otherwise two reports over the same object would each insert a duplicate definition.
        var pending = _db.ChangeTracker.Entries<ObjectDefinition>()
            .FirstOrDefault(e => e.State == EntityState.Added
                              && string.Equals(e.Entity.Code, code, StringComparison.OrdinalIgnoreCase));
        if (pending is not null) return pending.Entity.Id;

        var dto = _catalog.GetObject(code);
        var def = new ObjectDefinition
        {
            Code = code,
            NameEn = dto?.NameEn ?? code,
            NameAr = dto?.NameAr ?? code,
            Module = dto?.Module ?? "Platform",
            TableName = resolved.TableName,
            IsSystem = true,
            IsActive = true,
        };
        _db.Set<ObjectDefinition>().Add(def);
        return def.Id;
    }

    private static ReportDefinition BuildReport(Spec spec, ResolvedObject resolved, List<Col> columns, Guid objectDefId)
    {
        var report = new ReportDefinition
        {
            Code = spec.Code,
            NameEn = spec.NameEn,
            NameAr = spec.NameAr,
            Description = spec.Description,
            ReportType = spec.Type,
            Scope = ReportScope.Company,
            PrimaryObjectId = objectDefId,
            IsPublished = true,
            IsActive = true,
            Version = 1,
        };

        var order = 0;
        foreach (var c in columns)
        {
            report.Fields.Add(new ReportField
            {
                ReportDefinitionId = report.Id,
                FieldType = ReportFieldType.ObjectField,
                ObjectDefinitionId = objectDefId,
                FieldCode = c.Field,
                DisplayNameAr = c.Ar,
                DisplayNameEn = c.En,
                Aggregation = c.Agg,
                FormatPattern = c.Format,
                SortOrder = order++,
                IsVisible = true,
            });
        }

        // Computed columns come last so they read as derived from the columns above them.
        foreach (var calc in spec.Calculated)
        {
            // Only emit the formula when every variable it references is a column we actually selected;
            // otherwise the row evaluator would fault at run time on a missing variable.
            if (!FormulaInputsAvailable(calc.Formula, columns)) continue;

            report.Fields.Add(new ReportField
            {
                ReportDefinitionId = report.Id,
                FieldType = ReportFieldType.CalculatedField,
                FieldCode = calc.Code,
                DisplayNameAr = calc.Ar,
                DisplayNameEn = calc.En,
                CalculationText = calc.Formula,
                CalculationExpression = ReportFormulaCompiler.Compile(calc.Formula, null),
                FormatPattern = calc.Format,
                SortOrder = order++,
                IsVisible = true,
            });
        }

        var filterOrder = 0;
        foreach (var f in spec.Filters)
        {
            if (resolved.Field(f.Field) is null) continue;
            report.Filters.Add(new ReportFilter
            {
                ReportDefinitionId = report.Id,
                FieldCode = f.Field,
                Operator = f.Op,
                Value = f.Value,
                ValueTo = f.ValueTo,
                LogicalOperator = "AND",   // the SQL builder ANDs regardless; stored for fidelity
                IsParameter = f.IsParameter,
                SortOrder = filterOrder++,
            });
        }

        var sortOrder = 0;
        foreach (var (field, dir) in spec.Sorts)
        {
            if (resolved.Field(field) is null) continue;
            report.Sortings.Add(new ReportSorting
            {
                ReportDefinitionId = report.Id,
                FieldCode = field,
                Direction = dir,
                SortOrder = sortOrder++,
            });
        }

        var groupOrder = 0;
        foreach (var g in spec.GroupBy)
        {
            if (resolved.Field(g) is null) continue;
            report.Groupings.Add(new ReportGrouping
            {
                ReportDefinitionId = report.Id,
                FieldCode = g,
                SortOrder = groupOrder++,
            });
        }

        return report;
    }

    /// <summary>True when every identifier the formula mentions is one of the selected columns.</summary>
    private static bool FormulaInputsAvailable(string formula, List<Col> columns)
    {
        var available = columns.Select(c => c.Field).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var token = new System.Text.StringBuilder();
        foreach (var ch in formula + " ")
        {
            if (char.IsLetterOrDigit(ch) || ch == '_') { token.Append(ch); continue; }
            if (token.Length > 0)
            {
                var name = token.ToString();
                token.Clear();
                // Numeric literals are not variables.
                if (!char.IsDigit(name[0]) && !available.Contains(name)) return false;
            }
        }
        return true;
    }
}
