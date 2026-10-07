using System;
using System.IO;
using NUnit.Framework;
using Promuse.Contracts;
using Promuse.Contracts.Runs;
using Promuse.Net;

// The two halves of surviving a bad network: when a request may be sent again, and where a
// result waits when it cannot be sent at all.
//
// 规则写成纯函数就是为了这里 / RetryPolicy is pure on purpose - whether a 409 is retried is a rule
// worth pinning, and pinning it should not need a server or a coroutine.
public class RetryAndQueueTests
{
    private static ApiProblem Problem(int status, string code)
    {
        return new ApiProblem("about:blank", "t", status, code, null, null, null, null);
    }

    // --- which failures -------------------------------------------------------

    [Test]
    public void Network_ServerFaults_AndSlowDown_AreWorthRetrying()
    {
        Assert.IsTrue(RetryPolicy.IsTransient(Problem(0, TransportErrorCodes.Offline)));
        Assert.IsTrue(RetryPolicy.IsTransient(Problem(0, TransportErrorCodes.Timeout)));
        Assert.IsTrue(RetryPolicy.IsTransient(Problem(500, ErrorCodes.InternalError)));
        Assert.IsTrue(RetryPolicy.IsTransient(Problem(503, TransportErrorCodes.Malformed)), "a proxy's HTML 503 too");
        Assert.IsTrue(RetryPolicy.IsTransient(Problem(429, ErrorCodes.RateLimited)));
    }

    [Test]
    public void AnswersThatWouldNotChange_AreNotRetried()
    {
        Assert.IsFalse(RetryPolicy.IsTransient(Problem(409, ErrorCodes.InsufficientStamina)), "asking again does not add stamina");
        Assert.IsFalse(RetryPolicy.IsTransient(Problem(422, ErrorCodes.ValidationFailed)));
        Assert.IsFalse(RetryPolicy.IsTransient(Problem(401, ErrorCodes.Unauthorized)), "401 has its own refresh path");
        Assert.IsFalse(RetryPolicy.IsTransient(Problem(412, ErrorCodes.StateConflict)));
        Assert.IsFalse(RetryPolicy.IsTransient(null));
    }

    // --- which requests -------------------------------------------------------

    [Test]
    public void OnlyRequestsThatCannotDoubleAreRepeated()
    {
        Assert.IsTrue(RetryPolicy.IsSafeToRepeat("GET", null), "a read changes nothing");
        Assert.IsTrue(RetryPolicy.IsSafeToRepeat("POST", "key"), "a keyed write is replayed, not redone");
        Assert.IsFalse(RetryPolicy.IsSafeToRepeat("POST", null), "an unkeyed write could happen twice");
        Assert.IsFalse(RetryPolicy.IsSafeToRepeat("PUT", null));
    }

    // --- how long -------------------------------------------------------------

    [Test]
    public void Backoff_Doubles_WithinTwentyPercentJitter()
    {
        Assert.That(RetryPolicy.Delay(0, 0.5, 0.0).TotalSeconds, Is.EqualTo(0.4).Within(1e-9));
        Assert.That(RetryPolicy.Delay(0, 0.5, 0.999).TotalSeconds, Is.LessThan(0.6));
        Assert.That(RetryPolicy.Delay(1, 0.5, 0.5).TotalSeconds, Is.EqualTo(1.0).Within(1e-9));
        Assert.That(RetryPolicy.Delay(2, 0.5, 0.5).TotalSeconds, Is.EqualTo(2.0).Within(1e-9));
        Assert.That(RetryPolicy.Delay(1, 0.5, 7.0).TotalSeconds, Is.EqualTo(1.2).Within(1e-9), "out-of-range jitter is clamped");
    }

    // --- the queue --------------------------------------------------------------

    private string directory;

    [SetUp]
    public void MakeDirectory()
    {
        directory = Path.Combine(Path.GetTempPath(), "promuse-queue-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    [TearDown]
    public void RemoveDirectory()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    private static RunResult Result(int score)
    {
        return new RunResult(score, 10, 10, 0, 0, 0, true, "856c377e36e8ba6f", "H4sIAAAAAAAA");
    }

    [Test]
    public void Queue_KeepsEveryFieldOfAResult_AcrossInstances()
    {
        Guid run = Guid.NewGuid();
        new PendingRunResults(directory).Add(run, true, Result(12345), DateTimeOffset.UtcNow);

        // A fresh instance reads it back - what the next app launch does.
        PendingRunResults.Entry entry = new PendingRunResults(directory).Load()[0];

        Assert.AreEqual(run, entry.RunId);
        Assert.IsTrue(entry.Won);
        Assert.AreEqual(Result(12345), entry.Result, "the record round-trips whole, trace included");
    }

    [Test]
    public void Queue_HoldsOneEntryPerRun_AndRemovesIt()
    {
        PendingRunResults queue = new PendingRunResults(directory);
        Guid run = Guid.NewGuid();

        queue.Add(run, true, Result(1), DateTimeOffset.UtcNow);
        queue.Add(run, true, Result(2), DateTimeOffset.UtcNow);

        Assert.AreEqual(1, queue.Count, "a second result for the same run replaces the first");
        Assert.AreEqual(2, queue.Load()[0].Result.Score);

        queue.Remove(run);

        Assert.AreEqual(0, queue.Count);
        Assert.IsEmpty(Directory.GetFiles(directory), "an empty queue leaves no file behind");
    }

    [Test]
    public void Queue_DropsTheOldest_PastItsCapacity()
    {
        PendingRunResults queue = new PendingRunResults(directory);
        Guid first = Guid.NewGuid();

        queue.Add(first, true, Result(0), DateTimeOffset.UtcNow);
        for (int i = 1; i <= PendingRunResults.Capacity; i++) queue.Add(Guid.NewGuid(), true, Result(i), DateTimeOffset.UtcNow);

        Assert.AreEqual(PendingRunResults.Capacity, queue.Count);
        Assert.IsFalse(queue.Load().Exists(e => e.RunId == first));
    }

    [Test]
    public void Queue_ReadsAsEmpty_WhenTheFileIsGarbage()
    {
        File.WriteAllText(Path.Combine(directory, "pending-run-results.json"), "{ not json");
        UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning,
            new System.Text.RegularExpressions.Regex(@"\[PendingRunResults\] Could not read"));

        // 不能挡住玩家 / Nothing here may stop the game from starting.
        Assert.AreEqual(0, new PendingRunResults(directory).Count);
    }
}
