using OpenKO.Network.Framing;
using Xunit;

namespace OpenKO.Network.Tests;

public class PacketFramerTests
{
    private static byte[] Frame(params byte[] payload)
    {
        Assert.True(PacketFramer.TryFrame(payload, out byte[] frame));
        return frame;
    }

    [Fact]
    public void FrameLayout()
    {
        byte[] frame = Frame(0x01, 0x02, 0x03);

        Assert.Equal(new byte[] { 0xAA, 0x55, 0x03, 0x00, 0x01, 0x02, 0x03, 0x55, 0xAA }, frame);
    }

    [Fact]
    public void OversizedPayloadIsRejected()
    {
        var payload = new byte[8192 - 6 + 1];
        Assert.False(PacketFramer.TryFrame(payload, out _));

        var maxPayload = new byte[8192 - 6];
        Assert.True(PacketFramer.TryFrame(maxPayload, out byte[] frame));
        Assert.Equal(8192, frame.Length);
    }

    [Fact]
    public void SingleFrameRoundTrip()
    {
        var framer = new PacketFramer();
        framer.Feed(Frame(0x2C, 0xAB, 0xCD));

        Assert.True(framer.TryReadFrame(out byte[] payload));
        Assert.Equal(new byte[] { 0x2C, 0xAB, 0xCD }, payload);
        Assert.False(framer.TryReadFrame(out _));
    }

    [Fact]
    public void MultipleFramesInOneFeed()
    {
        var framer = new PacketFramer();
        var data = new List<byte>();
        data.AddRange(Frame(0x01));
        data.AddRange(Frame(0x02, 0x99));
        data.AddRange(Frame(0x03, 0x01, 0x02, 0x03));
        framer.Feed(data.ToArray());

        Assert.True(framer.TryReadFrame(out byte[] p1));
        Assert.Equal(new byte[] { 0x01 }, p1);
        Assert.True(framer.TryReadFrame(out byte[] p2));
        Assert.Equal(new byte[] { 0x02, 0x99 }, p2);
        Assert.True(framer.TryReadFrame(out byte[] p3));
        Assert.Equal(new byte[] { 0x03, 0x01, 0x02, 0x03 }, p3);
        Assert.False(framer.TryReadFrame(out _));
    }

    [Fact]
    public void FrameSplitAcrossFeeds()
    {
        var framer = new PacketFramer();
        byte[] frame = Frame(0x10, 0x20, 0x30, 0x40);

        // Feed byte by byte: no frame until the last byte arrives.
        for (int i = 0; i < frame.Length - 1; i++)
        {
            framer.Feed(new[] { frame[i] });
            Assert.False(framer.TryReadFrame(out _));
        }

        framer.Feed(new[] { frame[^1] });
        Assert.True(framer.TryReadFrame(out byte[] payload));
        Assert.Equal(new byte[] { 0x10, 0x20, 0x30, 0x40 }, payload);
    }

    [Fact]
    public void GarbageBeforeHeaderIsTolerated()
    {
        var framer = new PacketFramer();
        var data = new List<byte> { 0xDE, 0xAD, 0xBE, 0xEF };
        data.AddRange(Frame(0x42, 0x01));
        // C++ behavior: consuming 6+len from the head leaves the trailing bytes of
        // the *frame* unconsumed when garbage preceded it; feed a second frame and
        // make sure the stream still resyncs onto it eventually.
        data.AddRange(Frame(0x43, 0x02));
        framer.Feed(data.ToArray());

        Assert.True(framer.TryReadFrame(out byte[] first));
        Assert.Equal(new byte[] { 0x42, 0x01 }, first);

        // Drain: the resync logic (3-byte skips) must eventually deliver the second
        // frame or run dry — it must never loop forever or throw.
        var payloads = new List<byte[]>();
        for (int i = 0; i < 32 && framer.TryReadFrame(out byte[] p); i++)
            payloads.Add(p);

        Assert.Contains(payloads, p => p.SequenceEqual(new byte[] { 0x43, 0x02 }));
    }

    [Fact]
    public void BadTrailerAdvancesHeadByThree()
    {
        var framer = new PacketFramer();
        // Valid header + length, corrupted trailer.
        byte[] corrupt = { 0xAA, 0x55, 0x01, 0x00, 0x7F, 0x00, 0x00 };
        framer.Feed(corrupt);

        Assert.False(framer.TryReadFrame(out _)); // consumes 3 bytes (resync)

        // Remaining buffer: 0x00 0x7F 0x00 0x00 — no header, nothing to read.
        Assert.False(framer.TryReadFrame(out _));

        // A subsequent valid frame is still parsed.
        framer.Feed(Frame(0x55));
        Assert.True(framer.TryReadFrame(out byte[] payload));
        Assert.Equal(new byte[] { 0x55 }, payload);
    }

    [Fact]
    public void NegativeLengthConsumesNothing()
    {
        var framer = new PacketFramer();
        // length = 0x8000 = -32768 as int16
        framer.Feed(new byte[] { 0xAA, 0x55, 0x00, 0x80, 0x01, 0x02, 0x55, 0xAA });

        // The C++ waits for more data without consuming (goto cancelRoutine).
        Assert.False(framer.TryReadFrame(out _));
        Assert.False(framer.TryReadFrame(out _));
    }

    [Fact]
    public void LengthLargerThanBufferedDataWaits()
    {
        var framer = new PacketFramer();
        byte[] frame = Frame(0x01, 0x02, 0x03, 0x04, 0x05);

        framer.Feed(frame.AsSpan(0, 6).ToArray());
        Assert.False(framer.TryReadFrame(out _));

        framer.Feed(frame.AsSpan(6).ToArray());
        Assert.True(framer.TryReadFrame(out byte[] payload));
        Assert.Equal(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05 }, payload);
    }

    [Fact]
    public void EmptyPayloadFrameIsSkippedBySessions()
    {
        // A zero-length payload is a valid frame at the framer level.
        var framer = new PacketFramer();
        framer.Feed(Frame());

        Assert.True(framer.TryReadFrame(out byte[] payload));
        Assert.Empty(payload);
    }

    [Fact]
    public void FuzzRandomChunksNeverThrowAndValidFramesSurvive()
    {
        var rng = new Random(1298);
        for (int round = 0; round < 50; round++)
        {
            var framer = new PacketFramer();
            var stream = new List<byte>();
            var expected = new List<byte[]>();

            for (int i = 0; i < 20; i++)
            {
                if (rng.Next(3) == 0)
                {
                    // inject garbage
                    var garbage = new byte[rng.Next(1, 10)];
                    rng.NextBytes(garbage);
                    stream.AddRange(garbage);
                }
                else
                {
                    var payload = new byte[rng.Next(1, 50)];
                    rng.NextBytes(payload);
                    payload[0] = 0x31; // plausible opcode
                    expected.Add(payload);
                    Assert.True(PacketFramer.TryFrame(payload, out byte[] frame));
                    stream.AddRange(frame);
                }
            }

            // feed in random chunk sizes
            int pos = 0;
            var received = new List<byte[]>();
            while (pos < stream.Count)
            {
                int chunk = Math.Min(rng.Next(1, 40), stream.Count - pos);
                framer.Feed(stream.GetRange(pos, chunk).ToArray());
                pos += chunk;

                // Drain with a bounded loop: a resync consumes >= 3 bytes, so this
                // terminates; the bound only guards against regressions.
                for (int guard = 0; guard < 1000 && framer.TryReadFrame(out byte[] p); guard++)
                    received.Add(p);
            }

            // Garbage can eat *adjacent* frames (that is faithful to the C++), but
            // every frame parsed must be one of the frames we actually sent.
            foreach (byte[] p in received)
                Assert.Contains(expected, e => e.SequenceEqual(p));

            // Without garbage injection all frames must arrive; verify separately.
        }
    }

    [Fact]
    public void CleanStreamDeliversAllFramesRegardlessOfChunking()
    {
        var rng = new Random(9182);
        for (int round = 0; round < 30; round++)
        {
            var framer = new PacketFramer();
            var stream = new List<byte>();
            var expected = new List<byte[]>();

            for (int i = 0; i < 15; i++)
            {
                var payload = new byte[rng.Next(1, 300)];
                rng.NextBytes(payload);
                expected.Add(payload);
                Assert.True(PacketFramer.TryFrame(payload, out byte[] frame));
                stream.AddRange(frame);
            }

            int pos = 0;
            var received = new List<byte[]>();
            while (pos < stream.Count)
            {
                int chunk = Math.Min(rng.Next(1, 64), stream.Count - pos);
                framer.Feed(stream.GetRange(pos, chunk).ToArray());
                pos += chunk;

                while (framer.TryReadFrame(out byte[] p))
                    received.Add(p);
            }

            Assert.Equal(expected.Count, received.Count);
            for (int i = 0; i < expected.Count; i++)
                Assert.Equal(expected[i], received[i]);
        }
    }
}
