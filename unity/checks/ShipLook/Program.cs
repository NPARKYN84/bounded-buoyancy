// Writes the demo ship's meshes, paint and rig (ShipLook) to files, so that they can be drawn and looked at outside
// Unity: `dotnet run -c Release -- OUTDIR [navy|merchant|pirate] [runout]`; with `tackle` in place of the colours,
// the fish (TackleLook) and the submarine's hull instead. Meshes as "v x y z u v" and "f a b c" lines,
// paint as binary PPM (row 0 at the bottom, as Unity has it), parts as one line each.
using System;
using System.Globalization;
using System.IO;
using System.Text;
using BoundedBuoyancy.Samples;

static class Program
{
    static readonly CultureInfo C = CultureInfo.InvariantCulture;

    static void Mesh(string path, MeshData m)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < m.Positions.Count; i++)
        {
            P3 p = m.Positions[i];
            sb.AppendLine(string.Format(C, "v {0} {1} {2} {3} {4}", p.x, p.y, p.z, m.UV[2 * i], m.UV[2 * i + 1]));
        }
        for (int i = 0; i < m.Triangles.Count; i += 3)
            sb.AppendLine(string.Format(C, "f {0} {1} {2}", m.Triangles[i], m.Triangles[i + 1], m.Triangles[i + 2]));
        File.WriteAllText(path, sb.ToString());
    }

    static void Ppm(string path, byte[] rgba, int w, int h)
    {
        using var f = File.Create(path);
        byte[] head = Encoding.ASCII.GetBytes($"P6\n{w} {h}\n255\n");
        f.Write(head, 0, head.Length);
        for (int i = 0; i < w * h; i++)
            f.Write(rgba, 4 * i, 3);
    }

    static string V(P3 p) { return string.Format(C, "{0} {1} {2}", p.x, p.y, p.z); }

    static int Main(string[] args)
    {
        string dir = args.Length > 0 ? args[0] : "out";
        string who = args.Length > 1 ? args[1] : "pirate";
        if (who == "tackle")
        {
            Directory.CreateDirectory(dir);
            for (int k = 0; k < 4; k++)
            {
                Mesh(Path.Combine(dir, "fish" + k + ".txt"), TackleLook.Fish(k));
                Ppm(Path.Combine(dir, "fish" + k + ".ppm"), TackleLook.FishPaint(k, 512, 128), 512, 128);
                float er;
                P3 e = TackleLook.Eye(k, out er);
                File.WriteAllText(Path.Combine(dir, "eye" + k + ".txt"), V(e) + " " + er.ToString(C));
            }
            Mesh(Path.Combine(dir, "sub.txt"), TackleLook.SubHull(22f, 1.6f));
            Ppm(Path.Combine(dir, "sub.ppm"), TackleLook.SubPaint(22f, 1024, 256), 1024, 256);
            Mesh(Path.Combine(dir, "torus.txt"), TackleLook.Torus(0.5f, 0.1f));
            Console.WriteLine("Wrote " + dir + ": four fish, the submarine's hull, a ring");
            return 0;
        }
        bool runOut = args.Length > 2 && args[2] == "runout";
        Directory.CreateDirectory(dir);
        ShipColours c = who == "navy" ? ShipLook.Navy : who == "merchant" ? ShipLook.Merchant : ShipLook.Pirate;
        float[] ports = { -4f, -2f, 0f, 2f, 4f };
        Mesh(Path.Combine(dir, "hull.txt"), ShipLook.Hull());
        Mesh(Path.Combine(dir, "transom.txt"), ShipLook.Transom());
        Mesh(Path.Combine(dir, "deck.txt"), ShipLook.DeckMesh());
        Ppm(Path.Combine(dir, "hull.ppm"), ShipLook.HullPaint(2048, 512, c, ports), 2048, 512);
        Ppm(Path.Combine(dir, "transom.ppm"), ShipLook.TransomPaint(512, 512, c), 512, 512);
        Ppm(Path.Combine(dir, "deck.ppm"), ShipLook.DeckPaint(1024, 256), 1024, 256);
        Ppm(Path.Combine(dir, "sail.ppm"), ShipLook.SailPaint(256, 256), 256, 256);
        ShipLook.Rig rig = ShipLook.MakeRig(ports, runOut);
        for (int i = 0; i < rig.Sails.Count; i++)
            Mesh(Path.Combine(dir, "sail" + i + ".txt"), rig.Sails[i]);
        Mesh(Path.Combine(dir, "jib.txt"), rig.JibSail);
        var sb = new StringBuilder();
        foreach (Part p in rig.Parts)
            sb.AppendLine(p.Cylinder
                ? string.Format(C, "cyl {0} {1} {2} {3}", p.Look, V(p.A), V(p.B), p.Radius)
                : string.Format(C, "box {0} {1} {2} {3} {4}", p.Look, V(p.Centre), V(p.X), V(p.Y), V(p.Size)));
        Part fl = rig.Flag;
        sb.AppendLine(string.Format(C, "box {0} {1} {2} {3} {4}", fl.Look, V(fl.Centre), V(fl.X), V(fl.Y), V(fl.Size)));
        File.WriteAllText(Path.Combine(dir, "parts.txt"), sb.ToString());
        Console.WriteLine("Wrote " + dir + ": " + rig.Parts.Count + " parts, " + rig.Sails.Count + " sails, colours " + who);
        return 0;
    }
}
