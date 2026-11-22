using Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace UI {
    /// <summary>
    /// 主界面的倾斜来源 / Where Home's parallax gets its motion on a phone: how the phone is tilted.
    /// HomeUI.lua reads <see cref="X"/> and <see cref="Y"/> every frame and moves its two planes by
    /// them, in place of the pointer it follows in the Editor and on a PC, where there is no sensor.
    ///
    /// 重力传感器 / Read from the gravity sensor, or a low-passed accelerometer on a phone without one,
    /// through the Input System. C# reads it rather than the Lua: a sensor API reached only by reflection
    /// is stripped from an IL2CPP build, and the Lua has no OnDisable to switch the sensor off.
    ///
    /// 只在主界面在最上面时开 / On only while Home is the screen on top, the switch in the settings is on
    /// and the app has focus - off under any screen opened over Home, during a song (the UI camera, and
    /// with it Home, is switched off) and in the background, so it costs no battery elsewhere.
    ///
    /// 以拿着的姿势为中心 / Measured from the pose the phone rests in: taken when the sensor starts, then
    /// drifting toward the pose held now over <see cref="recentreSeconds"/>, so a tilt held for a while
    /// settles back to the middle, whether the player sits, lies down or puts the phone on a table.
    ///
    /// Added to Home by HomeUI.lua through <see cref="On"/>; the Lua also sets the tuning below from its
    /// own constants, so every number the effect ships with lives in one file.
    /// </summary>
    public class HomeTilt : MonoBehaviour {
        [Tooltip("Degrees away from the rest pose that move the planes all the way.")]
        public float rangeDegrees = 15f;

        [Tooltip("Degrees around the rest pose that move nothing, so a steady hand does not shimmer.")]
        public float deadZoneDegrees = 0.75f;

        [Tooltip("Seconds for the rest pose to follow about two thirds of the way to a pose held.")]
        public float recentreSeconds = 5f;

        [Tooltip("Seconds spent finding the rest pose when the sensor starts. The planes stay still meanwhile.")]
        public float settleSeconds = 0.25f;

        // 加速度计要先滤掉手抖 / The accelerometer carries every jolt of the hand as well as gravity;
        // a 0.1 s low-pass leaves gravity. The gravity sensor is already filtered.
        private const float AccelerometerSmoothing = 0.1f;

        private UIBase screen;
        private Sensor sensor;
        private bool switchedOn;
        private Vector3 smoothed;
        private bool smoothing;
        private Vector2 rest;
        private float settling;
        private ScreenOrientation orientation;

        /// <summary>
        /// The Editor and a PC: there is no sensor to read, so the Lua keeps following the pointer, which
        /// is also what the Parallax Tuner reproduces.
        /// </summary>
        public bool FollowsPointer => !Application.isMobilePlatform;

        /// <summary>True while a reading is coming in. False with the switch off, under another screen,
        /// while the rest pose is being found, or on a phone with no sensor - the planes then stay still.</summary>
        public bool Available { get; private set; }

        /// <summary>-1..1: the right edge lowered is positive.</summary>
        public float X { get; private set; }

        /// <summary>-1..1: the top edge tipped away is positive.</summary>
        public float Y { get; private set; }

        /// <summary>
        /// 找到或加上 / The tilt source on <paramref name="host"/>, added if it has none. In C# because a
        /// component missing in the Editor comes back from GetComponent as a stand-in object, not nil, so
        /// a check written in Lua would take it for a real one.
        /// </summary>
        public static HomeTilt On(GameObject host) {
            HomeTilt tilt = host.GetComponent<HomeTilt>();
            return tilt != null ? tilt : host.AddComponent<HomeTilt>();
        }

        /// <summary>Called by HomeUI.lua with its constants.</summary>
        public void Tune(float range, float deadZone, float recentre) {
            rangeDegrees = range;
            deadZoneDegrees = deadZone;
            recentreSeconds = recentre;
        }

        private void Awake() {
            screen = GetComponent<UIBase>();
        }

        private void Update() {
            if (!Wanted()) {
                Stop();
                return;
            }

            // 横屏翻转 / Turning the phone round to the other landscape passes through poses that are
            // nothing like the rest pose; start again from the new one.
            if (sensor != null && Screen.orientation != orientation) Stop();

            if (sensor == null && !Begin()) return;

            if (!TryRead(out Vector3 gravity)) return;

            Vector2 angles = TiltMath.Angles(gravity);
            float dt = Time.unscaledDeltaTime;

            if (settling > 0f) {
                // 先找静止姿势 / The first reading becomes the rest pose and then follows the phone fast,
                // so a hand still settling as Home appears does not count as a tilt.
                rest = settling >= settleSeconds ? angles : TiltMath.Recentre(rest, angles, dt, settleSeconds * 0.5f);
                settling -= dt;
                return;
            }

            rest = TiltMath.Recentre(rest, angles, dt, recentreSeconds);
            Vector2 offset = TiltMath.Offset(rest, angles);
            X = TiltMath.Normalise(offset.x, deadZoneDegrees, rangeDegrees);
            Y = TiltMath.Normalise(offset.y, deadZoneDegrees, rangeDegrees);
            Available = true;
        }

        private bool Wanted() {
            if (FollowsPointer || !GameSettings.HOME_TILT_ENABLE || !Application.isFocused) return false;

            UIBase top = UIManager.Inst().Top();
            return top != null && top == screen;
        }

        private bool Begin() {
            sensor = GravitySensor.current;
            if (sensor == null) sensor = Accelerometer.current;
            if (sensor == null) return false;

            // 只关自己开的 / Switched off later only if it was this that switched it on.
            if (!sensor.enabled) {
                InputSystem.EnableDevice(sensor);
                switchedOn = true;
            }

            orientation = Screen.orientation;
            smoothing = false;
            settling = settleSeconds;
            return true;
        }

        private bool TryRead(out Vector3 gravity) {
            gravity = Vector3.zero;

            if (sensor is GravitySensor gravitySensor) {
                gravity = gravitySensor.gravity.ReadValue();
            } else if (sensor is Accelerometer accelerometer) {
                Vector3 raw = accelerometer.acceleration.ReadValue();
                float k = 1f - Mathf.Exp(-Time.unscaledDeltaTime / AccelerometerSmoothing);
                smoothed = smoothing ? Vector3.Lerp(smoothed, raw, k) : raw;
                smoothing = true;
                gravity = smoothed;
            }

            // 一个重力上下 / About one g, or it is not gravity: a sensor that has just come back from the
            // background reads zero for a moment, and a shake reads far more. Keep the last value.
            float length = gravity.magnitude;
            return length > 0.5f && length < 1.5f;
        }

        private void Stop() {
            if (sensor != null && switchedOn && sensor.added) InputSystem.DisableDevice(sensor);

            sensor = null;
            switchedOn = false;
            Available = false;
            X = 0f;
            Y = 0f;
        }

        private void OnDisable() {
            Stop();
        }

        private void OnApplicationPause(bool paused) {
            if (paused) Stop();
        }
    }
}
