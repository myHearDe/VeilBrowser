using System.Runtime.InteropServices;

namespace VeilBrowser.Infrastructure;

/// <summary>
/// Corrects the maximized bounds of a borderless WPF window.
/// WindowStyle=None can otherwise expand to the monitor rectangle instead of
/// the Windows work area and render underneath a bottom, top or side taskbar.
/// All values stay in physical monitor pixels, so mixed-DPI displays are
/// handled by Windows rather than by manual scale calculations.
/// </summary>
internal static class WindowWorkAreaHelper
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpFrameChanged = 0x0020;
    private const uint SwpShowWindow = 0x0040;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private static readonly nint HwndTopmost = new(-1);

    public static void ApplyToMinMaxInfo(
        nint windowHandle,
        nint minMaxInfoPointer,
        bool useWorkArea)
    {
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(minMaxInfoPointer);
        var monitorArea = monitorInfo.MonitorArea;
        var targetArea = useWorkArea
            ? monitorInfo.WorkArea
            : monitorArea;
        var width = Math.Max(1, targetArea.Right - targetArea.Left);
        var height = Math.Max(1, targetArea.Bottom - targetArea.Top);

        minMaxInfo.MaxPosition.X = targetArea.Left - monitorArea.Left;
        minMaxInfo.MaxPosition.Y = targetArea.Top - monitorArea.Top;
        minMaxInfo.MaxSize.X = width;
        minMaxInfo.MaxSize.Y = height;
        minMaxInfo.MaxTrackSize.X = width;
        minMaxInfo.MaxTrackSize.Y = height;

        Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, fDeleteOld: false);
    }

    public static void ApplyFullscreenBounds(nint windowHandle)
    {
        var monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitor == nint.Zero)
        {
            return;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = (uint)Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return;
        }

        var bounds = monitorInfo.MonitorArea;
        SetWindowPos(
            windowHandle,
            HwndTopmost,
            bounds.Left,
            bounds.Top,
            Math.Max(1, bounds.Right - bounds.Left),
            Math.Max(1, bounds.Bottom - bounds.Top),
            SwpFrameChanged | SwpShowWindow | SwpNoOwnerZOrder);
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(
        nint windowHandle,
        uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(
        nint monitorHandle,
        ref MonitorInfo monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        nint windowHandle,
        nint insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }
}
