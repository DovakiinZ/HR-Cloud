using HR.Domain.Engines.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence.Configurations.Engines;

public class ReportDefinitionConfiguration : IEntityTypeConfiguration<ReportDefinition>
{
    public void Configure(EntityTypeBuilder<ReportDefinition> builder)
    {
        builder.ToTable("engine_report_definitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasMany(x => x.Fields).WithOne(x => x.ReportDefinition).HasForeignKey(x => x.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Relationships).WithOne(x => x.ReportDefinition).HasForeignKey(x => x.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Filters).WithOne(x => x.ReportDefinition).HasForeignKey(x => x.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Groupings).WithOne(x => x.ReportDefinition).HasForeignKey(x => x.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Sortings).WithOne(x => x.ReportDefinition).HasForeignKey(x => x.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Schedules).WithOne(x => x.ReportDefinition).HasForeignKey(x => x.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Shares).WithOne(x => x.ReportDefinition).HasForeignKey(x => x.ReportDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ReportTemplateConfiguration : IEntityTypeConfiguration<ReportTemplate>
{
    public void Configure(EntityTypeBuilder<ReportTemplate> builder)
    {
        builder.ToTable("engine_report_templates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Configuration).HasColumnType("jsonb").IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
    }
}

public class ReportFieldConfiguration : IEntityTypeConfiguration<ReportField>
{
    public void Configure(EntityTypeBuilder<ReportField> builder)
    {
        builder.ToTable("engine_report_fields");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FieldCode).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayNameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayNameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.CalculationExpression).HasMaxLength(1000);
        builder.Property(x => x.CalculationText).HasMaxLength(1000);
        builder.Property(x => x.FormatPattern).HasMaxLength(100);
    }
}

public class ReportRelationshipConfiguration : IEntityTypeConfiguration<ReportRelationship>
{
    public void Configure(EntityTypeBuilder<ReportRelationship> builder)
    {
        builder.ToTable("engine_report_relationships");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.JoinField).HasMaxLength(200).IsRequired();
        builder.Property(x => x.JoinType).HasMaxLength(20).IsRequired();
    }
}

public class ReportFilterConfiguration : IEntityTypeConfiguration<ReportFilter>
{
    public void Configure(EntityTypeBuilder<ReportFilter> builder)
    {
        builder.ToTable("engine_report_filters");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FieldCode).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(1000);
        builder.Property(x => x.ValueTo).HasMaxLength(1000);
        builder.Property(x => x.LogicalOperator).HasMaxLength(10);
    }
}

public class ReportGroupingConfiguration : IEntityTypeConfiguration<ReportGrouping>
{
    public void Configure(EntityTypeBuilder<ReportGrouping> builder)
    {
        builder.ToTable("engine_report_groupings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FieldCode).HasMaxLength(200).IsRequired();
    }
}

public class ReportSortingConfiguration : IEntityTypeConfiguration<ReportSorting>
{
    public void Configure(EntityTypeBuilder<ReportSorting> builder)
    {
        builder.ToTable("engine_report_sortings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FieldCode).HasMaxLength(200).IsRequired();
    }
}

public class ReportScheduleConfiguration : IEntityTypeConfiguration<ReportSchedule>
{
    public void Configure(EntityTypeBuilder<ReportSchedule> builder)
    {
        builder.ToTable("engine_report_schedules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CronExpression).HasMaxLength(100);
        builder.Property(x => x.Recipients).HasColumnType("jsonb").IsRequired();
    }
}

public class ReportShareConfiguration : IEntityTypeConfiguration<ReportShare>
{
    public void Configure(EntityTypeBuilder<ReportShare> builder)
    {
        builder.ToTable("engine_report_shares");
        builder.HasKey(x => x.Id);
    }
}
