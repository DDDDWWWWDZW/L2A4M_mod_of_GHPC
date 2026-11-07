using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GHPC;
using GHPC.AI;
using GHPC.Camera;
using GHPC.Equipment;
using GHPC.Equipment.Optics;
using GHPC.Mission;
using GHPC.Mission.Data;
using GHPC.Player;
using GHPC.State;
using GHPC.Utility;
using GHPC.Vehicle;
using GHPC.Weapons;
using GHPC.Weaponry;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(Leo2A4M.Leo2A4MMod), "Leopard 2A4M (WT visual swap)", "1.9.4", "Leo2A4M")]
[assembly: MelonGame("Radian Simulations LLC", "GHPC")]

namespace Leo2A4M
{
    public class Leo2A4MConverted : MonoBehaviour { }

    /// <summary>
    /// The auxiliary gunner sight keeps its vanilla camera position, which ends up inside the WT
    /// turret shell - the view is a black wall.  While such a slot is active the shell renderers are
    /// switched off (the vanilla interior stays visible) and restored on exit.
    /// </summary>
    public class SightShellHider : MonoBehaviour
    {
        private readonly List<Renderer> _shell = new List<Renderer>();
        private Vehicle _vic;
        private bool _hidden;

        public void Setup(Vehicle vic)
        {
            _vic = vic;
            _shell.Clear();
            foreach (var r in vic.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null)
                    continue;
                bool ours = false;
                for (var t = r.transform; t != null && t != vic.transform; t = t.parent)
                {
                    if (t.name.StartsWith("Leo2A4M ", StringComparison.Ordinal))
                    {
                        ours = true;
                        break;
                    }
                }
                if (ours)
                    _shell.Add(r);
            }
        }

        private void Update()
        {
            // The camera slot belongs to the PLAYER, and every L2A4M in the scene carries this
            // component: without the player check, every enemy 2A4M hid its shell whenever the
            // player sat in an auxiliary sight - which looked like "the enemy model failed to load".
            var player = PlayerInput.Instance != null ? PlayerInput.Instance.CurrentPlayerUnit : null;
            if (player == null || player != (GHPC.Unit)_vic)
            {
                if (_hidden)
                {
                    _hidden = false;
                    foreach (var r in _shell)
                        if (r != null)
                            r.enabled = true;
                }
                return;
            }

            var slot = CameraManager.Instance != null ? CameraManager.Instance._currentCamSlot : null;
            bool hide = slot != null && IsAuxSight(slot);
            if (hide == _hidden)
                return;
            _hidden = hide;
            foreach (var r in _shell)
                if (r != null)
                    r.enabled = !hide;
        }

        private static string _lastSlotName;

        private static bool IsAuxSight(CameraSlot slot)
        {
            string path = "";
            for (var t = slot.transform; t != null; t = t.parent)
                path = t.name + "/" + path;
            if (path != _lastSlotName)
            {
                _lastSlotName = path;
        
            }

            bool mainSight =
                path.IndexOf("GPS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("FLIR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("NODs", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("CITV", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("Commander", StringComparison.OrdinalIgnoreCase) >= 0;
            if (mainSight)
                return false;

            return path.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    /// <summary>
    /// Snapshot/restore guard for armour pieces.
    ///
    /// The Fulda 1989 mod retunes the armour of whichever Leopard it locks onto: it swaps the piece's
    /// ArmorCodexScriptable for its own "C-Tech composite" type and replaces the AarVisual material,
    /// which is what makes our 2A4M show the 2A4's armour interior in the AAR view.  Because it
    /// modifies existing components instead of creating new ones, the guard records the state right
    /// after our conversion and keeps putting it back whenever it changes.
    /// </summary>
    public class ForeignArmourGuard : MonoBehaviour
    {
        private Vehicle _vic;
        private float _timer;

        private readonly List<MonoBehaviour> _pieces = new List<MonoBehaviour>();
        private readonly List<object> _codex = new List<object>();
        private readonly List<Component> _aar = new List<Component>();
        private readonly List<object> _aarMaterial = new List<object>();
        private readonly List<float[]> _numbers = new List<float[]>();
        private readonly List<string> _names = new List<string>();
        private readonly List<Component> _extraAar = new List<Component>();
        private readonly List<object> _extraAarMat = new List<object>();
        private int _restored;

        public void Setup(Vehicle vic)
        {
            _vic = vic;
            // Snapshot only.  Writing armour/ammo state during the spawn itself can hang the mission
            // load, so the first sweep waits for the next tick.
            Snapshot();
            _timer = 0f;
            LogCodexNames();
        }

        private void LogCodexNames()
        {
            var seen = new List<string>();
            foreach (var c in _codex)
            {
                string d = c == null ? "<null>" : DescribeCodex(c);
                if (!seen.Contains(d))
                    seen.Add(d);
            }
                    }
        private void Snapshot()
        {
            _pieces.Clear();
            _codex.Clear();
            _aar.Clear();
            _aarMaterial.Clear();
            _numbers.Clear();
            _names.Clear();
            _extraAar.Clear();
            _extraAarMat.Clear();
            if (_vic == null)
                return;

            foreach (var mb in _vic.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null)
                    continue;
                var tn = mb.GetType().Name;
                if (tn != "UniformArmor" && tn != "VariableArmor")
                    continue;

                _pieces.Add(mb);
                var codex = GetFieldValue(mb, "_armorType");
                // A snapshot taken after Fulda already converted this plate would preserve the
                // foreign codex forever - record null so the sweep substitutes a vanilla one.
                _codex.Add(IsForeignCodex(codex) ? null : codex);
                _numbers.Add(CaptureNumbers(mb));
                _names.Add(GetFieldValue(mb, "_name") as string);

                var aar = FindAarVisual(mb.gameObject);
                _aar.Add(aar);
                _aarMaterial.Add(aar != null ? GetFieldValue(aar, "AarMaterial") ?? GetFieldValue(aar, "aarMaterial") : null);

                // The AAR/x-ray copy of a plate lives on a child object; Fulda recolours those too.
                foreach (var child in mb.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (child == null || child.GetType().Name != "AarVisual" || child == aar)
                        continue;
                    var mat = GetFieldValue(child, "AarMaterial") ?? GetFieldValue(child, "aarMaterial");
                    if (mat == null)
                        continue;
                    _extraAar.Add(child);
                    _extraAarMat.Add(mat);
                }
            }
        }

        private static bool IsNaN(float v) { return float.IsNaN(v); }

        private static float[] CaptureNumbers(MonoBehaviour mb)
        {
            var a = GetFieldValue(mb, "PrimarySabotRha") ?? GetFieldValue(mb, "_primarySabotRha");
            var b = GetFieldValue(mb, "PrimaryHeatRha") ?? GetFieldValue(mb, "_primaryHeatRha");
            return new[]
            {
                a is float fa ? fa : float.NaN,
                b is float fb ? fb : float.NaN
            };
        }


        /// <summary>
        /// Fulda rewrites the vehicle loadout (its DM33).  We do not trust a snapshot for this: if
        /// Fulda ran first the snapshot would already be DM33.  The canonical rounds are the vanilla
        /// 120 mm clips, located in the loaded resources by AmmoKit.
        /// </summary>
        private int RestoreLoadout()
        {
            // Disabled on purpose.  Even a "correct" clip written into LoadedAmmoList hangs the
            // mission load when the gun does not accept that clip, and it also fights the vanilla
            // loadout the game just built.  Ammunition is handled the safe way instead: AmmoKit only
            // retunes the round the vehicle already carries.
            return 0;
        }

        private void Update()
        {
            if (_vic == null)
                return;
            _timer += Time.unscaledDeltaTime;
            // Fulda works from a coroutine, so keep watching for the whole mission.
            if (_timer < 1f)
                return;
            _timer = 0f;
            Sweep();
        }

        private int Sweep()
        {
            int restored = 0;
            try
            {
                for (int i = 0; i < _pieces.Count; i++)
                {
                    var mb = _pieces[i];
                    if (mb == null)
                        continue;

                    // 1) armour codex swapped (C-Tech composite and friends)
                    object current = GetFieldValue(mb, "_armorType");
                    object original = _codex[i];
                    if (original == null)
                    {
                        // the plate was already foreign when we snapshotted: substitute a vanilla
                        // codex for its family and remember it as the new baseline
                        original = CanonicalCodexFor(mb);
                        _codex[i] = original;
                    }
                    else if (IsForeignCodex(original))
                    {
                        original = CanonicalCodexFor(mb);
                        _codex[i] = original;
                    }
                    if (original != null && !ReferenceEquals(current, original))
                    {
                        SetFieldValue(mb, "_armorType", original);
                        restored++;
                    }

                    // 2) AAR material swapped on the visible interior copy
                    var aar = _aar[i];
                    if (aar != null)
                    {
                        object mat = GetFieldValue(aar, "AarMaterial") ?? GetFieldValue(aar, "aarMaterial");
                        object originalMat = _aarMaterial[i];
                        if (originalMat != null && !ReferenceEquals(mat, originalMat))
                        {
                            if (!SetFieldValue(aar, "AarMaterial", originalMat))
                                SetFieldValue(aar, "aarMaterial", originalMat);
                            restored++;
                        }
                    }

                    // 3) protection numbers rewritten in place
                    var want = _numbers[i];
                    if (want != null)
                    {
                        var cur = CaptureNumbers(mb);
                        if (!IsNaN(want[0]) && Mathf.Abs(cur[0] - want[0]) > 0.01f)
                        {
                            if (!SetFieldValue(mb, "PrimarySabotRha", want[0]))
                                SetFieldValue(mb, "_primarySabotRha", want[0]);
                            restored++;
                        }
                        if (!IsNaN(want[1]) && Mathf.Abs(cur[1] - want[1]) > 0.01f)
                        {
                            if (!SetFieldValue(mb, "PrimaryHeatRha", want[1]))
                                SetFieldValue(mb, "_primaryHeatRha", want[1]);
                            restored++;
                        }
                    }

                    // 4) display name - this is the string the shot report prints ("C-Tech
                    // composite"), and it is a separate field from the codex
                    if (i < _names.Count)
                    {
                        var wantName = _names[i];
                        var curName = GetFieldValue(mb, "_name") as string;
                        if (!string.IsNullOrEmpty(wantName) && curName != wantName)
                        {
                            if (SetFieldValue(mb, "_name", wantName))
                                restored++;
                        }
                    }
                }

                // 4) AAR copies that live on child objects
                for (int i = 0; i < _extraAar.Count; i++)
                {
                    var c = _extraAar[i];
                    if (c == null || i >= _extraAarMat.Count)
                        continue;
                    var wantMat = _extraAarMat[i];
                    if (wantMat == null)
                        continue;
                    object cur = GetFieldValue(c, "AarMaterial") ?? GetFieldValue(c, "aarMaterial");
                    if (ReferenceEquals(cur, wantMat))
                        continue;
                    if (!SetFieldValue(c, "AarMaterial", wantMat))
                        SetFieldValue(c, "aarMaterial", wantMat);
                    restored++;
                }

                // 5) the loadout itself: Fulda also swaps the vehicle's ammunition for its DM33
                restored += RestoreLoadout();
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] armour guard: " + ex.Message);
            }

            if (restored > 0)
            {
                _restored += restored;
                MelonLogger.Msg("[Leo2A4M] armour guard: restored " + restored +
                    " piece(s) of " + (_vic != null ? _vic.name : "?") + ", total " + _restored);
            }
            return restored;
        }

        private static ArmorCodexScriptable _hullCodex;
        private static ArmorCodexScriptable _turretCodex;

        /// <summary>
        /// The asset name may not mention C-Tech at all - Fulda's marker lives in the ArmorType's
        /// display name ("C-Tech composite").  Check both, otherwise the snapshot is taken *after*
        /// the injection and the foreign codex is preserved as if it were the original.
        /// </summary>
        private static string DescribeCodex(object codex)
        {
            var so = codex as UnityEngine.Object;
            string s = so != null ? (so.name ?? "") : "";
            if (codex != null)
            {
                var at = GetFieldValue(codex, "ArmorType");
                if (at != null)
                {
                    object v = null;
                    var p = at.GetType().GetProperty("Name");
                    if (p != null)
                        v = p.GetValue(at);
                    if (v == null)
                        v = GetFieldValue(at, "Name") ?? GetFieldValue(at, "name");
                    if (v is string sv)
                        s += " | " + sv;
                }
            }
            return s;
        }

        private static bool IsForeignCodex(object codex)
        {
            if (codex == null)
                return false;
            string n = DescribeCodex(codex).ToLowerInvariant();
            // Fulda marks its pieces "C-Tech composite".  Do NOT match the bare "c tech": the
            // vanilla hull/turret arrays are named "Leo 2 hull array C tech", so a loose match made
            // the guard revert our own hull upgrade back to the B-tech donor values every second.
            if (n.Contains("leo2a4m"))
                return false;
            return n.Contains("c-tech") || n.Contains("ctech");
        }

        /// <summary>Vanilla Leo 2 array codex for the plate's family (never a Fulda one).</summary>
        private static object CanonicalCodexFor(MonoBehaviour mb)
        {
            if (_hullCodex == null || _turretCodex == null)
            {
                foreach (var c in Resources.FindObjectsOfTypeAll<ArmorCodexScriptable>())
                {
                    if (c == null || IsForeignCodex(c))
                        continue;
                    string n = (c.name ?? "").ToLowerInvariant();
                    if (_turretCodex == null && n.Contains("turret") && n.Contains("array"))
                        _turretCodex = c;
                    else if (_hullCodex == null && n.Contains("hull") && n.Contains("array"))
                        _hullCodex = c;
                }
            }

            string path = (mb.gameObject.name ?? "").ToLowerInvariant();
            for (var t = mb.transform.parent; t != null; t = t.parent)
                path = (t.name ?? "").ToLowerInvariant() + "/" + path;
            bool turret = path.Contains("turret") || path.Contains("cheek") || path.Contains("breech");
            var pick = turret ? _turretCodex : _hullCodex;
            return pick != null ? (object)pick : (turret ? _hullCodex : _turretCodex);
        }
        private static Component FindAarVisual(GameObject go)
        {
            foreach (var mb in go.GetComponents<MonoBehaviour>())
                if (mb != null && mb.GetType().Name == "AarVisual")
                    return mb;
            return null;
        }

        private static object GetFieldValue(object target, string name)
        {
            var f = Field(target, name);
            return f != null ? f.GetValue(target) : null;
        }

        private static bool SetFieldValue(object target, string name, object value)
        {
            var f = Field(target, name);
            if (f == null || value == null)
                return false;
            f.SetValue(target, value);
            return true;
        }

        private static readonly Dictionary<string, System.Reflection.FieldInfo> _fieldCache =
            new Dictionary<string, System.Reflection.FieldInfo>();

        /// <summary>
        /// Cached field lookup.  The guard touches ~156 pieces per vehicle per sweep, and the
        /// uncached GetField plus base-type walk dominated the cost of a sweep.
        /// </summary>
        private static System.Reflection.FieldInfo Field(object target, string name)
        {
            var type = target.GetType();
            string key = type.FullName + "|" + name;
            System.Reflection.FieldInfo cached;
            if (_fieldCache.TryGetValue(key, out cached))
                return cached;

            System.Reflection.FieldInfo found = null;
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                found = t.GetField(name,
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                if (found != null)
                    break;
            }
            _fieldCache[key] = found;
            return found;
        }
    }

    /// <summary>
    /// Reads a preference value straight out of MelonPreferences.cfg.  On this install
    /// MelonPreferences.Save() does not rewrite the file and Entry.Value can come back as the
    /// default, which made config edits look like they were ignored.
    /// </summary>
    internal static class Cfg
    {
        internal static string File_()
        {
            try
            {
                return System.IO.Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory,
                    "MelonPreferences.cfg");
            }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// MelonPreferences.cfg is shared by every installed mod and Tomlyn rejects the WHOLE file if
        /// one line is malformed - every mod then runs on defaults.  Repair the cheap case (a key with
        /// no value) and report duplicate keys so the owner of that section can be told.
        /// </summary>
        internal static void SanitizeFile()
        {
            try
            {
                string file = File_();
                if (file == null || !System.IO.File.Exists(file))
                    return;

                string[] lines = System.IO.File.ReadAllLines(file);
                bool changed = false;
                var dupes = new List<string>();
                string section = "<root>";
                var seen = new HashSet<string>();

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    string trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#", StringComparison.Ordinal))
                        continue;

                    if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
                    {
                        section = trimmed;
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq < 0)
                        continue;

                    string value = line.Substring(eq + 1).Trim();
                    if (value.Length == 0 || value.StartsWith("#", StringComparison.Ordinal))
                    {
                        lines[i] = line.Substring(0, eq + 1) + " \"\"";
                        changed = true;
                        MelonLogger.Warning("[Leo2A4M] cfg repair: line " + (i + 1) +
                            " had no value, set to empty string (" + trimmed + ")");
                        value = "\"\"";
                    }

                    string key = section + "|" + (line.Substring(0, eq).Trim().Trim('"'));
                    if (!seen.Add(key))
                        dupes.Add("line " + (i + 1) + " in " + section);
                }

                if (changed)
                {
                    string backup = file + ".bak-leo2a4m";
                    try { System.IO.File.Copy(file, backup, true); } catch (Exception) { }
                    System.IO.File.WriteAllLines(file, lines, new System.Text.UTF8Encoding(false));
                    MelonLogger.Msg("[Leo2A4M] cfg repaired (backup: " + backup + ")");
                }

                if (dupes.Count > 0)
                    MelonLogger.Warning("[Leo2A4M] cfg has duplicate keys (whole file fails to parse, " +
                        "every mod runs on defaults): " + string.Join("; ", dupes.ToArray()));
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] cfg sanitize failed: " + ex.Message);
            }
        }

        internal static string GetString(string key, string fallback)
        {
            try
            {
                string file = File_();
                if (file != null && System.IO.File.Exists(file))
                {
                    foreach (var line in System.IO.File.ReadAllLines(file))
                    {
                        if (line.IndexOf(key, StringComparison.Ordinal) < 0)
                            continue;
                        int eq = line.IndexOf('=');
                        if (eq < 0)
                            continue;
                        string v = line.Substring(eq + 1).Trim();
                        int hash = v.IndexOf('#');
                        if (hash >= 0)
                            v = v.Substring(0, hash).Trim();
                        v = v.Trim('"', ' ');
                        if (v.Length > 0)
                            return v;
                    }
                }
            }
            catch (Exception) { }
            return fallback;
        }

        internal static bool GetBool(string key, bool fallback)
        {
            string v = GetString(key, fallback ? "true" : "false").ToLowerInvariant();
            return v == "true" || v == "1" || v == "yes";
        }

        /// <summary>Appends any of our keys that are missing from the cfg file.</summary>
        internal static void EnsureKeys(params string[] keyValuePairs)
        {
            try
            {
                string file = File_();
                if (file == null || !System.IO.File.Exists(file))
                    return;
                string[] lines = System.IO.File.ReadAllLines(file);
                var missing = new List<string>();
                for (int i = 0; i + 1 < keyValuePairs.Length; i += 2)
                {
                    string key = keyValuePairs[i];
                    bool found = false;
                    foreach (var line in lines)
                        if (line.IndexOf(key, StringComparison.Ordinal) >= 0) { found = true; break; }
                    if (!found)
                        missing.Add("\"" + key + "\" = " + keyValuePairs[i + 1]);
                }
                if (missing.Count == 0)
                    return;

                var all = new List<string>(lines);
                int section = all.FindIndex(l => l.Trim() == "[Leo2A4M]");
                if (section < 0)
                {
                    all.Add("[Leo2A4M]");
                    section = all.Count - 1;
                }
                all.InsertRange(section + 1, missing);
                System.IO.File.WriteAllLines(file, all, new System.Text.UTF8Encoding(false));
                MelonLogger.Msg("[Leo2A4M] cfg: appended " + missing.Count + " missing key(s)");
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] cfg ensure failed: " + ex.Message);
            }
        }
    }

    public class Leo2A4MMod : MelonMod
    {
        internal const string UnitName = "LEO2A4M";
        internal const string FriendlyName = "Leopard 2A4M";
        private const string PartPrefix = "Leo2A4M ";

        // The 2A4M export is a different pose from the 2A5 one: its turret ring sits at
        // mesh z 0.342 (2A5: 0.379), so the shift that puts the ring on the GHPC pivot
        // (z 0.816, measured on LEO2A3) is 0.477.  BaseY stays -0.02 -- the road wheel
        // centre height is 0.4245 in both models.
        private static float BaseY = -0.02f + Lift;
        private static readonly float BaseZ = 0.477f + EnvF("LEO2A4M_SHIFTZ");

        private static float EnvF(string name)
        {
            float v;
            string s = Environment.GetEnvironmentVariable(name);
            return !string.IsNullOrEmpty(s) && float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f;
        }

        private static float Lift
        {
            get
            {
                string s = Environment.GetEnvironmentVariable("LEO2A4M_LIFT");
                float v;
                return !string.IsNullOrEmpty(s) && float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v) ? v : 0f;
            }
        }

        // Vanilla donors, best first. Fulda adds LEO2A3/LEO2A4 on top of LEO2A0.
        private static readonly string[] Donors = { "LEO2A3", "LEO2A2", "LEO2A1", "LEO2", "LEO2A0", "LEO2A4" };

        private static bool _registered;
        private static bool _registerRunning;
        private static bool _autoSpawned;
        private static int _spawnSerial = 1000;
        private static bool _assetsReady;
        private static string _donorName;
        private static Dictionary<string, Material> _materials;
        internal static Dictionary<string, Material> Materials { get { EnsureAssets(); return _materials; } }
        internal static string AssetRoot { get { return ModRoot; } }
        private static List<ObjPart> _parts;
        private static Dictionary<string, string> _anchors;

        private static bool _loggedUnits;

        internal static float HullX = 0f;
        internal static float HullY = 0f;
        internal static float HullZ = 0.08f;
        internal static float TurretX = 0f;
        internal static float TurretY = 0f;
        internal static float TurretZ = 0f;
        internal static float GunX = 0f;
        internal static float GunY = 0f;
        internal static float GunZ = 0f;
        internal static float ModelScale = 1f;
        internal static float HullScaleX = 1.06f;
        // ---- frozen tuning values (previously preferences) ----
        internal const bool  WtRunningGearOn      = false;   // use the WT wheels instead of the vanilla gear
        internal const bool  ShowVanillaGear      = true;    // keep the animated vanilla wheels/tracks
        internal const bool  TakeControlOnSpawn   = true;
        internal const bool  SmoothCitv           = true;
        internal const float CitvSensitivity      = 1.0f;    // 1 = vanilla pan speed
        internal const bool  RangeShowcaseSpawn   = false;   // spawn a spare 2A4M on ranges
        internal const bool  AmmoLoadoutRebuild   = false;   // rebuild racks with our round types

        internal static MelonPreferences_Entry<bool> CitvEnabled;
        internal static bool GunnerSightEnabled = true;
        internal static float GunnerSightX = 0f;
        internal static float GunnerSightY = 0.15f;
        internal static float GunnerSightZ = 0f;
        internal static bool CitvGen1 = false;
        internal static bool CitvUseBlit = false;
        internal static int CitvRes = 1280;
        internal static float CitvContrast = 2f;
        internal static float CitvExposure = 1.4f;
        internal static float CitvScanlines = 0f;
        internal static bool CitvRebuild = true;
        // false = the CITV looks around freely and only takes the turret over while RMB is held
        // (the M1A2C control model); true would slave the turret to the sight all the time.
        internal static bool CitvSlaveTurret = false;
        internal static float CitvOverrideComp = 1f;
        internal static float CitvBoreOffset = 0.55f;
        internal static float CitvPitchTrim = 0f;
        internal static float CitvGunBias = 0f;
        internal static bool CitvOpticFcsRange = true;
        internal static bool CitvOpticRotElev = false;
        internal static bool VerboseLog = false;
        internal static float CitvX = 0.55f;
        internal static float CitvY = 2.55f;
        internal static float CitvZ = 2.75f;
        internal static float GunSightX = 0.55f;
        internal static float GunSightY = 2.55f;
        internal static float GunSightZ = 2.75f;
        internal static bool GunnerGen2 = true;
        internal static float GunnerResScale = 2.5f;
        internal static float GunnerContrast = 1.9f;
        internal static float GunnerExposure = 1.5f;
        internal static float GunnerScanlines = 0f;

        // Texture look options consumed by ObjLoader (neutral by default).
        internal static MelonPreferences_Entry<string> PaintScheme;
        internal static MelonPreferences_Entry<string> HullArmourLevel;
        internal static MelonPreferences_Entry<bool> TurretAddonEnhanced;

        internal static MelonPreferences_Entry<string> ApAmmo;
        internal static float TextureSaturation = 1f;
        internal static float TextureMipBias = 0f;
        internal static float TextureBrightness = 1.15f;
        internal static float TextureContrast = 0.98f;
        internal static float TextureClean = 0f;
        internal static float TextureGreenStrength = 0f;

        private static string ModRoot
        {
            get { return Path.Combine(MelonEnvironment.ModsDirectory, "Leo2A4M"); }
        }

        /// Logs preferences that were never created. A duplicate identifier makes CreateEntry throw
        /// and silently leaves every later entry null, which then blows up further down (that is how
        /// the model conversion died in 0.1.49).
        private static void ReportMissingPrefs()
        {
            var missing = new List<string>();
            void Check(string name, object entry) { if (entry == null) missing.Add(name); }
            Check("CitvEnabled", CitvEnabled);
            Check("GunnerSightEnabled", GunnerSightEnabled);
            Check("GunnerSightX", GunnerSightX);
            if (missing.Count > 0)
                MelonLogger.Error("[Leo2A4M] preferences missing: " + string.Join(", ", missing) +
                    " - a duplicate identifier aborted every later CreateEntry");
        }

        public override void OnInitializeMelon()
        {
            if (SafeMode)
            {
                MelonLogger.Warning("[Leo2A4M] SAFE MODE: Harmony patches NOT applied");
                return;
            }
            HarmonyInstance.PatchAll();

            var cfg = MelonPreferences.CreateCategory("Leo2A4M");
            CitvEnabled = cfg.CreateEntry("CITV (Leo2A4M)", true);
            CitvEnabled.Comment = "Commander's independent thermal viewer (gen-1 imager)";













            PaintScheme = cfg.CreateEntry("Paint Scheme (Leo2A4M)", "Bronzegrun");
            HullArmourLevel = cfg.CreateEntry("Hull Armour Level (Leo2A4M)", "b");
            HullArmourLevel.Comment = "Choose the hull armour package: b / c / d. d is the strongest, b is the stock Leo 2A3 armour. Default: b";
            TurretAddonEnhanced = cfg.CreateEntry("Turret Add-on Armour (Leo2A4M)", false);
            TurretAddonEnhanced.Comment = "Adds extra protection to the turret front add-on armour. Default: false";
            ApAmmo = cfg.CreateEntry("AP Ammo (Leo2A4M)", "kewa1");
            ApAmmo.Comment = "kewa1 (608 mm), dm43 (562 mm), dm53 (668 mm, ignores 30% of ERA)";
            PaintScheme.Comment = "Bronzegrun (default), Sand, or Tricolor";

            // MelonPreferences keeps entries it read from the file even when the code no longer
            // creates them, and writes them back on exit.  Delete the retired tuning keys for good.
            try
            {
                string[] retired =
                {
                    "WT Running Gear (Leo2A4M)", "Vanilla Running Gear (Leo2A4M)",
                    "Take Control On Spawn (Leo2A4M)", "Smooth CITV Panning (Leo2A4M)",
                    "CITV Aim Sensitivity (Leo2A4M)", "CITV Free Look Key (Leo2A4M)",
                    "Range Showcase Spawn (Leo2A4M)", "Ammo Loadout Rebuild (Leo2A4M)",
                };
                var del = cfg.GetType().GetMethod("DeleteEntry");
                var get = cfg.GetType().GetMethod("GetEntry", new[] { typeof(string) });
                foreach (var key in retired)
                {
                    try
                    {
                        if (!cfg.HasEntry(key) || del == null)
                            continue;
                        var pt = del.GetParameters()[0].ParameterType;
                        object arg = pt == typeof(string)
                            ? (object)key
                            : get != null ? get.Invoke(cfg, new object[] { key }) : null;
                        if (arg != null)
                            del.Invoke(cfg, new[] { arg });
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }

            ReportMissingPrefs();
            MelonPreferences.Save();
            Cfg.SanitizeFile();
            Cfg.EnsureKeys(
                "Turret Add-on Armour (Leo2A4M)", "\"false\"",
                "Hull Armour Level (Leo2A4M)", "\"c\"",
                "AP Ammo (Leo2A4M)", "\"kewa1\"");


            MelonCoroutines.Start(RegisterRoutine());
            MelonLogger.Msg("[Leo2A4M] loaded, modRoot=" + ModRoot);
        }

        /// <summary>Set LEO2A4M_SAFE=1 in the environment to run the game without this mod's logic.</summary>
        private static bool SafeMode
        {
            get
            {
                try { return Environment.GetEnvironmentVariable("LEO2A4M_SAFE") == "1"; }
                catch (Exception) { return false; }
            }
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            if (SafeMode)
            {
                HarmonyInstance.UnpatchSelf();
                MelonLogger.Warning("[Leo2A4M] SAFE MODE: all patches removed, scene setup skipped");
                return;
            }

            MelonCoroutines.Start(RegisterRoutine());
            MelonCoroutines.Start(ScanExisting());
            // (HUD repair is attached per vehicle - coroutines die on scene load)
            if (sceneName.IndexOf("LOADER", StringComparison.OrdinalIgnoreCase) < 0)
            {
                MelonCoroutines.Start(AmmoWarmup());
            }

            // On a range scene, put a LEO2A4M next to the player so it can be inspected or shot at.
            // A mission whose scene is literally the range showcase always gets it; elsewhere the
            // switch below decides (training missions reuse the range scenes, so they stay off).
            bool isShowcase = sceneName != null &&
                sceneName.IndexOf("showcase", StringComparison.OrdinalIgnoreCase) >= 0;
            bool wantShowcase = isShowcase || RangeShowcaseSpawn;
            if (wantShowcase && IsRangeScene(sceneName))
            {
                _autoSpawned = false;
                StateController.RunOrDefer(GameState.PlayerReady,
                    new GameStateEventHandler(RangeSpawn), GameStatePriority.Medium);
            }
            else if (IsRangeScene(sceneName))
            {
            }
        }

private static IEnumerator RegisterRoutine()
        {
            if (_registerRunning || _registered)
                yield break;

            _registerRunning = true;
            while (true)
            {
                var lookups = Resources.FindObjectsOfTypeAll<UnitPrefabLookupScriptable>();
                if (lookups != null && lookups.Length > 0)
                {
                    bool added = false;
                    bool sawSource = false;
                    foreach (var lookup in lookups)
                    {
                        if (lookup == null || lookup.AllUnits == null)
                            continue;
                        if (lookup.AllUnits.Any(m => m != null && m.Name == UnitName))
                            continue;

                        if (!_loggedUnits)
                        {
                            _loggedUnits = true;
                        }
                        // pick the donor in preference order (LEO2A3 carries the EMES-15 thermal sight)
                        UnitPrefabLookupScriptable.UnitPrefabMetadata src = null;
                        foreach (var donorName in Donors)
                        {
                            src = lookup.AllUnits.FirstOrDefault(m => m != null &&
                                string.Equals(m.Name, donorName, StringComparison.OrdinalIgnoreCase));
                            if (src != null)
                                break;
                        }
                        if (src == null)
                            continue;

                        sawSource = true;
                        _donorName = src.Name;
                        var list = lookup.AllUnits.ToList();
                        list.Add(new UnitPrefabLookupScriptable.UnitPrefabMetadata
                        {
                            Name = UnitName,
                            FriendlyName = FriendlyName,
                            PrefabReference = src.PrefabReference,
                            BaseAmmoClipsReference = src.BaseAmmoClipsReference,
                            UseDecalLayout = src.UseDecalLayout,
                            DecalLayout = src.DecalLayout,
                            Army = src.Army,
                            Class = src.Class,
                            AlternativeClasses = src.AlternativeClasses,
                            AllowInCustomizer = true,
                        });
                        lookup.AllUnits = list.ToArray();
                        added = true;
                    }

                    if (added || sawSource)
                    {
                        _registered = true;
                        _registerRunning = false;
                        MelonLogger.Msg("[Leo2A4M] registered in UnitPrefabLookup, donor=" + _donorName);
                        yield break;
                    }
                }

                yield return null;            }
        }

        /// <summary>
        /// Keeps converting every LEO2A4M in the scene.  A one-shot scan was not enough: AI vehicles
        /// spawned later by a mission script slipped past the spawn hook and ended up completely
        /// unconverted - no WT model parts, no anti-Fulda guard, vanilla 2A4 armour and its DM33.
        /// </summary>
        /// <summary>
        /// The HUD builds its ammo label from whichever round already exists at that moment, so the
        /// rename has to happen as soon as the round asset is loaded - long before the player's
        /// vehicle spawns.  This polls the resource pool for a few seconds after every scene load.
        /// </summary>
        /// <summary>
        /// The loadout swap must not run inside Awake: rebuilding the racks before the weapon system
        /// is initialised left the gun with no ammo at all (HUD showed "Weapon name" placeholders).
        /// M1A2C does the same thing from a short retry coroutine after the vehicle exists.
        /// </summary>
        private static IEnumerator AmmoLater(Vehicle vic)
        {
            for (int i = 0; i < 8; i++)
            {
                yield return new WaitForSeconds(0.5f);
                if (vic == null)
                    yield break;
                AmmoKit.Apply(vic);
                AmmoKit.RefreshHud(vic);
            }
        }

        /// <summary>
        /// Keeps retrying the HUD ammo label: MainGun only exists once the player controller is created,
        /// which can be several seconds after the vehicle itself.
        /// </summary>
        private static IEnumerator HudWatch()
        {
            for (int i = 0; i < 150; i++)
            {
                AmmoKit.RefreshHud(null);
                yield return new WaitForSeconds(2f);
            }
        }
        private static IEnumerator AmmoWarmup()
        {
            // Give the scene a moment: the scan below walks every loaded asset and is far too
            // expensive to run while the loader is still working (that hung the loading screen once
            // other big mods were installed).
            yield return new WaitForSeconds(3f);
            for (int i = 0; i < 30; i++)
            {
                if (AmmoKit.Retuned)
                    yield break;
                if (AmmoKit.TryRetuneResources())
                {
                    yield break;
                }
                yield return new WaitForSeconds(1.5f);
            }
        }
        private static IEnumerator ScanExisting()
        {
            float waited = 0f;
            while (true)
            {
                yield return new WaitForSeconds(waited < 30f ? 2f : 10f);
                waited += 2f;
                try
                {
                    foreach (var vic in GameObject.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
                    {
                        if (vic == null || vic.gameObject == null)
                            continue;
                        if (vic.UniqueName != UnitName)
                            continue;

                        bool converted = vic.gameObject.GetComponent<Leo2A4MConverted>() != null;
                        if (!converted)
                        {
                            Convert(vic);
                            continue;
                        }

                        // ARMOR SIM spawns/respawns its own test vehicle copy, which loses the
                        // runtime-built add-on pieces.  EnsureBuilt is idempotent (marker component),
                        // so this is a cheap no-op for vehicles that already have them.
                        if (vic.GetComponent<AddOnArmour.AddOnArmourBuilt>() == null)
                        {
                            AddOnArmour.EnsureBuilt(vic);
                            AddOnArmour.AlignToVisuals(vic);
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning("[Leo2A4M] scene scan failed: " + ex.Message);
                }
            }
        }

        internal static void EnsureAssets()
        {
            if (_assetsReady && _materials != null && _parts != null)
                return;

            string obj = Path.Combine(ModRoot, "Leo2A4M_GHPC_1.2.obj");
            string tsv = Path.Combine(ModRoot, "materials.tsv");
            var infos = ObjLoader.LoadMatInfo(tsv);
            _materials = ObjLoader.BuildMaterials(infos, ModRoot);
            _parts = ObjLoader.Parse(obj);
            _anchors = ObjLoader.LoadParts(Path.Combine(ModRoot, "parts.tsv"));
            _assetsReady = true;
        }

        public static void Convert(Vehicle vic)
        {
            if (vic == null || vic.gameObject == null)
                return;
            if (vic.gameObject.GetComponent<Leo2A4MConverted>() != null)
                return;

            vic.gameObject.AddComponent<Leo2A4MConverted>();
            vic._uniqueName = UnitName;
            vic._friendlyName = FriendlyName;


            try
            {
                EnsureAssets();
                HideVanilla(vic);

                if (!ShowVanillaGear)
                    HideRunningGear(vic);

                var rig = FindRig(vic);
                var turret = rig != null ? FindChild(rig, "hull") : null;
                turret = turret != null ? FindChild(turret, "turret") : null;
                var mantlet = turret != null ? FindChild(turret, "mantlet") : null;

                // Every WT object is attached on its own, under the rig transform its anchor
                // names: armour blocks, cage panels and nets stay individually addressable.
                var hullExtra = new Vector3(HullX, BaseY + HullY, BaseZ + HullZ);
                var turretExtra = new Vector3(TurretX, BaseY + TurretY, BaseZ + TurretZ);
                var gunExtra = new Vector3(GunX, BaseY + GunY, BaseZ + GunZ);
                int attachedHull = 0, attachedTurret = 0, attachedGun = 0, skipped = 0;
                foreach (var part in _parts)
                {
                    string anchor = AnchorOf(part.Name);
                    if (anchor == "RunningGear" && !WtRunningGearOn)
                        continue;
                    Transform parent;
                    Vector3 extra;
                    if (anchor == "Turret")
                    {
                        parent = turret; extra = turretExtra;
                    }
                    else if (anchor == "Gun")
                    {
                        parent = mantlet; extra = gunExtra;
                    }
                    else
                    {
                        parent = vic.transform; extra = hullExtra;
                    }
                    if (parent == null)
                    {
                        skipped++;
                        continue;
                    }
                    if (AttachPart(vic, part, parent, extra) == null)
                        continue;
                    if (anchor == "Turret") attachedTurret++;
                    else if (anchor == "Gun") attachedGun++;
                    else attachedHull++;
                }

                // 2A4M keeps the 2A4 turret, but the WT add-on armour sits in front of the
                // donor's sight opening, so the gunner/thermal cameras still have to be lifted
                // in front of the armour skin.  The CITV is kept -- it only needs the commander
                // slot moved onto the WT optic head.
                MoveGunnerSight(vic);

                var shellHider = vic.gameObject.AddComponent<SightShellHider>();
                shellHider.Setup(vic);
                SetupGunnerThermal(vic);
                SetupCitv(vic);

                MelonLogger.Msg("[Leo2A4M] converted " + vic.name + " donor=" + _donorName);

                // fallback: if the pre-Awake hook did not catch this unit, install the add-on
                // armour pieces now (they are idempotent and log what they do)
                try
                {
                    AddOnArmour.EnsureBuilt(vic);
                    AddOnArmour.AlignToVisuals(vic);   // visuals exist now: snap the pieces onto them
                    MelonCoroutines.Start(AmmoLater(vic));   // configurable 120 mm loadout

                    HullArmour.Apply(vic);             // hull front composite level (b/c/d)

                    // guard against the Fulda 1989 mod injecting its C-Tech armour into our unit
                    var guard = vic.gameObject.AddComponent<ForeignArmourGuard>();
                    guard.Setup(vic);
                }
                catch (Exception ex) { MelonLogger.Warning("[Leo2A4M] addon armour: " + ex.Message); }
            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Leo2A4M] convert failed: " + ex);
                var marker = vic.gameObject.GetComponent<Leo2A4MConverted>();
                if (marker != null)
                    UnityEngine.Object.Destroy(marker);
            }
        }

        internal static Transform FindRig(Vehicle vic)
        {
            var rig = FindDeep(vic.transform, "leo2_rig");
            if (rig != null)
                return rig;
            var anim = vic.GetComponentsInChildren<Animator>(true).FirstOrDefault();
            return anim != null ? anim.transform : null;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null)
                return null;
            if (string.Equals(root.name, name, StringComparison.OrdinalIgnoreCase))
                return root;
            foreach (Transform child in root)
            {
                var found = FindDeep(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        internal static Transform FindChild(Transform parent, string name)
        {
            if (parent == null)
                return null;
            foreach (Transform child in parent)
            {
                if (string.Equals(child.name, name, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
            return null;
        }

        private static bool _probedVanillaMaterial;
        private static Vector3 _sightOldCentre;
        private static Vector3 _sightOldSize;
        private static bool _sightOldValid;
        private static Vector3 _sightTargetPos;
        private static bool _sightMoved;

private static void HideVanilla(Vehicle vic)
        {
            // Whitelist: keep only what is still needed (animated running gear, tracks,
            // interior) and hide every other vanilla visual. The Fulda donor prefab
            // carries extra props (camo net, unit crosses) outside LEO2_mesh, so a
            // blacklist of mesh children is not enough.
            var keep = new List<Transform>();
            var meshRoot = FindDeep(vic.transform, "LEO2_mesh");
            bool keepVanillaGear = ShowVanillaGear && !WtRunningGearOn;
            var gear = meshRoot != null ? FindDeep(meshRoot, "running gear") : null;
            if (gear != null && keepVanillaGear)
                keep.Add(gear);
            var tracks = FindDeep(vic.transform, "LEO2_tracks");
            if (tracks != null && keepVanillaGear)
                keep.Add(tracks);
            var interior = FindDeep(vic.transform, "LEO2_int");
            if (interior != null)
                keep.Add(interior);

            int hidden = 0;
            int kept = 0;
            foreach (var r in vic.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null)
                    continue;
                // Only the hull/turret meshes are replaced.  Particle systems, trails and line
                // renderers must stay enabled: the muzzle smoke, the dust kicked up around the
                // hull and the impact effects all live on the vanilla prefab as particle renderers.
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                    continue;
                bool whitelisted = keep.Any(k => r.transform.IsChildOf(k));
                if (whitelisted)
                {
                    if (!r.enabled)
                    {
                        r.enabled = true;
                        kept++;
                    }
                    continue;
                }
                if (r.enabled)
                {
                    r.enabled = false;
                    hidden++;
                }
            }
        }

        private static void HideRunningGear(Vehicle vic)
        {
            var meshRoot = FindDeep(vic.transform, "LEO2_mesh");
            var gear = meshRoot != null ? FindDeep(meshRoot, "running gear") : null;
            if (gear != null)
                DisableRenderers(gear);
            var tracks = FindDeep(vic.transform, "LEO2_tracks");
            if (tracks != null)
                DisableRenderers(tracks);
        }

        private static int DisableRenderers(Transform root)
        {
            int n = 0;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                // never touch particle systems / trails: they drive the dust and smoke effects
                if (r == null || !(r is MeshRenderer) && !(r is SkinnedMeshRenderer))
                    continue;
                if (r.enabled)
                {
                    r.enabled = false;
                    n++;
                }
            }
            return n;
        }

        /// <summary>Attach one OBJ part to the transform its anchor names.  Every source object
        /// stays a separate GameObject (named after the WT node) so per-piece armour values, AAR
        /// pieces and damage visuals can be hung off individual parts later.</summary>
        /// <summary>Builds an OBJ part under a parent using the standard vehicle-space transform.</summary>
        internal static GameObject BuildPart(ObjPart part, Transform parent)
        {
            if (part == null || parent == null)
                return null;
            var vic = parent.GetComponentInParent<Vehicle>();
            Vector3 local = vic != null ? parent.InverseTransformPoint(vic.transform.position) : Vector3.zero;
            var go = ObjLoader.Build(part, _materials, parent, local);
            if (go == null)
                return null;
            if (vic != null)
                go.transform.localRotation = Quaternion.Inverse(parent.rotation) * vic.transform.rotation;
            go.transform.localScale = Vector3.one;
            return go;
        }


        /// <summary>Builds an OBJ part under a parent with the standard vehicle-space transform.</summary>
        private static GameObject AttachPart(Vehicle vic, ObjPart part, Transform parent, Vector3 extra)
        {
            if (part == null || parent == null)
                return null;

            Vector3 local = parent.InverseTransformPoint(vic.transform.position) + extra;
            var go = ObjLoader.Build(part, _materials, parent, local);
            go.name = PartPrefix + part.Name;
            go.transform.localRotation = Quaternion.Inverse(parent.rotation) * vic.transform.rotation;
            float s = ModelScale > 0.01f ? ModelScale : 1f;
            float sx = AnchorOf(part.Name) == "Hull" ? (HullScaleX != null ? HullScaleX : 1f) : 1f;
            go.transform.localScale = new Vector3(s * sx, s, s);
            return go;
        }

        /// <summary>parts.tsv says which anchor a piece rides on; anything unlisted falls back to
        /// the hull so a missing file still produces a visible (if static) tank.</summary>
        private static string AnchorOf(string partName)
        {
            string anchor;
            if (_anchors != null && _anchors.TryGetValue(partName, out anchor) && !string.IsNullOrEmpty(anchor))
                return anchor;
            return "Hull";
        }

private static string Describe(object v)
        {
            if (v == null) return "null";
            var arr = v as System.Collections.IEnumerable;
            if (arr != null && !(v is string))
            {
                var sbd = new System.Text.StringBuilder("[");
                int n = 0;
                foreach (var item in arr)
                {
                    if (n++ > 0) sbd.Append(", ");
                    sbd.Append(item != null ? item.ToString() : "null");
                    if (n > 40) { sbd.Append(", ..."); break; }
                }
                return sbd.Append(']').ToString();
            }
            return v.ToString();
        }

/// The vanilla gunner sight camera sits inside the WT turret shell, which renders the
        /// sight black. Move it to the WT gunner's sight head so it looks out over the front plate.
        private static void MoveGunnerSight(Vehicle vic)
        {
            var optic = FindDeep(vic.transform, "Optic");
            if (optic == null || optic.parent == null || optic.parent.name != "GPS")
            {
                // fall back to any transform that carries a CameraSlot under the turret
                optic = null;
                foreach (var mb in vic.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (mb == null || mb.GetType().Name != "CameraSlot")
                        continue;
                    if (PathOf(mb.transform, vic.transform).IndexOf("GPS", StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                    optic = mb.transform;
                    break;
                }
            }
            if (optic == null)
            {
                MelonLogger.Warning("[Leo2A4M] gunner sight (GPS/Optic) not found");
                return;
            }

            Vector3 target = new Vector3(GunSightX, GunSightY, GunSightZ);

            // Move the WHOLE sight assembly (GPS) instead of just the two camera children.
            // The fire control reads the GPS transform as the sight line; moving only
            // Optic/FLIR left the FCS 1 m behind the camera, which is what made the
            // reticle hop when a range was put in.
            var gps = FindDeep(vic.transform, "GPS");
            Transform mover = gps != null ? gps : optic;
            Vector3 worldTarget = vic.transform.TransformPoint(target);
            Vector3 delta = worldTarget - optic.position;
            mover.position += delta;
        }

        private static void MoveSightUnder(Vehicle vic, Transform sight, Vector3 targetVehicle)
        {
            if (sight == null || sight.parent == null)
                return;
            Vector3 local = sight.parent.InverseTransformPoint(vic.transform.TransformPoint(targetVehicle));
            sight.localPosition = local;
        }

        /// <summary>
        /// The donor (LEO2A3) carries the first-generation EMES-15 imager: 480x240 buffer, a chunk
        /// pixel cookie, wobble and blur.  The 2A4M's gunner sight is second generation, so give the
        /// slot a bigger render target, drop the pixel grid/wobble/blur and sharpen the colour ramp.
        /// Everything is set on a private copy of the blit material -- the donor's material is
        /// shared with every other Leopard in the scene.
        /// </summary>
        private static void SetupGunnerThermal(Vehicle vic)
        {
            try
            {
                if (GunnerGen2 == null || !GunnerGen2)
                {
                    return;
                }

                var gps = FindDeep(vic.transform, "GPS");
                Transform flir = null;
                if (gps != null)
                {
                    foreach (Transform child in gps)
                        if (string.Equals(child.name, "FLIR", StringComparison.OrdinalIgnoreCase))
                            flir = child;
                }
                if (flir == null)
                    flir = FindDeep(vic.transform, "FLIR");
                var slot = flir != null ? flir.GetComponent<CameraSlot>() : null;
                if (slot == null)
                {
                    MelonLogger.Warning("[Leo2A4M] gunner thermal: FLIR CameraSlot not found");
                    return;
                }

                int beforeW = slot.FLIRWidth;
                int beforeH = slot.FLIRHeight;
                float scale = GunnerResScale != null ? Mathf.Clamp(GunnerResScale, 1f, 4f) : 2.5f;
                slot.OverrideFLIRResolution = true;
                slot.FLIRWidth = Mathf.Clamp(Mathf.RoundToInt(beforeW * scale), 480, 4096);
                slot.FLIRHeight = Mathf.Clamp(Mathf.RoundToInt(beforeH * scale), 240, 4096);
                slot.BaseBlur = 0f;
                slot.VibrationPreBlur = false;

                var srcMat = slot.FLIRBlitMaterialOverride;
                if (srcMat == null)
                {
                }
                else
                {
                    var shader = Shader.Find("Blit (FLIR)/Blit Simple");
                    if (shader == null)
                    {
                        MelonLogger.Warning("[Leo2A4M] gunner thermal: blit shader missing");
                    }
                    else
                    {
                        var mat = new Material(shader);
                        mat.CopyPropertiesFromMaterial(srcMat);
                        mat.SetTexture("_PixelCookie", null);
                        if (mat.HasProperty("_WobbleAmp")) mat.SetFloat("_WobbleAmp", 0f);
                        if (mat.HasProperty("_Blur")) mat.SetFloat("_Blur", 0f);
                        if (GunnerContrast != null && mat.HasProperty("_ColorRampExp"))
                            mat.SetFloat("_ColorRampExp", GunnerContrast);
                        if (GunnerExposure != null && mat.HasProperty("_ColorRampExposure"))
                            mat.SetFloat("_ColorRampExposure", GunnerExposure);
                        if (GunnerScanlines != null && mat.HasProperty("_ScanlineBrightness"))
                            mat.SetFloat("_ScanlineBrightness", GunnerScanlines);
                        mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
                        slot.FLIRBlitMaterialOverride = mat;
                    }
                }

                // the FLIR buffer is created when the slot initialises, long before this runs
                slot.enabled = false;
                slot.enabled = true;

            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] gunner thermal setup failed: " + ex.Message);
            }
        }

/// Port of the M1A2C CITV: take the commander's night slot, turn it into an independent
        /// thermal viewer with the monitor overlay and the (gen-1) FLIR blit material.
        /// plates, so the two wedge arrays and the centre block are built here from the turret
        /// The donor's gunner sight sits where the LEO2A3 had it; on the L2A5 the sight housing is
        /// on the turret roof, right of the centreline, just behind the wedge arrays. The camera and
        /// its interior mesh are moved as one, so the X-ray view matches the exterior model.
        private static void MoveGunnerSightInterior(Vehicle vic)
        {
            try
            {
                if (GunnerSightEnabled != null && !GunnerSightEnabled)
                    return;

                var turretObj = vic.transform.Find("leo2_rig/hull/turret");
                // resolve through the camera slots so the right object is moved
                CameraSlot opticSlot = null, flirSlot = null;
                foreach (var cs in vic.GetComponentsInChildren<CameraSlot>(true))
                {
                    if (cs == null)
                        continue;
                    if (cs.gameObject.name == "Optic")
                        opticSlot = cs;
                    else if (cs.gameObject.name == "FLIR")
                        flirSlot = cs;
                }
                var optic = opticSlot != null ? opticSlot.transform : FindDeep(vic.transform, "Optic");
                if (turretObj == null || optic == null)
                {
                    MelonLogger.Warning("[Leo2A4M] gunner sight interior: turret or Optic not found");
                    return;
                }

                // Place it from the turret's own world bounds instead of hand-picked numbers:
                // right of the centreline, just above the roof, a little ahead of centre. The
                // previous build used turret-local numbers that came from bounding-box space, which
                // put the sight inside the hull and blacked out the gunner view.
                var turretMeshT = FindDeep(turretObj, "Leo2A4M Turret");
                var rend = turretMeshT != null ? turretMeshT.GetComponent<Renderer>() : null;
                if (rend == null)
                {
                    MelonLogger.Warning("[Leo2A4M] gunner sight interior: turret renderer not found");
                    return;
                }

                var b = rend.bounds;
                Vector3 up = vic.transform.up;
                Vector3 fwd = vic.transform.forward;
                Vector3 right = vic.transform.right;
                Vector3 target = b.center
                    - right * (b.extents.x * 0.33f)   // gunner's side is the left of the turret
                    + fwd * (b.extents.z * 0.09f)
                    + up * (b.extents.y * 0.45f)
                    + new Vector3(GunnerSightX, GunnerSightY, GunnerSightZ);

                RaycastHit hit;
                if (Physics.Raycast(target, fwd, out hit, 3f, ~0, QueryTriggerInteraction.Ignore))
                {
                    MelonLogger.Warning("[Leo2A4M] gunner sight interior: target blocked by '" +
                        hit.collider.name + "' at " + hit.distance.ToString("F2") +
                        " m, keeping the original position");
                    return;
                }

                // Move the whole sight assembly so the visible interior mesh travels with the camera.
                var gps = FindDeep(vic.transform, "GPS");
                var mover = gps != null ? gps : optic;
                var before = mover.position;
                // remember the hole the sight leaves behind: the composite block goes exactly there
                bool haveBounds = false;
                Bounds oldBounds = new Bounds(before, Vector3.zero);
                foreach (var r in mover.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null)
                        continue;
                    var bs = r.bounds.size;
                    // the sight carries a full-screen quad (bounds around 10 m) and empty reticle
                    // meshes; neither describes the physical housing, so they are skipped
                    if (bs.x > 2f || bs.y > 2f || bs.z > 2f || bs.sqrMagnitude < 0.0001f)
                        continue;
                    if (!haveBounds) { oldBounds = r.bounds; haveBounds = true; }
                    else oldBounds.Encapsulate(r.bounds);
                }
                _sightOldCentre = haveBounds ? oldBounds.center : before;
                _sightOldSize = haveBounds ? oldBounds.size : new Vector3(0.8f, 0.6f, 0.8f);
                _sightOldValid = true;

                Vector3 delta = target - (optic != null ? optic.position : mover.position);
                mover.position += delta;
                _sightTargetPos = target;
                _sightMoved = true;

                // the dump showed the visible interior is not under GPS: these are the pieces that
                // make up the sight body inside the turret
                foreach (var t in vic.GetComponentsInChildren<Transform>(true))
                {
                    if (t == null)
                        continue;
                    string n = t.name.ToLowerInvariant();
                    if (!n.Contains("gunsight eyepiece") && !n.Equals("sight open"))
                        continue;
                    if (n.Equals("sight open"))
                        continue;   // the opening stays where it is; it gets filled instead
                    t.position += delta;
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Leo2A4M] gunner sight move failed: " + ex);
            }
        }



        private static object GetMember(object o, string name)
        {
            if (o == null)
                return null;
            var t = o.GetType();
            var f = t.GetField(name, System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f != null)
                return f.GetValue(o);
            var p = t.GetProperty(name, System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            return p != null && p.CanRead ? p.GetValue(o) : null;
        }

        private static void TrySetMember(object o, string name, object value)
        {
            if (o == null)
                return;
            var t = o.GetType();
            var f = t.GetField(name, System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (f != null)
            {
                f.SetValue(o, value);
                return;
            }
            var p = t.GetProperty(name, System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (p != null && p.CanWrite)
                p.SetValue(o, value);
        }







        private static void SetupCitv(Vehicle vic)
        {
            try
            {
                if (CitvEnabled == null || !CitvEnabled.Value)
                    return;

                CitvAssets.Load(vic);
                if (CitvAssets.Monitor == null)
                {
                    MelonLogger.Warning("[Leo2A4M] CITV monitor asset missing (Leo2A4M/citv_monitor)");
                    return;
                }

                CameraSlot slot = null;
                var nods = FindDeep(vic.transform, "NODs");
                if (nods != null)
                    slot = nods.GetComponent<CameraSlot>();
                if (slot == null)
                {
                    slot = vic.GetComponentsInChildren<CameraSlot>(true)
                        .FirstOrDefault(s2 => s2 != null &&
                            s2.gameObject.name.IndexOf("NOD", StringComparison.OrdinalIgnoreCase) >= 0);
                }
                if (slot == null)
                {
                    MelonLogger.Warning("[Leo2A4M] CITV: no commander night slot found");
                    return;
                }

// Give the commander sight the same standing the gunner sight has: a UsableOptic
                // bound to the gun's fire control, registered among the vehicle's designated camera
                // slots.  Without it the FCS never solves through this sight (measured: the camera
                // is slaved to the FCS aim vector but the shot keeps a constant offset), which is
                // exactly the difference from the M1A2C mod, whose CITV rides on a registered optic.
                try
                {
                    UsableOptic gunOptic = null;
                    FireControlSystem fcs = null;
                    foreach (var o in vic.GetComponentsInChildren<UsableOptic>(true))
                    {
                        if (o == null || o.FCS == null) continue;
                        if (gunOptic == null) { gunOptic = o; fcs = o.FCS; }
                        if (o.name.IndexOf("FERO", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            gunOptic = o; fcs = o.FCS;
                            break;
                        }
                    }

                    var citvOptic = slot.GetComponent<UsableOptic>();
                    if (citvOptic == null)
                        citvOptic = slot.gameObject.AddComponent<UsableOptic>();

                    if (fcs != null)
                        citvOptic.FCS = fcs;
                    citvOptic.slot = slot;
                    citvOptic.Transform = slot.transform;
                    // GPS/Optic and FLIR (the sights that actually shoot straight) use FcsRange;
                    // FERO Z18 uses Fixed.  Default to FcsRange and keep both switches exposed.
                    bool useFcsRange = CitvOpticFcsRange == null || CitvOpticFcsRange;
                    citvOptic.Alignment = useFcsRange
                        ? OpticAlignment.FcsRange
                        : (gunOptic != null ? gunOptic.Alignment : OpticAlignment.Fixed);
                    citvOptic.RotateElevation = CitvOpticRotElev != null && CitvOpticRotElev;
                    citvOptic.RotateAzimuth = false;
                    citvOptic.RegisterAtStart = true;
                    if (gunOptic != null)
                    {
                        citvOptic.LevelTransform = gunOptic.LevelTransform;
                        citvOptic.LocalElevationLimits = gunOptic.LocalElevationLimits;
                        citvOptic.LocalAzimuthLimits = gunOptic.LocalAzimuthLimits;
                        // Mounts binds a sight to the platforms it may command; without them the
                        // fire control has nothing to aim through this optic.
                        try { citvOptic.Mounts = gunOptic.Mounts; } catch (Exception) { }
                        // reticleMesh is the sight's boresight reference; without it the fire control
                        // has no zero to solve against, which is the last thing that can explain a
                        // fixed offset in override mode.
                        try
                        {
                            var rmGun = gunOptic.reticleMesh;
                            if (rmGun != null)
                                citvOptic.reticleMesh = rmGun;
                        }
                        catch (Exception ex) { MelonLogger.Warning("[Leo2A4M] reticleMesh: " + ex.Message); }
                    }

                    // NOTE: the slot is deliberately NOT added to Unit._designatedCameraSlots.
                    // That array is what the C key cycles through, so appending the CITV slot made
                    // C go commander -> CITV instead of commander -> third person.  The UsableOptic
                    // above is enough for the sight to exist; the cycle stays vanilla.
                }
                catch (Exception ex) { MelonLogger.Warning("[Leo2A4M] CITV optic register failed: " + ex.Message); }

                CITVManager.Init();
                var citv = slot.gameObject.AddComponent<CITV>();
                citv.Setup(vic, new Vector3(CitvX, CitvY, CitvZ));
                slot.CanToggleFlirPolarity = false;
                slot.gameObject.AddComponent<CitvFovFix>();

                if (vic._targetSpotterSettings != null)
                {
                    vic.TargetSpotterSettings._sightDistance = 4000f;
                    vic.TargetSpotterSettings._nightSightDistanceIdeal = 4000f;
                    vic.TargetSpotterSettings._nightSightDistancePassive = 4000f;
                }


            }
            catch (Exception ex)
            {
                MelonLogger.Error("[Leo2A4M] CITV failed: " + ex);
            }
        }

internal static string PathOf(Transform t, Transform root)
        {
            var parts = new List<string>();
            var cur = t;
            while (cur != null && cur != root)
            {
                parts.Add(cur.name);
                cur = cur.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static IEnumerator RangeSpawn(GameState _)
        {
            // Same recipe as the M1A2C mod: wait for PlayerReady to settle, spawn next to the
            // player, Neutral allegiance, then hand the unit the two fields the vanilla
            // "spawn at runtime" path sets and start its engine.
            yield return new WaitForSeconds(2.5f);
            if (_autoSpawned)
                yield break;
            _autoSpawned = true;

            var player = PlayerInput.Instance != null ? PlayerInput.Instance.CurrentPlayerUnit as Vehicle : null;
            Vector3 pos;
            Quaternion rot;

            if (player != null)
            {
                // 18 m ahead and 9 m to the LEFT of the player so it clears the other range vehicles
                pos = player.transform.position + player.transform.forward * 18f - player.transform.right * 9f;
                rot = player.transform.rotation;
            }
            else
            {
                pos = new Vector3(1195.5f, 23.5f, 1573.0f);
                rot = Quaternion.Euler(359.3f, 263.3f, 359.9f);
            }

            // The Leo2A5 mod spawns its 2A5 on exactly this same point (same range trigger,
            // same offsets), which stacks the two tanks.  Stand 10 m further left and 5 m
            // further forward, in vehicle-local axes, so both can be inspected side by side.
            pos += rot * new Vector3(-10f, 0f, 5f);

            // The 2A4M stands on the gentle part of the slope, so a plain ground snap is
            // enough here -- the height-profile search is only used by the 2A5 mod, whose
            // spot sits on top of a 0.8 m mound.
            Vector3 origin = pos + Vector3.up * 10f;
            RaycastHit hit;
            if (Physics.Raycast(origin, Vector3.down, out hit, 50f, ~0, QueryTriggerInteraction.Ignore))
                pos = hit.point + Vector3.up * 0.2f;

            MelonLogger.Msg("[Leo2A4M] range auto-spawn at " + pos.ToString("F2") +
                " player=" + (player != null ? player.name : "null"));
            var unit = UnitSpawner.Instance.SpawnUnit(UnitName, new UnitMetaData
            {
                Name = UnitName,
                Allegiance = Faction.Neutral,
                Position = pos,
                Rotation = rot,
                UnitType = UnitType.GroundVehicle,
            }, null, null);

            var spawned = unit as Vehicle;
            if (spawned == null)
            {
                MelonLogger.Error("[Leo2A4M] range spawn returned no vehicle");
                yield break;
            }

            spawned.NoPlayerControl = false;
            // M1A2C also uses 9001 for its range unit; the game keys the switch list by
            // InstanceId, so sharing the number hides one of the two vehicles. Use our own
            // block and skip anything already taken.
            var usedIds = new HashSet<int>();
            foreach (var v in GameObject.FindObjectsByType<Vehicle>(FindObjectsSortMode.None))
                if (v != null)
                    usedIds.Add(v.InstanceId);
            int newId = 20000 + (++_spawnSerial);
            while (usedIds.Contains(newId))
                newId++;
            spawned.InstanceId = newId;
            MelonCoroutines.Start(EnsureEngine(spawned));
        }

        /// <summary>Start the engine / audio the way the vanilla runtime spawn does.</summary>
        private static IEnumerator EnsureEngine(Vehicle vic)
        {
            yield return new WaitForSeconds(1.5f);
            if (vic == null)
                yield break;
            try
            {
                var controller = vic.VehicleController;
                if (controller == null)
                    controller = vic.transform.root.GetComponentInChildren<NWH.VehiclePhysics.VehicleController>(true);

                var engine = controller != null
                    ? controller.engine
                    : vic.GetComponentInChildren<NWH.VehiclePhysics.Engine>(true);
                if (engine == null)
                    engine = vic.GetComponentInChildren<NWH.VehiclePhysics.Engine>(true);

                if (engine != null)
                {
                    engine.runOnStartup = true;
                    engine.runOnEnable = true;
                    if (!engine.IsRunning)
                        engine.Start();
                }

                var sound = vic.GetComponentInChildren<EngineSound>(true);
                if (sound != null)
                {
                    if (!sound.gameObject.activeSelf)
                        sound.gameObject.SetActive(true);
                    sound.enabled = true;
                }

            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] engine ensure failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Only the real gunnery ranges.  The old test also matched the bare word "range", which made
        /// the showcase tank spawn inside training missions and read as a hostile vehicle.
        /// </summary>
        private static bool IsRangeScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
                return false;
            // TankRange only: TR01 is also loaded by training missions, and spawning the showcase
            // tank there reads as an extra hostile vehicle.
            return sceneName.IndexOf("TankRange", StringComparison.OrdinalIgnoreCase) >= 0
                || sceneName.IndexOf("showcase", StringComparison.OrdinalIgnoreCase) >= 0;
        }


}

    [HarmonyPatch(typeof(UnitSpawner), "SpawnUnit", new Type[]
    {
        typeof(string), typeof(UnitMetaData), typeof(WaypointHolder), typeof(Transform)
    })]
    public static class SpawnPatch
    {
        public static void Postfix(string uniqueName, IUnit __result)
        {
            if (!string.Equals(uniqueName, Leo2A4MMod.UnitName, StringComparison.OrdinalIgnoreCase))
                return;

            var vic = __result as Vehicle;
            if (vic == null)
            {
                MelonLogger.Warning("[Leo2A4M] spawn result is not a Vehicle");
                return;
            }

            Leo2A4MMod.Convert(vic);
        }
    }
}



















































































