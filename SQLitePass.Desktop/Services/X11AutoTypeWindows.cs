using System.Runtime.InteropServices;
using System.Text;
using static SQLitePass.Desktop.Services.X11AutoTypeNative;

namespace SQLitePass.Desktop.Services;

internal sealed class X11AutoTypeWindows
{
    private const uint Window = 33;
    private const uint String = 31;
    private const uint WmName = 39;
    private readonly IntPtr _display;
    private readonly IntPtr _connection;
    private readonly uint _root;
    private readonly uint _activeWindow;
    private readonly uint _utf8Name;
    private readonly uint _utf8String;

    internal X11AutoTypeWindows(IntPtr display)
    {
        _display = display;
        _connection = XGetXCBConnection(display);
        _root = (uint)XDefaultRootWindow(display);
        _activeWindow = (uint)XInternAtom(display, "_NET_ACTIVE_WINDOW", 0);
        _utf8Name = (uint)XInternAtom(display, "_NET_WM_NAME", 0);
        _utf8String = (uint)XInternAtom(display, "UTF8_STRING", 0);
    }

    internal uint ForegroundWindow
    {
        get
        {
            IntPtr reply = GetProperty(_root, _activeWindow, Window, 1);
            if (reply == IntPtr.Zero) return 0;
            uint active;
            try
            {
                if (xcb_get_property_value_length(reply) != sizeof(uint)) return 0;
                active = (uint)Marshal.ReadInt32(xcb_get_property_value(reply));
            }
            finally { free(reply); }
            uint focus = GetFocus();
            return IsInside(focus, active) ? active : 0;
        }
    }

    internal string GetTitle(uint window) =>
        ReadTitle(window, _utf8Name, _utf8String, Encoding.UTF8) ?? ReadTitle(window, WmName, String, Encoding.Latin1) ?? "";

    internal uint GetProcessId(uint window)
    {
        XGrabServer(_display);
        try { return GetParent(window) == 0 ? 0 : QueryProcess(window); }
        finally
        {
            XUngrabServer(_display);
            XFlush(_display);
        }
    }

    // Called while the write connection holds a server grab, so the focus and client IDs stay stable.
    internal bool IsFocused(uint window, uint pid)
    {
        uint focus = GetFocus();
        return IsInside(focus, window) && QueryProcess(window) == pid && (focus == window || QueryProcess(focus) == pid);
    }

    private uint GetFocus()
    {
        XGetInputFocus(_display, out nuint focus, out _);
        return (uint)focus;
    }

    private bool IsInside(uint focus, uint window)
    {
        if (window <= 1 || window == _root) return false;
        for (int depth = 0; depth < 24 && focus > 1 && focus != _root; ++depth)
        {
            if (focus == window) return true;
            focus = GetParent(focus);
        }
        return false;
    }

    private uint GetParent(uint window)
    {
        if (window <= 1) return 0;
        var cookie = xcb_query_tree(_connection, window);
        IntPtr reply = xcb_query_tree_reply(_connection, cookie, out IntPtr error);
        try { return reply == IntPtr.Zero ? 0 : Marshal.PtrToStructure<TreeReply>(reply).Parent; }
        finally { free(reply); free(error); }
    }

    private uint QueryProcess(uint window)
    {
        var spec = new ClientIdSpec { Client = window, Mask = 2 }; // XRES_CLIENT_ID_PID_MASK
        if (XResQueryClientIds(_display, 1, ref spec, out nint count, out IntPtr ids) != 0) return 0;
        try
        {
            int pid = count == 1 ? XResGetClientPid(ids) : 0;
            return pid > 0 ? (uint)pid : 0;
        }
        finally { XResClientIdsDestroy(count, ids); }
    }

    private string? ReadTitle(uint window, uint property, uint type, Encoding encoding)
    {
        IntPtr reply = GetProperty(window, property, type, 256);
        if (reply == IntPtr.Zero) return null;
        try
        {
            var header = Marshal.PtrToStructure<PropertyReply>(reply);
            if (header.Format != 8 || header.BytesAfter != 0) return null;
            int length = xcb_get_property_value_length(reply);
            var data = new byte[length];
            Marshal.Copy(xcb_get_property_value(reply), data, 0, length);
            return encoding.GetString(data);
        }
        finally { free(reply); }
    }

    private IntPtr GetProperty(uint window, uint property, uint type, uint length)
    {
        if (window <= 1) return IntPtr.Zero;
        var cookie = xcb_get_property(_connection, 0, window, property, type, 0, length);
        IntPtr reply = xcb_get_property_reply(_connection, cookie, out IntPtr error);
        free(error);
        if (reply == IntPtr.Zero || Marshal.PtrToStructure<PropertyReply>(reply).Type == type) return reply;
        free(reply);
        return IntPtr.Zero;
    }
}
