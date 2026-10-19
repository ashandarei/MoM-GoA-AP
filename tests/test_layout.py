"""Check the engine layout contract against the decoder's canonical state."""
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
LAYOUT = json.loads((ROOT / "DualSenseGamepad.json").read_text())
CONTROLS = {item["name"]: item for item in LAYOUT["controls"]}


class DualSenseLayoutTests(unittest.TestCase):
    def test_custom_format_and_registration(self):
        self.assertEqual(LAYOUT["format"], "MOMP")
        self.assertEqual(LAYOUT["extend"], "Gamepad")
        self.assertNotIn("device", LAYOUT)
        adapter = (ROOT / "DualSenseInput.cs").read_text()
        self.assertIn("SdlDualSenseReport.ConvertInPlace", adapter)
        self.assertIn("state->stateFormat = new FourCC('M', 'O', 'M', 'P')", adapter)

    def test_sdl_button_indices(self):
        names = ["buttonSouth", "buttonEast", "buttonWest", "buttonNorth",
                 "select", "systemButton", "start", "leftStickPress",
                 "rightStickPress", "leftShoulder", "rightShoulder"]
        for bit, name in enumerate(names):
            control = CONTROLS[name]
            self.assertEqual((control["offset"], control["bit"], control["sizeInBits"]),
                             (bit // 8, bit % 8, 1))

    def test_independent_dpad_bits(self):
        self.assertEqual(CONTROLS["dpad"]["offset"], 1)
        for bit, direction in enumerate(["up", "down", "left", "right"], 3):
            control = CONTROLS["dpad/" + direction]
            self.assertEqual((control["layout"], control["offset"], control["bit"],
                              control["sizeInBits"]), ("Button", 0, bit, 1))

    def test_signed_sticks_and_unsigned_triggers(self):
        for name, offset in [("leftStick", 2), ("rightStick", 4)]:
            self.assertEqual(CONTROLS[name]["offset"], offset)
            self.assertEqual(CONTROLS[name]["sizeInBits"], 16)
            for index, axis in enumerate(["x", "y"]):
                control = CONTROLS[name + "/" + axis]
                self.assertEqual((control["format"], control["offset"], control["defaultState"]),
                                 ("SBYT", index, 0))
                self.assertIn("invert=false", control["parameters"])
        for name, offset in [("leftTrigger", 6), ("rightTrigger", 7)]:
            self.assertEqual((CONTROLS[name]["format"], CONTROLS[name]["offset"]), ("BYTE", offset))


if __name__ == "__main__":
    unittest.main()
