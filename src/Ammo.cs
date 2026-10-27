using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Effects;
using GHPC.Weapons;
using GHPC.Weaponry;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace Leo2A4M
{
    /// <summary>
    /// Configurable 120 mm ammunition for the 2A4M.
    ///
    /// Rounds are built by shallow-copying the vanilla German rounds the donor already carries
    /// (DM23 for the kinetic side, DM12A1 for the chemical side) and overriding only the numbers the
    /// design asks for 鈥?everything else therefore matches the vanilla behaviour exactly.
    ///
    ///   AP (selectable):  DM43 / DM53 / KEW-A1 (default)
    ///   HEAT:             vanilla DM12A1, used as-is
    ///
    /// DM53 additionally keeps 70 % of the effect of the Kontakt-5 / Relikt ERA schema (i.e. ignores
    /// 30 % of it), the same mechanism the M1A2C mod uses for the M829 series.
    /// </summary>
    internal static class AmmoKit
    {
        private static AmmoType _donorAp;
        private static AmmoType _donorHeat;

        internal static AmmoType ApType;
        internal static AmmoType HeatType;

        internal static AmmoType Dm43Type;
        internal static AmmoType Dm53Type;
        internal static AmmoType KewA1Type;

        private static bool _applied;

        /// <summary>
        /// Retunes the vanilla 120 mm AP round in place.  Nothing else is touched: the ammo racks,
        /// their counts, the visible round models and the feeding logic all stay vanilla, which is
        /// the only way the numbers and the models keep matching.
        /// </summary>
        internal static void Apply(Vehicle vic)
        {
            var lm = vic != null ? vic.GetComponent<LoadoutManager>() : null;
            if (lm == null)
            {
                MelonLogger.Warning("[Leo2A4M] ammo: no LoadoutManager");
                return;
            }
            if (_applied)
                return;
            _applied = true;

            try
            {
                FindDonors(lm);
                if (_donorAp == null)
                {
                    MelonLogger.Warning("[Leo2A4M] ammo: vanilla AP round not found, keeping defaults");
                    return;
                }

                string sel = Pick(Leo2A4MMod.ApAmmo != null ? Leo2A4MMod.ApAmmo.Value : "kewa1");
                string name;
                float pen, vel, mass, spall;
                float eraRatio = 1f;
                switch (sel)
                {
                    case "dm43":
                        name = "DM43 APFSDS-T"; pen = 580f; vel = 1700f; mass = 4f; spall = 1.0f;
                        break;
                    case "dm53":
                        name = "DM53 APFSDS-T"; pen = 650f; vel = 1670f; mass = 5f; spall = 1.15f;
                        eraRatio = 0.70f;
                        break;
                    default:
                        name = "KEW-A1 APFSDS-T"; pen = 618f; vel = 1740f; mass = 4f; spall = -1f;
                        break;
                }

                _donorAp.Name = name;
                _donorAp.RhaPenetration = pen;
                _donorAp.MuzzleVelocity = vel;
                _donorAp.Mass = mass;
                if (spall > 0f)
                    _donorAp.SpallMultiplier = spall;
                ApplyEraRatio(_donorAp, eraRatio);

                MelonLogger.Msg("[Leo2A4M] ammo: vanilla AP round retuned -> " + _donorAp.Name +
                    " (" + pen.ToString("F0") + " mm, " + vel.ToString("F0") + " m/s, " +
                    mass.ToString("F1") + " kg); racks untouched, counts stay vanilla");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] ammo failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Finds the vanilla AP / HEAT rounds from the loadout the donor already carries.  HEAT is
        /// only needed to tell the two apart; nothing is written to it.
        /// </summary>
        private static void FindDonors(LoadoutManager lm)
        {
            var clips = lm.LoadedAmmoList != null ? lm.LoadedAmmoList.AmmoClips : null;
            if (clips != null)
            {
                foreach (var cc in clips)
                {
                    var type = TypeOf(cc);
                    if (type == null)
                        continue;
                    if (_donorAp == null && !IsHeatAmmo(type))
                        _donorAp = type;
                    else if (_donorHeat == null && IsHeatAmmo(type))
                        _donorHeat = type;
                }
            }

            if (_donorAp != null)
                return;

            foreach (var codex in Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>())
            {
                if (codex == null || codex.AmmoType == null || IsHeatAmmo(codex.AmmoType))
                    continue;
                if (codex.name.IndexOf("DM23", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _donorAp = codex.AmmoType;
                    break;
                }
            }
        }

        /// <summary>ratio &lt; 1 means part of the ERA schema effect is bypassed (e.g. DM53).</summary>
        private static void ApplyEraRatio(AmmoType type, float ratio)
        {
            if (type == null || ratio >= 0.999f)
                return;
            try
            {
                const string assembly = "PactIncreasedLethality";
                var schemaType = Type.GetType(Assembly.CreateQualifiedName(assembly,
                    "PactIncreasedLethality.EraSchema"));
                var k5Type = Type.GetType(Assembly.CreateQualifiedName(assembly,
                    "PactIncreasedLethality.Kontakt5"));
                var reliktType = Type.GetType(Assembly.CreateQualifiedName(assembly,
                    "PactIncreasedLethality.Relikt"));
                if (schemaType == null || k5Type == null || reliktType == null)
                    return;

                var eraSo = schemaType.GetField("era_so", System.Reflection.BindingFlags.Public |
                                                         System.Reflection.BindingFlags.Instance);
                if (eraSo == null)
                    return;

                Func<Type, ArmorCodexScriptable> getCodex = t2 =>
                    (ArmorCodexScriptable)eraSo.GetValue(t2.GetField("schema",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)
                        .GetValue(null));

                type.ArmorOptimizations = new[]
                {
                    new AmmoType.ArmorOptimization { Armor = getCodex(k5Type), RhaRatio = ratio },
                    new AmmoType.ArmorOptimization { Armor = getCodex(reliktType), RhaRatio = ratio }
                };
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] ERA optimization unavailable: " + ex.Message);
            }
        }
        /// <summary>HEAT: slow, high explosive content.  KE: everything else (APFSDS/APDS).</summary>
        private static bool IsHeatAmmo(AmmoType type)
        {
            if (type == null)
                return false;
            return type.MuzzleVelocity < 1300f || type.TntEquivalentKg > 0.1f;
        }

        private static AmmoType TypeOf(AmmoClipCodexScriptable cc)
        {
            try
            {
                var clip = cc != null ? cc.ClipType : null;
                if (clip == null || clip.MinimalPattern == null || clip.MinimalPattern.Length == 0)
                    return null;
                var codex = clip.MinimalPattern[0];
                return codex != null ? codex.AmmoType : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string Pick(string value)
        {
            string v = (value ?? "").Replace(" ", "").Replace("-", "").Replace("_", "").ToLowerInvariant();
            if (v == "dm43") return "dm43";
            if (v == "dm53") return "dm53";
            return "kewa1";
        }

        private static AmmoType ClipAmmoType(AmmoType.AmmoClip clip)
        {
            try
            {
                if (clip == null || clip.MinimalPattern == null || clip.MinimalPattern.Length == 0)
                    return null;
                var codex = clip.MinimalPattern[0];
                return codex != null ? codex.AmmoType : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Empties a rack and clears its slot bookkeeping (mirrors M1A2C's Util.EmptyRack).</summary>
        private static void EmptyRack(GHPC.Weapons.AmmoRack rack)
        {
            try
            {
                // The rack owns its visible round models.  Clearing only the clip list leaves the
                // old models in their slots, so the racks keep showing rounds that no longer exist
                // (and never lose them when firing).  Remove the visuals first, then the data.
                var remove = typeof(GHPC.Weapons.AmmoRack).GetMethod("RemoveAmmoVisualFromSlot",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (remove != null && rack.VisualSlots != null)
                {
                    for (int i = 0; i < rack.VisualSlots.Count; i++)
                    {
                        try { remove.Invoke(rack, new object[] { (byte)i }); }
                        catch (Exception) { }
                    }
                }

                var stored = typeof(GHPC.Weapons.AmmoRack).GetProperty("StoredClips");
                if (stored != null && stored.CanWrite)
                    stored.SetValue(rack, new System.Collections.Generic.List<AmmoType.AmmoClip>());
                else if (rack.StoredClips != null)
                    rack.StoredClips.Clear();

                var slots = typeof(GHPC.Weapons.AmmoRack).GetField("SlotIndicesByAmmoType",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
                if (slots != null)
                    slots.SetValue(rack, Activator.CreateInstance(slots.FieldType));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] empty rack: " + ex.Message);
            }
        }

internal static void ShallowCopy(object dest, object src)
        {
            if (dest == null || src == null)
                return;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
                                       BindingFlags.Public | BindingFlags.NonPublic;
            var destFields = dest.GetType().GetFields(flags);
            foreach (var srcField in src.GetType().GetFields(flags))
            {
                FieldInfo destField = null;
                foreach (var f in destFields)
                {
                    if (f.Name == srcField.Name)
                    {
                        destField = f;
                        break;
                    }
                }
                if (destField == null || destField.IsLiteral || srcField.FieldType != destField.FieldType)
                    continue;
                destField.SetValue(dest, srcField.GetValue(src));
            }
        }

        private static void CacheAmmo(AmmoType ammo)
        {
            if (ammo == null)
                return;
            try
            {
                var mgr = ImpactDecalsManager.Instance;
                if (mgr == null || mgr._ImpactDecalsScriptable == null)
                    return;
                int id;
                mgr._ImpactDecalsScriptable.CacheNewData(ammo, out id);
                ammo.CachedIndex = id;
            }
            catch (Exception)
            {
            }
        }

    }
}
