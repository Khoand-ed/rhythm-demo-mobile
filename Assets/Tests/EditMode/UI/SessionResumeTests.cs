using NUnit.Framework;
using UI.Sub;

// 上次的会话 / What the login screen does with the session a previous run left on the device: reopen it
// only while the game is open to this build, then go where a password sign-in goes; and when that
// fails, give up only a session the device itself has let go of. What PromuseApi does to the session
// when the server refuses or cannot be reached is pinned in TokenRefreshTests.
public class SessionResumeTests
{
    [Test]
    public void AStoredSession_IsReopened_WhileTheGameIsOpenToThisBuild()
    {
        Assert.IsTrue(SessionResume.ShouldTry(hasStoredSession: true, closedToThisBuild: false));
    }

    [Test]
    public void NothingIsReopened_WithoutASession_OrOverAMaintenanceOrUpdateWarning()
    {
        Assert.IsFalse(SessionResume.ShouldTry(hasStoredSession: false, closedToThisBuild: false),
            "no session: the login screen as it always was");
        Assert.IsFalse(SessionResume.ShouldTry(hasStoredSession: true, closedToThisBuild: true),
            "the warning is up and the server would refuse: the session waits for the game to open");
    }

    [Test]
    public void AReopenedSession_GoesWhereAPasswordSignInGoes()
    {
        Assert.AreEqual(SessionResume.Next.EnterGame,
            SessionResume.After(resumed: true, hasName: true, stillHeld: true));
        Assert.AreEqual(SessionResume.Next.ChooseName,
            SessionResume.After(resumed: true, hasName: false, stillHeld: true),
            "an account that left before choosing its name chooses it before Home, as after a password");
    }

    [Test]
    public void ASessionTheDeviceLetGoOf_SendsThePlayerToSignInAgain()
    {
        // Expired or revoked: the server refused the refresh token and PromuseApi dropped it.
        Assert.AreEqual(SessionResume.Next.SignInAgain,
            SessionResume.After(resumed: false, hasName: false, stillHeld: false));
    }

    [Test]
    public void ASessionStillHeld_IsOfferedAgain_NotThrownAway()
    {
        // No network, the server down or in maintenance: nothing said the session is bad, and signing in
        // by password instead would only start a second one.
        Assert.AreEqual(SessionResume.Next.TryAgain,
            SessionResume.After(resumed: false, hasName: false, stillHeld: true));
    }
}
