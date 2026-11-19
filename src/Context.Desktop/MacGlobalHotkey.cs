using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Context;

/// <summary>
/// The real <see cref="IGlobalHotkey"/> on macOS (job 260): one key and modifiers (see <see cref="GlobalHotkeys"/>)
/// through Carbon's <c>RegisterEventHotKey</c> on the application event target, with one event handler for
/// <c>kEventHotKeyPressed</c>. Job 282: every registered hotkey's handler sees every press, so the handler reads the
/// pressed hotkey's id (<c>GetEventParameter</c>) and raises <see cref="Pressed"/> only for its own id; another id is
/// passed on (<c>eventNotHandledErr</c>) to the next handler. The handler delegate is kept in a field so it is not
/// collected while registered. Register it on the main thread, before the window's run loop starts.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacGlobalHotkey : IGlobalHotkey
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    // 'keyb' and kEventHotKeyPressed.
    private const uint EventClassKeyboard = 0x6B657962;
    private const uint EventHotKeyPressed = 5;

    // kEventParamDirectObject ('----') and typeEventHotKeyID ('hkid').
    private const uint ParamDirectObject = 0x2D2D2D2D;
    private const uint TypeEventHotKeyId = 0x686B6964;

    // eventNotHandledErr: the press is another hotkey's.
    private const int EventNotHandled = -9874;

    // 'Ctxt', the hotkeys' signature.
    private const uint Signature = 0x43747874;

    private readonly EventHandlerProc handler;
    private readonly uint keyCode;
    private readonly uint modifiers;
    private readonly uint id;
    private IntPtr hotKeyRef;
    private IntPtr handlerRef;

    public MacGlobalHotkey(uint keyCode, uint modifiers, uint id)
    {
        this.keyCode = keyCode;
        this.modifiers = modifiers;
        this.id = id;
        handler = OnEvent;
    }

    public event EventHandler? Pressed;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EventHandlerProc(IntPtr callRef, IntPtr eventRef, IntPtr userData);

    public bool Register()
    {
        if (hotKeyRef != IntPtr.Zero)
        {
            return true;
        }

        try
        {
            IntPtr target = GetApplicationEventTarget();
            EventTypeSpec[] types = [new EventTypeSpec { EventClass = EventClassKeyboard, EventKind = EventHotKeyPressed }];
            if (InstallEventHandler(target, handler, 1, types, IntPtr.Zero, out handlerRef) != 0)
            {
                handlerRef = IntPtr.Zero;
                return false;
            }

            EventHotKeyId hotKeyId = new EventHotKeyId { Signature = Signature, Id = id };
            if (RegisterEventHotKey(keyCode, modifiers, hotKeyId, target, 0, out hotKeyRef) != 0)
            {
                hotKeyRef = IntPtr.Zero;
                RemoveEventHandler(handlerRef);
                handlerRef = IntPtr.Zero;
                return false;
            }

            return true;
        }
        catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (hotKeyRef != IntPtr.Zero)
        {
            UnregisterEventHotKey(hotKeyRef);
            hotKeyRef = IntPtr.Zero;
        }

        if (handlerRef != IntPtr.Zero)
        {
            RemoveEventHandler(handlerRef);
            handlerRef = IntPtr.Zero;
        }
    }

    private int OnEvent(IntPtr callRef, IntPtr eventRef, IntPtr userData)
    {
        try
        {
            if (GetEventParameter(
                    eventRef, ParamDirectObject, TypeEventHotKeyId, IntPtr.Zero,
                    (nuint)Marshal.SizeOf<EventHotKeyId>(), IntPtr.Zero, out EventHotKeyId pressed) != 0
                || pressed.Signature != Signature
                || pressed.Id != id)
            {
                return EventNotHandled;
            }

            Pressed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception)
        {
            // Never let an exception cross into native code.
        }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint EventClass;
        public uint EventKind;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyId
    {
        public uint Signature;
        public uint Id;
    }

    [DllImport(Carbon)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(
        IntPtr target, EventHandlerProc handler, uint numTypes, EventTypeSpec[] list, IntPtr userData, out IntPtr outRef);

    [DllImport(Carbon)]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(
        uint keyCode, uint modifiers, EventHotKeyId id, IntPtr target, uint options, out IntPtr outRef);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(IntPtr hotKeyRef);

    [DllImport(Carbon)]
    private static extern int GetEventParameter(
        IntPtr eventRef, uint name, uint desiredType, IntPtr actualType, nuint bufferSize, IntPtr actualSize,
        out EventHotKeyId data);
}
