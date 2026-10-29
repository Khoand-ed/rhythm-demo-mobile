using System.ComponentModel.DataAnnotations;

namespace Promuse.Api.Manufacture;

public sealed class ManufactureOptions
{
    public const string SectionName = "Manufacture";

    /// <summary>
    /// 本地加速 / How many times faster than real time the stations run. 60 makes an hour a minute,
    /// so a station that fills in 8 hours fills in 8 minutes - the Development settings use it, for
    /// trying the screens and for a demonstration.
    ///
    /// 只许开发环境 / Anywhere but Development it must be 1, and the server refuses to start
    /// otherwise (<see cref="Allows"/>). A faster staging would be a different game from the one
    /// players get, and a faster production would hand out the economy many times over.
    /// </summary>
    [Range(1, 3600)]
    public int TimeScale { get; set; } = 1;

    /// <summary>Whether a server in this environment may run at <paramref name="timeScale"/>.</summary>
    public static bool Allows(int timeScale, bool isDevelopment) => timeScale == 1 || isDevelopment;
}
