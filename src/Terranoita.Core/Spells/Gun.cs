using System;
using System.Collections.Generic;
using System.Linq;

namespace Terranoita.Spells
{
    /// <summary>
    /// What one spell does when a wand plays it (stage 3). Filled from the spells sheet, which comes from the player's
    /// gun_actions.lua (tncli spells). Spells whose Noita function does more than this are marked there for hand work.
    /// </summary>
    public sealed class Spell
    {
        public string Id;
        public string Type;                    // projectile, static_projectile, modifier, draw_many, material, other, utility, passive
        public float Mana;
        public int MaxUses = -1;               // -1 = unlimited
        public string[] Projectiles = new string[0];
        public Trigger[] Triggers = new Trigger[0];
        public int Draws;                      // cards drawn right after it, into the same shot (modifiers 1, multicasts n)
        public float ReloadAdd;                // added to the wand's recharge time, frames
        public Dictionary<string, float> ConfigAdd = new Dictionary<string, float>();   // shot config c.x += n
        public Dictionary<string, float> ConfigMul = new Dictionary<string, float>();   // shot config c.x *= n
    }

    public sealed class Trigger
    {
        public string Kind;                    // timer, hit_world, death
        public string File;
        public int Draws;
        public int Frames;
    }

    /// <summary>A wand: stats and the spells in its slots (null = empty slot).</summary>
    public sealed class Wand
    {
        public bool Shuffle;
        public int SpellsPerCast = 1;
        public float CastDelay;                // frames (Noita fire_rate_wait)
        public float RechargeTime;             // frames (Noita reload_time)
        public float ManaMax, ManaChargePerSecond;
        public int Capacity;
        public float Spread, SpeedMultiplier = 1;
        public List<Spell> AlwaysCast = new List<Spell>();
        public List<Spell> Slots = new List<Spell>();
    }

    /// <summary>One shot: the projectiles fired together and the config they share (modifiers act on all of them).</summary>
    public sealed class Shot
    {
        public readonly List<ShotProjectile> Projectiles = new List<ShotProjectile>();
        public readonly Dictionary<string, float> Config = new Dictionary<string, float>();
        public float Get(string key) => Config.TryGetValue(key, out var v) ? v : 0;
    }

    public sealed class ShotProjectile
    {
        public string File;
        public Trigger Trigger;                // null for a plain projectile
        public Shot Payload;                   // what the trigger releases
    }

    public sealed class CastResult
    {
        public Shot Shot;
        public readonly List<string> Played = new List<string>();   // spell ids, in play order (payloads included)
        public float CastDelay;                // frames until the next cast if no recharge
        public float Recharge;                 // frames of recharge started by this cast (0 = none)
        public bool Recharged => Recharge > 0 || Wrapped;
        public bool Wrapped;                   // the deck ran out mid-cast and wrapped to the start
        public float Wait => Math.Max(CastDelay, Recharge);
    }

    /// <summary>
    /// Noita's deck/hand/discard cast loop (data/scripts/gun/gun.lua in the player's Noita) for one held wand.
    /// Cards are drawn from the top of the deck; spells that cannot be paid or have no uses left are discarded
    /// and the next card is tried; when the deck runs out mid-cast it wraps (discards back on top) and the wand
    /// recharges after the cast. Pure logic: the game side turns Shot into Terraria projectiles.
    /// </summary>
    public sealed class Gun
    {
        public readonly Wand Wand;
        public float Mana;
        public float Cooldown;                 // frames until the wand can cast again

        sealed class Card { public Spell Spell; public int Index; public int Uses; }

        readonly List<Card> _deck = new List<Card>(), _hand = new List<Card>(), _discarded = new List<Card>();
        readonly Random _rng;
        Card[] _all;
        CastResult _result;
        bool _wrapped;

        public Gun(Wand wand, Random rng = null)
        {
            Wand = wand;
            _rng = rng ?? new Random();
            Mana = wand.ManaMax;
            Rebuild();
        }

        /// <summary>Call after changing the wand's slots: the deck starts over (uses remaining are kept per slot).</summary>
        public void Rebuild()
        {
            var old = _all;
            _all = Wand.Slots.Select((s, i) => s == null ? null : new Card
            {
                Spell = s, Index = i,
                Uses = old != null && i < old.Length && old[i]?.Spell == s ? old[i].Uses : s.MaxUses,
            }).Where(c => c != null).ToArray();
            _deck.Clear(); _hand.Clear(); _discarded.Clear();
            _deck.AddRange(_all);
            Order(_deck);
        }

        public IEnumerable<string> Deck => _deck.Select(c => c.Spell.Id);
        public int UsesLeft(int slot) => _all.FirstOrDefault(c => c.Index == slot)?.Uses ?? -1;

        /// <summary>Advance time: mana charges and the cooldown counts down.</summary>
        public void Tick(float frames = 1)
        {
            Mana = Math.Min(Wand.ManaMax, Mana + Wand.ManaChargePerSecond * frames / 60f);
            Cooldown = Math.Max(0, Cooldown - frames);
        }

        /// <summary>Cast once if the wand is ready; null while it is cooling down.</summary>
        public CastResult Cast()
        {
            if (Cooldown > 0)
                return null;
            _result = new CastResult { Shot = NewShot() };
            _wrapped = false;
            float reload = Wand.RechargeTime;
            foreach (var s in Wand.AlwaysCast)
                Play(new Card { Spell = s, Index = -1, Uses = -1 }, _result.Shot, ref reload, true);
            DrawMany(Wand.SpellsPerCast, _result.Shot, ref reload);

            _result.CastDelay = Math.Max(0, _result.Shot.Get("fire_rate_wait"));
            _result.Wrapped = _wrapped;
            if (_wrapped || _deck.Count == 0)
            {
                // recharge: everything goes back into the deck
                _result.Recharge = Math.Max(0, reload);
                _deck.AddRange(_hand); _deck.AddRange(_discarded);
                _hand.Clear(); _discarded.Clear();
                Order(_deck);
            }
            else
            {
                _discarded.AddRange(_hand);
                _hand.Clear();
            }
            Cooldown = _result.Wait;
            var r = _result;
            _result = null;
            return r;
        }

        Shot NewShot()
        {
            var s = new Shot();
            s.Config["fire_rate_wait"] = Wand.CastDelay;
            s.Config["spread_degrees"] = Wand.Spread;
            s.Config["speed_multiplier"] = Wand.SpeedMultiplier;
            return s;
        }

        void DrawMany(int n, Shot shot, ref float reload)
        {
            for (int i = 0; i < n; i++)
            {
                if (Draw(shot, ref reload))
                    continue;
                // that card was skipped (no mana / no uses): try the following ones
                while (_deck.Count > 0 && !Draw(shot, ref reload)) { }
            }
        }

        /// <summary>Draws the top card and plays it; false when it was skipped or nothing could be drawn.</summary>
        bool Draw(Shot shot, ref float reload)
        {
            if (_deck.Count == 0)
            {
                if (_discarded.Count == 0)
                    return false;
                _deck.AddRange(_discarded);
                _discarded.Clear();
                Order(_deck);
                _wrapped = true;
            }
            var card = _deck[0];
            _deck.RemoveAt(0);
            if (card.Uses == 0 || card.Spell.Mana > Mana)
            {
                _discarded.Add(card);
                return false;
            }
            Mana -= card.Spell.Mana;
            Play(card, shot, ref reload, false);
            return true;
        }

        void Play(Card card, Shot shot, ref float reload, bool alwaysCast)
        {
            var s = card.Spell;
            if (!alwaysCast)
            {
                _hand.Add(card);
                if (card.Uses > 0)
                    card.Uses--;
            }
            _result.Played.Add(s.Id);
            reload += s.ReloadAdd;
            foreach (var kv in s.ConfigAdd)
                shot.Config[kv.Key] = shot.Get(kv.Key) + kv.Value;
            foreach (var kv in s.ConfigMul)
                shot.Config[kv.Key] = (shot.Config.TryGetValue(kv.Key, out var v) ? v : 1) * kv.Value;
            foreach (var p in s.Projectiles)
                shot.Projectiles.Add(new ShotProjectile { File = p });
            foreach (var t in s.Triggers)
            {
                // the payload is a shot of its own; its cast delay changes count for the wand (to check against gun.lua)
                var payload = new Shot();
                DrawMany(t.Draws, payload, ref reload);
                if (payload.Config.TryGetValue("fire_rate_wait", out var fw))
                    shot.Config["fire_rate_wait"] = shot.Get("fire_rate_wait") + fw;
                shot.Projectiles.Add(new ShotProjectile { File = t.File, Trigger = t, Payload = payload });
            }
            if (s.Draws > 0)
                DrawMany(s.Draws, shot, ref reload);
        }

        void Order(List<Card> cards)
        {
            if (Wand.Shuffle)
            {
                for (int i = cards.Count - 1; i > 0; i--)
                {
                    int j = _rng.Next(i + 1);
                    (cards[i], cards[j]) = (cards[j], cards[i]);
                }
            }
            else
                cards.Sort((a, b) => a.Index.CompareTo(b.Index));
        }
    }
}
