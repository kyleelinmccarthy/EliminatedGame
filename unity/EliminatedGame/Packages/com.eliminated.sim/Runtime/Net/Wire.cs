using System.Collections.Generic;
using System.IO;
using System.Text;
using Eliminated.Sim.Model;

namespace Eliminated.Sim.Net
{
    /// <summary>Compact per-actor record sent each tick (view-relevant fields only).</summary>
    public struct NetActor
    {
        public string Id;
        public float X, Y, Facing, Scale, Progress;
        public sbyte Team;
        public byte Anim;
        public short Number;
        public byte Flags; // bit0 alive, bit1 it, bit2 frozen, bit3 burning, bit4 shield, bit5 ghost

        public const byte Alive = 1, It = 2, Frozen = 4, Burning = 8, Shield = 16, Ghost = 32;
        public bool Has(byte f) => (Flags & f) != 0;
    }

    /// <summary>A decoded snapshot frame (the wire form of <see cref="Snapshot"/>).</summary>
    public sealed class SnapshotFrame
    {
        public GameId Game;
        public double T;
        public bool HasStartAt;
        public double StartAt;
        public List<NetActor> Actors = new List<NetActor>();
        public List<Effect> Fx = new List<Effect>();
        public string DataJson; // per-game payload, serialized by the client (e.g. JsonUtility)
    }

    /// <summary>
    /// Binary codec for the netcode: encodes <see cref="GameInput"/> (client→host)
    /// and snapshot frames (host→client) to compact byte arrays. Pure C# (no
    /// UnityEngine) so it is fully round-trip unit-tested headlessly; the Unity
    /// transport (NGO over Relay) just ships these bytes. The heterogeneous
    /// per-game <c>Snapshot.Data</c> is carried as an opaque JSON string the
    /// client (de)serializes with JsonUtility.
    /// </summary>
    public static class Wire
    {
        private static readonly Encoding Utf8 = new UTF8Encoding(false);

        // ── GameInput ────────────────────────────────────────────────────
        public static byte[] EncodeInput(GameInput input)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Utf8);
            w.Write((byte)input.Kind);
            w.Write(input.Seq);
            switch (input.Kind)
            {
                case InputKind.Move: w.Write(input.Dx); w.Write(input.Dy); break;
                case InputKind.Action: WriteStr(w, input.Name); w.Write(input.On); break;
                case InputKind.Aim: w.Write(input.Angle); break;
                case InputKind.Choose: WriteStr(w, input.Value); break;
                case InputKind.Tap: break;
            }
            w.Flush();
            return ms.ToArray();
        }

        public static GameInput DecodeInput(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms, Utf8);
            var kind = (InputKind)r.ReadByte();
            int seq = r.ReadInt32();
            var input = new GameInput { Kind = kind, Seq = seq };
            switch (kind)
            {
                case InputKind.Move: input.Dx = r.ReadSingle(); input.Dy = r.ReadSingle(); break;
                case InputKind.Action: input.Name = ReadStr(r); input.On = r.ReadBoolean(); break;
                case InputKind.Aim: input.Angle = r.ReadSingle(); break;
                case InputKind.Choose: input.Value = ReadStr(r); break;
            }
            return input;
        }

        // ── Snapshot frame ───────────────────────────────────────────────
        public static byte[] EncodeFrame(GameId game, double t, double? startAt,
            IReadOnlyList<Actor> actors, IReadOnlyList<Effect> fx, string dataJson)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms, Utf8);
            w.Write((byte)game);
            w.Write(t);
            w.Write(startAt.HasValue);
            w.Write(startAt ?? 0.0);

            int an = actors?.Count ?? 0;
            w.Write(an);
            for (int i = 0; i < an; i++) WriteActor(w, actors[i]);

            int fn = fx?.Count ?? 0;
            w.Write(fn);
            for (int i = 0; i < fn; i++)
            {
                var e = fx[i];
                w.Write((byte)e.Kind); w.Write(e.X); w.Write(e.Y); w.Write(e.A); WriteStr(w, e.Tag);
            }

            WriteStr(w, dataJson);
            w.Flush();
            return ms.ToArray();
        }

        public static SnapshotFrame DecodeFrame(byte[] bytes)
        {
            using var ms = new MemoryStream(bytes);
            using var r = new BinaryReader(ms, Utf8);
            var f = new SnapshotFrame
            {
                Game = (GameId)r.ReadByte(),
                T = r.ReadDouble(),
                HasStartAt = r.ReadBoolean(),
                StartAt = r.ReadDouble()
            };
            int an = r.ReadInt32();
            for (int i = 0; i < an; i++) f.Actors.Add(ReadActor(r));
            int fn = r.ReadInt32();
            for (int i = 0; i < fn; i++)
                f.Fx.Add(new Effect((EffectKind)r.ReadByte(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), ReadStr(r)));
            f.DataJson = ReadStr(r);
            return f;
        }

        private static void WriteActor(BinaryWriter w, Actor a)
        {
            WriteStr(w, a.Id);
            w.Write(a.Pos.X); w.Write(a.Pos.Y); w.Write(a.Facing); w.Write(a.Scale); w.Write(a.Progress);
            w.Write((sbyte)a.Team);
            w.Write((byte)a.Anim);
            w.Write((short)a.Number);
            byte flags = 0;
            if (a.Alive) flags |= NetActor.Alive;
            if (a.It) flags |= NetActor.It;
            if (a.Frozen) flags |= NetActor.Frozen;
            if (a.Burning) flags |= NetActor.Burning;
            if (a.Shield) flags |= NetActor.Shield;
            if (a.Ghost) flags |= NetActor.Ghost;
            w.Write(flags);
        }

        private static NetActor ReadActor(BinaryReader r) => new NetActor
        {
            Id = ReadStr(r),
            X = r.ReadSingle(), Y = r.ReadSingle(), Facing = r.ReadSingle(), Scale = r.ReadSingle(), Progress = r.ReadSingle(),
            Team = r.ReadSByte(),
            Anim = r.ReadByte(),
            Number = r.ReadInt16(),
            Flags = r.ReadByte()
        };

        private static void WriteStr(BinaryWriter w, string s)
        {
            w.Write(s != null);
            if (s != null) w.Write(s);
        }

        private static string ReadStr(BinaryReader r) => r.ReadBoolean() ? r.ReadString() : null;
    }
}
