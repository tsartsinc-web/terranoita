using System;
using System.Globalization;
using System.Threading;

namespace Terranoita.Noita
{
    /// <summary>
    /// Lua turns numbers into text with the thread's culture: on a Russian Windows 183.33 becomes "183,33" and
    /// tonumber() of it fails, so Noita's scripts run with the invariant culture.
    /// </summary>
    public struct LuaCulture : IDisposable
    {
        /// <summary>
        /// Noita's LuaJIT walks a table's list part (1..n) first in pairs(); MoonSharp does not, and Noita's scripts rely
        /// on it (gun_procedural.lua get_gun_probs met its total_prob before the entries: every wand got the same
        /// stats). Run first in every script.
        /// </summary>
        public const string Prelude = @"
local _next = next
local _floor = math.floor
function pairs(t)
  local keys, n = {}, #t
  for i = 1, n do keys[i] = i end
  for k in _next, t do
    if not (type(k) == 'number' and k >= 1 and k <= n and _floor(k) == k) then keys[#keys + 1] = k end
  end
  local i = 0
  return function()
    i = i + 1
    local k = keys[i]
    if k ~= nil then return k, t[k] end
  end, t, nil
end
";

        readonly CultureInfo _old;

        LuaCulture(CultureInfo old) { _old = old; }

        public static LuaCulture Enter()
        {
            var old = Thread.CurrentThread.CurrentCulture;
            Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
            return new LuaCulture(old);
        }

        public void Dispose()
        {
            if (_old != null)
                Thread.CurrentThread.CurrentCulture = _old;
        }
    }
}
