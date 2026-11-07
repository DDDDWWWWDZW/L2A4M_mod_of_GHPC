using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Equipment;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leo2A4M
{
    /// <summary>
    /// ERA bypass for our rounds, following the M1A2C recipe: instead of hard-coding the ERA mod's
    /// schemas (PactIncreasedLethality only, and null during spawn), every ERA plate actually present
    /// in the scene contributes its own codex to the round's ArmorOptimizations. That covers any mod's
    /// ERA, including ones loaded after us.
    /// </summary>
    internal static class EraBypass
    {
        private static readonly Dictionary<string, HashSet<int>> Registered =
            new Dictionary<string, HashSet<int>>();
        private static FieldInfo _isEra;
        private static FieldInfo _armorType;
        private static float _lastScan;

        /// <summary>Round name -> how much of the ERA effect it keeps (1 = full).</summary>
        internal static readonly Dictionary<string, float> Ratios = new Dictionary<string, float>();

        private static void CacheFields()
        {
            if (_isEra == null)
                _isEra = typeof(UniformArmor).GetField("_isEra",
                    BindingFlags.Instance | BindingFlags.NonPublic);
            if (_armorType == null)
                _armorType = typeof(UniformArmor).GetField("_armorType",
                    BindingFlags.Instance | BindingFlags.NonPublic);
        }

        internal static void Populate(AmmoType ammo, float remaining)
        {
            if (ammo == null || remaining >= 0.999f)
                return;
            try
            {
                Ratios[ammo.Name] = remaining;
                ScanAmmo(ammo, remaining);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] era bypass populate failed: " + ex.Message);
            }
        }

        private static void ScanAmmo(AmmoType ammo, float remaining)
        {
            CacheFields();
            if (_isEra == null || _armorType == null)
                return;

            string id = (ammo.Name ?? "") + "|" + remaining.ToString("F2");
            HashSet<int> seen;
            if (!Registered.TryGetValue(id, out seen))
            {
                seen = new HashSet<int>();
                Registered[id] = seen;
            }

            var opts = ammo.ArmorOptimizations;
            var list = opts != null
                ? new List<AmmoType.ArmorOptimization>(opts)
                : new List<AmmoType.ArmorOptimization>();
            bool changed = false;

            foreach (var armor in UnityEngine.Object.FindObjectsByType<UniformArmor>(FindObjectsSortMode.None))
            {
                if (armor == null || armor.gameObject == null)
                    continue;
                bool era;
                try { era = (bool)_isEra.GetValue(armor); } catch (Exception) { continue; }
                if (!era)
                    continue;
                var codex = _armorType.GetValue(armor) as ArmorCodexScriptable;
                if (codex == null)
                    continue;
                int cid = codex.GetInstanceID();
                if (seen.Contains(cid))
                    continue;
                if (list.Any(o => o.Armor == codex))
                {
                    seen.Add(cid);
                    continue;
                }
                seen.Add(cid);
                list.Add(new AmmoType.ArmorOptimization { Armor = codex, RhaRatio = remaining });
                changed = true;
            }

            if (changed)
            {
                ammo.ArmorOptimizations = list.ToArray();
                MelonLogger.Msg("[Leo2A4M] era bypass: " + ammo.Name + " keeps " +
                    remaining.ToString("F2") + " of " + list.Count + " ERA codex(es)");
            }
        }

        internal static void Apply(LiveRound round)
        {
            try
            {
                if (round == null || round.Info == null)
                    return;
                string name = round.Info.Name ?? "";
                float remaining;
                if (!Ratios.TryGetValue(name, out remaining))
                    return;

                // Cheap throttle: once we know every ERA plate, a pass every half second is plenty.
                if (Time.realtimeSinceStartup - _lastScan < 0.5f)
                    return;
                _lastScan = Time.realtimeSinceStartup;
                ScanAmmo(round.Info, remaining);
            }
            catch (Exception) { }
        }
    }

    [HarmonyPatch(typeof(LiveRound), "penCheck")]
    internal static class EraBypassPenCheckPatch
    {
        private static void Prefix(LiveRound __instance)
        {
            EraBypass.Apply(__instance);
        }
    }
}
