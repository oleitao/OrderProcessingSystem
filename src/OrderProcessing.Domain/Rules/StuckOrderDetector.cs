using OrderProcessing.Domain.Enums;

namespace OrderProcessing.Domain.Rules;

/// <summary>
/// Defines what counts as a "stuck" Order for the Recovery Worker (Phase 14). Kept separate from
/// EF Core so the rule itself is unit-testable without a database.
/// </summary>
public static class StuckOrderDetector
{
    public static bool IsStuck(OrderStatus status, DateTime? updatedAtUtc, DateTime nowUtc, TimeSpan threshold) =>
        status == OrderStatus.Processing
        && updatedAtUtc is not null
        && updatedAtUtc.Value < nowUtc - threshold;
}
