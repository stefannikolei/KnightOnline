using System;

namespace KnightOnline.Shared;

/// <summary>
/// C# port of the C++ CCircularBuffer class
/// </summary>
public class CircularBuffer
{
    private byte[] _buffer;
    private int _bufferSize;
    private int _headPos;
    private int _tailPos;

    public CircularBuffer(int size)
    {
        if (size <= 0)
            throw new ArgumentException("Buffer size must be positive", nameof(size));
        
        _bufferSize = size;
        _buffer = new byte[_bufferSize];
        _headPos = 0;
        _tailPos = 0;
    }

    public int BufferSize => _bufferSize;
    public int HeadPos => _headPos;
    public int TailPos => _tailPos;

    public void SetEmpty()
    {
        _headPos = _tailPos = 0;
    }

    public int GetValidCount()
    {
        int count = _tailPos - _headPos;
        if (count < 0)
            count = _bufferSize + count;
        return count;
    }

    public byte GetHeadData()
    {
        return _buffer[_headPos];
    }

    public void PutData(byte data)
    {
        int len = 1;
        while (IsOverFlowCondition(len))
            BufferResize();

        _buffer[_tailPos++] = data;
        if (_tailPos == _bufferSize)
            _tailPos = 0;
    }

    public void PutData(byte[] data, int length)
    {
        if (length <= 0)
        {
            Console.WriteLine("CircularBuffer.PutData: length is <= 0");
            return;
        }

        while (IsOverFlowCondition(length))
            BufferResize();

        if (IsIndexOverFlow(length))
        {
            int firstCopyLen = _bufferSize - _tailPos;
            int secondCopyLen = length - firstCopyLen;
            
            Array.Copy(data, 0, _buffer, _tailPos, firstCopyLen);

            if (secondCopyLen > 0)
            {
                Array.Copy(data, firstCopyLen, _buffer, 0, secondCopyLen);
                _tailPos = secondCopyLen;
            }
            else
            {
                _tailPos = 0;
            }
        }
        else
        {
            Array.Copy(data, 0, _buffer, _tailPos, length);
            _tailPos += length;
        }
    }

    public void PutData(byte[] data)
    {
        PutData(data, data.Length);
    }

    public int GetOutData(byte[] data)
    {
        int len = GetValidCount();
        int fc = _bufferSize - _headPos;
        
        if (len > fc)
        {
            int sc = len - fc;
            Array.Copy(_buffer, _headPos, data, 0, fc);
            Array.Copy(_buffer, 0, data, fc, sc);
            _headPos = sc;
        }
        else
        {
            Array.Copy(_buffer, _headPos, data, 0, len);
            _headPos += len;
            if (_headPos == _bufferSize)
                _headPos = 0;
        }
        
        return len;
    }

    public void GetData(byte[] data, int length)
    {
        if (length <= 0 || length > GetValidCount())
            throw new ArgumentException("Invalid length for GetData");

        if (length < _bufferSize - _headPos)
        {
            Array.Copy(_buffer, _headPos, data, 0, length);
        }
        else
        {
            int fc = _bufferSize - _headPos;
            int sc = length - fc;
            Array.Copy(_buffer, _headPos, data, 0, fc);
            if (sc > 0)
                Array.Copy(_buffer, 0, data, fc, sc);
        }
    }

    public bool HeadIncrease(int increment = 1)
    {
        if (increment > GetValidCount())
            throw new ArgumentException("Increment exceeds valid count");
        
        _headPos += increment;
        _headPos %= _bufferSize;
        return _headPos != _tailPos;
    }

    private bool IsOverFlowCondition(int len)
    {
        return len >= _bufferSize - GetValidCount();
    }

    private bool IsIndexOverFlow(int len)
    {
        return len + _tailPos >= _bufferSize;
    }

    private void BufferResize()
    {
        int prevBufSize = _bufferSize;
        _bufferSize <<= 1; // Double the size
        byte[] newBuffer = new byte[_bufferSize];
        
        Array.Copy(_buffer, newBuffer, prevBufSize);

        if (_tailPos < _headPos)
        {
            Array.Copy(_buffer, 0, newBuffer, prevBufSize, _tailPos);
            _tailPos += prevBufSize;
        }

        _buffer = newBuffer;
    }
}