using HR.Domain.Engines.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence.Configurations.Engines;

public class ReportFolderConfiguration : IEntityTypeConfiguration<ReportFolder>
{
    public void Configure(EntityTypeBuilder<ReportFolder> b)
    {
        b.ToTable("engine_report_folders");
        b.HasKey(x => x.Id);
        b.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        b.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.ParentFolderId });
    }
}

public class ReportTagConfiguration : IEntityTypeConfiguration<ReportTag>
{
    public void Configure(EntityTypeBuilder<ReportTag> b)
    {
        b.ToTable("engine_report_tags");
        b.HasKey(x => x.Id);
        b.Property(x => x.Name).HasMaxLength(100).IsRequired();
        b.Property(x => x.Color).HasMaxLength(20);
        b.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public class ReportDefinitionTagConfiguration : IEntityTypeConfiguration<ReportDefinitionTag>
{
    public void Configure(EntityTypeBuilder<ReportDefinitionTag> b)
    {
        b.ToTable("engine_report_definition_tags");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.ReportDefinitionId, x.ReportTagId }).IsUnique();
    }
}

public class ReportUserStateConfiguration : IEntityTypeConfiguration<ReportUserState>
{
    public void Configure(EntityTypeBuilder<ReportUserState> b)
    {
        b.ToTable("engine_report_user_states");
        b.HasKey(x => x.Id);
        b.HasIndex(x => new { x.TenantId, x.UserId, x.ReportDefinitionId }).IsUnique();
    }
}
