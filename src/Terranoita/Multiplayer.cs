using System;
using System.Linq;
using System.Reflection;
using Terraria;

namespace Terranoita.Game
{
    /// <summary>
    /// Playing together through Melty and Steam (author): Terraria's own Steam lobbies. The host (Host &amp; Play with
    /// Steam) writes "Hosting at &lt;lobby id&gt;" to our log, Melty reads it and starts the friends' games with Terraria's
    /// own "+connect_lobby &lt;lobby id&gt;" (NetClientSocialModule.CheckParameters), which Terranoita.exe passes on.
    /// Read by reflection: Terraria's Steam classes (SocialAPI.Network._lobby: Lobby.Id/Owner, Steamworks.SteamUser).
    /// </summary>
    public static class Multiplayer
    {
        static ulong _logged;
        static bool _broken;

        public static void Update()
        {
            if (_broken || Main.gameMenu || Main.netMode != 1 || Main.GameUpdateCount % 60 != 0)
                return;
            try
            {
                var net = Terraria.Social.SocialAPI.Network;
                if (net == null)
                    return;
                var lobby = Field(net, "_lobby");
                ulong id = SteamId(Field(lobby, "Id")), owner = SteamId(Field(lobby, "Owner"));
                if (id == 0 || id == _logged)
                    return;
                _logged = id;
                var steamUser = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("Steamworks.SteamUser")).FirstOrDefault(t => t != null);
                ulong me = SteamId(steamUser?.GetMethod("GetSteamID", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null));
                // Melty's join link reads this line (recipe.multiplayer.connect.address) on the host's PC
                Entry.Log(owner != 0 && owner == me ? "Hosting at " + id : "Steam lobby " + id + " (joined)");
            }
            catch (Exception ex)
            {
                _broken = true;
                Entry.Log("Steam lobby not readable: " + ex.GetBaseException().Message);
            }
        }

        static object Field(object o, string name)
        {
            for (var t = o?.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (f != null)
                    return f.GetValue(o);
            }
            return null;
        }

        static ulong SteamId(object cSteamId) => cSteamId == null ? 0 : (ulong)(Field(cSteamId, "m_SteamID") ?? 0UL);
    }
}
