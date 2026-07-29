using System.Runtime.InteropServices;

namespace VeilBrowser.Infrastructure;

internal static class SystemIdleTime
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint TickCount;
    }

    public static TimeSpan GetIdleDuration()
    {
        var info = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };
        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        var elapsedMilliseconds = unchecked((uint)Environment.TickCount - info.TickCount);
        return TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

#pragma warning disable SYSLIB1054 // This tiny blittable Win32 structure needs no source-generated marshalling.
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo lastInputInfo);
#pragma warning restore SYSLIB1054
}
