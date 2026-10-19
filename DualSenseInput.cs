using MelonLoader;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Utilities;

namespace MoMController;

internal sealed class DualSenseInput
{
    private readonly MelonLogger.Instance logger;
    private readonly Il2CppSystem.Action<InputEventPtr, InputDevice> callback;
    private readonly HashSet<int> observed = new();

    internal DualSenseInput(MelonLogger.Instance logger)
    {
        this.logger = logger;
        callback = (Action<InputEventPtr, InputDevice>)OnEvent;
        InputSystem.add_onEvent(callback);
    }

    private unsafe void OnEvent(InputEventPtr ev, InputDevice device)
    {
        if (device == null || ev.handled) return;
        var layout = device.layout.ToString();
        if (layout != "MoMDualSenseBluetooth" && layout != "MoMDualSenseUSB") return;
        // Device/configuration events must retain the engine's normal handling.
        if ((int)ev.type != StateEvent.Type) return;
        var state = StateEvent.From(ev);
        if (state == null || (int)state->stateFormat != (int)new FourCC('H', 'I', 'D')) return;
        var bytes = new Span<byte>(state->state, checked((int)state->stateSizeInBytes));
        var reportId = bytes.Length > 0 ? bytes[0] : 0;
        if (!SdlDualSenseReport.ConvertInPlace(bytes, layout == "MoMDualSenseBluetooth"))
        {
            ev.handled = true; // Never interpret unknown packets as stick/button data.
            return;
        }
        state->stateFormat = new FourCC('M', 'O', 'M', 'P');
        if (observed.Add((device.deviceId << 8) | reportId))
            logger.Msg($"SDL-derived DualSense input active: layout={layout}, device={device.deviceId}, report=0x{reportId:X2}, bytes={bytes.Length}.");
    }
}
