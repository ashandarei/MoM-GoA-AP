/*
  Simple DirectMedia Layer
  Copyright (C) 1997-2025 Sam Lantinga <slouken@libsdl.org>

  This software is provided 'as-is', without any express or implied
  warranty.  In no event will the authors be held liable for any damages
  arising from the use of this software.

  Permission is granted to anyone to use this software for any purpose,
  including commercial applications, and to alter it and redistribute it
  freely, subject to the following restrictions:
  1. The origin of this software must not be misrepresented; you must not
     claim that you wrote the original software. If you use this software
     in a product, an acknowledgment in the product documentation would be
     appreciated but is not required.
  2. Altered source versions must be plainly marked as such, and must not be
     misrepresented as being the original software.
  3. This notice may not be removed or altered from any source distribution.
*/

// Altered C# adaptation of the simple/common PS5 input handlers in SDL 3.2.28:
// https://github.com/libsdl-org/SDL/blob/7f3ae3d57459e59943a4ecfefc8f6277ec6bf540/src/joystick/hidapi/SDL_hidapi_ps5.c
// Changes: input-only subset, fixed standard DualSense transports, stateless
// decoding and a mod-defined 8-byte MOMP state. No SDL runtime is required.
// Unlike SDL's signed 16-bit axes, sticks use symmetric signed bytes with
// center 128 mapped to zero and positive Y pointing up. Sensors, touchpad,
// output reports, device discovery and Bluetooth CRC checking are omitted.
using System.Buffers.Binary;

namespace MoMController;

internal static class SdlDualSenseReport
{
    internal const int StateSize = 8;

    // MOMP: little-endian buttons (SDL gamepad enum order), four signed stick
    // axes, then two unsigned triggers. Every field is emitted on every report.
    internal static bool ConvertInPlace(Span<byte> report, bool bluetooth)
    {
        int start, buttonsOffset, triggersOffset;
        if (!bluetooth && report.Length == 64 && report[0] == 0x01)
            (start, buttonsOffset, triggersOffset) = (1, 7, 4);
        else if (bluetooth && report.Length is 10 or 78 && report[0] == 0x01)
            (start, buttonsOffset, triggersOffset) = (1, 4, 7);
        else if (bluetooth && report.Length == 78 && report[0] == 0x31)
            (start, buttonsOffset, triggersOffset) = (2, 7, 4);
        else
            return false;

        var data = report.Slice(start);
        var faceAndHat = data[buttonsOffset];
        var switches = data[buttonsOffset + 1];
        var system = data[buttonsOffset + 2];
        ushort buttons = 0;
        void Set(bool pressed, int bit) { if (pressed) buttons |= (ushort)(1 << bit); }
        Set((faceAndHat & 0x20) != 0, 0); // South / Cross
        Set((faceAndHat & 0x40) != 0, 1); // East / Circle
        Set((faceAndHat & 0x10) != 0, 2); // West / Square
        Set((faceAndHat & 0x80) != 0, 3); // North / Triangle
        Set((switches & 0x10) != 0, 4);   // Back / Create
        Set((system & 0x01) != 0, 5);     // Guide
        Set((switches & 0x20) != 0, 6);   // Start / Options
        Set((switches & 0x40) != 0, 7);   // Left stick
        Set((switches & 0x80) != 0, 8);   // Right stick
        Set((switches & 0x01) != 0, 9);   // Left shoulder
        Set((switches & 0x02) != 0, 10);  // Right shoulder
        var hat = faceAndHat & 0x0f;
        Set(hat is 7 or 0 or 1, 11);     // Up
        Set(hat is 3 or 4 or 5, 12);     // Down
        Set(hat is 5 or 6 or 7, 13);     // Left
        Set(hat is 1 or 2 or 3, 14);     // Right

        // Stage before writing: the destination aliases the incoming report.
        Span<byte> state = stackalloc byte[StateSize];
        BinaryPrimitives.WriteUInt16LittleEndian(state, buttons);
        for (var axis = 0; axis < 4; axis++)
        {
            var value = Math.Clamp((int)data[axis] - 128, -127, 127);
            if ((axis & 1) != 0) value = -value;
            state[2 + axis] = unchecked((byte)(sbyte)value);
        }
        var left = data[triggersOffset];
        var right = data[triggersOffset + 1];
        state[6] = left == 0 && (switches & 0x04) != 0 ? (byte)255 : left;
        state[7] = right == 0 && (switches & 0x08) != 0 ? (byte)255 : right;
        state.CopyTo(report);
        return true;
    }
}
