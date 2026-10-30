namespace Promuse.Persistence.Entities;

/// <summary>
/// One completed response, kept so that a retry of the same request replays it
/// instead of doing the work twice.
///
/// 手机会重试 / The case this exists for is ordinary: a phone sends a sign-up,
/// the response is lost on a train, the client retries. Without this the second
/// attempt creates a second account and the player has two.
///
/// 先排队, 唯一键兜底 / Two requests with one key take turns: the idempotency
/// filter holds an advisory lock named by key and endpoint until the answer is
/// stored, so the second finds the first one's record and replays it. The unique
/// key on (Key, Endpoint) is the last word behind that, not the mechanism - with
/// only the index, overlapping retries both ran the handler, a pull was rolled and
/// charged once per copy, and only the second insert was refused.
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
    ///
    /// The filter writes "{account id, or anon} {method} {path}" - or, when that
    /// is longer than the column, "{account id, or anon} sha256:{its digest}".
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
    ///
    /// 过期就不算数 / Past it the record answers nothing: the key is free again,
    /// and the next request under it is a new request, whose answer takes the
    /// old one's place. IdempotencySweep deletes the ones nobody comes back for.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Still the key's answer at <paramref name="now"/>. Never replay a record
    /// that is not: it is a stored response, player state included, and after
    /// its 24 hours nobody promised it to anyone.
    /// </summary>
    public bool IsLive(DateTimeOffset now) => ExpiresAt > now;
}
