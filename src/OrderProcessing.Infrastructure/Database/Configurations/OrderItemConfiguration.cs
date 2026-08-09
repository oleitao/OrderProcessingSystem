using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderProcessing.Domain.Entities;

namespace OrderProcessing.Infrastructure.Database.Configurations;

public sealed class OrderItemConfiguration : IEntityTypeConfiguration<OrderItem>
{
    public void Configure(EntityTypeBuilder<OrderItem> builder)
    {
        builder.ToTable("order_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.ProductName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(i => i.Quantity)
            .IsRequired();

        builder.Property(i => i.UnitPrice)
            .HasPrecision(18, 2);

        // LineTotal is Quantity * UnitPrice computed in memory, never persisted.
        builder.Ignore(i => i.LineTotal);

        builder.HasIndex(i => i.OrderId);
    }
}
