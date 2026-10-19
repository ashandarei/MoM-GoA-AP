using HarmonyLib;
using Il2CppGumi.Device;
using MelonLoader;

namespace MoMController;

internal static class QuitDiagnostics
{
    private static MelonLogger.Instance? logger;
    private static string previous = "";

    internal static void Install(MelonLogger.Instance log)
    {
        logger = log;
        var harmony = new HarmonyLib.Harmony("ArchipelagoMine.MoM.QuitDiagnostics");
        foreach (var name in new[] { "OnApplicationQuit", "ExecuteCompleteProcess" })
            harmony.Patch(AccessTools.Method(typeof(ApplicationQuitManager), name),
                prefix: new HarmonyMethod(typeof(QuitDiagnostics), nameof(Entering)),
                postfix: new HarmonyMethod(typeof(QuitDiagnostics), nameof(Leaving)));
        foreach (var method in new[] {
            AccessTools.Method(typeof(Il2CppGumi.Sound.SoundManager), "Release"),
            AccessTools.Method(typeof(Il2Cpp.SeadInitializer), "Release"),
            AccessTools.Method(typeof(Il2CppGumi.Device.Application.Windows.ApplicationWindows), "End") })
            harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(QuitDiagnostics), nameof(Entering)),
                postfix: new HarmonyMethod(typeof(QuitDiagnostics), nameof(Leaving)));
        harmony.Patch(AccessTools.Method(typeof(ApplicationQuitManager), "Update"),
            postfix: new HarmonyMethod(typeof(QuitDiagnostics), nameof(Observe)));
    }

    private static void Entering(System.Reflection.MethodBase __originalMethod) =>
        logger?.Msg($"Quit trace: entering {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}; thread={Environment.CurrentManagedThreadId}.");
    private static void Leaving(System.Reflection.MethodBase __originalMethod) =>
        logger?.Msg($"Quit trace: returned {__originalMethod.DeclaringType?.Name}.{__originalMethod.Name}.");
    private static void Observe(ApplicationQuitManager __instance)
    {
        var state = $"started={__instance._quitStart} confirmed={__instance._quit} dialog={ApplicationQuitManager._displayMessageBox} bgm={ApplicationQuitManager._checkPlayingBGM}";
        if (state == previous) return;
        previous = state;
        logger?.Msg("Quit trace: " + state);
    }
}
