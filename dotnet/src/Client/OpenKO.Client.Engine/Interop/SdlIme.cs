using System.Runtime.InteropServices;

namespace OpenKO.Client.Engine.Interop;

/// <summary>
/// Optional binding to the SDL2 library MonoGame (DesktopGL) already loaded:
/// positions the OS IME composition/candidate window at the focused edit box
/// (<c>SDL_SetTextInputRect</c>) and gates text input on edit focus
/// (<c>SDL_StartTextInput</c>/<c>SDL_StopTextInput</c>). Degrades to a no-op when
/// SDL can't be resolved (headless/tests), so callers never need to guard.
/// </summary>
public static class SdlIme
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SdlRect
    {
        public int X, Y, W, H;
    }

    private delegate void StartStopFn();

    private unsafe delegate void SetTextInputRectFn(SdlRect* rect);

    private static readonly StartStopFn? Start;
    private static readonly StartStopFn? Stop;
    private static readonly SetTextInputRectFn? SetRect;

    public static bool Available => Start != null;

    static SdlIme()
    {
        // MonoGame's SDL is already loaded into the process; try the usual soname
        // spellings per platform so NativeLibrary binds to the same instance.
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
                Start = Marshal.GetDelegateForFunctionPointer<StartStopFn>(
                    NativeLibrary.GetExport(lib, "SDL_StartTextInput"));
                Stop = Marshal.GetDelegateForFunctionPointer<StartStopFn>(
                    NativeLibrary.GetExport(lib, "SDL_StopTextInput"));
                unsafe
                {
                    SetRect = Marshal.GetDelegateForFunctionPointer<SetTextInputRectFn>(
                        NativeLibrary.GetExport(lib, "SDL_SetTextInputRect"));
                }

                return;
            }
            catch (EntryPointNotFoundException)
            {
                Start = null;
                Stop = null;
                SetRect = null;
            }
        }
    }

    public static void StartTextInput() => Start?.Invoke();

    public static void StopTextInput() => Stop?.Invoke();

    /// <summary>Anchor the OS composition/candidate window to the given screen rect.</summary>
    public static unsafe void SetTextInputRect(int x, int y, int width, int height)
    {
        if (SetRect == null)
            return;
        var rect = new SdlRect { X = x, Y = y, W = width, H = height };
        SetRect(&rect);
    }
}
