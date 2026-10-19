using System.Buffers.Binary;
using MoMController;

internal static class SdlDualSenseReportTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    // Independent fixtures for USB common, BT simple, BT padded simple and
    // BT enhanced common. Logical inputs must decode identically on all four.
    private static byte[] Packet(int transport, byte face = 8, byte switches = 0,
        byte system = 0, byte x = 128, byte y = 128, byte rx = 128,
        byte ry = 128, byte lt = 0, byte rt = 0)
    {
        var packet = new byte[transport == 0 ? 64 : transport == 1 ? 10 : 78];
        packet[0] = transport == 3 ? (byte)0x31 : (byte)1;
        if (transport == 3) packet[1] = 0x99;
        var start = transport == 3 ? 2 : 1;
        packet[start] = x; packet[start + 1] = y;
        packet[start + 2] = rx; packet[start + 3] = ry;
        var simple = transport is 1 or 2;
        var buttons = start + (simple ? 4 : 7);
        var triggers = start + (simple ? 7 : 4);
        packet[buttons] = face; packet[buttons + 1] = switches;
        packet[buttons + 2] = system;
        packet[triggers] = lt; packet[triggers + 1] = rt;
        return packet;
    }

    private static byte[] Decode(byte[] packet, int transport)
    {
        var tail = packet.AsSpan(SdlDualSenseReport.StateSize).ToArray();
        Check(SdlDualSenseReport.ConvertInPlace(packet, transport != 0), "Valid transport rejected.");
        Check(packet.AsSpan(SdlDualSenseReport.StateSize).SequenceEqual(tail), "Wrote outside canonical state.");
        return packet.AsSpan(0, SdlDualSenseReport.StateSize).ToArray();
    }

    internal static void Run()
    {
        for (var transport = 0; transport < 4; transport++)
        {
            Check(Decode(Packet(transport), transport).All(x => x == 0), "Neutral controls are not zero.");
            var chord = Decode(Packet(transport, face: 0x28, switches: 3,
                x: 0, y: 255, rx: 255, ry: 0, lt: 55, rt: 66), transport);
            Check(chord.SequenceEqual(new byte[] { 1, 6, 129, 129, 127, 127, 55, 66 }),
                "Transport/chord/axis/trigger mapping mismatch.");
            foreach (var (face, switches, system, bit) in new[] {
                (0x28,0,0,0), (0x48,0,0,1), (0x18,0,0,2), (0x88,0,0,3),
                (8,0x10,0,4), (8,0,1,5), (8,0x20,0,6), (8,0x40,0,7),
                (8,0x80,0,8), (8,1,0,9), (8,2,0,10) })
            {
                var state = Decode(Packet(transport, (byte)face, (byte)switches, (byte)system), transport);
                Check(BinaryPrimitives.ReadUInt16LittleEndian(state) == (1 << bit), "Button isolation failed.");
            }
            var hats = new[] { 1 << 11, (1 << 11) | (1 << 14), 1 << 14,
                (1 << 12) | (1 << 14), 1 << 12, (1 << 12) | (1 << 13),
                1 << 13, (1 << 11) | (1 << 13), 0, 0, 0, 0, 0, 0, 0, 0 };
            for (byte hat = 0; hat < 16; hat++)
                Check(BinaryPrimitives.ReadUInt16LittleEndian(Decode(Packet(transport, hat), transport)) == hats[hat],
                    "D-pad compass/neutral mapping failed.");
            var digital = Decode(Packet(transport, switches: 0x0c), transport);
            Check(digital[6] == 255 && digital[7] == 255, "Digital trigger fallback missing.");
            var analog = Decode(Packet(transport, switches: 0x0c, lt: 1, rt: 254), transport);
            Check(analog[6] == 1 && analog[7] == 254, "Digital trigger overrode nonzero analog input.");
            var previousX = -128;
            var previousY = 128;
            for (var value = 0; value < 256; value++)
            {
                var state = Decode(Packet(transport, x: (byte)value, y: (byte)value,
                    rx: (byte)value, ry: (byte)value, lt: (byte)value, rt: (byte)value), transport);
                var x = unchecked((sbyte)state[2]); var y = unchecked((sbyte)state[3]);
                Check(x >= previousX && y <= previousY && x == -y, "Stick direction/monotonicity failed.");
                Check(state[2] == state[4] && state[3] == state[5], "Right stick differs from left.");
                Check(state[6] == value && state[7] == value, "Analog trigger range mismatch.");
                previousX = x; previousY = y;
            }
        }
        // Exhaust report length/ID/transport boundaries; rejection is atomic.
        foreach (var bluetooth in new[] { false, true })
        foreach (var id in new byte[] { 0, 1, 0x31, 0xff })
        for (var length = 0; length <= 100; length++)
        {
            var packet = Enumerable.Repeat((byte)0xa5, length).ToArray();
            if (length > 0) packet[0] = id;
            var original = packet.ToArray();
            var expected = bluetooth ? (id == 1 && length is 10 or 78) || (id == 0x31 && length == 78)
                : id == 1 && length == 64;
            Check(SdlDualSenseReport.ConvertInPlace(packet, bluetooth) == expected, "Report boundary mismatch.");
            if (!expected) Check(packet.SequenceEqual(original), "Rejected input was modified.");
        }
        Console.WriteLine("PASS: SDL-derived USB/BT decoder, buttons/chords, all hats, axis/trigger ranges, digital fallback and malformed report boundaries.");
    }
}
