using System;
using System.Reflection;
using GHPC.Equipment;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace Leo2A4M
{
    /// <summary>
    /// Configurable protection level for the hull front composite array.
    ///
    /// The donor's upper glacis is a <c>VariableArmor</c>: its real protection is
    /// <c>AverageRha (10) x codex multiplier</c>, and the donor plate uses the shared
    /// "Leo 2 hull array B tech" codex.  Changing a shared ScriptableObject would also change every
    /// other LEO2A3 in the mission, so this clones the ArmorType for our vehicle only and writes
    /// multipliers that produce the requested KE/CE.
    ///
    ///   B (default) 300 KE / 600 CE - the donor, left completely untouched
    ///   C           512 KE / 850 CE
    ///   D           592 KE / 960 CE
    /// </summary>
    internal static class HullArmour
    {
        private const float BaseKe = 300f;
        private const float BaseCe = 600f;

        // The in-game readout includes angle/LOS effects, so a plate whose flat value is X shows
        // slightly less for KE and slightly more for CE at the ~50 deg impact used for measuring:
        //   KE: flat 500 -> reads 484   (0.968)
        //   CE: flat 850 -> reads 904   (1.0635)
        // The level numbers below are what the readout should show, so they are divided by these
        // factors to get the flat values written into the codex.
        private const float KeDisplayFactor = 0.968f;
        private const float CeDisplayFactor = 1.0635f;

        private const float LevelCKe = 500f;    // readout target
        private const float LevelCCe = 850f;    // readout target
        private const float LevelDKe = 592f;    // readout target
        private const float LevelDCe = 960f;    // readout target

        internal static void Apply(Vehicle vic)
        {
            if (vic == null)
                return;

            string level = Level(Leo2A4MMod.HullArmourLevel != null
                ? Leo2A4MMod.HullArmourLevel.Value
                : "b");
            if (level == "b")
            {
                MelonLogger.Msg("[Leo2A4M] hull armour: level B (donor values untouched)");
                return;
            }

            // the constants are readout targets; convert them to flat (normal incidence) values
            float keTarget = level == "d" ? LevelDKe : LevelCKe;
            float ceTarget = level == "d" ? LevelDCe : LevelCCe;
            float ke = keTarget / KeDisplayFactor;
            float ce = ceTarget / CeDisplayFactor;

            var plate = FindPlate(vic, "upper glacis array");
            if (plate == null)
            {
                MelonLogger.Warning("[Leo2A4M] hull armour: upper glacis array not found");
                return;
            }

            try
            {
                var donorCodex = ReadObject(plate, "_armorType") as ArmorCodexScriptable;
                ArmorType donorType = donorCodex != null ? donorCodex.ArmorType : null;
                if (donorType == null)
                {
                    MelonLogger.Warning("[Leo2A4M] hull armour: plate has no ArmorType");
                    return;
                }

                // The engine multiplies the plate's GEOMETRIC thickness by these multipliers, so the
                // multiplier has to be scaled by target/baseline - writing target/AverageRha instead
                // produced absurd values (24544 mm).
                float ratioKe = ke / BaseKe;
                float ratioCe = ce / BaseCe;

                var at = new ArmorType();
                AmmoKit.ShallowCopy(at, donorType);
                at.Name = "Leo 2 hull array " + level.ToUpperInvariant() + " tech (Leo2A4M)";
                at.ThicknessSource = ArmorType.RhaSource.Multipliers;
                at.RhaeMultiplierKe = donorType.RhaeMultiplierKe * ratioKe;
                at.RhaeMultiplierCe = donorType.RhaeMultiplierCe * ratioCe;

                var codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
                codex.name = at.Name;
                codex.ArmorType = at;
                WriteObject(plate, "_armorType", codex);

                MelonLogger.Msg("[Leo2A4M] hull armour: level " + level.ToUpperInvariant() +
                    " -> readout target " + keTarget.ToString("F0") + " KE / " + ceTarget.ToString("F0") +
                    " CE, flat " + ke.ToString("F0") + "/" + ce.ToString("F0") + " (mult " +
                    at.RhaeMultiplierKe.ToString("F2") + "/" + at.RhaeMultiplierCe.ToString("F2") +
                    " from donor " + donorType.RhaeMultiplierKe.ToString("F3") + "/" + donorType.RhaeMultiplierCe.ToString("F3") + ")");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] hull armour failed: " + ex.Message);
            }
        }

private static MonoBehaviour FindPlate(Vehicle vic, string name)
        {
            foreach (var mb in vic.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null)
                    continue;
                if (mb.GetType().Name != "VariableArmor")
                    continue;
                if (string.Equals(mb.gameObject.name, name, StringComparison.OrdinalIgnoreCase))
                    return mb;
            }
            return null;
        }

        private static float ReadFloat(object target, string name)
        {
            var f = Field(target, name);
            return f != null ? Convert.ToSingle(f.GetValue(target)) : 0f;
        }

        private static object ReadObject(object target, string name)
        {
            var f = Field(target, name);
            return f != null ? f.GetValue(target) : null;
        }

        private static void WriteObject(object target, string name, object value)
        {
            var f = Field(target, name);
            if (f != null)
                f.SetValue(target, value);
        }

        private static FieldInfo Field(object target, string name)
        {
            for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                                         BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (f != null)
                    return f;
            }
            return null;
        }

        private static string Level(string value)
        {
            string v = (value ?? "").Trim().ToLowerInvariant();
            if (v.StartsWith("c")) return "c";
            if (v.StartsWith("d")) return "d";
            return "b";
        }
    }
}
