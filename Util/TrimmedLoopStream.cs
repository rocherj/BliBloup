using System;
using NAudio.Wave;

/// <summary>
/// A read-only window over part of a <see cref="WaveStream"/>, optionally looping that region.
///
/// This is how the non destructive trim is applied at playback time: the original file is
/// never modified, the stream just starts at the trim point and stops (or wraps) at the end
/// point. Byte offsets are aligned on the source block size so MP3/WAV decoding stays valid.
/// </summary>
public class TrimmedLoopStream : WaveStream
{
    private readonly WaveStream _source;
    private readonly long _segmentStart;
    private readonly long _segmentLength;
    private long _position;
    private bool _loop;

    public TrimmedLoopStream(WaveStream source, double startSeconds, double endSeconds, bool loop)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));

        var format = source.WaveFormat;
        long blockAlign = Math.Max(1, format.BlockAlign);
        long length = source.Length;

        if (length < blockAlign)
        {
            _segmentStart = 0;
            _segmentLength = 0;
            _loop = loop;
            return;
        }

        long start = (long)Math.Round(Math.Max(0, startSeconds) * format.AverageBytesPerSecond);
        start -= start % blockAlign;
        start = Math.Clamp(start, 0, length - blockAlign);

        long end;
        if (endSeconds > startSeconds + 0.001)
        {
            end = (long)Math.Round(endSeconds * format.AverageBytesPerSecond);
            end -= end % blockAlign;
        }
        else
        {
            end = length;
        }

        end = Math.Clamp(end, start + blockAlign, length);

        _segmentStart = start;
        _segmentLength = end - start;
        _loop = loop;

        _source.Position = _segmentStart;
    }

    public override WaveFormat WaveFormat => _source.WaveFormat;

    public override long Length => _segmentLength;

    public override long Position
    {
        get => _position;
        set
        {
            _position = Math.Clamp(value, 0, _segmentLength);
            _source.Position = _segmentStart + _position;
        }
    }

    public bool Loop
    {
        get => _loop;
        set => _loop = value;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int totalRead = 0;

        while (totalRead < count)
        {
            long remaining = _segmentLength - _position;
            if (remaining <= 0)
            {
                if (!_loop)
                {
                    break;
                }

                _position = 0;
                _source.Position = _segmentStart;
                continue;
            }

            int toRead = (int)Math.Min(count - totalRead, remaining);
            int read = _source.Read(buffer, offset + totalRead, toRead);

            if (read <= 0)
            {
                if (!_loop)
                {
                    break;
                }

                _position = 0;
                _source.Position = _segmentStart;
                continue;
            }

            _position += read;
            totalRead += read;
        }

        return totalRead;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _source.Dispose();
        }

        base.Dispose(disposing);
    }
}
