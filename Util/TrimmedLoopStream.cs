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

        // Guard against NaN/infinite selections coming from the UI: they would produce a
        // nonsensical byte range and could make Read loop or throw.
        if (double.IsNaN(startSeconds) || double.IsInfinity(startSeconds) || startSeconds < 0)
        {
            startSeconds = 0;
        }

        if (double.IsNaN(endSeconds) || double.IsInfinity(endSeconds) || endSeconds < 0)
        {
            endSeconds = 0;
        }

        var format = source.WaveFormat;
        long blockAlign = Math.Max(1, format.BlockAlign);
        long length = source.Length;
        long bytesPerSecond = Math.Max(1, format.AverageBytesPerSecond);

        if (length < blockAlign)
        {
            // Nothing playable: a zero length window makes Read return 0 immediately.
            _segmentStart = 0;
            _segmentLength = 0;
            _loop = loop;
            return;
        }

        long start = (long)Math.Round(startSeconds * bytesPerSecond);
        start -= start % blockAlign;
        start = Math.Clamp(start, 0, length - blockAlign);

        // endSeconds == 0 (or any value not after start) means "until the end of the file",
        // which is how the trim model stores an unset end.
        long end;
        if (endSeconds > startSeconds + 0.001)
        {
            end = (long)Math.Round(endSeconds * bytesPerSecond);
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

        if (_segmentLength <= 0)
        {
            _segmentLength = 0;
            return;
        }

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
        if (count <= 0 || _segmentLength <= 0)
        {
            return 0;
        }

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
