using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Microsoft.Xna.Framework;
using MoonSharp.Interpreter;
using Terranoita.Noita;
using Terraria;
using Terraria.Net;

namespace Terranoita.Game
{
    /// <summary>
    /// Terranoita's own channel between the players' games and the server (PC-40; both sides run the mod since
    /// HostServer): a Terraria NetModule registered right after Terraria's own (NetworkInitializer.Load), so it gets the
    /// same id on every side. Messages: Cast (a player's wand cast, shown in the other players' games).
    /// </summary>
    public sealed class TerranoitaNet : NetModule
    {
        const byte Cast = 1;
        static bool _registered;
        static int _logged;

        /// <summary>Registers the module once, after Terraria's own (the client: at mod start, Initialize ran before;
        /// the server: in the postfix of NetworkInitializer.Load, the mod starts before Initialize there).</summary>
        public static void Register()
        {
            if (_registered)
                return;
            _registered = true;
            NetManager.Instance.Register<TerranoitaNet>();
            Entry.Log("net: Terranoita channel registered");
        }

        [Hook("net_register")]
        [HarmonyPatch(typeof(Terraria.Initializers.NetworkInitializer), nameof(Terraria.Initializers.NetworkInitializer.Load))]
        static class RegisterPatch
        {
            static void Postfix() => Register();
        }

        // ---- a cast: the shots the caster's gun.lua made, with their settings, from where and which way ----

        /// <summary>The local player cast: the other players see it (multiplayer client only).</summary>
        public static void SendCast(int player, IList<LuaShot> shots, Vector2 tip, Vector2 dir)
        {
            if (Main.netMode != 1 || !_registered || shots.Count == 0)
                return;
            try
            {
                var ms = new MemoryStream();
                using (var w = new BinaryWriter(ms))
                {
                    w.Write(Cast);
                    w.Write((byte)player);
                    w.Write(tip.X); w.Write(tip.Y); w.Write(dir.X); w.Write(dir.Y);
                    WriteShots(w, shots);
                }
                var bytes = ms.ToArray();
                var packet = CreatePacket<TerranoitaNet>(bytes.Length);
                packet.Writer.Write(bytes);
                NetManager.Instance.SendToServer(packet);
            }
            catch (Exception ex) { Entry.Error("net cast send", ex); }
        }

        public override bool Deserialize(BinaryReader reader, int userId)
        {
            try
            {
                byte kind = reader.ReadByte();
                if (kind != Cast)
                    return false;
                int player = reader.ReadByte();
                var tip = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                var dir = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                var shots = ReadShots(reader);
                if (Main.dedServ)
                {
                    // the server passes it on to the others: the sender's index is the one it came from
                    var ms = new MemoryStream();
                    using (var w = new BinaryWriter(ms))
                    {
                        w.Write(Cast);
                        w.Write((byte)userId);
                        w.Write(tip.X); w.Write(tip.Y); w.Write(dir.X); w.Write(dir.Y);
                        WriteShots(w, shots);
                    }
                    var bytes = ms.ToArray();
                    var packet = CreatePacket<TerranoitaNet>(bytes.Length);
                    packet.Writer.Write(bytes);
                    NetManager.Instance.Broadcast(packet, userId);
                }
                else if (player != Main.myPlayer && player >= 0 && player < Main.maxPlayers && Main.player[player].active)
                {
                    if (_logged++ < 3)
                        Entry.Log("net: cast of player " + player + " (" + shots.Count + " shots) shown");
                    Magic.SpellShots.FireRemote(shots, tip, dir, Main.player[player]);
                }
                return true;
            }
            catch (Exception ex)
            {
                Entry.Error("net receive", ex);
                return false;
            }
        }

        // shots share their settings tables (one per spell group: formations need it), so tables go once, shots by index
        static void WriteShots(BinaryWriter w, IList<LuaShot> shots)
        {
            var configs = new List<Dictionary<string, DynValue>>();
            var index = new Dictionary<Dictionary<string, DynValue>, int>();
            void Collect(IList<LuaShot> list)
            {
                foreach (var s in list)
                {
                    if (s.Config != null && !index.ContainsKey(s.Config))
                    {
                        index[s.Config] = configs.Count;
                        configs.Add(s.Config);
                    }
                    Collect(s.Payload);
                }
            }
            Collect(shots);
            w.Write((ushort)configs.Count);
            foreach (var c in configs)
            {
                w.Write((ushort)c.Count);
                foreach (var kv in c)
                {
                    w.Write(kv.Key);
                    WriteValue(w, kv.Value, 0);
                }
            }
            void Shots(IList<LuaShot> list)
            {
                w.Write((ushort)list.Count);
                foreach (var s in list)
                {
                    w.Write(s.File ?? "");
                    w.Write(s.Trigger ?? "");
                    w.Write(s.TriggerFrames);
                    w.Write(s.Config != null ? index[s.Config] : -1);
                    Shots(s.Payload);
                }
            }
            Shots(shots);
        }

        static List<LuaShot> ReadShots(BinaryReader r)
        {
            var script = new Script(CoreModules.None);
            int n = r.ReadUInt16();
            var configs = new List<Dictionary<string, DynValue>>(n);
            for (int i = 0; i < n; i++)
            {
                int m = r.ReadUInt16();
                var c = new Dictionary<string, DynValue>(m);
                for (int k = 0; k < m; k++)
                {
                    string key = r.ReadString();
                    c[key] = ReadValue(r, script);
                }
                configs.Add(c);
            }
            List<LuaShot> Shots()
            {
                int count = r.ReadUInt16();
                var list = new List<LuaShot>(count);
                for (int i = 0; i < count; i++)
                {
                    var s = new LuaShot { File = r.ReadString() };
                    string trigger = r.ReadString();
                    s.Trigger = trigger.Length == 0 ? null : trigger;
                    s.TriggerFrames = r.ReadInt32();
                    int ci = r.ReadInt32();
                    s.Config = ci >= 0 && ci < configs.Count ? configs[ci] : null;
                    s.Payload = Shots();
                    list.Add(s);
                }
                return list;
            }
            return Shots();
        }

        // Lua values of the settings: numbers, strings, booleans, tables of them
        static void WriteValue(BinaryWriter w, DynValue v, int depth)
        {
            if (v == null || depth > 4)
            {
                w.Write((byte)0);
                return;
            }
            switch (v.Type)
            {
                case DataType.Number: w.Write((byte)1); w.Write(v.Number); break;
                case DataType.String: w.Write((byte)2); w.Write(v.String); break;
                case DataType.Boolean: w.Write((byte)3); w.Write(v.Boolean); break;
                case DataType.Table:
                    w.Write((byte)4);
                    var pairs = new List<TablePair>(v.Table.Pairs);
                    w.Write((ushort)pairs.Count);
                    foreach (var p in pairs)
                    {
                        WriteValue(w, p.Key, depth + 1);
                        WriteValue(w, p.Value, depth + 1);
                    }
                    break;
                default: w.Write((byte)0); break;   // functions and the like: not settings a shot reads
            }
        }

        static DynValue ReadValue(BinaryReader r, Script script)
        {
            switch (r.ReadByte())
            {
                case 1: return DynValue.NewNumber(r.ReadDouble());
                case 2: return DynValue.NewString(r.ReadString());
                case 3: return DynValue.NewBoolean(r.ReadBoolean());
                case 4:
                    var t = new Table(script);
                    int n = r.ReadUInt16();
                    for (int i = 0; i < n; i++)
                    {
                        var k = ReadValue(r, script);
                        var val = ReadValue(r, script);
                        if (k.Type != DataType.Nil)
                            t.Set(k, val);
                    }
                    return DynValue.NewTable(t);
                default: return DynValue.Nil;
            }
        }
    }
}
