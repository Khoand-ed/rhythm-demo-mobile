using UnityEngine;

namespace UI {
    /// <summary>
    /// 倾斜的算术 / The arithmetic behind Home's tilt, kept apart from the sensor so it can be tested
    /// without one: from the direction gravity pulls to two angles, and from an angle away from the
    /// pose the phone rests in to the -1..1 the parallax already takes from the pointer.
    ///
    /// 屏幕坐标 / Gravity arrives in screen space, as the Input System delivers it with its default
    /// orientation compensation: x toward the screen's right edge, y up the screen, z out of the screen
    /// toward the player, in g. Held upright it reads (0, -1, 0); lying on its back, (0, 0, -1).
    /// </summary>
    public static class TiltMath {
        /// <summary>
        /// (lean, recline) in degrees. Lean is positive when the screen's right edge is lower than its
        /// left. Recline is 0 upright and 90 lying on its back, and grows as the top edge tips away.
        /// </summary>
        public static Vector2 Angles(Vector3 gravity) {
            float length = gravity.magnitude;
            if (length < 1e-4f) return Vector2.zero;

            Vector3 g = gravity / length;
            float lean = Mathf.Asin(Mathf.Clamp(g.x, -1f, 1f)) * Mathf.Rad2Deg;
            float recline = Mathf.Atan2(-g.z, -g.y) * Mathf.Rad2Deg;
            return new Vector2(lean, recline);
        }

        /// <summary>
        /// 侧躺时不看后仰 / How far the recline angle can be trusted, 0..1. Recline reads gravity's share
        /// in the screen's y-z plane, and that share vanishes as gravity lines up with the screen's x
        /// axis - a player lying on their side holds the landscape phone long edge down. There a 2 degree
        /// wobble swings recline by 10, and crossing the axis flips it by 180. Full weight up to about 60
        /// degrees of lean, none past about 75.
        /// </summary>
        public static float ReclineWeight(Vector3 gravity) {
            float length = gravity.magnitude;
            if (length < 1e-4f) return 0f;

            float share = Mathf.Sqrt(gravity.y * gravity.y + gravity.z * gravity.z) / length;
            return Mathf.Clamp01((share - 0.25f) / 0.25f);
        }

        /// <summary>How far <paramref name="now"/> is from <paramref name="rest"/>, each angle the short way round.</summary>
        public static Vector2 Offset(Vector2 rest, Vector2 now) {
            return new Vector2(Mathf.DeltaAngle(rest.x, now.x), Mathf.DeltaAngle(rest.y, now.y));
        }

        /// <summary>
        /// An angle away from the rest pose as -1..1: nothing inside the dead zone, so a steady hand does
        /// not shimmer, then linear to ±1 at <paramref name="range"/>, and held there beyond it.
        /// </summary>
        public static float Normalise(float degrees, float deadZone, float range) {
            float beyond = Mathf.Abs(degrees) - deadZone;
            if (beyond <= 0f) return 0f;

            float span = Mathf.Max(0.001f, range - deadZone);
            return Mathf.Sign(degrees) * Mathf.Min(1f, beyond / span);
        }

        /// <summary>
        /// 慢慢回中 / Moves the rest pose toward the pose held now, so a tilt held for a while settles
        /// back to the middle. Exponential in time: after <paramref name="seconds"/>, about two thirds of
        /// the way. Angles wrap, so a pose either side of ±180 moves the short way round.
        /// </summary>
        public static Vector2 Recentre(Vector2 rest, Vector2 now, float deltaTime, float seconds) {
            if (seconds <= 0f) return now;

            float k = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / seconds);
            Vector2 offset = Offset(rest, now);
            return new Vector2(rest.x + offset.x * k, rest.y + offset.y * k);
        }
    }
}
