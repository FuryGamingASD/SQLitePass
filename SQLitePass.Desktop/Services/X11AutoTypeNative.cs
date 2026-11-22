using System.Runtime.InteropServices;

namespace SQLitePass.Desktop.Services;

internal static class X11AutoTypeNative
{
    private const string X11 = "libX11.so.6";
    private const string Xcb = "libxcb.so.1";
    private const string XRes = "libXRes.so.1";
    private const string XTest = "libXtst.so.6";

    [DllImport(X11)] internal static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(X11)] internal static extern int XCloseDisplay(IntPtr display);
    [DllImport(X11)] internal static extern nuint XDefaultRootWindow(IntPtr display);
    [DllImport(X11)] internal static extern nuint XInternAtom(IntPtr display, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int onlyIfExists);
    [DllImport(X11)] internal static extern int XGetInputFocus(IntPtr display, out nuint window, out int revertTo);
    [DllImport(X11)] internal static extern int XGrabServer(IntPtr display);
    [DllImport(X11)] internal static extern int XUngrabServer(IntPtr display);
    [DllImport(X11)] internal static extern int XFlush(IntPtr display);
    [DllImport(X11)] internal static extern int XPending(IntPtr display);
    [DllImport(X11)] internal static extern int XNextEvent(IntPtr display, IntPtr message);
    [DllImport(X11)] internal static extern int XGrabKeyboard(IntPtr display, nuint window, int ownerEvents, int pointerMode, int keyboardMode, nuint time);
    [DllImport(X11)] internal static extern int XUngrabKeyboard(IntPtr display, nuint time);
    [DllImport(X11)] internal static extern int XDisplayKeycodes(IntPtr display, out int min, out int max);
    [DllImport(X11)] internal static extern byte XKeysymToKeycode(IntPtr display, nuint symbol);
    [DllImport(X11)] internal static extern int XkbSelectEvents(IntPtr display, uint device, uint change, uint values);
    [DllImport(X11)] internal static extern int XkbRefreshKeyboardMapping(IntPtr message);
    [DllImport(X11)] internal static extern int XkbGetState(IntPtr display, uint device, out KeyboardState state);
    [DllImport(X11)] internal static extern uint XkbKeysymToModifiers(IntPtr display, nuint symbol);
    [DllImport(X11)] internal static extern int XkbLookupKeySym(IntPtr display, byte code, uint state, out uint remainingModifiers, out nuint symbol);
    [DllImport("libxkbcommon.so.0")] internal static extern uint xkb_keysym_to_utf32(uint symbol);
    [DllImport(XTest)] internal static extern int XTestQueryExtension(IntPtr display, out int eventBase, out int errorBase, out int major, out int minor);
    [DllImport(XTest)] internal static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, int pressed, nuint delay);
    [DllImport(XRes)] internal static extern int XResQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport(XRes)] internal static extern int XResQueryVersion(IntPtr display, out int major, out int minor);
    [DllImport(XRes)] internal static extern int XResQueryClientIds(IntPtr display, nint specs, ref ClientIdSpec spec, out nint count, out IntPtr ids);
    [DllImport(XRes)] internal static extern int XResGetClientPid(IntPtr id);
    [DllImport(XRes)] internal static extern void XResClientIdsDestroy(nint count, IntPtr ids);
    [DllImport("libX11-xcb.so.1")] internal static extern IntPtr XGetXCBConnection(IntPtr display);
    [DllImport(Xcb)] internal static extern uint xcb_query_tree(IntPtr connection, uint window);
    [DllImport(Xcb)] internal static extern IntPtr xcb_query_tree_reply(IntPtr connection, uint cookie, out IntPtr error);
    [DllImport(Xcb)] internal static extern uint xcb_get_property(IntPtr connection, byte delete, uint window, uint property, uint type, uint offset, uint length);
    [DllImport(Xcb)] internal static extern IntPtr xcb_get_property_reply(IntPtr connection, uint cookie, out IntPtr error);
    [DllImport(Xcb)] internal static extern IntPtr xcb_get_property_value(IntPtr reply);
    [DllImport(Xcb)] internal static extern int xcb_get_property_value_length(IntPtr reply);
    [DllImport("libc.so.6")] internal static extern void free(IntPtr memory);

    [StructLayout(LayoutKind.Sequential)] internal struct ClientIdSpec { public nuint Client; public uint Mask; }

    [StructLayout(LayoutKind.Explicit, Size = 18)] internal struct KeyboardState
    {
        [FieldOffset(0)] public byte Group;
        [FieldOffset(7)] public byte BaseModifiers;
        [FieldOffset(8)] public byte LatchedModifiers;
        [FieldOffset(9)] public byte LockedModifiers;
        [FieldOffset(13)] public byte LookupModifiers;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)] internal struct TreeReply
    {
        [FieldOffset(12)] public uint Parent;
    }

    [StructLayout(LayoutKind.Explicit, Size = 32)] internal struct PropertyReply
    {
        [FieldOffset(1)] public byte Format;
        [FieldOffset(8)] public uint Type;
        [FieldOffset(12)] public uint BytesAfter;
    }
}
