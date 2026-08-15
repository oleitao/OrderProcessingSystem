using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderProcessing.Infrastructure.Outbox;

namespace OrderProcessing.Infrastructure.Database.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);

        builder.Property(message => message.Type)
            .HasMaxLength(100)
            .IsRequired();

        // Payload is always a JSON-serialized event; jsonb lets Postgres validate/store it
        // properly instead of treating it as opaque text.
        builder.Property(message => message.Payload)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(message => message.OccurredAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(message => message.ProcessedAtUtc)
            .HasColumnType("timestamp with time zone");

        builder.Property(message => message.RetryCount)
            .IsRequired();

        builder.Property(message => message.LastError)
            .HasMaxLength(2000);

        // Speeds up the Outbox Worker's polling query (Phase 8): WHERE ProcessedAtUtc IS NULL.
        builder.HasIndex(message => message.ProcessedAtUtc)
            .HasFilter("\"ProcessedAtUtc\" IS NULL");
    }
}
