using HR.Domain.Engines.Attendance;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence.Configurations;

// NOTE: AttendanceRecord itself is already mapped (to "engine_attendance_records") by
// Configurations/Engines/LeaveAttendanceConfigurations.cs. The new calculation columns added to the
// entity are mapped by convention — we deliberately do NOT add a second config here to avoid a
// conflicting (and unsafe — a day can legitimately have multiple source rows) unique index.

public class ShiftConfiguration : IEntityTypeConfiguration<Shift>
{
    public void Configure(EntityTypeBuilder<Shift> builder)
    {
        builder.ToTable("attendance_shifts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NameAr).HasMaxLength(150).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
        builder.Property(x => x.WeekendDays).HasMaxLength(40);
        builder.HasIndex(x => x.TenantId);
    }
}

public class ShiftAssignmentConfiguration : IEntityTypeConfiguration<ShiftAssignment>
{
    public void Configure(EntityTypeBuilder<ShiftAssignment> builder)
    {
        builder.ToTable("attendance_shift_assignments");
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.Shift).WithMany().HasForeignKey(x => x.ShiftId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId });
        builder.HasIndex(x => new { x.TenantId, x.DepartmentId });
        builder.HasIndex(x => new { x.TenantId, x.BranchId });
        builder.HasIndex(x => new { x.TenantId, x.JobTitleId });
    }
}

public class AttendancePunchConfiguration : IEntityTypeConfiguration<AttendancePunch>
{
    public void Configure(EntityTypeBuilder<AttendancePunch> builder)
    {
        builder.ToTable("attendance_punches");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(50);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PunchTime });
        builder.HasIndex(x => x.AttendanceRecordId);
    }
}

public class AttendanceCorrectionConfiguration : IEntityTypeConfiguration<AttendanceCorrection>
{
    public void Configure(EntityTypeBuilder<AttendanceCorrection> builder)
    {
        builder.ToTable("attendance_corrections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.AttendanceRecordId);
    }
}

public class AttendancePolicyConfiguration : IEntityTypeConfiguration<AttendancePolicy>
{
    public void Configure(EntityTypeBuilder<AttendancePolicy> builder)
    {
        builder.ToTable("attendance_policies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NameAr).HasMaxLength(150);
        builder.Property(x => x.NameEn).HasMaxLength(150);
        builder.HasIndex(x => x.TenantId);
    }
}

public class AttendancePermissionConfiguration : IEntityTypeConfiguration<AttendancePermission>
{
    public void Configure(EntityTypeBuilder<AttendancePermission> builder)
    {
        builder.ToTable("attendance_permissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Source).HasMaxLength(50);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasIndex(x => x.TenantId);
        // Lookup by employee/day and calendar-month range (calc, display, cap tally).
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Date });
        // Per-type usage tally (Finding 1): filter by type before summing minutes/counts.
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.PermissionTypeId, x.Date });
        // Idempotency: one permission per approved request instance.
        builder.HasIndex(x => new { x.TenantId, x.RequestInstanceId });
    }
}

public class AttendanceHolidayConfiguration : IEntityTypeConfiguration<AttendanceHoliday>
{
    public void Configure(EntityTypeBuilder<AttendanceHoliday> builder)
    {
        builder.ToTable("attendance_holidays");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.NameAr).HasMaxLength(150).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(150).IsRequired();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Date });
    }
}

public class AttendanceAuditLogConfiguration : IEntityTypeConfiguration<AttendanceAuditLog>
{
    public void Configure(EntityTypeBuilder<AttendanceAuditLog> builder)
    {
        builder.ToTable("attendance_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Action).HasMaxLength(80).IsRequired();
        builder.Property(x => x.DetailsAr).HasMaxLength(1000);
        builder.Property(x => x.DetailsEn).HasMaxLength(1000);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.AttendanceRecordId });
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Date });
    }
}
