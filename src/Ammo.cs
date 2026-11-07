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
    /// design asks for 闂?everything else therefore matches the vanilla behaviour exactly.
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

        private static bool _retuned;
        private static readonly HashSet<int> _appliedVehicles = new HashSet<int>();
        private static readonly HashSet<string> _loggedSkips = new HashSet<string>();

        /// <summary>One line per distinct reason a vehicle was skipped, so silent returns are visible.</summary>
        private static void LogSkip(int key, string reason)
        {
            if (_loggedSkips.Add(reason))
                MelonLogger.Msg("[Leo2A4M] ammo: skipped (vehicle " + key + "): " + reason);
        }

        /// <summary>True once the round has been renamed/re-statted this session.</summary>
        internal static bool Retuned { get { return _retuned; } }

        /// <summary>Per-vehicle work: every converted vehicle needs its own loadout swapped.</summary>
        internal static bool TryRetuneResources()
        {
            return _appliedVehicles.Count > 0;
        }

        /// <summary>
        /// Retunes the vanilla 120 mm AP round in place.  Nothing else is touched: the ammo racks,
        /// their counts, the visible round models and the feeding logic all stay vanilla, which is
        /// the only way the numbers and the models keep matching.
        /// </summary>
        internal static void Apply(Vehicle vic)
        {
            if (vic == null)
                return;

            // Per vehicle: each converted vehicle has its own LoadoutManager and feed, so the loadout
            // swap has to run for every one of them (a session-wide flag only fixed the first).
            int key = vic.GetInstanceID();
            if (_appliedVehicles.Contains(key))
                return;

            // The gun lives on WeaponsManager.Weapons[]; this is the path the M1A2C mod uses and the
            // only one that reliably finds it here.
            var wm = vic.GetComponent<WeaponsManager>();
            if (wm == null || wm.Weapons == null || wm.Weapons.Length == 0)
            {
                LogSkip(key, "no WeaponsManager yet");
                return;
            }
            var gun = wm.Weapons[0].Weapon;
            if (gun == null || gun.CodexEntry == null)
            {
                LogSkip(key, "gun/codex not ready");
                return;
            }

            var lm = vic.GetComponent<LoadoutManager>();
            if (lm == null || lm.LoadedAmmoList == null)
            {
                LogSkip(key, "no LoadoutManager/LoadedAmmoList");
                return;
            }
            var old = lm.LoadedAmmoList.AmmoClips;
            if (old == null || old.Length == 0)
            {
                LogSkip(key, "empty vanilla clip list");
                return;
            }
            try
            {
                EnsureAmmo(gun.CodexEntry);
                if (ApClip == null || HeatClip == null)
                {
                    MelonLogger.Warning("[Leo2A4M] ammo: vanilla donors not found yet");
                    return;
                }

                // Rebuilding the racks only works when our rounds have a visual model (the rack
                // instantiates it per slot).  Without one SpawnVisualModel throws and the mission
                // never finishes loading, so fall back to the in-place retune in that case.
                bool haveVisuals = ApType != null && ApType.VisualModel != null &&
                                   HeatType != null && HeatType.VisualModel != null;
                bool wantRebuild = Leo2A4MMod.AmmoLoadoutRebuild;

                if (haveVisuals && wantRebuild)
                {
                    var fresh = new AmmoClipCodexScriptable[old.Length];
                    int heatSlots = 0;
                    for (int i = 0; i < old.Length; i++)
                    {
                        bool heat = IsHeatClip(old[i]);
                        fresh[i] = heat ? HeatClip : ApClip;
                        if (heat) heatSlots++;
                    }

                    lm.LoadedAmmoList.AmmoClips = fresh;
                    lm.SpawnCurrentLoadout();

                    try
                    {
                        if (gun.Feed != null)
                            gun.Feed.AmmoTypeInBreech = null;
                    }
                    catch (Exception) { }

                    MelonLogger.Msg("[Leo2A4M] ammo: loadout rebuilt -> " + ApType.Name +
                        " (" + apPen.ToString("F0") + " mm, coeff " + DragCoeff.ToString("F2") + ") + " +
                        HeatType.Name + "; slots=" + fresh.Length + " heatSlots=" + heatSlots +
                        "; counts untouched (vehicle " + key + ")");
                }
                else
                {
                    // Safe path: retune the rounds this vehicle already carries, leave the racks alone.
                    // Combat Overhaul's HUD prints the CLIP display name (AmmoType.AmmoClip.Name), which
                    // is a separate string from AmmoType.Name, so both have to be set.
                    foreach (var clip in old)
                    {
                        if (clip == null || clip.ClipType == null)
                            continue;
                        bool heatClip = IsHeatAmmo(TypeOf(clip));
                        if (heatClip || !Is120mm(TypeOf(clip)))
                            continue;
                        string before = clip.ClipType.Name;
                        clip.ClipType.Name = SelectedName();
                        MelonLogger.Msg("[Leo2A4M] clip label: '" + before + "' -> '" +
                            clip.ClipType.Name + "'");
                    }
                    int retuned = 0;
                    foreach (var clip in old)
                    {
                        var type = TypeOf(clip);
                        if (type == null || IsHeatAmmo(type) || !Is120mm(type))
                            continue;
                        ApplySelected(type);
                        _vehicleApType = type;
                        retuned++;
                    }

                    // HUD: PlayerControl.OnGUI prints PlayerControl._gun.CurrentAmmo, i.e. the gun's
                    // AvailableAmmo array.  Pointing those slots at the round we just retuned fixes the
                    // label without touching the racks (SpawnCurrentLoadout stays out of this path).
                    AlignHudSlots(key);
                }

                _appliedVehicles.Add(key);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] ammo rebuild failed: " + ex);
            }
        }

        /// <summary>
        /// Points the HUD's ammo slots at the instance we actually retuned.  HUD reads
        /// PlayerControl._gun.CurrentAmmo -> MainGun.AvailableAmmo, and that array can hold a different
        /// AmmoType instance than the clips do; type searches for MainGun return nothing on this build,
        /// so the gun is taken from PlayerControl itself.
        /// </summary>
        private static MainGun _mainGun;

        /// <summary>
        /// HUD prints PlayerControl._gun.CurrentAmmo -> MainGun.AvailableAmmo.  MainGun is reachable
        /// from the vehicle's weapon (WeaponSystem.MainGun); type searches for it return 0 because it is
        /// not a scene component, so it has to be taken from the weapon.
        /// </summary>
        private static bool _hudAligned;

        /// <summary>
        /// HUD prints PlayerControl._gun.CurrentAmmo -> MainGun.AvailableAmmo.  MainGun instances may
        /// not exist yet when the vehicle is converted (the player controller is created later), so this
        /// stays callable and simply retries; it stops once a slot has been renamed.
        /// </summary>
        private static void AlignHudSlots(int vehicleKey)
        {
            if (_hudAligned)
                return;
            try
            {
                var guns = UnityEngine.Object.FindObjectsByType<MainGun>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                if (guns == null || guns.Length == 0)
                {
                    return;
                }

                string want = SelectedName();
                int renamed = 0;
                foreach (var gun in guns)
                {
                    var arr = gun != null ? gun.AvailableAmmo : null;
                    if (arr == null)
                        continue;
                    for (int i = 0; i < arr.Length; i++)
                    {
                        var type = arr[i];
                        if (type == null)
                            continue;
                        bool heat = IsHeatAmmo(type);
                        if (heat || string.Equals(type.Name, want, StringComparison.Ordinal))
                            continue;
                        ApplySelected(type);
                        renamed++;
                    }
                }
                if (renamed > 0)
                {
                    _hudAligned = true;
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] HUD align failed: " + ex.Message);
            }
        }

        /// <summary>Re-runs the HUD alignment; cheap enough to retry while the player takes control.</summary>
        internal static void RefreshHud(Vehicle vic)
        {
            if (vic != null)
                AlignHudSlots(vic.GetInstanceID());
        }

        internal static AmmoClipCodexScriptable ApClip;
        internal static AmmoClipCodexScriptable HeatClip;
        private static float apPen;

        /// <summary>Builds our rounds once, compatible with the given gun codex (M1A2C recipe).</summary>
        private static void EnsureAmmo(WeaponSystemCodexScriptable gunCodex)
        {
            if (ApClip != null && HeatClip != null)
                return;

            AmmoType donorAp = null, donorHeat = null;
            foreach (var codex in Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>())
            {
                if (codex == null || codex.AmmoType == null)
                    continue;
                string cname = (codex.name ?? "").ToLowerInvariant();
                var type = codex.AmmoType;
                if (donorAp == null && cname.Contains("dm23") && Is120mm(type) && !IsHeatAmmo(type))
                    donorAp = type;
                else if (donorHeat == null && (cname.Contains("dm12") || cname.Contains("heat"))
                         && Is120mm(type) && IsHeatAmmo(type))
                    donorHeat = type;
                if (donorAp != null && donorHeat != null)
                    break;
            }

            if (donorAp == null)
            {
                MelonLogger.Warning("[Leo2A4M] ammo: DM23 donor missing");
                return;
            }

            string sel = Pick(Leo2A4MMod.ApAmmo != null ? Leo2A4MMod.ApAmmo.Value : "kewa1");
            string name;
            float pen, vel, mass, spall, eraRatio = 1f, coeff = DragCoeff;
            switch (sel)
            {
                case "dm43":
                    name = "DM43 APFSDS-T"; pen = 562f; vel = 1700f; mass = 4f; spall = 1.14f;
                    break;
                case "dm53":
                    name = "DM53 APFSDS-T"; pen = 668f; vel = 1670f; mass = 5f; spall = 1.20f;
                    eraRatio = 0.70f;   // ignores 30 % of the ERA armour value
                    break;
                default:
                    name = "KEW-A1 APFSDS-T"; pen = 608f; vel = 1740f; mass = 4f; spall = 1.08f;
                    break;
            }
            apPen = pen;

            ApType = MakeType(donorAp, name);
            ApType.RhaPenetration = pen;
            ApType.MuzzleVelocity = vel;
            ApType.Mass = mass;
            ApType.Coeff = coeff;
            if (spall > 0f)
                ApType.SpallMultiplier = spall;
            if (eraRatio < 0.999f)
                EraBypass.Populate(ApType, eraRatio);

            HeatType = donorHeat != null ? MakeType(donorHeat, donorHeat.Name) : MakeType(donorAp, "DM12A1 HEAT-T");

            AmmoCodexScriptable apCodex, heatCodex;
            ApClip = MakeClip("clip_Leo2A4M_AP", ApType, gunCodex, out apCodex);
            HeatClip = MakeClip("clip_Leo2A4M_HEAT", HeatType, gunCodex, out heatCodex);

            FinishVisual(donorAp, ApType, apCodex);
            if (donorHeat != null)
                FinishVisual(donorHeat, HeatType, heatCodex);
        }

        private static AmmoType MakeType(AmmoType donor, string name)
        {
            var type = new AmmoType();
            ShallowCopy(type, donor);
            type.Name = name;
            type.Caliber = 120f;
            type.CachedIndex = donor != null ? donor.CachedIndex : 0;
            return type;
        }

        private static AmmoClipCodexScriptable MakeClip(string clipName, AmmoType type,
                                                        WeaponSystemCodexScriptable gunCodex,
                                                        out AmmoCodexScriptable codex)
        {
            codex = ScriptableObject.CreateInstance<AmmoCodexScriptable>();
            codex.name = clipName + "_codex";
            codex.AmmoType = type;

            var clip = new AmmoType.AmmoClip();
            clip.Capacity = 1;
            clip.Name = type.Name;
            clip.MinimalPattern = new[] { codex };

            var clipCodex = ScriptableObject.CreateInstance<AmmoClipCodexScriptable>();
            clipCodex.name = clipName;
            clipCodex.ClipType = clip;
            clipCodex.CompatibleWeaponSystems = new[] { gunCodex };
            return clipCodex;
        }

        private static void FinishVisual(AmmoType donor, AmmoType type, AmmoCodexScriptable codex)
        {
            if (type == null)
                return;
            var source = donor != null && donor.VisualModel != null ? donor.VisualModel : AnyVisual();
            if (source == null)
            {
                MelonLogger.Warning("[Leo2A4M] ammo: no visual model available for " + type.Name);
                return;
            }
            var vis = UnityEngine.Object.Instantiate(source);
            UnityEngine.Object.DontDestroyOnLoad(vis);
            vis.hideFlags = HideFlags.DontUnloadUnusedAsset;
            vis.name = type.Name + " visual";
            var av = vis.GetComponent<AmmoStoredVisual>();
            if (av != null)
            {
                av.AmmoType = type;
                av.AmmoScriptable = codex;
            }
            type.VisualModel = vis;
        }

        /// <summary>Any round visual in the pool, used when the donor round has none.</summary>
        private static GameObject AnyVisual()
        {
            try
            {
                foreach (var codex in Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>())
                {
                    var t = codex != null ? codex.AmmoType : null;
                    if (t != null && t.VisualModel != null && Is120mm(t))
                        return t.VisualModel;
                }
                foreach (var codex in Resources.FindObjectsOfTypeAll<AmmoCodexScriptable>())
                {
                    var t = codex != null ? codex.AmmoType : null;
                    if (t != null && t.VisualModel != null)
                        return t.VisualModel;
                }
            }
            catch (Exception) { }
            return null;
        }

        private static bool IsHeatClip(AmmoClipCodexScriptable clipCodex)
        {
            var type = TypeOf(clipCodex);
            return IsHeatAmmo(type);
        }


        /// <summary>
        /// Finds the vanilla AP / HEAT rounds from the loadout the donor already carries.  HEAT is
        /// only needed to tell the two apart; nothing is written to it.
        /// </summary>
        /// <summary>
        /// Retunes the rounds the vehicle's own guns list in MainGun.AvailableAmmo - that is exactly
        /// what PlayerControl.OnGUI prints as the HUD round name, and it can hold AmmoType instances
        /// that are not the ones found while scanning the asset pool.
        /// </summary>
        /// <summary>
        /// Collects the guns whose AvailableAmmo feeds the HUD.  The player's own gun is the one that
        /// matters (PlayerControl.OnGUI prints PlayerControl._gun.CurrentAmmo), and type searches for
        /// MainGun come back empty on this build, so read the field instead.
        /// </summary>


        /// <summary>Name of the round selected in the player config.</summary>
        private static string SelectedName()
        {
            switch (Pick(Leo2A4MMod.ApAmmo != null ? Leo2A4MMod.ApAmmo.Value : "kewa1"))
            {
                case "dm43": return "DM43 APFSDS-T";
                case "dm53": return "DM53 APFSDS-T";
                default: return "KEW-A1 APFSDS-T";
            }
        }

        /// <summary>Applies the configured round's stats to an existing AmmoType in place.</summary>
        private static void ApplySelected(AmmoType type)
        {
            switch (Pick(Leo2A4MMod.ApAmmo != null ? Leo2A4MMod.ApAmmo.Value : "kewa1"))
            {
                case "dm43": Retune(type, "DM43 APFSDS-T", 562f, 1700f, 4f, 1.14f, 1f); break;
                case "dm53": Retune(type, "DM53 APFSDS-T", 668f, 1670f, 5f, 1.20f, 0.70f); break;
                default: Retune(type, "KEW-A1 APFSDS-T", 608f, 1740f, 4f, 1.08f, 1f); break;
            }
        }

        private static void Retune(AmmoType type, string name, float pen, float vel, float mass,
                                   float spall, float eraRatio, float coeff = DragCoeff)
        {
            if (type == null)
                return;
            type.Name = name;
            type.RhaPenetration = pen;
            type.MuzzleVelocity = vel;
            type.Mass = mass;
            type.Coeff = coeff;
            if (spall > 0f)
                type.SpallMultiplier = spall;
            if (eraRatio < 0.999f)
                EraBypass.Populate(type, eraRatio);
        }

        /// <summary>Drag coefficient for the retuned AP rounds (the vanilla DM23 template uses 0.30).</summary>
        private const float DragCoeff = 0.18f;

        internal static AmmoClipCodexScriptable VanillaApClip;
        /// <summary>The kinetic AmmoType instance this vehicle's clips actually use.</summary>
        private static AmmoType _vehicleApType;
        internal static AmmoClipCodexScriptable VanillaHeatClip;

        /// <summary>
        /// Renames/re-stats EVERY 120 mm DM23 round found in the loaded assets.  The game keeps more
        /// than one instance (the one a clip points at and the one the HUD read can differ), and the
        /// HUD samples AmmoType.Name every frame - so all copies have to carry our values.
        /// </summary>

        /// <summary>Strict name-only lookup, for the early path where no loadout exists yet.</summary>
        private static void FindDonorsByName()
        {
            foreach (var clipCodex in Resources.FindObjectsOfTypeAll<AmmoClipCodexScriptable>())
            {
                var type = TypeOf(clipCodex);
                if (type == null || !Is120mm(type) || IsHeatAmmo(type))
                    continue;
                string n = (type.Name ?? "").ToUpperInvariant();
                if (n.Contains("DM23"))
                {
                    _donorAp = type;
                    VanillaApClip = clipCodex;
                    return;
                }
            }
        }

        /// <summary>
        /// Finds the vanilla 120 mm clips by scanning the loaded resources instead of trusting the
        /// vehicle loadout: another mod (Fulda 1989) rewrites loadouts, so by the time we look the
        /// loadout may already hold its DM33 - and a snapshot taken then would preserve the wrong
        /// round forever.
        /// </summary>
        private static void FindDonors(LoadoutManager lm)
        {
            // 1) Prefer the round the vehicle already carries: that is the one the gun accepts, so
            // retuning it can never upset the loadout or the gun.
            var clips = lm != null && lm.LoadedAmmoList != null ? lm.LoadedAmmoList.AmmoClips : null;
            if (clips != null)
            {
                foreach (var cc in clips)
                {
                    var type = TypeOf(cc);
                    if (type == null)
                        continue;
                    if (!Is120mm(type))
                        continue;
                    if (_donorAp == null && !IsHeatAmmo(type))
                    {
                        _donorAp = type;
                        VanillaApClip = cc;
                    }
                    else if (_donorHeat == null && IsHeatAmmo(type))
                    {
                        _donorHeat = type;
                        VanillaHeatClip = cc;
                    }
                }
            }

            if (_donorAp != null)
                return;

            // 2) Fallback: strict name match in the resource pool (never "the first kinetic clip" -
            // the API order is arbitrary and a foreign round breaks the whole mission).
            foreach (var clipCodex in Resources.FindObjectsOfTypeAll<AmmoClipCodexScriptable>())
            {
                var type = TypeOf(clipCodex);
                if (type == null || !Is120mm(type) || IsHeatAmmo(type))
                    continue;
                string n = (type.Name ?? "").ToUpperInvariant();
                if (n.Contains("DM23"))
                {
                    _donorAp = type;
                    VanillaApClip = clipCodex;
                    break;
                }
            }
        }

        /// <summary>
        /// True for 120 mm rounds.  Needed because the M1's 105 mm DM23 has the same name and a
        /// plausible mass - retuning it silently turned our tank's gun into a 105 mm one.
        /// </summary>
        private static bool Is120mm(AmmoType type)
        {
            if (type == null)
                return false;
            float cal;
            try { cal = type.GetFinalCaliber(); }
            catch (Exception) { return false; }
            return cal > 114f && cal < 126f;
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


    /// <summary>

    }
}
