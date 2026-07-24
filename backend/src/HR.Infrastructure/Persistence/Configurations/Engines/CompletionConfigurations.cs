using HR.Domain.Engines.Completion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence.Configurations.Engines;

public class CompletionRunConfiguration : IEntityTypeConfiguration<CompletionRun>
{
    public void Configure(EntityTypeBuilder<CompletionRun> builder)
    {
        builder.ToTable("engine_completion_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FailureReason).HasMaxLength(2000);
        builder.HasIndex(x => x.RequestInstanceId).IsUnique();
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.Status);

        builder.HasMany(x => x.Effects)
            .WithOne(e => e.Run)
            .HasForeignKey(e => e.CompletionRunId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class CompletionEffectConfiguration : IEntityTypeConfiguration<CompletionEffect>
{
    public void Configure(EntityTypeBuilder<CompletionEffect> builder)
    {
        builder.ToTable("engine_completion_effects");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EffectType).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Payload).HasColumnType("jsonb");
        builder.Property(x => x.ResultSummary).HasColumnType("jsonb");
        builder.Property(x => x.ExecutorName).HasMaxLength(200);
        builder.Property(x => x.ExecutorVersion).HasMaxLength(50);
        builder.Property(x => x.TargetEntityType).HasMaxLength(200);
        builder.Property(x => x.FailureReason).HasMaxLength(2000);
        builder.HasIndex(x => x.RequestInstanceId);
        builder.HasIndex(x => x.CompletionRunId);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.Status);

        builder.Property(x => x.IdempotencyKey).HasMaxLength(200);
        builder.Property(x => x.LeasedBy).HasMaxLength(100);
        builder.Property(x => x.MaxAttempts).HasDefaultValue(1);
        builder.HasIndex(x => x.IdempotencyKey).IsUnique();
        // The worker's "due" query filters on these; index keeps polling cheap.
        builder.HasIndex(x => new { x.Status, x.ScheduledFor, x.NextAttemptAt });
    }
}

public class EffectAttemptConfiguration : IEntityTypeConfiguration<EffectAttempt>
{
    public void Configure(EntityTypeBuilder<EffectAttempt> builder)
    {
        builder.ToTable("engine_effect_attempts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FailureReason).HasMaxLength(2000);
        builder.HasIndex(x => x.CompletionEffectId);
        builder.HasIndex(x => x.TenantId);

        builder.HasOne(x => x.Effect)
            .WithMany()
            .HasForeignKey(x => x.CompletionEffectId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
