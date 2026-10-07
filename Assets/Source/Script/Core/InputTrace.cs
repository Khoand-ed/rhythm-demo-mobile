// 每一帧的输入 / What the player's hands did, one record per gameplay frame.
//
// 记原始输入, 不记判定 / Raw input rather than judgements. A list of "Perfect at 12.3s" is the
// client's opinion of what happened, and the server cannot check an opinion. The lanes that
// went down and the lanes being held, with the song time GameManager judged them at, are what
// the judging consumed - so the server can run the same judging again and reach its own
// answer, which is the whole point of keeping it.
//
// 每一帧, 不是每个事件 / Every frame, even ones where nothing was pressed. Misses are found by
// time passing, holds tick and end on time, and fever ends on the first frame past its
// deadline: all of that happens on frames without input, at times a list of presses alone
// cannot reproduce. Recording the frames themselves sidesteps the question of whether the
// judging is frame-rate independent, which today it is not quite.
//
// 格式 / Little-endian, 8-byte header then 6 bytes per frame:
//
//   0  'P' 'T'        magic
//   2  version        1
//   3  laneCount      1..8
//   4  frameCount     int32
//   8  per frame:     float32 songTime (IEEE bits), byte pressed mask, byte held mask
//
// About 43 KB for a two-minute song at 60 fps before compression; the masks are almost all
// zero and gzip takes most of that away in transport.

/// <summary>
/// Records frames into a growing buffer. Allocation-free per frame once the buffer has grown
/// to the song's length, which matters at 60 frames a second on a phone.
/// </summary>
public sealed class InputTraceWriter
{
    public const byte FormatVersion = 1;
    public const int HeaderSize = 8;
    public const int FrameSize = 6;

    /// <summary>
    /// An hour at 60 fps. Past this the writer stops and says so rather than growing without
    /// bound - a song is minutes long, so hitting it means something upstream never stopped.
    /// </summary>
    public const int MaxFrames = 216000;

    private byte[] buffer = new byte[HeaderSize + FrameSize * 8192];
    private int frames;
    private int laneCount;

    public int FrameCount
    {
        get { return frames; }
    }

    public bool Overflowed { get; private set; }

    /// <summary>Starts a fresh trace. Called at the top of every attempt, retries included.</summary>
    public void Begin(int lanes)
    {
        laneCount = lanes;
        frames = 0;
        Overflowed = false;
    }

    public void Record(float songTime, int pressedMask, int heldMask)
    {
        if (frames >= MaxFrames)
        {
            Overflowed = true;
            return;
        }

        int at = HeaderSize + frames * FrameSize;

        if (at + FrameSize > buffer.Length)
        {
            byte[] grown = new byte[buffer.Length * 2];
            System.Array.Copy(buffer, grown, at);
            buffer = grown;
        }

        FloatBits bits = default(FloatBits);
        bits.Float = songTime;

        WriteInt(buffer, at, bits.Int);
        buffer[at + 4] = (byte)pressedMask;
        buffer[at + 5] = (byte)heldMask;

        frames++;
    }

    public byte[] ToArray()
    {
        byte[] result = new byte[HeaderSize + frames * FrameSize];

        System.Array.Copy(buffer, HeaderSize, result, HeaderSize, frames * FrameSize);

        result[0] = (byte)'P';
        result[1] = (byte)'T';
        result[2] = FormatVersion;
        result[3] = (byte)laneCount;
        WriteInt(result, 4, frames);

        return result;
    }

    internal static void WriteInt(byte[] into, int at, int value)
    {
        into[at] = (byte)value;
        into[at + 1] = (byte)(value >> 8);
        into[at + 2] = (byte)(value >> 16);
        into[at + 3] = (byte)(value >> 24);
    }

    internal static int ReadInt(byte[] from, int at)
    {
        return from[at] | (from[at + 1] << 8) | (from[at + 2] << 16) | (from[at + 3] << 24);
    }
}

/// <summary>
/// A decoded trace. Built only through <see cref="TryRead"/>, which refuses anything the writer
/// could not have produced: on the server these bytes arrive from the network.
/// </summary>
public sealed class InputTrace
{
    public int LaneCount { get; private set; }

    public float[] Times { get; private set; }

    public byte[] Pressed { get; private set; }

    public byte[] Held { get; private set; }

    public int FrameCount
    {
        get { return Times.Length; }
    }

    public static bool TryRead(byte[] data, int maxFrames, out InputTrace trace, out string error)
    {
        trace = null;

        if (data == null || data.Length < InputTraceWriter.HeaderSize)
        {
            error = "shorter than the header";
            return false;
        }

        if (data[0] != (byte)'P' || data[1] != (byte)'T')
        {
            error = "not an input trace";
            return false;
        }

        if (data[2] != InputTraceWriter.FormatVersion)
        {
            error = "unsupported version " + data[2];
            return false;
        }

        int lanes = data[3];
        if (lanes < 1 || lanes > 8)
        {
            error = "lane count " + lanes + " is outside 1..8";
            return false;
        }

        int frames = InputTraceWriter.ReadInt(data, 4);
        if (frames < 0 || frames > maxFrames)
        {
            error = "frame count " + frames + " is outside 0.." + maxFrames;
            return false;
        }

        // 长度必须刚好 / Exactly the length the header promises. Trailing bytes are not padding
        // to be ignored; they are something the writer never produces.
        long expected = InputTraceWriter.HeaderSize + (long)frames * InputTraceWriter.FrameSize;
        if (data.Length != expected)
        {
            error = "length " + data.Length + " does not match " + frames + " frames";
            return false;
        }

        float[] times = new float[frames];
        byte[] pressed = new byte[frames];
        byte[] held = new byte[frames];

        // 超出车道的位 / Bits above the lane count would be presses on lanes that do not exist.
        int outside = ~((1 << lanes) - 1) & 0xFF;

        for (int i = 0; i < frames; i++)
        {
            int at = InputTraceWriter.HeaderSize + i * InputTraceWriter.FrameSize;

            FloatBits bits = default(FloatBits);
            bits.Int = InputTraceWriter.ReadInt(data, at);
            float time = bits.Float;

            if (float.IsNaN(time) || float.IsInfinity(time))
            {
                error = "frame " + i + " has a non-finite time";
                return false;
            }

            // 可以相等 / Equal is allowed: the audio clock advances in buffer-sized steps, so two
            // frames close together can read the same song time. Backwards is not.
            if (i > 0 && time < times[i - 1])
            {
                error = "frame " + i + " goes back in time";
                return false;
            }

            if ((data[at + 4] & outside) != 0 || (data[at + 5] & outside) != 0)
            {
                error = "frame " + i + " uses a lane that does not exist";
                return false;
            }

            times[i] = time;
            pressed[i] = data[at + 4];
            held[i] = data[at + 5];
        }

        trace = new InputTrace
        {
            LaneCount = lanes,
            Times = times,
            Pressed = pressed,
            Held = held,
        };

        error = null;
        return true;
    }
}
