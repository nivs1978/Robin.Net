// Project: Robin.Net
// File: StatusLayout.cs
// License: GNU General Public License version 3 (see LICENSE)

namespace RobinHood.Engine;

// Names the byte at an offset of the saved data-segment image (0..0x2116), for trace comparison reports.
public static class StatusLayout
{
    // Start, length, element size, row size (0 for a plain array), name.
    static readonly (int Start, int Length, int Element, int Row, string Name)[] Fields =
    [
        (0x0000, 2, 2, 0, "time_frame"), (0x0002, 2, 2, 0, "camera_lock"), (0x0004, 2, 2, 0, "host"), (0x0006, 1, 1, 0, "cubeset"), (0x0007, 2, 2, 0, "num_mobiles"),
        (0x0009, 80, 2, 0, "mob_x"), (0x0059, 80, 2, 0, "mob_y"), (0x00a9, 40, 1, 0, "mob_z"), (0x00d1, 40, 1, 0, "mob_dir"), (0x00f9, 40, 1, 0, "mob_carried"),
        (0x0121, 40, 1, 0, "mob_behind"), (0x0149, 40, 1, 0, "mob_above"), (0x0171, 40, 1, 0, "mob_type"), (0x0199, 40, 1, 0, "mob_targetx"), (0x01c1, 40, 1, 0, "mob_targety"),
        (0x01e9, 40, 1, 0, "mob_subtargetx"), (0x0211, 40, 1, 0, "mob_subtargety"), (0x0239, 40, 1, 0, "mob_follow"), (0x0261, 80, 2, 0, "mob_base_spr"), (0x02b1, 40, 1, 0, "mob_pose"),
        (0x02d9, 40, 1, 0, "mob_about_face"), (0x0301, 40, 1, 0, "mob_mobility"), (0x0329, 40, 1, 0, "alert"), (0x0351, 3200, 2, 80, "interactions"), (0x0fd1, 1280, 1, 32, "mob_attr"),
        (0x14d1, 40, 1, 0, "mob_behaviour"), (0x14f9, 40, 1, 0, "mob_homex"), (0x1521, 40, 1, 0, "mob_homey"), (0x1549, 80, 2, 0, "mob_signal"), (0x1599, 1280, 1, 32, "pose_alias"),
        (0x1a99, 40, 1, 0, "mob_puff"), (0x1ac1, 40, 1, 0, "saved_mode"), (0x1ae9, 20, 1, 0, "ideolibenable"), (0x1afd, 20, 1, 0, "button_regen"), (0x1b11, 1280, 2, 32, "seq_buffer"),
        (0x2011, 40, 1, 0, "seq_count"), (0x2039, 40, 1, 0, "seq_name"), (0x2061, 60, 2, 6, "sig_store"), (0x209d, 2, 2, 0, "sig_time"), (0x209f, 80, 2, 0, "sig_stack"), (0x20ef, 40, 1, 0, "mob_blip_colour")
    ];

    public static string Describe(int offset)
    {
        foreach (var (start, length, element, row, name) in Fields)
        {
            if (offset < start || offset >= start + length) continue;
            var relative = offset - start;
            if (row > 0) return $"{name}[{relative / row}][{relative % row / element}]";
            return length == element ? name : $"{name}[{relative / element}]";
        }
        return $"offset 0x{offset:X4}";
    }
}
