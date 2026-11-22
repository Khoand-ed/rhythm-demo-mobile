using NUnit.Framework;
using UI;
using UnityEngine;

// 倾斜的算术 / The arithmetic Home's tilt runs on, apart from the sensor that feeds it: gravity in screen
// space to (lean, recline), an angle away from the rest pose to -1..1, and the rest pose drifting toward
// the pose held. Gravity is written as the Input System gives it: x right, y up the screen, z toward the
// player, upright (0, -1, 0), lying on its back (0, 0, -1).
public class TiltMathTests
{
    private const float Tolerance = 0.01f;

    private static Vector3 Reclined(float degrees)
    {
        float r = degrees * Mathf.Deg2Rad;
        return new Vector3(0f, -Mathf.Cos(r), -Mathf.Sin(r));
    }

    // --- angles ----------------------------------------------------------------------------------

    [Test]
    public void Upright_IsLevelAndNotReclined()
    {
        Vector2 angles = TiltMath.Angles(new Vector3(0f, -1f, 0f));

        Assert.AreEqual(0f, angles.x, Tolerance);
        Assert.AreEqual(0f, angles.y, Tolerance);
    }

    [TestCase(40f)]
    [TestCase(90f)]
    public void Reclining_ReadsAsRecline(float degrees)
    {
        Vector2 angles = TiltMath.Angles(Reclined(degrees));

        Assert.AreEqual(0f, angles.x, Tolerance, "no lean");
        Assert.AreEqual(degrees, angles.y, Tolerance);
    }

    [Test]
    public void TheRightEdgeLowered_LeansPositive()
    {
        float r = 20f * Mathf.Deg2Rad;
        Vector2 angles = TiltMath.Angles(new Vector3(Mathf.Sin(r), -Mathf.Cos(r), 0f));

        Assert.AreEqual(20f, angles.x, Tolerance);
    }

    [Test]
    public void TheLengthOfGravity_DoesNotMatter()
    {
        // A low-passed accelerometer is rarely exactly 1 g.
        Assert.AreEqual(TiltMath.Angles(Reclined(35f)).y, TiltMath.Angles(Reclined(35f) * 1.2f).y, Tolerance);
    }

    // --- -1..1 -----------------------------------------------------------------------------------

    [Test]
    public void InsideTheDeadZone_NothingMoves()
    {
        Assert.AreEqual(0f, TiltMath.Normalise(0.5f, 0.75f, 15f));
        Assert.AreEqual(0f, TiltMath.Normalise(-0.5f, 0.75f, 15f));
    }

    [Test]
    public void PastTheDeadZone_ItGrowsToOneAtTheRange_AndStopsThere()
    {
        Assert.AreEqual(0.5f, TiltMath.Normalise(7.875f, 0.75f, 15f), Tolerance, "halfway between dead zone and range");
        Assert.AreEqual(1f, TiltMath.Normalise(15f, 0.75f, 15f), Tolerance);
        Assert.AreEqual(1f, TiltMath.Normalise(40f, 0.75f, 15f), Tolerance, "held at 1 beyond the range");
        Assert.AreEqual(-1f, TiltMath.Normalise(-40f, 0.75f, 15f), Tolerance, "and the same the other way");
    }

    // --- the rest pose -----------------------------------------------------------------------------

    [Test]
    public void AHeldTilt_SettlesBackToTheMiddle()
    {
        Vector2 rest = Vector2.zero;
        Vector2 held = new Vector2(10f, 0f);

        rest = TiltMath.Recentre(rest, held, 5f, 5f);
        Assert.AreEqual(10f * (1f - Mathf.Exp(-1f)), rest.x, Tolerance, "about two thirds after the time constant");

        for (int frame = 0; frame < 60 * 30; frame++) rest = TiltMath.Recentre(rest, held, 1f / 60f, 5f);
        Assert.AreEqual(0f, TiltMath.Normalise(TiltMath.Offset(rest, held).x, 0.75f, 15f), "after 30 s the tilt reads as the middle");
    }

    [Test]
    public void AnglesAcrossHalfATurn_MoveTheShortWayRound()
    {
        Vector2 offset = TiltMath.Offset(new Vector2(0f, 175f), new Vector2(0f, -175f));

        Assert.AreEqual(10f, offset.y, Tolerance, "175 to -175 is 10 degrees on, not 350 back");
    }
}
