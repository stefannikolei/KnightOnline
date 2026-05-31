using OpenKO.Client;
using OpenKO.Common;
using OpenKO.Net;

// Entry point for the cross-platform OpenKO client.
//
// Two modes:
//   (default)     open a window with an OpenGL context (requires a display)
//   --selftest    run headless checks of the ported foundation layers (for CI / no-display hosts)
//
// On Linux without a display we automatically fall back to the self-test so the app is still
// runnable in a container.

bool selftest = args.Contains("--selftest");
bool hasDisplay = OperatingSystem.IsWindows()
    || OperatingSystem.IsMacOS()
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
    || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

if (selftest || !hasDisplay)
{
    if (!selftest)
        Console.WriteLine("No display detected; running headless self-test. Pass a display or use --selftest explicitly.");

    return SelfTest.Run();
}

try
{
    using var window = new GameWindow();
    Console.WriteLine($"Starting OpenKO client window ({window.Width}x{window.Height}). Press ESC to quit.");
    window.Run();
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Failed to create window ({ex.GetType().Name}: {ex.Message}).");
    Console.Error.WriteLine("Falling back to headless self-test.");
    return SelfTest.Run();
}

internal static class SelfTest
{
    public static int Run()
    {
        Console.WriteLine("OpenKO cross-platform foundation self-test");
        Console.WriteLine("==========================================");

        bool ok = true;

        // 1) Packet round-trips a typical login payload.
        var login = new Packet(GameOpcode.Login);
        login.Append((byte)1);
        login.AppendString("account");
        login.AppendString("password");
        ok &= Check("Packet opcode", login.Opcode == (byte)GameOpcode.Login);
        ok &= Check("Packet payload begins with opcode + version byte",
            login.Size > 2 && login[0] == 0x01 && login[1] == 0x01);

        // 2) Framing round-trip without encryption.
        uint counter = 0;
        byte[] frame = PacketFraming.BuildFrame(login.Contents, crypto: null, ref counter);
        bool framed = PacketFraming.TryParseFrame(frame, crypto: null, out byte[] payload, out int consumed);
        ok &= Check("Frame parses back", framed && consumed == frame.Length);
        ok &= Check("Frame payload round-trips", payload.AsSpan().SequenceEqual(login.Contents));

        // 3) Framing round-trip with the JvCryption layer enabled.
        var clientCrypto = new JvCryption();
        ulong key = clientCrypto.GenerateKey();
        var serverCrypto = new JvCryption { PublicKey = key };
        serverCrypto.Init();
        counter = 0;
        // (note: send/receive crypto envelopes differ by design; here we only verify the cipher is reversible)
        Span<byte> sample = stackalloc byte[32];
        for (int i = 0; i < sample.Length; i++) sample[i] = (byte)(i * 7 + 3);
        Span<byte> enc = stackalloc byte[32];
        Span<byte> dec = stackalloc byte[32];
        clientCrypto.Encrypt(32, sample, enc);
        serverCrypto.Decrypt(32, enc, dec);
        ok &= Check("JvCryption is reversible with shared key", sample.SequenceEqual(dec));

        // 4) CRC32 known-answer ("123456789" => 0xCBF43926 for the standard final-XOR variant).
        uint crcReg = Crc32.Compute(System.Text.Encoding.ASCII.GetBytes("123456789"));
        uint crcFinal = crcReg ^ 0xFFFFFFFFu;
        ok &= Check($"CRC32 known answer (got 0x{crcFinal:X8})", crcFinal == 0xCBF43926u);

        Console.WriteLine("==========================================");
        Console.WriteLine(ok ? "ALL CHECKS PASSED" : "SOME CHECKS FAILED");
        return ok ? 0 : 1;
    }

    private static bool Check(string name, bool condition)
    {
        Console.WriteLine($"  [{(condition ? "PASS" : "FAIL")}] {name}");
        return condition;
    }
}
