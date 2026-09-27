namespace Promuse.Persistence.Entities;

/// <summary>
/// One completed response, kept so that a retry of the same request replays it
/// instead of doing the work twice.
///
/// 手机会重试 / The case this exists for is ordinary: a phone sends a sign-up,
/// the response is lost on a train, the client retries. Without this the second
/// attempt creates a second account and the player has two.
///
/// 唯一键才是机制 / The mechanism is the unique key on (Key, Endpoint), not the
/// lookup that precedes it. Two concurrent retries both find nothing, both
/// proceed, and the database refuses the second insert - that refusal is what
/// makes this safe under a race, and it is why this has to be a relational
/// constraint rather than a check in application code.
/// </summary>
public class IdempotencyRecord
{
    public Guid Id { get; set; }

    /// <summary>The client's Idempotency-Key header.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Scoped by endpoint so that the same key used against two different
    /// operations is two different records. A client that reuses a key across
    /// endpoints is confused, but it should not get one endpoint's answer to
    /// another endpoint's question.
    /// </summary>
    public string Endpoint { get; set; } = string.Empty;

    /// <summary>
    /// SHA-256 of the request body. Same key with a different body is not a
    /// retry - it is a bug or an attack - and answering it with the first
    /// response would be a lie, so it is refused with 409 instead.
    /// </summary>
    public string RequestHash { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public string ResponseBody { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// 24 hours, per the contract. Long enough to cover any retry a client will
    /// actually make, short enough that this table does not grow without bound.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
