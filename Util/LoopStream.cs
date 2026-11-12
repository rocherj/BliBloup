using NAudio.Wave;

public class LoopStream : WaveStream
{
    private readonly WaveStream _sourceStream;
    private bool _loop = true;
    private long _position;

    public bool Loop
    {
        get => _loop;
        set => _loop = value;
    }

    public LoopStream(WaveStream sourceStream)
    {
        _sourceStream = sourceStream;
    }

    public override WaveFormat WaveFormat => _sourceStream.WaveFormat;
    
    public override long Length => _sourceStream.Length;

    public override long Position
    {
        get => _position;
        set
        {
            _position = 0;
            var length = _sourceStream.Length;

            if (length <= 0)
            {
                return;
            }

            _sourceStream.Position = value < 0 ? 0 : value >= length ? length - 1 : value;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (count <= 0)
        {
            return 0;
        }

        int totalRead = 0;
        int stalledWraps = 0;

        while (totalRead < count)
        {
            int read = _sourceStream.Read(buffer, offset + totalRead, count - totalRead);

            if (read <= 0)
            {
                if (!_loop)
                {
                    break;
                }

                // A stalled or empty source would otherwise loop forever on the audio thread.
                var length = _sourceStream.Length;
                stalledWraps++;

                if (length <= 0 || _sourceStream.Position == 0 || stalledWraps >= 2)
                {
                    break;
                }

                _sourceStream.Position = 0;
                continue;
            }

            stalledWraps = 0;
            totalRead += read;
        }

        _position += totalRead;
        return totalRead;
    }

    protected override void Dispose(bool disposing)
    {
        _sourceStream.Dispose();
        base.Dispose(disposing);
    }
}