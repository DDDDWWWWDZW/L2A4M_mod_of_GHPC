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
    ///   D           600 KE / 960 CE
    /// </summary>
    internal static class HullArmour
    {
        private const float BaseKe = 300f;
        private const float BaseCe = 600f;

        // The in-game readout includes angle/LOS effects, so the flat value written into the codex
        // does not equal the number the player sees.  Both factors below are measured back-to-front:
        //   CE: target 960 -> plate reads 960, so the CE factor was already right.
        //   KE: target 592 -> plate read 670, i.e. the flat value must be scaled DOWN by 670/592.
        // KeDisplayFactor is therefore the measured readout/flat ratio (0.968 * 670/592 = 1.0955),
        // which makes the readout land on the target for both C and D.
        private const float KeDisplayFactor = 1.0955f;
        private const float CeDisplayFactor = 1.0635f;

        private const float LevelCKe = 500f;    // readout target
        private const float LevelCCe = 850f;    // readout target
        private const float LevelDKe = 600f;    // readout target
        private const float LevelDCe = 960f;    // readout target

        internal static void Apply(Vehicle vic)
        {
            if (vic == null)
                return;

            string raw = Leo2A4MMod.HullArmourLevel != null
                ? (Leo2A4MMod.HullArmourLevel.Value ?? "")
                : "";
            string fromFile = Cfg.GetString("Hull Armour Level (Leo2A4M)", null);
            if (!string.IsNullOrEmpty(fromFile) && fromFile != raw)
                raw = fromFile;
            string level = Level(raw);
            if (level == "b")
            {
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

        /// <summary>
        /// Fallback for installs where MelonPreferences hands back the default instead of the stored
        /// value: read the key straight out of MelonPreferences.cfg.
        /// </summary>
        private static string ReadLevelFromFile()
        {
            try
            {
                string dir = MelonLoader.Utils.MelonEnvironment.UserDataDirectory;
                string file = System.IO.Path.Combine(dir, "MelonPreferences.cfg");
                if (!System.IO.File.Exists(file))
                    return null;
                foreach (var line in System.IO.File.ReadAllLines(file))
                {
                    if (line.IndexOf("Hull Armour Level (Leo2A4M)", StringComparison.Ordinal) < 0)
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq < 0)
                        continue;
                    string v = line.Substring(eq + 1).Trim();
                    int hash = v.IndexOf('#');
                    if (hash >= 0)
                        v = v.Substring(0, hash).Trim();
                    v = v.Trim('"', ' ');
                    return v.ToLowerInvariant();
                }
            }
            catch (Exception) { }
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
