using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Context;

/// <summary>
/// Notices the Windows session ending (job 334): a background thread owns a hidden top-level window of its own class
/// (not a message-only window, which gets no broadcasts) and runs a <c>GetMessage</c> loop. <c>WM_QUERYENDSESSION</c>
/// always answers TRUE and records nothing, so it never blocks or vetoes the end. <c>WM_ENDSESSION</c> maps its
/// parameters through <see cref="ExitReasons.FromEndSession"/> and records a reason through
/// <see cref="CrashLog.RecordExit"/> before it returns. It never throws; when the window can't be made the app runs on
/// without it. No test loads this class: it talks to user32.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SessionEndWatcher
{
    private const string ClassName = "ContextSessionEndWatcher";
    private const uint WmQueryEndSession = 0x0011;
    private const uint WmEndSession = 0x0016;
    private const uint WsExToolWindow = 0x00000080;
    private const uint WsOverlapped = 0x00000000;

    private readonly CrashLog crashLog;

    // Kept in a field so the delegate is not collected while the window class uses it.
    private readonly WndProc procedure;

    public SessionEndWatcher(CrashLog crashLog)
    {
        this.crashLog = crashLog;
        procedure = Procedure;
    }

    private delegate IntPtr WndProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    /// <summary>Starts the window thread; false when the thread can't be started. Never throws.</summary>
    public bool Start()
    {
        try
        {
            Thread thread = new Thread(Run) { IsBackground = true, Name = "Context session end" };
            thread.Start();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private void Run()
    {
        try
        {
            IntPtr instance = GetModuleHandle(null);
            WndClassEx windowClass = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                Procedure = Marshal.GetFunctionPointerForDelegate(procedure),
                Instance = instance,
                ClassName = ClassName,
            };
            if (RegisterClassEx(ref windowClass) == 0)
            {
                return;
            }

            IntPtr window = CreateWindowEx(
                WsExToolWindow, ClassName, ClassName, WsOverlapped, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (window == IntPtr.Zero)
            {
                return;
            }

            while (GetMessage(out Msg message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        catch (Exception)
        {
            // The app runs on without the watcher.
        }
    }

    private IntPtr Procedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (message == WmQueryEndSession)
            {
                return new IntPtr(1);
            }

            if (message == WmEndSession)
            {
                if (ExitReasons.FromEndSession(wParam != IntPtr.Zero, lParam.ToInt64()) is { } reason)
                {
                    crashLog.RecordExit(reason);
                }

                return IntPtr.Zero;
            }

            return DefWindowProc(window, message, wParam, lParam);
        }
        catch (Exception)
        {
            return message == WmQueryEndSession ? new IntPtr(1) : IntPtr.Zero;
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size;
        public uint Style;
        public IntPtr Procedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Window;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public int X;
        public int Y;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "GetModuleHandleW")]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "RegisterClassExW")]
    private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, EntryPoint = "CreateWindowExW")]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "GetMessageW")]
    private static extern int GetMessage(out Msg message, IntPtr window, uint filterMin, uint filterMax);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool TranslateMessage(ref Msg message);

    [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "DispatchMessageW")]
    private static extern IntPtr DispatchMessage(ref Msg message);

    [DllImport("user32.dll", ExactSpelling = true, EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
