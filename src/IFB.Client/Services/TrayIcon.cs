using System.Runtime.InteropServices;

namespace IFB.Client.Services;

public sealed class TrayIcon : IDisposable
{
    private readonly IntPtr _window;
    private readonly SubclassProc _callback;
    private readonly Action _open, _exit;
    private const uint CallbackMessage = 0x8001;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct IconData
    {
        public uint Size; public IntPtr Window; public uint Id, Flags, Message; public IntPtr Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags; public Guid Guid; public IntPtr BalloonIcon;
    }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] private static extern IntPtr DefSubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("comctl32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool RemoveWindowSubclass(IntPtr window, SubclassProc callback, UIntPtr id);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "Shell_NotifyIconW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Notify(uint action, ref IconData data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr LoadIcon(IntPtr instance, IntPtr resource);
    [DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string text);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern uint TrackPopupMenu(IntPtr menu, uint flags, int x, int y, int reserved, IntPtr window, IntPtr rect);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DestroyMenu(IntPtr menu);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool ShowWindow(IntPtr window, int command);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    private IconData Data => new() { Size = (uint)Marshal.SizeOf<IconData>(), Window = _window, Id = 1,
        Flags = 1 | 2 | 4, Message = CallbackMessage, Icon = LoadIcon(IntPtr.Zero, new IntPtr(32512)), Tip = "IFB Connect", Info = "", InfoTitle = "" };
    public TrayIcon(IntPtr window, Action open, Action exit)
    {
        _window = window; _open = open; _exit = exit; _callback = Handle;
        SetWindowSubclass(window, _callback, new UIntPtr(0x1FB), UIntPtr.Zero); var data = Data; Notify(0, ref data);
    }
    private IntPtr Handle(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == CallbackMessage)
        {
            if ((uint)lParam.ToInt64() == 0x203) _open();
            if ((uint)lParam.ToInt64() == 0x205)
            {
                var menu = CreatePopupMenu(); AppendMenu(menu, 0, new UIntPtr(1), "Open IFB Connect"); AppendMenu(menu, 0, new UIntPtr(2), "Exit");
                GetCursorPos(out var point); SetForegroundWindow(window);
                var command = TrackPopupMenu(menu, 0x100 | 0x2, point.X, point.Y, 0, window, IntPtr.Zero); DestroyMenu(menu);
                if (command == 1) _open(); if (command == 2) _exit();
            }
            return IntPtr.Zero;
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }
    public void Balloon(string title, string text)
    { var data = Data; data.Flags = 0x10; data.InfoTitle = title.Length > 63 ? title[..63] : title; data.Info = text.Length > 255 ? text[..255] : text; data.InfoFlags = 1; Notify(1, ref data); }
    public void Dispose() { var data = Data; Notify(2, ref data); RemoveWindowSubclass(_window, _callback, new UIntPtr(0x1FB)); }
}
