using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Infrastructure.Database.Configurations;

public sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_records");

        builder.HasKey(record => record.Id);

        builder.Property(record => record.Key)
            .HasMaxLength(200)
            .IsRequired();

        // The unique constraint, not the prior lookup, is what actually prevents two concurrent
        // requests with the same key from creating two Orders — see IdempotencyKeyConflictException.
        builder.HasIndex(record => record.Key)
            .IsUnique();

        builder.Property(record => record.OrderId)
            .IsRequired();

        builder.Property(record => record.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}
