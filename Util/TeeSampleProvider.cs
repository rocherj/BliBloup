using System;
using NAudio.Wave;

/// <summary>
/// Pass-through sample provider that also copies its output into a monitor buffer. The virtual
/// microphone mixer reads from this provider; the monitoring output reads the copy independently,
/// without consuming the original microphone stream.
/// </summary>
public sealed class TeeSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private byte[] _scratch = Array.Empty<byte>();

    public TeeSampleProvider(ISampleProvider source)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        WaveFormat = source.WaveFormat;
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>Optional buffer consumed by the local monitoring output.</summary>
    public BufferedWaveProvider? Sink { get; set; }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);

        if (read <= 0 || Sink is null)
        {
            return read;
        }

        int byteCount = read * sizeof(float);

        if (_scratch.Length < byteCount)
        {
            _scratch = new byte[byteCount];
        }

        Buffer.BlockCopy(buffer, offset * sizeof(float), _scratch, 0, byteCount);

        try
        {
            Sink.AddSamples(_scratch, 0, byteCount);
        }
        catch
        {
            // Monitoring must never interrupt the virtual microphone path.
        }

        return read;
    }
}
