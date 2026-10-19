using System.Diagnostics;
using System.Runtime.InteropServices;
using MelonLoader;

namespace MoMController;

internal static class WindowFocusReturn
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(uint processId);

    internal static void AllowKh3(MelonLogger.Instance logger)
    {
        // Transfer permission while MoM still owns foreground; KH3 performs
        // the actual focus change after this process has fully exited.
        var processes = Process.GetProcessesByName("KINGDOM HEARTS III");
        try
        {
            if (processes.Length != 1 || processes[0].HasExited) return;
            var pid = checked((uint)processes[0].Id);
            logger.Msg($"KH3 focus return permission: pid={pid}, granted={AllowSetForegroundWindow(pid)}.");
        }
        catch (Exception error) { logger.Warning("Could not grant KH3 focus return: " + error.Message); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
}
