#nullable enable

using System;
using System.IO;
using System.IO.Compression;

namespace Promuse.Net
{
    /// <summary>
    /// The input trace's transport wrapping: gzip, then base64 so it rides inside the JSON body.
    ///
    /// 为什么压缩 / A two-minute song is about 43 KB of frames, almost all of it lane masks that are
    /// zero. Gzip takes most of that away, which matters on a phone on mobile data far more than
    /// the few milliseconds of CPU it costs once, on the results screen.
    ///
    /// The server unwraps it in TraceCodec, with a cap on how far it lets the bytes expand.
    /// </summary>
    public static class TracePacker
    {
        public static string Pack(byte[] raw)
        {
            using (MemoryStream output = new MemoryStream())
            {
                using (GZipStream gzip = new GZipStream(output, CompressionLevel.Optimal, true))
                {
                    gzip.Write(raw, 0, raw.Length);
                }

                return Convert.ToBase64String(output.ToArray());
            }
        }
    }
}
