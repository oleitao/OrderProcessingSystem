using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderProcessing.Application.Exceptions;
using OrderProcessing.Application.Interfaces;
using OrderProcessing.Domain.Entities;
using OrderProcessing.Infrastructure.Database;

namespace OrderProcessing.Infrastructure.Repositories;

public sealed class OrderRepository(OrderDbContext dbContext) : IOrderRepository
{
    // Name EF Core generated for the unique index (see AddIdempotencyRecords migration).
    private const string IdempotencyKeyConstraintName = "IX_idempotency_records_Key";

    public async Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        await dbContext.Orders.AddAsync(order, cancellationToken);
    }

    public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Order>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Orders
            .Include(order => order.Items)
            .OrderByDescending(order => order.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (TryGetConflictingIdempotencyKey(ex, out var idempotencyKey))
        {
            // The prior SELECT-then-INSERT lookup is only a fast path; two requests can both pass
            // it concurrently. This unique-constraint violation is the actual guarantee that only
            // one Order ever gets created per Idempotency-Key.
            throw new IdempotencyKeyConflictException(idempotencyKey, ex);
        }
    }

    private static bool TryGetConflictingIdempotencyKey(DbUpdateException exception, out string idempotencyKey)
    {
        idempotencyKey = string.Empty;

        if (exception.InnerException is not PostgresException
            {
                SqlState: PostgresErrorCodes.UniqueViolation,
                ConstraintName: IdempotencyKeyConstraintName
            })
        {
            return false;
        }

        var record = exception.Entries
            .Select(entry => entry.Entity)
            .OfType<IdempotencyRecord>()
            .FirstOrDefault();

        if (record is null)
            return false;

        idempotencyKey = record.Key;
        return true;
    }
}
