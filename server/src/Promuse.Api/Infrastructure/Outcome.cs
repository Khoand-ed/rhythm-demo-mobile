using Promuse.Contracts;

namespace Promuse.Api.Infrastructure;

/// <summary>
/// What a service method answers: a value, or the problem to return instead.
///
/// 不用异常做控制流 / Deliberately a return value rather than a thrown exception.
/// "That username is taken" is an ordinary outcome of registering, not a fault,
/// and a codebase that throws for expected answers ends up with catch blocks that
/// also swallow the real faults.
/// </summary>
public readonly record struct Outcome<T>(T? Value, ApiProblem? Problem, bool Created = false)
{
    public static Outcome<T> Ok(T value, bool created = false) => new(value, null, created);

    public static Outcome<T> Fail(ApiProblem problem) => new(default, problem);

    public bool IsSuccess => Problem is null;
}
