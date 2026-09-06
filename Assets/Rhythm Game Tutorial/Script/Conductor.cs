using UnityEngine;

// Owns song time for everything: spawning, note movement and hit judging all
// read SongTime from here, so they cannot drift relative to one another.
// Anchored to AudioSettings.dspTime rather than AudioSource.time because
// AudioSource.time is quantised to the audio buffer and can repeat or jump
// between frames - too coarse next to a 50ms perfect window.
public class Conductor : MonoBehaviour
{
    public static Conductor instance;

    public AudioSource theMusic;

    // Shifts judging earlier (negative) or later (positive) to compensate for
    // output latency on a given device. Tune by ear.
    public float latencyOffset = 0f;

    // Per-song sync offset, taken from the chart. Device latency is a per-player
    // calibration; this is a per-chart constant. Both shift judging, so both
    // have to appear in every time computation - hence TotalOffset below.
    public float songOffset = 0f;

    // Silence before song time 0, giving the first note runway to travel in.
    public float startDelay = 0.5f;

    private float TotalOffset
    {
        get { return latencyOffset + songOffset; }
    }

    private double songStartDsp;
    private float pausedSongTime;

    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }

    // Seconds since the start of the song. Negative during the lead-in, which
    // lets notes spawn and travel before the first sample plays.
    public float SongTime
    {
        get
        {
            if (!IsPlaying) return 0f;
            if (IsPaused) return pausedSongTime;

            return (float)(AudioSettings.dspTime - songStartDsp) - TotalOffset;
        }
    }

    public bool IsFinished
    {
        get
        {
            if (!IsPlaying || IsPaused) return false;
            if (theMusic == null || theMusic.clip == null) return false;

            return SongTime >= theMusic.clip.length;
        }
    }

    void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (theMusic == null)
        {
            theMusic = GetComponent<AudioSource>();
        }
    }

    // extraLeadIn is how much silent run-up the notes need before song time 0,
    // so the earliest note can start at its spawn marker rather than popping in
    // partway down its path. NoteSpawner.GetRequiredLeadIn() computes it.
    public void StartSong(float extraLeadIn = 0f)
    {
        double delay = Mathf.Max(startDelay, extraLeadIn);
        double startAt = AudioSettings.dspTime + delay;
        songStartDsp = startAt;

        // PlayScheduled starts on an exact dsp boundary; Play() only starts
        // some time during the next audio callback.
        theMusic.PlayScheduled(startAt);

        IsPlaying = true;
        IsPaused = false;
    }

    public void Pause()
    {
        if (!IsPlaying || IsPaused) return;

        pausedSongTime = SongTime;
        theMusic.Pause();
        IsPaused = true;
    }

    public void Resume()
    {
        if (!IsPlaying || !IsPaused) return;

        // Re-anchor so SongTime picks up exactly where it left off.
        songStartDsp = AudioSettings.dspTime - TotalOffset - pausedSongTime;
        theMusic.UnPause();
        IsPaused = false;
    }

    // Jumps the song, and therefore every note, to an absolute time.
    public void Seek(float songTime)
    {
        songTime = Mathf.Max(0f, songTime);

        if (theMusic.clip != null)
        {
            theMusic.time = Mathf.Min(songTime, theMusic.clip.length - 0.01f);
        }

        songStartDsp = AudioSettings.dspTime - TotalOffset - songTime;
        pausedSongTime = songTime;
    }

    public void StopSong()
    {
        theMusic.Stop();
        IsPlaying = false;
        IsPaused = false;
    }
}
