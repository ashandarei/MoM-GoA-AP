using System.Reflection;
using System.Text.Json;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Layouts;

[assembly: MelonInfo(typeof(MoMController.ControllerMod), "MoM Direct Controller", "0.1.4", "ArchipelagoMine")]
[assembly: MelonGame("SQUARE ENIX", "KINGDOM HEARTS Melody of Memory")]

namespace MoMController;

public sealed class ControllerMod : MelonMod
{
    private string previousDevices = "";
    private readonly Dictionary<int, string> previousButtons = new();
    private float nextStatus;
    private string statusPath = "";
    private bool failed;
    private bool diagnostics;
    private SongLaunchService? songLaunch;
    private SongResultRecorder? songResults;
    private DualSenseInput? controllerInput;
    private static object HidSummary(string capabilities)
    {
        if (string.IsNullOrEmpty(capabilities)) return new { };
        using var json = JsonDocument.Parse(capabilities);
        var values = new Dictionary<string, JsonElement>();
        foreach (var key in new[] { "vendorId", "productId", "usage", "usagePage", "inputReportSize", "outputReportSize" })
            if (json.RootElement.TryGetProperty(key, out var value)) values[key] = value.Clone();
        return values;
    }

    public override void OnInitializeMelon()
    {
        statusPath = Path.Combine(MelonEnvironment.UserDataDirectory, "MoMController-status.json");
        diagnostics = File.Exists(Path.Combine(MelonEnvironment.UserDataDirectory, "MoMController-diagnostics.enabled"));
        using var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("MoMController.DualSenseGamepad.json")!;
        using var reader = new StreamReader(resource);
        var layout = reader.ReadToEnd();
        controllerInput = new DualSenseInput(LoggerInstance);
        foreach (var (size, name) in new[] { (64, "MoMDualSenseUSB"), (78, "MoMDualSenseBluetooth") })
        {
            var matcher = new InputDeviceMatcher().WithInterface("HID")
                .WithCapability("vendorId", 1356).WithCapability("productId", 3302)
                .WithCapability("inputReportSize", size);
            ValidateMatcher(matcher, size);
            // Register separately: this old IL2CPP build does not reliably pass the
            // non-blittable Nullable<InputDeviceMatcher> value through interop.
            // JSON device matchers also discard numeric capability restrictions.
            InputSystem.RegisterLayout(layout.Replace("MoMDualSenseUSB", name).Replace("DualSense (USB)",
                size == 64 ? "DualSense (USB)" : "DualSense (Bluetooth)"), name, new Il2CppSystem.Nullable<InputDeviceMatcher>());
            InputSystem.RegisterLayoutMatcher(name, matcher);
        }
        LoggerInstance.Msg("Registered direct DualSense USB and Bluetooth HID layouts. Built-in DualShock 4 support is preserved.");
        LoggerInstance.Msg("Controller diagnostics " + (diagnostics ? "enabled" : "disabled"));
        if (diagnostics) QuitDiagnostics.Install(LoggerInstance);
        songLaunch = SongLaunchService.FromPendingRequest(LoggerInstance);
        songResults = new SongResultRecorder(LoggerInstance);
        // Separate from optional input diagnostics: the client can verify a fresh installation.
        var readyPath = Path.Combine(MelonEnvironment.UserDataDirectory, "MoMInstallReady.json");
        File.WriteAllText(readyPath + ".tmp", JsonSerializer.Serialize(new {
            schema = 1, version = "0.1.4", process_id = Environment.ProcessId, utc = DateTime.UtcNow
        }));
        File.Move(readyPath + ".tmp", readyPath, true);
    }

    private void ValidateMatcher(InputDeviceMatcher matcher, int reportSize)
    {
        // Exercise the game's matcher, including non-Sony HID devices, without
        // adding devices or queuing synthetic inputs into gameplay.
        foreach (var (vendor, product, size, expected) in new[] {
            (1356, 3302, 64, reportSize == 64), (1356, 3302, 78, reportSize == 78),
            (1356, 1476, 64, false), (1356, 2508, 64, false),
            (2821, 6822, 64, false), (1356, 3302, 0, false) })
        {
            var desc = new InputDeviceDescription {
                interfaceName = "HID", product = "Controller matcher check",
                capabilities = JsonSerializer.Serialize(new { vendorId = vendor, productId = product, inputReportSize = size })
            };
            if ((matcher.MatchPercentage(desc) > 0) != expected)
                throw new InvalidOperationException($"HID matcher rejected validation case {vendor}:{product}:{size}");
        }
        LoggerInstance.Msg($"Passed six native HID matcher checks for report size {reportSize} (USB, Bluetooth, DS4, unrelated HID).");
    }

    public override void OnUpdate()
    {
        SongCatalogExporter.Update(LoggerInstance);
        songLaunch?.Update();
        songResults?.Update();
        if (!diagnostics || failed || Time.realtimeSinceStartup < nextStatus) return;
        nextStatus = Time.realtimeSinceStartup + 0.1f;
        try
        {
            var devices = InputSystem.devices;
            var unsupportedEntries = new List<object>();
            var unsupported = InputSystem.GetUnsupportedDevices();
            for (var u = 0; u < unsupported.Count; u++)
            {
                var desc = unsupported[u];
                if (desc.product?.Contains("DualSense") != true) continue;
                unsupportedEntries.Add(new { product = desc.product, hid = HidSummary(desc.capabilities) });
            }
            var entries = new List<object>();
            var names = new List<string>();
            for (var i = 0; i < devices.Count; ++i)
            {
                var device = devices[i];
                var description = device.description;
                names.Add($"{device.deviceId}:{device.layout}:{description.product}");
                var pad = device.TryCast<Gamepad>();
                string buttons = "";
                if (pad != null)
                {
                    var pressed = new List<string>();
                    foreach (var (name, control) in new[] {
                        ("Cross", pad.buttonSouth), ("Circle", pad.buttonEast),
                        ("Square", pad.buttonWest), ("Triangle", pad.buttonNorth),
                        ("L1", pad.leftShoulder), ("R1", pad.rightShoulder),
                        ("L2", pad.leftTrigger), ("R2", pad.rightTrigger),
                        ("Options", pad.startButton), ("Create", pad.selectButton),
                        ("L3", pad.leftStickButton), ("R3", pad.rightStickButton),
                        ("Up", pad.dpad.up), ("Down", pad.dpad.down),
                        ("Left", pad.dpad.left), ("Right", pad.dpad.right) })
                        if (control.isPressed) pressed.Add(name);
                    buttons = string.Join("+", pressed);
                    if (!previousButtons.TryGetValue(device.deviceId, out var old) || old != buttons)
                    {
                        LoggerInstance.Msg($"Controller {device.deviceId} buttons: {(buttons.Length == 0 ? "released" : buttons)}");
                        previousButtons[device.deviceId] = buttons;
                    }
                }
                entries.Add(new { id = device.deviceId, layout = device.layout.ToString(), product = description.product,
                    interfaceName = description.interfaceName, hid = HidSummary(description.capabilities),
                    enabled = device.enabled, isGamepad = pad != null, buttons,
                    leftX = pad?.leftStick.x.ReadValue(), leftY = pad?.leftStick.y.ReadValue(),
                    rightX = pad?.rightStick.x.ReadValue(), rightY = pad?.rightStick.y.ReadValue() });
            }
            var deviceNames = string.Join("; ", names);
            if (deviceNames != previousDevices)
            {
                LoggerInstance.Msg("Input devices: " + deviceNames);
                previousDevices = deviceNames;
            }
            File.WriteAllText(statusPath + ".tmp", JsonSerializer.Serialize(new { utc = DateTime.UtcNow,
                focused = Application.isFocused, devices = entries, unsupported = unsupportedEntries }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(statusPath + ".tmp", statusPath, true);
        }
        catch (Exception ex)
        {
            failed = true;
            LoggerInstance.Error("Controller diagnostics failed: " + ex);
        }
    }

    public override void OnApplicationQuit()
    {
        WindowFocusReturn.AllowKh3(LoggerInstance);
        songResults?.Flush(force: true);
    }
}
