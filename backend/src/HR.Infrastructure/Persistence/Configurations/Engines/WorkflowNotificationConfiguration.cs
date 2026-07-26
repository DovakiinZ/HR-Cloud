using HR.Domain.Engines.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence.Configurations.Engines;

public class WorkflowNotificationRuleConfiguration : IEntityTypeConfiguration<WorkflowNotificationRule>
{
    public void Configure(EntityTypeBuilder<WorkflowNotificationRule> builder)
    {
        builder.ToTable("workflow_notification_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.RequestTypeCode).HasMaxLength(100);
        builder.Property(x => x.RecipientsJson).HasColumnType("jsonb");
        builder.Property(x => x.SubjectAr).HasMaxLength(300);
        builder.Property(x => x.SubjectEn).HasMaxLength(300);
        builder.Property(x => x.SystemKey).HasMaxLength(200);
        // Dispatcher hot path: tenant + type + event + active.
        builder.HasIndex(x => new { x.TenantId, x.RequestTypeCode, x.Event, x.IsActive });
        builder.HasIndex(x => new { x.TenantId, x.StepOrder });
        // Seed identity is unique per tenant (filtered to seeded rows).
        builder.HasIndex(x => new { x.TenantId, x.SystemKey })
            .IsUnique()
            .HasFilter("\"SystemKey\" IS NOT NULL");
    }
}

public class WorkflowNotificationDispatchConfiguration : IEntityTypeConfiguration<WorkflowNotificationDispatch>
{
    public void Configure(EntityTypeBuilder<WorkflowNotificationDispatch> builder)
    {
        builder.ToTable("workflow_notification_dispatches");
        builder.HasKey(x => x.Id);
        // The idempotency key.
        builder.HasIndex(x => new { x.RequestInstanceId, x.Event, x.StepOrder, x.RuleId, x.UserId })
            .IsUnique();
    }
}
