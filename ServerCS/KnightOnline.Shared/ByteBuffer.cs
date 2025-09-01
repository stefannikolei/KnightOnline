using System;
using System.Collections.Generic;
using System.Text;

namespace KnightOnline.Shared;

/// <summary>
/// C# port of the C++ ByteBuffer class for packet handling
/// </summary>
public class ByteBuffer
{
    private List<byte> _storage;
    private int _rpos;
    private int _wpos;
    private bool _doubleByte;

    public ByteBuffer()
    {
        _storage = new List<byte>();
        _rpos = 0;
        _wpos = 0;
        _doubleByte = false;
    }

    public ByteBuffer(int reserve)
    {
        _storage = new List<byte>(reserve);
        _rpos = 0;
        _wpos = 0;
        _doubleByte = false;
    }

    public ByteBuffer(ByteBuffer other)
    {
        _storage = new List<byte>(other._storage);
        _rpos = other._rpos;
        _wpos = other._wpos;
        _doubleByte = other._doubleByte;
    }

    public int Size => _wpos;
    public int ReadPos => _rpos;
    public int WritePos => _wpos;

    public byte[] Contents => _storage.ToArray();

    public void Clear()
    {
        _storage.Clear();
        _rpos = 0;
        _wpos = 0;
    }

    public void Reserve(int size)
    {
        if (_storage.Capacity < size)
            _storage.Capacity = size;
    }

    public void Resize(int newSize)
    {
        while (_storage.Count < newSize)
            _storage.Add(0);
        _wpos = newSize;
    }

    // Read operations
    public void SetReadPos(int pos) => _rpos = pos;
    public void SetWritePos(int pos) => _wpos = pos;

    public T Read<T>() where T : struct
    {
        var size = System.Runtime.InteropServices.Marshal.SizeOf<T>();
        if (_rpos + size > _wpos)
            throw new InvalidOperationException("Not enough data to read");

        var bytes = new byte[size];
        for (int i = 0; i < size; i++)
            bytes[i] = _storage[_rpos + i];
        
        _rpos += size;
        
        // Convert bytes to the requested type
        return (T)Convert.ChangeType(BitConverter.ToInt32(bytes, 0), typeof(T));
    }

    public byte ReadByte()
    {
        if (_rpos >= _wpos)
            throw new InvalidOperationException("Not enough data to read");
        return _storage[_rpos++];
    }

    public short ReadInt16()
    {
        if (_rpos + 2 > _wpos)
            throw new InvalidOperationException("Not enough data to read");
        var result = BitConverter.ToInt16(_storage.ToArray(), _rpos);
        _rpos += 2;
        return result;
    }

    public int ReadInt32()
    {
        if (_rpos + 4 > _wpos)
            throw new InvalidOperationException("Not enough data to read");
        var result = BitConverter.ToInt32(_storage.ToArray(), _rpos);
        _rpos += 4;
        return result;
    }

    public string ReadString()
    {
        int len = _doubleByte ? ReadInt16() : ReadByte();
        return ReadString(len);
    }

    public string ReadString(int length)
    {
        if (_rpos + length > _wpos)
            throw new InvalidOperationException("Not enough data to read");

        var result = Encoding.UTF8.GetString(_storage.ToArray(), _rpos, length);
        _rpos += length;
        return result.TrimEnd('\0');
    }

    // Write operations
    public void Append(byte value)
    {
        EnsureCapacity(1);
        _storage.Add(value);
        _wpos++;
    }

    public void Append(byte[] data)
    {
        if (data == null || data.Length == 0)
            return;

        EnsureCapacity(data.Length);
        _storage.AddRange(data);
        _wpos += data.Length;
    }

    public void Append<T>(T value) where T : struct
    {
        byte[] bytes;
        switch (value)
        {
            case byte b:
                bytes = new[] { b };
                break;
            case short s:
                bytes = BitConverter.GetBytes(s);
                break;
            case int i:
                bytes = BitConverter.GetBytes(i);
                break;
            case long l:
                bytes = BitConverter.GetBytes(l);
                break;
            default:
                throw new NotSupportedException($"Type {typeof(T)} not supported");
        }
        Append(bytes);
    }

    public void Append(string str)
    {
        if (string.IsNullOrEmpty(str))
            return;

        var bytes = Encoding.UTF8.GetBytes(str);
        if (_doubleByte)
            Append((short)bytes.Length);
        else
            Append((byte)bytes.Length);
        
        Append(bytes);
    }

    public void AppendString(string str, int fixedLength)
    {
        var bytes = new byte[fixedLength];
        if (!string.IsNullOrEmpty(str))
        {
            var strBytes = Encoding.UTF8.GetBytes(str);
            Array.Copy(strBytes, bytes, Math.Min(strBytes.Length, fixedLength - 1));
        }
        Append(bytes);
    }

    private void EnsureCapacity(int additionalBytes)
    {
        var requiredCapacity = _wpos + additionalBytes;
        if (_storage.Capacity < requiredCapacity)
            _storage.Capacity = Math.Max(requiredCapacity, _storage.Capacity * 2);
    }
}