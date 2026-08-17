using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Infrastructure.Database.Configurations;

public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        builder.ToTable("processed_messages");

        // MessageId (the RabbitMQ event's EventId) is the primary key itself, not a separate
        // surrogate Id — its uniqueness is exactly what makes this table work as a dedup guard.
        builder.HasKey(message => message.MessageId);

        builder.Property(message => message.ProcessedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();
    }
}
