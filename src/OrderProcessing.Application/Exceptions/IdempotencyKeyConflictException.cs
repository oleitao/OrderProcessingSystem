namespace OrderProcessing.Application.Exceptions;

/// <summary>
/// Thrown by the repository when the database's unique constraint on IdempotencyRecords.Key
/// rejects an insert — meaning another concurrent request won the race for the same key.
/// This, not the prior lookup, is what actually guarantees only one Order gets created.
/// </summary>
public sealed class IdempotencyKeyConflictException(string idempotencyKey, Exception innerException)
    : Exception($"Idempotency key '{idempotencyKey}' was concurrently used by another request.", innerException)
{
    public string IdempotencyKey { get; } = idempotencyKey;
}
