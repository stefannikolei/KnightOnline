namespace OpenKO.Common;

/// <summary>
/// Port of the C++ <c>CCircularBuffer</c> (shared/CircularBuffer.h), used by the socket receive
/// path to reassemble framed packets from the byte stream. Grows by doubling on overflow.
/// </summary>
public sealed class CircularBuffer
{
    private byte[] _buffer;
    private int _head;
    private int _tail;

    public CircularBuffer(int size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        _buffer = new byte[size];
    }

    public int Capacity => _buffer.Length;

    /// <summary>Number of valid (unread) bytes currently buffered.</summary>
    public int ValidCount
    {
        get
        {
            int count = _tail - _head;
            if (count < 0)
                count = _buffer.Length + count;
            return count;
        }
    }

    public void SetEmpty()
    {
        _head = 0;
        _tail = 0;
    }

    private bool IsOverflow(int len) => len >= _buffer.Length - ValidCount;

    private bool IsIndexOverflow(int len) => len + _tail >= _buffer.Length;

    private void Resize()
    {
        int prevSize = _buffer.Length;
        var newBuffer = new byte[prevSize << 1];
        Array.Copy(_buffer, newBuffer, prevSize);

        if (_tail < _head)
        {
            Array.Copy(_buffer, 0, newBuffer, prevSize, _tail);
            _tail += prevSize;
        }

        _buffer = newBuffer;
    }

    /// <summary>Appends data into the ring, growing it if needed.</summary>
    public void PutData(ReadOnlySpan<byte> data)
    {
        if (data.Length <= 0)
            return;

        while (IsOverflow(data.Length))
            Resize();

        int len = data.Length;
        if (IsIndexOverflow(len))
        {
            int firstCopyLen = _buffer.Length - _tail;
            int secondCopyLen = len - firstCopyLen;

            data[..firstCopyLen].CopyTo(_buffer.AsSpan(_tail));

            if (secondCopyLen > 0)
            {
                data.Slice(firstCopyLen, secondCopyLen).CopyTo(_buffer.AsSpan(0));
                _tail = secondCopyLen;
            }
            else
            {
                _tail = 0;
            }
        }
        else
        {
            data.CopyTo(_buffer.AsSpan(_tail));
            _tail += len;
        }
    }

    /// <summary>Peeks <paramref name="len"/> bytes from the head without advancing.</summary>
    public void GetData(Span<byte> dest, int len)
    {
        if (len <= 0 || len > ValidCount)
            throw new ArgumentOutOfRangeException(nameof(len));

        if (len < _buffer.Length - _head)
        {
            _buffer.AsSpan(_head, len).CopyTo(dest);
        }
        else
        {
            int fc = _buffer.Length - _head;
            int sc = len - fc;
            _buffer.AsSpan(_head, fc).CopyTo(dest);
            if (sc > 0)
                _buffer.AsSpan(0, sc).CopyTo(dest[fc..]);
        }
    }

    /// <summary>Advances the head, consuming bytes. Returns false once the buffer is empty.</summary>
    public bool HeadIncrease(int increasement = 1)
    {
        if (increasement > ValidCount)
            throw new ArgumentOutOfRangeException(nameof(increasement));

        _head += increasement;
        _head %= _buffer.Length;
        return _head != _tail;
    }
}
