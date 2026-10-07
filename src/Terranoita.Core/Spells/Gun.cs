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
    /// and the next card is tried. The wand's own draws (spells per cast) stop when the deck runs out; draws made by
    /// spells (modifiers, multicasts, trigger payloads) wrap: the discards go back into the deck and the wand
    /// recharges after the cast (gun.lua draw_action, instant_reload_if_empty). The recharge time is kept across
    /// casts until a recharge (current_reload_time). Uses are spent at the end of the cast, only if it fired
    /// something or the spell is other/utility (move_hand_to_discarded); a spell with no uses left leaves the deck.
    /// Always-cast spells are free and a modifier's extra draw does not happen for them (_play_permanent_card).
    /// Pure logic: the game side turns Shot into Terraria projectiles.
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
        bool _wrapped, _reloading, _gotProjectiles;
        float _reload;                         // gun.lua current_reload_time: spells add to it until the next recharge

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
            _deck.AddRange(_all.Where(c => c.Uses != 0));
            Order(_deck);
            _reload = Wand.RechargeTime;
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
            _wrapped = _reloading = _gotProjectiles = false;
            foreach (var s in Wand.AlwaysCast)
            {
                if (s.Mana < 0)
                    Mana -= s.Mana;            // handle_mana_addition: only mana-giving always-casts touch mana
                Play(new Card { Spell = s, Index = -1, Uses = -1 }, _result.Shot, true);
            }
            DrawMany(Wand.SpellsPerCast, _result.Shot, false);

            _result.CastDelay = Math.Max(0, _result.Shot.Get("fire_rate_wait"));
            _result.Wrapped = _wrapped;
            // move_hand_to_discarded: uses are spent now; a spell with none left is not put back
            foreach (var card in _hand)
            {
                bool spends = _gotProjectiles || card.Spell.Type == "other" || card.Spell.Type == "utility";
                if (spends && card.Uses > 0)
                    card.Uses--;
                if (card.Uses != 0)
                    _discarded.Add(card);
            }
            _hand.Clear();
            // the deck ran out (the wand's own draw found it empty, or it is empty now) or a spell's draw wrapped
            if (_wrapped || _reloading || _deck.Count == 0)
            {
                _result.Recharge = Math.Max(0, _reload);
                _deck.AddRange(_discarded);
                _discarded.Clear();
                Order(_deck);
                _reload = Wand.RechargeTime;
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

        /// <summary>gun.lua draw_actions: wrap = instant_reload_if_empty (true for draws made by spells).</summary>
        void DrawMany(int n, Shot shot, bool wrap)
        {
            for (int i = 0; i < n; i++)
            {
                if (!Draw(shot, wrap))
                {
                    // that card was skipped (no mana / no uses): try the following ones
                    while (_deck.Count > 0 && !Draw(shot, wrap)) { }
                }
                if (_reloading)
                    return;
            }
        }

        /// <summary>Draws the top card and plays it; false when it was skipped.</summary>
        bool Draw(Shot shot, bool wrap)
        {
            if (_deck.Count == 0)
            {
                if (!wrap)
                {
                    _reloading = true;         // the wand's own draw: the cast ends here
                    return true;
                }
                _deck.AddRange(_discarded);
                _discarded.Clear();
                Order(_deck);
                _wrapped = true;
                if (_deck.Count == 0)
                    return true;
            }
            var card = _deck[0];
            _deck.RemoveAt(0);
            if (card.Uses == 0 || card.Spell.Mana > Mana)
            {
                _discarded.Add(card);
                return false;
            }
            Mana -= card.Spell.Mana;
            Play(card, shot, false);
            return true;
        }

        void Play(Card card, Shot shot, bool alwaysCast)
        {
            var s = card.Spell;
            if (!alwaysCast)
                _hand.Add(card);
            if (s.Type == "projectile" || s.Type == "static_projectile" || s.Type == "material")
                _gotProjectiles = true;
            _result.Played.Add(s.Id);
            _reload += s.ReloadAdd;
            foreach (var kv in s.ConfigAdd)
                shot.Config[kv.Key] = shot.Get(kv.Key) + kv.Value;
            foreach (var kv in s.ConfigMul)
                shot.Config[kv.Key] = (shot.Config.TryGetValue(kv.Key, out var v) ? v : 1) * kv.Value;
            foreach (var p in s.Projectiles)
                shot.Projectiles.Add(new ShotProjectile { File = p });
            foreach (var t in s.Triggers)
            {
                // the payload is a shot of its own (gun.lua draw_shot(create_shot(n), true)); its cast delay changes count
                // for the wand: gun.lua passes every shot's state to the game, which adds them up (as players know it)
                var payload = new Shot();
                DrawMany(t.Draws, payload, true);
                if (payload.Config.TryGetValue("fire_rate_wait", out var fw))
                    shot.Config["fire_rate_wait"] = shot.Get("fire_rate_wait") + fw;
                shot.Projectiles.Add(new ShotProjectile { File = t.File, Trigger = t, Payload = payload });
            }
            // SPECIAL RULE of gun.lua: an always-cast modifier's draw_actions(1) draws nothing
            if (s.Draws > 0 && !(alwaysCast && s.Draws == 1))
                DrawMany(s.Draws, shot, true);
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
