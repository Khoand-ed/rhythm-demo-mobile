using System.IO.Compression;

namespace Promuse.Api.Runs;

/// <summary>
/// The trace's transport wrapping: gzip, then base64 so it rides in JSON.
///
/// 解压要设上限 / Decompression is capped. A few kilobytes of gzip can expand to gigabytes, and
/// the bytes come from the network; the cap is the largest trace the writer can ever produce,
/// so anything bigger is not a trace whatever else it is.
/// </summary>
public static class TraceCodec
{
    /// <summary>The largest raw trace InputTraceWriter can produce.</summary>
    public static readonly int MaxRawBytes =
        InputTraceWriter.HeaderSize + InputTraceWriter.MaxFrames * InputTraceWriter.FrameSize;

    /// <summary>
    /// Refused before decoding at all. A real trace compresses to a few tens of kilobytes; this
    /// leaves room for an hour-long one without letting a body of arbitrary size through.
    /// </summary>
    public const int MaxEncodedChars = 2 * 1024 * 1024;

    /// <summary>
    /// The compressed bytes, kept as received, and the raw trace inside them. A wrapper that does
    /// not decode yields an empty raw trace rather than an error: what the device sent is still
    /// evidence, and the review names it TRACE_INVALID like any other unreadable trace.
    /// </summary>
    public static (byte[] Compressed, byte[] Raw) Unpack(string encoded)
    {
        byte[] compressed;

        try
        {
            compressed = Convert.FromBase64String(encoded);
        }
        catch (FormatException)
        {
            return ([], []);
        }

        try
        {
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();

            byte[] buffer = new byte[16 * 1024];
            int read;

            while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + read > MaxRawBytes) return (compressed, []);
                output.Write(buffer, 0, read);
            }

            return (compressed, output.ToArray());
        }
        catch (InvalidDataException)
        {
            return (compressed, []);
        }
    }

    /// <summary>The inverse, for tests and tools. The device does the same in Promuse.Net.</summary>
    public static string Pack(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(raw, 0, raw.Length);
        }

        return Convert.ToBase64String(output.ToArray());
    }
}
