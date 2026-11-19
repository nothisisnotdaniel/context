using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Context;

/// <summary>
/// The real <see cref="IGlobalHotkey"/> on macOS (job 260): Control+Option+Shift+S through Carbon's
/// <c>RegisterEventHotKey</c> on the application event target, with one event handler for
/// <c>kEventHotKeyPressed</c>. The handler delegate is kept in a field so it is not collected while registered.
/// Register it on the main thread, before the window's run loop starts.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed class MacGlobalHotkey : IGlobalHotkey
{
    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    // 'keyb' and kEventHotKeyPressed.
    private const uint EventClassKeyboard = 0x6B657962;
    private const uint EventHotKeyPressed = 5;

    // kVK_ANSI_S and the Carbon modifier bits controlKey, optionKey, shiftKey.
    private const uint KeyS = 0x01;
    private const uint Modifiers = 0x1000 | 0x0800 | 0x0200;

    // 'Ctxt', the hotkey's signature.
    private const uint Signature = 0x43747874;

    private readonly EventHandlerProc handler;
    private IntPtr hotKeyRef;
    private IntPtr handlerRef;

    public MacGlobalHotkey()
    {
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

            EventHotKeyId id = new EventHotKeyId { Signature = Signature, Id = 1 };
            if (RegisterEventHotKey(KeyS, Modifiers, id, target, 0, out hotKeyRef) != 0)
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
}
