using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Infrastructure.Database.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("orders");

        builder.HasKey(o => o.Id);

        builder.Property(o => o.CustomerName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(o => o.CustomerEmail)
            .HasMaxLength(320)
            .IsRequired();

        // Stored as text (not int) so the raw row is readable during manual DB inspection
        // and adding/reordering statuses never shifts existing persisted values.
        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.TotalAmount)
            .HasPrecision(18, 2);

        // timestamptz requires DateTime.Kind == Utc, which every Domain assignment already uses.
        builder.Property(o => o.CreatedAtUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired();

        builder.Property(o => o.UpdatedAtUtc)
            .HasColumnType("timestamp with time zone");

        builder.HasMany(o => o.Items)
            .WithOne()
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Items has no public setter (only Order.Create can populate it), so EF must
        // write through the private `_items` backing field instead of the property.
        builder.Navigation(o => o.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
