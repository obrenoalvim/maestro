using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Maestro;

internal static partial class Native
{
    public const int SystemProcessInformation = 5;
    public const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    public const uint ProcessSetInformation = 0x0200;
    public const uint ProcessQueryLimitedInformation = 0x1000;
    public const int ProcessIoPriority = 33;
    public const int ProcessPowerThrottling = 4;

    [StructLayout(LayoutKind.Sequential)]
    public struct PowerThrottlingState
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    [LibraryImport("ntdll.dll")]
    public static partial int NtQuerySystemInformation(int infoClass, nint buffer, int length, out int returnLength);

    [LibraryImport("ntdll.dll")]
    public static partial int NtSetInformationProcess(SafeProcessHandle process, int infoClass, ref int info, int length);

    [LibraryImport("ntdll.dll")]
    public static partial int NtQueryInformationProcess(SafeProcessHandle process, int infoClass, out int info, int length, out int returnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessInformation(SafeProcessHandle process, int infoClass, ref PowerThrottlingState info, int size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint GetPriorityClass(SafeProcessHandle process);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPriorityClass(SafeProcessHandle process, uint priorityClass);

    public const uint EventSystemForeground = 0x0003;
    public const uint WinEventOutOfContext = 0x0000;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    public delegate void WinEventProc(nint hook, uint eventType, nint window, int objectId, int childId, uint thread, uint time);

    [LibraryImport("user32.dll")]
    public static partial nint SetWinEventHook(uint eventMin, uint eventMax, nint module, nint callback, uint pid, uint thread, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWinEvent(nint hook);

    [LibraryImport("user32.dll")]
    public static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    public static partial uint GetWindowThreadProcessId(nint window, out int pid);

    public const int ExStyleIndex = -20;
    public const long ExTransparent = 0x20;
    public const long ExToolWindow = 0x80;
    public const long ExLayered = 0x80000;
    public const long ExNoActivate = 0x8000000;
    public const uint ModAlt = 0x1;
    public const uint ModControl = 0x2;
    public const uint ModNoRepeat = 0x4000;
    public const int HotkeyMessage = 0x0312;
    private const int ImmersiveDarkMode = 20;
    private const uint NoSize = 0x1;
    private const uint NoActivate = 0x10;
    private static readonly nint TopMost = -1;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static partial nint GetWindowLongPtr(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static partial nint SetWindowLongPtr(nint window, int index, nint value);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint window, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint window, int id);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    public static void PinOnTop(nint window, int x, int y) => SetWindowPos(window, TopMost, x, y, 0, 0, NoSize | NoActivate);

    public static void UseDarkTitleBar(nint window)
    {
        var on = 1;
        DwmSetWindowAttribute(window, ImmersiveDarkMode, ref on, sizeof(int));
    }

    public static int ForegroundPid() => WindowPid(GetForegroundWindow());

    public static int WindowPid(nint window)
    {
        GetWindowThreadProcessId(window, out var pid);
        return pid;
    }
}
