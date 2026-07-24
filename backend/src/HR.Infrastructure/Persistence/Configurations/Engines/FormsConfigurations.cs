using HR.Domain.Engines.Forms;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence.Configurations.Engines;

public class FormDefinitionConfiguration : IEntityTypeConfiguration<FormDefinition>
{
    public void Configure(EntityTypeBuilder<FormDefinition> builder)
    {
        builder.ToTable("engine_form_definitions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Module).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasMany(x => x.Fields).WithOne(x => x.FormDefinition).HasForeignKey(x => x.FormDefinitionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Submissions).WithOne(x => x.FormDefinition).HasForeignKey(x => x.FormDefinitionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class FormFieldConfiguration : IEntityTypeConfiguration<FormField>
{
    public void Configure(EntityTypeBuilder<FormField> builder)
    {
        builder.ToTable("engine_form_fields");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SectionName).HasMaxLength(200);
        builder.Property(x => x.ValidationRules).HasColumnType("jsonb");
        builder.Property(x => x.Options).HasColumnType("jsonb");
        builder.Property(x => x.MetadataJson).HasColumnType("jsonb");
        builder.HasMany(x => x.SubmissionValues).WithOne(x => x.FormField).HasForeignKey(x => x.FormFieldId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class FormSubmissionConfiguration : IEntityTypeConfiguration<FormSubmission>
{
    public void Configure(EntityTypeBuilder<FormSubmission> builder)
    {
        builder.ToTable("engine_form_submissions");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.FormDefinitionId);
        builder.HasMany(x => x.Values).WithOne(x => x.FormSubmission).HasForeignKey(x => x.FormSubmissionId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class FormSubmissionValueConfiguration : IEntityTypeConfiguration<FormSubmissionValue>
{
    public void Configure(EntityTypeBuilder<FormSubmissionValue> builder)
    {
        builder.ToTable("engine_form_submission_values");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FieldCode).HasMaxLength(100).IsRequired();
    }
}
