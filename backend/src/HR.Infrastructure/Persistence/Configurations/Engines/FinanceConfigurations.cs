using HR.Domain.Engines.Finance.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence.Configurations.Engines;

public class FinancialLedgerEntryConfiguration : IEntityTypeConfiguration<FinancialLedgerEntry>
{
    public void Configure(EntityTypeBuilder<FinancialLedgerEntry> builder)
    {
        builder.ToTable("engine_finance_ledger_entries");
        builder.HasKey(x => x.Id);
        builder.Ignore(x => x.SignedAmount);

        builder.Property(x => x.EntryNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.ComponentCode).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.ReferenceType).HasMaxLength(120);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.EntryNumber }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId, x.Currency });
        builder.HasIndex(x => x.PayrollRunId);
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
        builder.HasIndex(x => x.ReversesEntryId);
        builder.HasIndex(x => x.SourceModule);
    }
}

public class PayrollDefinitionConfiguration : IEntityTypeConfiguration<PayrollDefinition>
{
    public void Configure(EntityTypeBuilder<PayrollDefinition> builder)
    {
        builder.ToTable("engine_payroll_definitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasIndex(x => x.Status);

        builder.HasMany(x => x.Versions)
            .WithOne(v => v.Definition)
            .HasForeignKey(v => v.PayrollDefinitionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PayrollDefinitionVersionConfiguration : IEntityTypeConfiguration<PayrollDefinitionVersion>
{
    public void Configure(EntityTypeBuilder<PayrollDefinitionVersion> builder)
    {
        builder.ToTable("engine_payroll_definition_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EmployeeFilterJson).HasColumnType("jsonb");
        builder.Property(x => x.CycleConfigJson).HasColumnType("jsonb");
        builder.Property(x => x.SelectionScopeJson).HasColumnType("jsonb");
        builder.Property(x => x.CalcSettingsJson).HasColumnType("jsonb");
        builder.Property(x => x.PaymentMethodScopeJson).HasColumnType("jsonb");
        builder.Property(x => x.CutoffDay).HasDefaultValue(27);
        builder.Property(x => x.DayBasis).HasDefaultValue(HR.Domain.Enums.DayBasis.CalendarMonth);
        builder.Property(x => x.CarryToNextPeriod).HasDefaultValue(true);
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(2000);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.PayrollDefinitionId, x.VersionNumber }).IsUnique();
        builder.HasIndex(x => x.RuleSetVersionId);
    }
}

public class RuleSetConfiguration : IEntityTypeConfiguration<RuleSet>
{
    public void Configure(EntityTypeBuilder<RuleSet> builder)
    {
        builder.ToTable("engine_finance_rule_sets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();

        builder.HasMany(x => x.Versions)
            .WithOne(v => v.RuleSet)
            .HasForeignKey(v => v.RuleSetId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RuleSetVersionConfiguration : IEntityTypeConfiguration<RuleSetVersion>
{
    public void Configure(EntityTypeBuilder<RuleSetVersion> builder)
    {
        builder.ToTable("engine_finance_rule_set_versions");
        builder.HasKey(x => x.Id);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.RuleSetId, x.VersionNumber }).IsUnique();

        builder.HasMany(x => x.Rules)
            .WithOne(r => r.Version)
            .HasForeignKey(r => r.RuleSetVersionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RuleConfiguration : IEntityTypeConfiguration<Rule>
{
    public void Configure(EntityTypeBuilder<Rule> builder)
    {
        builder.ToTable("engine_finance_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200);
        builder.Property(x => x.ConditionText).HasMaxLength(4000);
        builder.Property(x => x.ConditionAstJson).HasColumnType("jsonb");
        builder.Property(x => x.ExpressionText).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ExpressionAstJson).HasColumnType("jsonb");
        builder.Property(x => x.OutputComponentCode).HasMaxLength(80).IsRequired();

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.RuleSetVersionId, x.Code }).IsUnique();
    }
}

public class FormulaFunctionConfiguration : IEntityTypeConfiguration<FormulaFunction>
{
    public void Configure(EntityTypeBuilder<FormulaFunction> builder)
    {
        builder.ToTable("engine_finance_formula_functions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ParametersCsv).HasMaxLength(500);
        builder.Property(x => x.ExpressionText).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.ExpressionAstJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public class PayrollRunConfiguration : IEntityTypeConfiguration<PayrollRun>
{
    public void Configure(EntityTypeBuilder<PayrollRun> builder)
    {
        builder.ToTable("engine_payroll_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RunNumber).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.GrossTotal).HasColumnType("decimal(18,2)");
        builder.Property(x => x.DeductionTotal).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetTotal).HasColumnType("decimal(18,2)");
        builder.Property(x => x.CalculationVersion).HasMaxLength(20);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.ValidationResultJson).HasColumnType("jsonb");

        builder.Property(x => x.TargetPeriodYear).IsRequired();
        builder.Property(x => x.TargetPeriodMonth).IsRequired();

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.RunNumber }).IsUnique();
        builder.HasIndex(x => x.PayrollDefinitionId);
        builder.HasIndex(x => x.State);
        builder.HasIndex(x => new { x.TargetPeriodYear, x.TargetPeriodMonth });
        // One active run per (definition, period). Excludes Cancelled (11); SP6 will extend the filter
        // to also exclude Voided/Superseded states once those are introduced.
        builder.HasIndex(x => new { x.PayrollDefinitionId, x.TargetPeriodYear, x.TargetPeriodMonth })
               .IsUnique()
               .HasFilter("\"State\" <> 11");   // 11 = Cancelled (PayrollRunState.Cancelled)

        builder.HasMany(x => x.Transitions)
            .WithOne(t => t.Run)
            .HasForeignKey(t => t.PayrollRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Payslips)
            .WithOne(p => p.Run)
            .HasForeignKey(p => p.PayrollRunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Items)
            .WithOne(i => i.Run)
            .HasForeignKey(i => i.PayrollRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PayrollRunItemConfiguration : IEntityTypeConfiguration<PayrollRunItem>
{
    public void Configure(EntityTypeBuilder<PayrollRunItem> builder)
    {
        builder.ToTable("engine_payroll_run_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Error).HasMaxLength(2000);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.PayrollRunId, x.EmployeeId }).IsUnique();
        builder.HasIndex(x => new { x.PayrollRunId, x.State });
    }
}

public class PayrollPayslipConfiguration : IEntityTypeConfiguration<PayrollPayslip>
{
    public void Configure(EntityTypeBuilder<PayrollPayslip> builder)
    {
        builder.ToTable("engine_payroll_payslips");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EmployeeNumber).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EmployeeName).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        builder.Property(x => x.GrossEarnings).HasColumnType("decimal(18,2)");
        builder.Property(x => x.TotalDeductions).HasColumnType("decimal(18,2)");
        builder.Property(x => x.NetAmount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.FactsJson).HasColumnType("jsonb");
        builder.Property(x => x.ComponentsJson).HasColumnType("jsonb");
        builder.Property(x => x.WarningsJson).HasColumnType("jsonb");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.PayrollRunId, x.EmployeeId }).IsUnique();
        builder.HasIndex(x => x.EmployeeId);
    }
}

public class PayrollRunTransitionConfiguration : IEntityTypeConfiguration<PayrollRunTransition>
{
    public void Configure(EntityTypeBuilder<PayrollRunTransition> builder)
    {
        builder.ToTable("engine_payroll_run_transitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(1000);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.PayrollRunId);
    }
}

public class PayrollRunPopulationConfiguration : IEntityTypeConfiguration<PayrollRunPopulation>
{
    public void Configure(EntityTypeBuilder<PayrollRunPopulation> builder)
    {
        builder.ToTable("engine_payroll_run_population");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.TenantId, x.PayrollRunId });
        builder.Property(x => x.EmployeeNumber).HasMaxLength(64);
        builder.Property(x => x.EmployeeName).HasMaxLength(256);
        builder.Property(x => x.ExclusionReasonCode).HasMaxLength(64);

        builder.HasOne<PayrollRun>()
            .WithMany()
            .HasForeignKey(x => x.PayrollRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PayrollTransactionConfiguration : IEntityTypeConfiguration<PayrollTransaction>
{
    public void Configure(EntityTypeBuilder<PayrollTransaction> builder)
    {
        builder.ToTable("engine_payroll_transactions");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Amount).HasColumnType("decimal(18,2)");
        builder.Property(x => x.SourceModule).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ReferenceType).HasMaxLength(120);
        builder.Property(x => x.Notes).HasMaxLength(2000);
        builder.Property(x => x.StatusReason).HasMaxLength(1000);
        builder.Property(x => x.ReversalReason).HasMaxLength(1000);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId });
        builder.HasIndex(x => new { x.TenantId, x.Kind, x.Status });
        builder.HasIndex(x => new { x.TenantId, x.TargetPeriodYear, x.TargetPeriodMonth });
        builder.HasIndex(x => new { x.ReferenceType, x.ReferenceId });
        builder.HasIndex(x => x.ReversesTransactionId);
    }
}

public class PayrollTransactionAttendanceReferenceConfiguration : IEntityTypeConfiguration<PayrollTransactionAttendanceReference>
{
    public void Configure(EntityTypeBuilder<PayrollTransactionAttendanceReference> builder)
    {
        builder.ToTable("engine_payroll_transaction_attendance_refs");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.AmountContribution).HasColumnType("decimal(18,2)");

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.PayrollTransactionId);
        builder.HasIndex(x => x.AttendanceRecordId);

        builder.HasOne<PayrollTransaction>()
            .WithMany()
            .HasForeignKey(x => x.PayrollTransactionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
