using System.Runtime.InteropServices;
using System.Text;

namespace OpenKO.Client.Engine.Interop;

/// <summary>
/// Observes the OS IME's in-progress composition string via SDL2's event-watch
/// hook (<c>SDL_AddEventWatch</c>), surfacing the <c>SDL_TEXTEDITING</c> events
/// that MonoGame's own event loop discards. A focused edit box can then render
/// the underlined composition preview at the caret, like the IMM32 original.
///
/// <para>SDL2 is the same instance MonoGame (DesktopGL) already loaded. The whole
/// binding is defensive: if SDL can't be resolved or the symbols are missing
/// (headless/tests, or a non-SDL platform), it degrades to an inert no-op —
/// <see cref="Available"/> is false, <see cref="Install"/> does nothing, and the
/// native callback never throws back into SDL. Nothing here is required for text
/// input to work; it only enriches the preview.</para>
/// </summary>
public static class SdlImeComposition
{
    private const uint SdlTextEditing = 0x302; // SDL_TEXTEDITING event type

    // SDL_TextEditingEvent field byte offsets within the SDL_Event union:
    //   Uint32 type; Uint32 timestamp; Uint32 windowID;
    //   char text[32]; Sint32 start; Sint32 length;
    private const int OffsetText = 12;
    private const int TextSize = 32;   // SDL_TEXTEDITINGEVENT_TEXT_SIZE
    private const int OffsetStart = OffsetText + TextSize; // 44

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int SdlEventFilter(IntPtr userdata, IntPtr sdlEvent);

    private delegate void EventWatchFn(IntPtr filter, IntPtr userdata);

    private static readonly EventWatchFn? Add;
    private static readonly EventWatchFn? Del;

    // Rooted so the GC can't collect the delegate the native side holds.
    private static readonly SdlEventFilter Filter = OnSdlEvent;

    private static readonly object Gate = new();
    private static string _text = string.Empty;
    private static int _cursor;
    private static bool _installed;

    /// <summary>Raised on the game thread when a composition event arrives (text, cursor).</summary>
    public static event Action<string, int>? CompositionChanged;

    /// <summary>True when the SDL event-watch symbols were resolved and the hook can be installed.</summary>
    public static bool Available => Add != null;

    /// <summary>The most recent in-progress composition string ("" when idle).</summary>
    public static string CompositionText
    {
        get
        {
            lock (Gate)
                return _text;
        }
    }

    /// <summary>Caret offset within <see cref="CompositionText"/> reported by the IME.</summary>
    public static int CompositionCursor
    {
        get
        {
            lock (Gate)
                return _cursor;
        }
    }

    static SdlImeComposition()
    {
        // Match the soname spellings MonoGame uses so NativeLibrary binds the same
        // SDL2 instance already in the process.
        string[] candidates = OperatingSystem.IsWindows()
            ? ["SDL2.dll", "SDL2"]
            : OperatingSystem.IsMacOS()
                ? ["libSDL2-2.0.0.dylib", "libSDL2.dylib", "SDL2"]
                : ["libSDL2-2.0.so.0", "libSDL2.so", "SDL2"];

        foreach (string name in candidates)
        {
            if (!NativeLibrary.TryLoad(name, out IntPtr lib))
                continue;

            try
            {
                Add = Marshal.GetDelegateForFunctionPointer<EventWatchFn>(
                    NativeLibrary.GetExport(lib, "SDL_AddEventWatch"));
                Del = Marshal.GetDelegateForFunctionPointer<EventWatchFn>(
                    NativeLibrary.GetExport(lib, "SDL_DelEventWatch"));
                return;
            }
            catch (EntryPointNotFoundException)
            {
                Add = null;
                Del = null;
            }
        }
    }

    /// <summary>Register the event watch (idempotent). No-op when SDL is unavailable.</summary>
    public static void Install()
    {
        if (_installed || Add == null)
            return;

        try
        {
            Add(Marshal.GetFunctionPointerForDelegate(Filter), IntPtr.Zero);
            _installed = true;
        }
        catch
        {
            // A non-SDL platform or a mismatched ABI: stay a no-op rather than crash.
        }
    }

    /// <summary>Remove the event watch. Safe to call when it was never installed.</summary>
    public static void Uninstall()
    {
        if (!_installed || Del == null)
            return;

        try
        {
            Del(Marshal.GetFunctionPointerForDelegate(Filter), IntPtr.Zero);
        }
        catch
        {
            // Ignore: tearing down interop must never throw.
        }

        _installed = false;
        lock (Gate)
        {
            _text = string.Empty;
            _cursor = 0;
        }
    }

    // Invoked by SDL from the thread that pumps events (MonoGame's game thread).
    // Must never throw back into native code, and watchers ignore the return value.
    private static int OnSdlEvent(IntPtr userdata, IntPtr sdlEvent)
    {
        try
        {
            if (sdlEvent != IntPtr.Zero && (uint)Marshal.ReadInt32(sdlEvent) == SdlTextEditing)
            {
                string text = ReadUtf8(sdlEvent + OffsetText, TextSize);
                int cursor = Marshal.ReadInt32(sdlEvent, OffsetStart);
                lock (Gate)
                {
                    _text = text;
                    _cursor = cursor;
                }

                CompositionChanged?.Invoke(text, cursor);
            }
        }
        catch
        {
            // Swallow everything: a managed exception unwinding into SDL is fatal.
        }

        return 0;
    }

    private static string ReadUtf8(IntPtr ptr, int maxBytes)
    {
        int len = 0;
        while (len < maxBytes && Marshal.ReadByte(ptr, len) != 0)
            len++;
        if (len == 0)
            return string.Empty;

        byte[] buffer = new byte[len];
        Marshal.Copy(ptr, buffer, 0, len);
        return Encoding.UTF8.GetString(buffer);
    }
}
