using HR.Domain.Engines.Assets;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HR.Infrastructure.Persistence.Configurations.Engines;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("engine_assets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Code).HasMaxLength(100).IsRequired();
        builder.Property(x => x.NameAr).HasMaxLength(200).IsRequired();
        builder.Property(x => x.NameEn).HasMaxLength(200).IsRequired();
        builder.Property(x => x.SerialNumber).HasMaxLength(200);
        builder.HasIndex(x => new { x.TenantId, x.Code }).IsUnique();
        builder.HasMany(x => x.Custodies).WithOne(x => x.Asset)
            .HasForeignKey(x => x.AssetId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class AssetCustodyConfiguration : IEntityTypeConfiguration<AssetCustody>
{
    public void Configure(EntityTypeBuilder<AssetCustody> builder)
    {
        builder.ToTable("engine_asset_custodies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.HasIndex(x => new { x.TenantId, x.EmployeeId });

        // The double-assignment guard, enforced by the database rather than only by the executor's
        // pre-check: a filtered unique index over open custodies. Two concurrent approvals for the
        // same asset both pass an application-level check and then one of them violates this, which
        // fails that completion run instead of silently handing one laptop to two people.
        builder.HasIndex(x => new { x.TenantId, x.AssetId })
            .IsUnique()
            .HasFilter("\"ReturnedAt\" IS NULL AND \"IsDeleted\" = false");
    }
}
