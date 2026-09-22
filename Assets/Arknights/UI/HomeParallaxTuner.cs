#if UNITY_EDITOR
using UnityEngine;

/// <summary>
/// 在 Play Mode 里实时调 HomeUI 的视差 / Live tuning for HomeUI's parallax, so the numbers
/// can be found by feel instead of by editing Lua and restarting Play Mode each time.
///
/// Attached at runtime by <c>Arknights/Home/Parallax Tuner</c>. Never saved into a prefab
/// or a scene, and the whole file is behind UNITY_EDITOR so it cannot reach a build.
///
/// 它覆盖 Lua, 而不是改 Lua / It overwrites what HomeUI.lua.txt already wrote this frame
/// rather than modifying the Lua. The Lua's update() is driven from UIBase's Update, and
/// LateUpdate runs after every Update, so whatever this sets is what survives to render.
/// That means the Lua stays the single source of truth for shipped values - this only
/// borrows the transforms while you are dialling them in. Press "Copy Lua block" when the
/// motion feels right and paste the result over the constants in HomeUI.lua.txt.
///
/// 数学必须和 Lua 一字不差 / The pointer maths below is a deliberate transcription of the
/// Lua, down to the squared movement threshold and the idle recentre. If it drifts from
/// the Lua the tuned numbers stop transferring, which would make this tool worse than
/// useless - it would be confidently wrong.
/// </summary>
public class HomeParallaxTuner : MonoBehaviour
{
    [Header("远景 / Far plane — translation only, by design")]
    [Range(-80f, 80f)] public float move1X = -28f;
    [Range(-80f, 80f)] public float move1Y = 16f;

    [Header("近景 / Near plane — translation")]
    [Range(-80f, 80f)] public float move2X = 32f;
    [Range(-80f, 80f)] public float move2Y = -26f;

    [Header("倾斜 / Tilt — NOTE the axes are crossed")]
    [Tooltip("Euler X. Driven by VERTICAL pointer movement, so this is the up/down tilt.")]
    [Range(-20f, 20f)] public float move2TiltX = -5f;

    [Tooltip("Euler Y. Driven by HORIZONTAL pointer movement, so this is the left/right tilt.")]
    [Range(-20f, 20f)] public float move2TiltY = 9f;

    [Header("跟随 / Follow")]
    [Range(0.5f, 20f)] public float followSpeed = 6f;
    [Range(0f, 5f)] public float idleSeconds = 1f;

    [Header("开关 / Off hands the transforms straight back to the Lua")]
    public bool active = true;

    // 由菜单项填入 / Filled in by the menu item, read from the prefab asset. The Lua captures
    // these in init() before anything moves, so by the time this component is attached the
    // live transforms are already displaced and cannot be sampled.
    [HideInInspector] public Transform move1;
    [HideInInspector] public Transform move2;
    [HideInInspector] public Vector3 move1Base;
    [HideInInspector] public Vector3 move2Base;

    private const float MovedThreshold = 4f;

    private Vector2 target;
    private Vector2 current;
    private float lastX = -99999f;
    private float lastY = -99999f;
    private float idle;

    private void LateUpdate()
    {
        if (!active || move1 == null || move2 == null) return;

        Step();

        move1.localPosition = new Vector3(
            move1Base.x + current.x * move1X,
            move1Base.y + current.y * move1Y,
            move1Base.z);

        move2.localPosition = new Vector3(
            move2Base.x + current.x * move2X,
            move2Base.y + current.y * move2Y,
            move2Base.z);

        move2.localRotation = Quaternion.Euler(
            current.y * move2TiltX,
            current.x * move2TiltY,
            0f);
    }

    /// <summary>Advances <c>current</c> exactly the way HomeUI.lua.txt's update() does.</summary>
    private void Step()
    {
        bool moved = false;

        if (ScreenHasMouse())
        {
            Vector3 pointer = Input.mousePosition;
            float dx = pointer.x - lastX;
            float dy = pointer.y - lastY;

            if (dx * dx + dy * dy > MovedThreshold)
            {
                lastX = pointer.x;
                lastY = pointer.y;

                // 按屏幕归一化, 不是画布 / Normalised against the screen, not the canvas -
                // the two only agree at the reference resolution.
                target = new Vector2(
                    pointer.x / Screen.width * 2f - 1f,
                    pointer.y / Screen.height * 2f - 1f);

                moved = true;
            }
        }

        if (moved)
        {
            idle = 0f;
        }
        else
        {
            idle += Time.deltaTime;
            if (idle >= idleSeconds) target = Vector2.zero;
        }

        current = Vector2.Lerp(current, target, followSpeed * Time.deltaTime);
    }

    private static bool ScreenHasMouse()
    {
        Vector3 p = Input.mousePosition;
        return p.x <= Screen.width && p.x > 0f && p.y <= Screen.height && p.y > 0f;
    }

    /// <summary>The constant block to paste over the one in HomeUI.lua.txt.</summary>
    public string ToLuaBlock()
    {
        return "local MOVE1_X = " + N(move1X) + "\n" +
               "local MOVE1_Y = " + N(move1Y) + "\n" +
               "local MOVE2_X = " + N(move2X) + "\n" +
               "local MOVE2_Y = " + N(move2Y) + "\n" +
               "\n" +
               "local MOVE2_TILT_X = " + N(move2TiltX) + "\n" +
               "local MOVE2_TILT_Y = " + N(move2TiltY) + "\n" +
               "\n" +
               "local IDLE_SECONDS = " + N(idleSeconds) + "\n" +
               "local FOLLOW_SPEED = " + N(followSpeed);
    }

    // 整数就不带小数点 / Whole numbers print without a decimal part, so a pasted block reads
    // like the hand-written constants it replaces rather than like generated output.
    private static string N(float v)
    {
        return Mathf.Approximately(v, Mathf.Round(v))
            ? Mathf.RoundToInt(v).ToString()
            : v.ToString("0.##");
    }
}
#endif
