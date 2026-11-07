using System;
using System.Collections.Generic;
using System.IO;
using GHPC;
using GHPC.Equipment;
using GHPC.Utility;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace Leo2A4M
{
    /// <summary>
    /// Add-on armour of the 2A4M, built the way the ghpc-addon-armor methodology prescribes for
    /// non-ERA add-on plates (M1A2C TUSK pattern):
    ///
    ///   * created BEFORE the vehicle's Awake, so the armour list picks them up;
    ///   * one object per plate, real mesh, layer 8 ("Raycast Only") + tag "Penetrable",
    ///     NON-convex MeshCollider;
    ///   * UniformArmor + runtime ArmorCodexScriptable (ThicknessSource = Multipliers, multiplier 1)
    ///     with the absolute value in PrimarySabotRha / PrimaryHeatRha;
    ///   * not ERA: they never detonate or disappear when hit.
    ///
    /// Pieces come from the whole-vehicle OBJ (geometry authored in vehicle space):
    ///
    ///   vehicle#bone_t1      breech front wedge, rides the gun        50 KE / 200 CE
    ///   vehicle#bone_t2      gunner side turret front wedge            50 / 200
    ///   vehicle#bone_t3      loader side turret front wedge            50 / 200
    ///   vehicle#hull_fupaa1  hull front upper add-on plate             25 / 150
    /// </summary>
    internal static class AddOnArmour
    {
        private const string ObjFile = "Leo2A4M_GHPC_1.2.obj";

        private sealed class Piece
        {
            internal string Match;    // full OBJ object name (never a prefix: _net_ variants exist)
            internal string Host;     // donor armour piece it rides with
            internal float Ke;
            internal float Ce;
            internal bool Hull;       // true = mounts under the hull armour root

            internal Piece(string match, string host, float ke, float ce, bool hull = false)
            {
                Match = match;
                Host = host;
                Ke = ke;
                Ce = ce;
                Hull = hull;
            }
        }

        private static readonly Piece[] Pieces =
        {
            // turret front wedges + breech front wedge
            new Piece("bone_t1", "gun breech", 140f, 200f),
            new Piece("bone_t2", "cheek array left A", 140f, 200f),
            new Piece("bone_t3", "cheek array right A", 140f, 200f),
            // hull front upper add-on plate
            new Piece("hull_fupaa", "upper glacis array", 50f, 300f, true),
            // turret side add-on composite (hatches, then rear plates, then the big side arrays)
            new Piece("hatch_07_98", "array left", 30f, 60f),
            new Piece("hatch_06_99", "array right", 30f, 60f),
            new Piece("ex_armor_l_13_102", "array left", 60f, 200f),
            new Piece("ex_armor_r_13_94", "array right", 60f, 200f),
            new Piece("ex_armor_l_11_100", "array left", 100f, 200f),
            new Piece("ex_armor_l_12_93", "array left", 100f, 200f),
            new Piece("ex_armor_r_11_97", "array right", 100f, 200f),
            new Piece("ex_armor_r_12_101", "array right", 100f, 200f),
            // hull side add-on composite (skirt blocks, 100 KE / 450 CE each)
            new Piece("ex_armor_l_01_9", "hull left upper", 50f, 150f, true),
            new Piece("ex_armor_l_02_11", "hull left upper", 50f, 150f, true),
            new Piece("ex_armor_l_03_12", "hull left upper", 50f, 150f, true),
            new Piece("ex_armor_l_04_17", "hull left upper", 50f, 150f, true),
            new Piece("ex_armor_l_05_18", "hull left lower", 50f, 150f, true),
            new Piece("ex_armor_l_06_19", "hull left lower", 50f, 150f, true),
            new Piece("ex_armor_r_01_8", "hull right upper", 50f, 150f, true),
            new Piece("ex_armor_r_02_10", "hull right upper", 50f, 150f, true),
            new Piece("ex_armor_r_03_13", "hull right upper", 50f, 150f, true),
            new Piece("ex_armor_r_04_14", "hull right upper", 50f, 150f, true),
            new Piece("ex_armor_r_05_15", "hull right lower", 50f, 150f, true),
            new Piece("ex_armor_r_06_16", "hull right lower", 50f, 150f, true),
            // rear grille / slat armour (5 KE / 120 CE each)
            new Piece("ex_decor_05_22", "hull rear", 5f, 120f, true),
            new Piece("ex_decor_l_04_21", "hull left lower", 5f, 120f, true),
            new Piece("ex_decor_r_04_20", "hull right lower", 5f, 120f, true),
            new Piece("ex_decor_06_92", "bustle rear", 5f, 120f),
        };

        private static List<ObjPart> _parts;
        private static bool _loadTried;
        private static bool _audited;

        /// <summary>
        /// Marker so we know this instance already has its add-on pieces.  The previous check used
        /// Transform.Find on a vehicle-root path, but the pieces are parented under the donor armour
        /// root, so it never matched: the sweep rebuilt 28 objects every two seconds (log spam plus
        /// a severe frame-rate hit).
        /// </summary>
        public class AddOnArmourBuilt : MonoBehaviour { }

        /// <summary>Fallback path: called after conversion; skips work already done pre-Awake.</summary>
        internal static void EnsureBuilt(Vehicle vic)
        {
            if (vic == null)
                return;
            if (vic.GetComponent<AddOnArmourBuilt>() != null)
                return;
            Build(vic);
            if (vic.GetComponent<AddOnArmourBuilt>() == null)
                vic.gameObject.AddComponent<AddOnArmourBuilt>();
        }

        internal static void Build(Vehicle vic)
        {
            if (vic == null)
                return;

            var parts = LoadParts();
            if (parts == null || parts.Count == 0)
            {
                MelonLogger.Warning("[Leo2A4M] addon: no matching parts in " + ObjFile);
                return;
            }

            var rig = Leo2A4MMod.FindRig(vic);
            var hull = rig != null ? Leo2A4MMod.FindChild(rig, "hull") : null;
            var turret = hull != null ? Leo2A4MMod.FindChild(hull, "turret") : null;
            var mantlet = turret != null ? Leo2A4MMod.FindChild(turret, "mantlet") : null;

            int built = 0;
            foreach (var part in parts)
            {
                var spec = SpecFor(part.Name);
                if (spec == null)
                    continue;

                // the armour list lives under the vehicle's ARMOUR roots (audit:
                // "LEO2A4 HULL COLLIDERS/LEO2_armour_hull" and
                // "LEO2A4 TURRET COLLIDERS/LEO2_armour_turret/turret armour"); parenting to a donor
                // plate instead inherits that plate's scale and shrinks the piece
                bool isHull = spec.Hull;
                var root = FindArmourRoot(vic, isHull);
                Transform parent = root != null ? root : Fallback(spec, hull, turret, mantlet);
                if (parent == null)
                {
                    MelonLogger.Warning("[Leo2A4M] addon " + part.Name + ": no parent");
                    continue;
                }

                var go = Leo2A4MMod.BuildPart(part, parent);
                if (go == null)
                    continue;
                go.name = "A4M addon " + part.Name;

                // the mesh is authored in vehicle space (metres): undo any parent scale so the piece
                // lands exactly where the visual plate is
                var ps = parent.lossyScale;
                go.transform.localScale = new Vector3(
                    1f / Mathf.Max(Mathf.Abs(ps.x), 0.001f),
                    1f / Mathf.Max(Mathf.Abs(ps.y), 0.001f),
                    1f / Mathf.Max(Mathf.Abs(ps.z), 0.001f));
                go.layer = ArmourLayer();
                try { go.tag = "Penetrable"; } catch (Exception) { }
                // layer 8 is not drawn by the main camera; the visible plate comes from the normal
                // part pipeline (parts.tsv).  The renderer stays alive and an AarVisual is attached
                // so the AAR / x-ray pass draws this piece as internal structure.
                MakeAarVisible(vic, go);

                var mf = go.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null)
                {
                    MelonLogger.Warning("[Leo2A4M] addon " + part.Name + ": no mesh");
                    continue;
                }
                var mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = false;
                mc.isTrigger = false;

                MakeArmour(vic, go, spec);
                built++;
            }

            bool pack = Cfg.GetBool("Turret Add-on Armour (Leo2A4M)",
                Leo2A4MMod.TurretAddonEnhanced != null && Leo2A4MMod.TurretAddonEnhanced.Value);
            var w1 = SpecFor("bone_t1");
            MelonLogger.Msg("[Leo2A4M] addon armour: " + built + " piece(s); turret package=" + pack +
                "; wedge " + (w1 != null ? w1.Ke.ToString("F0") + "/" + w1.Ce.ToString("F0") : "?"));
        }

        /// <summary>
        /// Copies the transform of the matching visual part ("Leo2A4M &lt;obj name&gt;") onto the
        /// armour piece.  Both are the same OBJ mesh, so making the transforms identical places the
        /// collision piece exactly on the visible plate regardless of parent-space quirks.
        /// </summary>
        internal static void AlignToVisuals(Vehicle vic)
        {
            if (vic == null)
                return;
            try
            {
                int fixedCount = 0;
                foreach (var tr in vic.GetComponentsInChildren<Transform>(true))
                {
                    if (tr == null || !tr.name.StartsWith("A4M addon ", StringComparison.Ordinal))
                        continue;
                    string partName = tr.name.Substring("A4M addon ".Length);
                    var vis = FindVisual(vic, partName);
                    if (vis == null)
                    {
                        MelonLogger.Warning("[Leo2A4M] align: no visual for " + partName);
                        continue;
                    }
                    tr.SetParent(vis.parent, true);
                    tr.position = vis.position;
                    tr.rotation = vis.rotation;
                    tr.localScale = vis.localScale;
                    fixedCount++;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] align failed: " + ex.Message);
            }
        }

        private static Transform FindVisual(Vehicle vic, string partName)
        {
            foreach (var tr in vic.GetComponentsInChildren<Transform>(true))
            {
                if (tr == null) continue;
                if (tr.name == "Leo2A4M " + partName)
                    return tr;
            }
            return null;
        }
        private static Piece SpecFor(string name)
        {
            foreach (var p in Pieces)
            {
                if (name.IndexOf(p.Match, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                // Optional turret front add-on package: swap the three wedges for the enhanced values.
                bool wedge = p.Match == "bone_t1" || p.Match == "bone_t2" || p.Match == "bone_t3";
                bool enhanced = Cfg.GetBool("Turret Add-on Armour (Leo2A4M)",
                    Leo2A4MMod.TurretAddonEnhanced != null && Leo2A4MMod.TurretAddonEnhanced.Value);
                if (wedge && enhanced)
                    return new Piece(p.Match, p.Host, 200f, 280f, p.Hull);
                return p;
            }
            return null;
        }

        private static Transform Fallback(Piece spec, Transform hull, Transform turret, Transform mantlet)
        {
            if (spec.Match.StartsWith("hull_", StringComparison.OrdinalIgnoreCase))
                return hull;
            if (spec.Match == "bone_t1")
                return mantlet != null ? mantlet : turret;
            return turret;
        }

        /// <summary>
        /// Registers the piece with the AAR / x-ray pass.  The component lives in GHPC.AarVisual;
        /// it is looked up by name so the mod keeps working if the assembly name differs.  The
        /// vehicle's own AAR material is reused so the piece looks like the rest of the internals.
        /// </summary>
        private static void MakeAarVisible(Vehicle vic, GameObject go)
        {
            try
            {
                var type = typeof(UniformArmor).Assembly.GetType("GHPC.AarVisual");
                if (type == null)
                {
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        type = asm.GetType("GHPC.AarVisual");
                        if (type != null) break;
                    }
                }
                if (type == null)
                {
                    MelonLogger.Warning("[Leo2A4M] AarVisual type not found");
                    return;
                }

                var comp = go.GetComponent(type) ?? go.AddComponent(type);
                SetField(comp, "HideUntilAar", true);
                SetField(comp, "hideUntilAar", true);
                SetField(comp, "SwitchMaterials", true);
                SetField(comp, "switchMaterials", true);
                var mat = FindAarMaterial(vic);
                if (mat != null)
                {
                    SetField(comp, "AarMaterial", mat);
                    SetField(comp, "aarMaterial", mat);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] AAR visual failed: " + ex.Message);
            }
        }

        private static Material FindAarMaterial(Vehicle vic)
        {
            foreach (var mb in vic.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "AarVisual")
                    continue;
                foreach (var nm in new[] { "AarMaterial", "aarMaterial" })
                {
                    var f = mb.GetType().GetField(nm, System.Reflection.BindingFlags.Instance |
                                                      System.Reflection.BindingFlags.Public |
                                                      System.Reflection.BindingFlags.NonPublic);
                    if (f == null || f.FieldType != typeof(Material))
                        continue;
                    var v = f.GetValue(mb) as Material;
                    if (v != null)
                        return v;
                }
            }
            return null;
        }

        private static void MakeArmour(Vehicle vic, GameObject go, Piece spec)
        {
            var armour = go.AddComponent<UniformArmor>();
            armour.SetName(go.name);
            armour.PrimarySabotRha = spec.Ke;
            armour.PrimaryHeatRha = spec.Ce;
            armour.SecondarySabotRha = 0f;
            armour.SecondaryHeatRha = 0f;
            armour.AngleMatters = false;
            armour.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
            try
            {
                armour.DetonatedObjects = new GameObject[0];
                armour.UndetonatedObjects = new[] { go };
            }
            catch (Exception) { }
            armour.Unit = vic;

            var at = new ArmorType();
            at.Name = go.name;
            at.ThicknessSource = ArmorType.RhaSource.Multipliers;
            at.RhaeMultiplierKe = 1f;
            at.RhaeMultiplierCe = 1f;
            at.CanRicochet = true;
            at.CanShatterLongRods = true;
            at.CrushThicknessModifier = 1f;
            var codex = ScriptableObject.CreateInstance<ArmorCodexScriptable>();
            codex.name = go.name + " codex";
            codex.ArmorType = at;
            SetField(armour, "_armorType", codex);
            SetField(armour, "_isEra", false);
            // Decals and the impact spark/smoke effect hang off this flag: leaving it true (the ERA
            // default) suppressed every hit effect on the pieces, which cover most of the vehicle.
            SetField(armour, "_noImpactDecals", false);
        }

        private static void SetField(object target, string name, object value)
        {
            try
            {
                for (var t = target.GetType(); t != null && t != typeof(object); t = t.BaseType)
                {
                    var f = t.GetField(name, System.Reflection.BindingFlags.Instance |
                                              System.Reflection.BindingFlags.Public |
                                              System.Reflection.BindingFlags.NonPublic |
                                              System.Reflection.BindingFlags.DeclaredOnly);
                    if (f != null)
                    {
                        f.SetValue(target, value);
                        return;
                    }
                }
            }
            catch (Exception) { }
        }

        private static int ArmourLayer()
        {
            int layer = LayerMask.NameToLayer("Raycast Only");
            return layer >= 0 ? layer : 8;
        }

        /// <summary>Finds the vehicle's ARMOUR root for hull or turret pieces.</summary>
        private static Transform FindArmourRoot(Vehicle vic, bool hull)
        {
            Transform best = null;
            foreach (var tr in vic.GetComponentsInChildren<Transform>(true))
            {
                if (tr == null || tr.gameObject.layer != 8)
                    continue;
                string n = tr.name;
                bool match = hull
                    ? n.IndexOf("armour_hull", StringComparison.OrdinalIgnoreCase) >= 0
                    : n.IndexOf("turret armour", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!match)
                    continue;
                // prefer the innermost node that still carries the name
                best = tr;
                if (n.Equals("turret armour", StringComparison.OrdinalIgnoreCase))
                    break;
            }
            return best;
        }
        private static MonoBehaviour FindArmourPiece(Vehicle vic, string name)
        {
            foreach (var mb in vic.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var tn = mb.GetType().Name;
                if (tn != "UniformArmor" && tn != "VariableArmor") continue;
                if (string.Equals(mb.gameObject.name, name, StringComparison.OrdinalIgnoreCase))
                    return mb;
            }
            return null;
        }

        private static List<ObjPart> LoadParts()
        {
            if (_loadTried)
                return _parts;
            _loadTried = true;
            try
            {
                string path = Path.Combine(Leo2A4MMod.AssetRoot, ObjFile);
                var all = ObjLoader.Parse(path);
                _parts = new List<ObjPart>();
                if (all != null)
                {
                    foreach (var p in all)
                        if (p != null && SpecFor(p.Name) != null)
                            _parts.Add(p);
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] addon obj failed: " + ex.Message);
                _parts = null;
            }
            return _parts;
        }

}
}
