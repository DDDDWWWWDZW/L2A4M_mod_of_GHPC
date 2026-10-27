using System;
using System.IO;
using System.Linq;
using System.Reflection;
using GHPC;
using GHPC.Utility;
using HarmonyLib;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;
using UnityEngine.Rendering.PostProcessing;

namespace Leo2A4M
{
    public static class CitvAssets
    {
        public static GameObject Monitor;
        public static GameObject FlirPost;
        public static Material FlirBlit;
        public static CameraSlot SourceSlot;
        private static AssetBundle _monitorBundle;

        public static void Load(Vehicle vic)
        {
            if (Monitor == null)
                LoadMonitor();

            var flir = vic.transform.Find("leo2_rig/hull/turret/---TURRET SCRIPTS---/GPS/FLIR");
            if (flir == null)
            {
                var fcs = vic.GetComponentInChildren<GHPC.Weapons.FireControlSystem>(true);
                flir = fcs != null ? fcs.transform.Find("FLIR") : null;
            }

            if (flir != null)
            {
                var slot = flir.GetComponent<CameraSlot>();
                if (slot != null)
                    SourceSlot = slot;
                if (slot != null && slot.FLIRBlitMaterialOverride != null)
                {
                    var shader = Shader.Find("Blit (FLIR)/Blit Simple");
                    if (shader != null)
                    {
                        FlirBlit = new Material(shader);
                        FlirBlit.CopyPropertiesFromMaterial(slot.FLIRBlitMaterialOverride);
                        FlirBlit.SetTexture("_PixelCookie", null);
                        if (FlirBlit.HasProperty("_WobbleAmp")) FlirBlit.SetFloat("_WobbleAmp", 0f);
                        if (FlirBlit.HasProperty("_Blur")) FlirBlit.SetFloat("_Blur", 0f);
                        FlirBlit.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }
                }

                var post = flir.Find("FLIR Post Processing - Green");
                if (post != null)
                    FlirPost = post.gameObject;
            }

            if (FlirBlit != null)
            {
                var sb = new System.Text.StringBuilder();
                foreach (var pn in FlirBlit.GetPropertyNames(MaterialPropertyType.Float))
                    sb.Append(pn).Append('=').Append(FlirBlit.GetFloat(pn)).Append(' ');
                sb.Clear();
                foreach (var pn in FlirBlit.GetTexturePropertyNames())
                    sb.Append(pn).Append('=').Append(FlirBlit.GetTexture(pn) != null ? "tex" : "null").Append(' ');
            }

            MelonLogger.Msg("[Leo2A4M] CITV assets monitor=" + (Monitor != null) +
                " post=" + (FlirPost != null) + " mat=" + (FlirBlit != null));
        }

        private static void LoadMonitor()
        {
            string ownPath = Path.Combine(MelonEnvironment.ModsDirectory, "Leo2A4M", "citv_monitor");
            if (File.Exists(ownPath))
            {
                _monitorBundle = AssetBundle.LoadFromFile(ownPath);
                if (_monitorBundle != null)
                {
                    Monitor = _monitorBundle.LoadAsset<GameObject>("CITV MONITOR");
                    if (Monitor != null)
                        Monitor.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
            }

            if (Monitor == null)
            {
                Monitor = Resources.FindObjectsOfTypeAll<GameObject>()
                    .FirstOrDefault(g => g != null && g.name == "CITV MONITOR");
            }

            if (Monitor == null)
            {
                string legacyPath = Path.Combine(MelonEnvironment.ModsDirectory, "M1A2C", "citv_monitor");
                if (File.Exists(legacyPath))
                {
                    _monitorBundle = AssetBundle.LoadFromFile(legacyPath);
                    if (_monitorBundle != null)
                    {
                        Monitor = _monitorBundle.LoadAsset<GameObject>("CITV MONITOR");
                        if (Monitor != null)
                            Monitor.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    }
                }
            }
        }
    }
    public class CITVManager : MonoBehaviour
    {
        public GameObject monitor;
        public GameObject wfov;
        public GameObject nfov;
        private Transform _reticle;
        private FieldInfo _snapp;
        private FieldInfo _maxDistance;
        private bool _citvActive;
        private bool _lastCitvActive;

        public static void Init()
        {
            if (GameObject.Find("Leo2A4M CITV manager") != null)
                return;
            var root = GameObject.Find("_APP_GHPC_");
            if (root == null)
                return;

            var obj = new GameObject("Leo2A4M CITV manager");
            obj.transform.SetParent(root.transform);
            obj.AddComponent<CITVManager>();
            MelonLogger.Msg("[Leo2A4M] CITV manager created");
        }

        private void Awake()
        {
            if (CitvAssets.Monitor == null)
            {
                enabled = false;
                return;
            }

            var root = GameObject.Find("_APP_GHPC_");
            var canvas = root.transform.Find("UIHUDCanvas");
            if (canvas != null)
                _reticle = canvas.Find("3P aim reticle");

            _snapp = typeof(BufferedCameraFollow).GetField("SNAPPINESS", BindingFlags.Static | BindingFlags.NonPublic);
            _maxDistance = typeof(BufferedCameraFollow).GetField("MAX_AIM_DISTANCE", BindingFlags.Instance | BindingFlags.NonPublic);

            monitor = Instantiate(CitvAssets.Monitor, root.transform);
            monitor.transform.localPosition = Vector3.zero;
            monitor.transform.localEulerAngles = Vector3.zero;
            var c = monitor.GetComponent<Canvas>();
            if (c != null)
            {
                c.overrideSorting = true;
                c.sortingOrder = 100;
            }
            monitor.transform.SetAsLastSibling();

            foreach (Transform child in monitor.GetComponentsInChildren<Transform>(true))
            {
                string n = child.name.ToLowerInvariant();
                if (n.Contains("cross") || n.Contains("reticle") || n.Contains("frame"))
                    child.gameObject.SetActive(false);
            }

            monitor.SetActive(false);

            nfov = monitor.transform.Find("nfov") != null ? monitor.transform.Find("nfov").gameObject : null;
            wfov = monitor.transform.Find("wfov") != null ? monitor.transform.Find("wfov").gameObject : null;
        }

        private Vector3 CitvAimPoint
        {
            get
            {
                var follow = CameraManager.Instance != null ? CameraManager.Instance.CameraFollow : null;
                if (follow == null || follow.BufferedCamera == null || _maxDistance == null)
                    return follow != null && follow.BufferedCamera != null
                        ? follow.BufferedCamera.transform.position + follow.CurrentAimVector * 3000f
                        : Vector3.zero;

                float max = (float)_maxDistance.GetValue(follow.BufferedCamera);
                Ray ray = new Ray(follow.BufferedCamera.transform.position, follow.CurrentAimVector);
                RaycastHit hit;
                if (!Physics.Raycast(ray, out hit, max, ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask.value))
                    return follow.BufferedCamera.transform.position + ray.direction * max;
                return hit.point;
            }
        }

        private void Update()
        {
            var camSlot = CameraManager.Instance != null ? CameraManager.Instance._currentCamSlot : null;
            bool active = camSlot != null && camSlot.GetComponent<CITV>() != null;
            if (!active && camSlot != null)
                active = string.Equals(camSlot.gameObject.name, "NODs", StringComparison.OrdinalIgnoreCase);
            _citvActive = active;
            if (camSlot != null && active != _lastCitvActive)
            {
                _lastCitvActive = active;
            }

            if (monitor != null)
                monitor.SetActive(active);
            _citvActive = active;
            if (_reticle != null)
                _reticle.gameObject.SetActive(!active);

            if (active && nfov != null && wfov != null)
            {
                bool narrow = camSlot.CurrentFov < 10f;
                nfov.SetActive(narrow);
                wfov.SetActive(!narrow);
            }

            if (Leo2A4MMod.CitvSmooth != null && Leo2A4MMod.CitvSmooth.Value)
            {
                if (_snapp != null)
                    _snapp.SetValue(null, active ? 0.20f : 10f);
                if (active && InputUtil.MainPlayer != null && InputUtil.MainPlayer.GetButton("Smooth Aim"))
                {
                    var weapon = PlayerInput.Instance != null ? PlayerInput.Instance.CurrentPlayerWeapon : null;
                    if (weapon != null && weapon.Weapon != null && weapon.Weapon.FCS != null)
                        weapon.Weapon.FCS.SetAimWorldPosition(CitvAimPoint);
                }
            }
        }
    }

    /// <summary>Shared flag: true while the player is looking through the CITV.  The Harmony
    /// prefixes below scale the camera's traverse/elevate input, which is the path the optic
    /// actually pans through (aimSensitivity3P only feeds the third-person camera).</summary>
    public static class CitvState
    {
        public static bool Active;
        public static float Scale = 1f;
        public static int ScaledCalls;
        public static bool LoggedFirstCall;
    }

    [HarmonyPatch(typeof(GHPC.Camera.BufferedCameraFollow), "TraverseBy")]
    public static class CitvTraversePatch
    {
        private static void Prefix(ref float __0)
        {
            if (!CitvState.Active)
                return;
            __0 *= CitvState.Scale;
            CitvState.ScaledCalls++;
            if (!CitvState.LoggedFirstCall)
            {
                CitvState.LoggedFirstCall = true;
                MelonLogger.Msg("[Leo2A4M] CITV traverse patch active, scale " + CitvState.Scale.ToString("F3"));
            }
        }
    }

    [HarmonyPatch(typeof(GHPC.Camera.BufferedCameraFollow), "ElevateBy")]
    public static class CitvElevatePatch
    {
        private static void Prefix(ref float __0)
        {
            if (!CitvState.Active)
                return;
            __0 *= CitvState.Scale;
        }
    }

    public class CitvFovFix : MonoBehaviour
    {
        private CameraSlot slot;

        /// <summary>Player-tunable multiplier for the sight's aiming speed.</summary>
        public static float Sensitivity()
        {
            var e = Leo2A4MMod.CitvSens;
            return e != null ? Mathf.Clamp(e.Value, 0.05f, 3f) : 0.3f;
        }

        private void Awake()
        {
            slot = GetComponent<CameraSlot>();
        }

        private CITV _citv;
        private float _logTimer;

        private void LateUpdate()
        {
            var cm = CameraManager.Instance;
            if (cm == null || slot == null)
                return;

            if (_citv == null)
                _citv = GetComponent<CITV>();
            bool active = (_citv != null && _citv.IsActive) || cm._currentCamSlot == slot;
            CitvState.Active = active;

            // No FOV term: scaling by slot.CurrentFov made the feel change from level to level
            // (a narrower default FOV on another map made the sight crawl).  The multiplier is the
            // player setting and nothing else.
            CitvState.Scale = Mathf.Clamp(Sensitivity(), 0.05f, 3f);


            if (!active)
            {
                if (cm.CameraFollow != null)
                    cm.CameraFollow.aimSensitivity3P = 5f;
                return;
            }

            float fov = slot.CurrentFov;
            if (fov <= 1f || fov > 30f)
                fov = slot.DefaultFov > 1f ? slot.DefaultFov : 15f;

            if (cm.CameraFollow != null)
                cm.CameraFollow.aimSensitivity3P = 1.25f * (fov / 60f) * Sensitivity();
        }
    }

    public class CITV : MonoBehaviour
    {
        public GameObject model;
        public bool IsActive { get; private set; }

        private CameraSlot nods;
        private PostProcessVolume _volume;
        private static GHPC.Camera.BufferedCameraFollow camera;
        private int id;
        private float curr_sens = 5f;

        private void Awake()
        {
            camera = CameraManager.Instance != null ? CameraManager.Instance.CameraFollow : null;
            nods = GetComponent<CameraSlot>();
            id = gameObject.GetInstanceID();
        }

        public void Setup(Vehicle unit, Vector3 localPosition)
        {
            nods = GetComponent<CameraSlot>();
            if (nods == null)
                return;

            id = gameObject.GetInstanceID();
            // WT PERI R17A2 sits on the turret roof, left of centre; the camera is placed at its head
            nods.transform.localPosition = parentSpace(unit, nods.transform, localPosition);
            // Aim trim (degrees): the gun lands a *constant* angle below the crosshair (measured
            // ~32 px at both 350 m and 950 m, i.e. ~0.22 deg), so pitching the sight down by that
            // much makes the crosshair sit on the impact line.  A constant pitch fits every range,
            // while a ballistic drop would grow with distance -- which is why the earlier
            // drop-compensation attempts could not fix it.
            ApplyPitchTrim();

            var nativeReticle = nods.transform.Find("Reticle Mesh");
            if (nativeReticle != null)
            {
                nativeReticle.gameObject.SetActive(false);
                foreach (var r in nativeReticle.GetComponentsInChildren<Renderer>(true))
                    r.enabled = false;
            }

            nods.VisionType = NightVisionType.Thermal;
            nods.IsExterior = false;
            nods.BaseBlur = 0f;
            nods.OverrideFLIRResolution = true;
            nods.CanToggleFlirPolarity = false;
            // Match the gunner's sight sharpness: no extra blur, full FLIR resolution.
            // Set "CITV Gen-1 Look (Leo2A4M)" = true for the soft 320x240 first-gen picture.
            bool gen1 = Leo2A4MMod.CitvGen1 != null && Leo2A4MMod.CitvGen1;
            if (gen1)
            {
                nods.FLIRWidth = 320;
                nods.FLIRHeight = 240;
                nods.BaseBlur = 1.6f;
                nods.DefaultFov = 15f;
                nods.OtherFovs = new[] { 8f, 3f };
            }
            nods.SpriteType = CameraSpriteManager.SpriteType.DefaultScope;
            // Mirror the gunner's working FLIR: same blit material, same render-target size, and
            // the SimpleNightVision post path disabled (that path paints a flat green panel).
            var src = CitvAssets.SourceSlot;
            // Use OUR copy, not the donor slot's shared material: editing the shared one would
            // change the gunner's sight too, and the copy is where the sharpening lives.
            if (CitvAssets.FlirBlit != null)
                nods.FLIRBlitMaterialOverride = CitvAssets.FlirBlit;
            else if (src != null && src.FLIRBlitMaterialOverride != null)
                nods.FLIRBlitMaterialOverride = src.FLIRBlitMaterialOverride;

            // The donor renders its FLIR into 480x240 across 9 degrees; at the wider 15 degree
            // default this sight looked like a smear. Bigger target, same narrow FOV as the gunner.
            // Leopard 2A5 carries a first-generation thermal imager: the picture is coarser than
            // the M1A2C sight and it cannot magnify as far. 640x360 keeps the targets readable but
            // visibly softer, matching the early PERI image.
            int resW = Leo2A4MMod.CitvRes != null ? Mathf.Clamp(Leo2A4MMod.CitvRes, 320, 2160) : 640;
            nods.OverrideFLIRResolution = true;
            nods.FLIRWidth = resW;
            nods.FLIRHeight = resW * 9 / 16;

            // tune the thermal look: the donor ramp is low contrast, which reads as "blurry"
            if (nods.FLIRBlitMaterialOverride != null)
            {
                var m = nods.FLIRBlitMaterialOverride;
                if (m.HasProperty("_WobbleAmp")) m.SetFloat("_WobbleAmp", 0f);
                if (Leo2A4MMod.CitvContrast != null && m.HasProperty("_ColorRampExp"))
                    m.SetFloat("_ColorRampExp", Leo2A4MMod.CitvContrast);
                if (Leo2A4MMod.CitvExposure != null && m.HasProperty("_ColorRampExposure"))
                    m.SetFloat("_ColorRampExposure", Leo2A4MMod.CitvExposure);
                if (Leo2A4MMod.CitvScanlines != null && m.HasProperty("_ScanlineBrightness"))
                    m.SetFloat("_ScanlineBrightness", Leo2A4MMod.CitvScanlines);
            }
            // Wider fields of view than the M1A2C (15/10/4): the gen-1 sight tops out at ~6x,
            // so its narrowest step is 6 degrees instead of the American 4.
            nods.DefaultFov = 12f;
            nods.OtherFovs = new[] { 8f, 6f };
            nods.VibrationPreBlur = false;
            nods.VibrationShakeMultiplier = 0f;

            // The slot carries a night-vision post stack (green wash + blur + scanlines). The
            // gunner's FLIR does not use it, which is why that sight is crisp. Turn it off so the
            // CITV renders through the same blit path, then add a small deliberate softness.
            // The FLIR render target is built when the slot initialises, which happens during the
            // prefab clone - long before we get here. Toggling the component makes it rebuild with
            // the resolution set above instead of the 100x100 default it woke up with.
            if (Leo2A4MMod.CitvRebuild == null || Leo2A4MMod.CitvRebuild)
            {
                nods.enabled = false;
                nods.enabled = true;
                MelonLogger.Msg("[Leo2A4M] CITV slot re-initialised for " + nods.FLIRWidth + "x" + nods.FLIRHeight);
            }

            var simpleNv = nods.GetComponent<SimpleNightVision>();
            if (simpleNv != null)
                simpleNv.enabled = false;

            int volumes = 0;
            foreach (var ppv in nods.GetComponentsInChildren<PostProcessVolume>(true))
            {
                if (ppv == null)
                    continue;
                ppv.enabled = false;
                volumes++;
            }
            MelonLogger.Msg("[Leo2A4M] CITV post: simpleNV=" + (simpleNv != null ? simpleNv.enabled.ToString() : "none") +
                " volumes disabled=" + volumes);

            // Put the gunner sight's thermal grading on this slot and switch the volume back on
            // (the loop above turned it off): its profile is "GHPC FLIR Early Gen", while this
            // slot shipped with "GHPC NOD gen 2 US", an image-intensifier look.
            if (CitvAssets.SourceSlot != null)
            {
                var srcVol = CitvAssets.SourceSlot.GetComponent<PostProcessVolume>();
                var dstVol = nods.GetComponent<PostProcessVolume>();
                if (srcVol != null && dstVol != null)
                {
                    dstVol.sharedProfile = srcVol.sharedProfile;
                    dstVol.enabled = false;
                    _volume = dstVol;
                    MelonLogger.Msg("[Leo2A4M] CITV post profile := " +
                        (srcVol.sharedProfile != null ? srcVol.sharedProfile.name : "null") + " enabled=" + dstVol.enabled);
                }
            }

            var simple = Leo2A4MMod.CitvUseBlit != null && Leo2A4MMod.CitvUseBlit
                ? nods.GetComponent<SimpleNightVision>() : null;
            if (simple != null && CitvAssets.FlirPost != null)
            {
                var oldVolume = nods.GetComponent<PostProcessVolume>();
                if (oldVolume != null)
                    Destroy(oldVolume);

                var post = Instantiate(CitvAssets.FlirPost, nods.transform);
                var mainCam = post.transform.Find("MainCam Volume");
                if (mainCam != null)
                    mainCam.gameObject.SetActive(false);

                var flirVolume = post.transform.Find("FLIR Only Volume");
                if (flirVolume != null)
                {
                    var volume = flirVolume.GetComponent<PostProcessVolume>();
                    if (volume != null)
                    {
                        volume.enabled = true;
                        simple._postVolume = volume;
                        simple._postVolume.enabled = false;
                    }
                }
            }
        }

        private static Vector3 parentSpace(Vehicle unit, Transform t, Vector3 vehicleSpace)
        {
            if (unit == null || t == null || t.parent == null)
                return vehicleSpace;
            return t.parent.InverseTransformPoint(unit.transform.TransformPoint(vehicleSpace));
        }

        private bool _pushedAim;
        private Transform _hudReticle;
        private static System.Reflection.FieldInfo _snappiness;
        private static float _savedSnap = -1f;

        private Transform _tiltNode;

        /// <summary>The slot's own rotation is driven by the game every frame, so the aim trim lives
        /// on an intermediate node we own: `<commander head>/CITV_TILT/<slot>`.</summary>
        private float _hudProbeTimer;
        private float _aimProbeTimer;

        private static string Path(Transform t)
        {
            string s = t.name;
            while (t.parent != null && t.parent.name != "UIHUDCanvas")
            {
                t = t.parent;
                s = t.name + "/" + s;
            }
            return s;
        }
        private void ApplyPitchTrim()
        {
            if (nods == null)
                return;
            float trim = Leo2A4MMod.CitvPitchTrim != null ? Leo2A4MMod.CitvPitchTrim : 0f;
            if (Mathf.Abs(trim) < 0.001f) return;   // registered optic: no trim unless asked for

            if (_tiltNode == null)
            {
                var parent = nods.transform.parent;
                if (parent == null)
                    return;
                var existing = parent.Find("CITV_TILT");
                var go = existing != null ? existing.gameObject : new GameObject("CITV_TILT");
                _tiltNode = go.transform;
                _tiltNode.SetParent(parent, false);
                _tiltNode.localPosition = Vector3.zero;
                _tiltNode.localRotation = Quaternion.identity;
                _tiltNode.localScale = Vector3.one;
                nods.transform.SetParent(_tiltNode, true);   // keep the world pose
                MelonLogger.Msg("[Leo2A4M] CITV tilt node created under " + parent.name);
            }

            _tiltNode.localRotation = Quaternion.Euler(trim, 0f, 0f);
            _lastTrim = trim;
        }

        private float _lastTrim;
        private float _lastLaseRange = -1f;
        private float _laseWindow;
        private static bool _preCullHooked;
        private static readonly System.Collections.Generic.List<CITV> _activeCitvs =
            new System.Collections.Generic.List<CITV>();

        private void OnEnable()
        {
            // The game rewrites the sight pose after LateUpdate but before rendering, so the trim
            // has to be applied in the last callback that still reaches the render camera.
            if (!_activeCitvs.Contains(this))
                _activeCitvs.Add(this);
            if (!_preCullHooked)
            {
                _preCullHooked = true;
                Camera.onPreCull += OnPreCullCamera;
            }
        }

        private void OnDisable()
        {
            _activeCitvs.Remove(this);
            if (_activeCitvs.Count == 0 && _preCullHooked)
            {
                Camera.onPreCull -= OnPreCullCamera;
                _preCullHooked = false;
            }
        }

        private static void OnPreCullCamera(Camera cam)
        {
            if (cam == null)
                return;
            for (int i = 0; i < _activeCitvs.Count; i++)
            {
                var c = _activeCitvs[i];
                if (c != null && c.IsActive)
                {
                    c.ApplyPitchTrim();
                    // Push the aim point here, after the game has laid the gun for this frame:
                    // when the push ran in Update it was overwritten by the commander override,
                    // which is why the impact kept a fixed offset.
                    if (c._aimingNow)
                        c.PushAimPoint();
                }
            }
        }

        private void PushAimPoint()
        {
            var w = PlayerInput.Instance != null ? PlayerInput.Instance.CurrentPlayerWeapon : null;
            if (w == null || w.Weapon == null || w.Weapon.FCS == null)
                return;
            w.Weapon.FCS.SetAimWorldPosition(CitvAimPoint);
        }

        private void LateUpdate()
        {
            // The borrowed volume is global, so it must only be on while this sight is the active
            // one; leaving it enabled at load left a black FLIR vignette on the whole screen.
            if (_volume != null)
                _volume.enabled = IsActive;

            ApplyPitchTrim();

            // Same two touches the M1A2C mod makes for its CITV: hide the HUD's 3P aim reticle
            // (the sight has its own crosshair, the HUD one marks a different point) and ease the
            // camera SNAPPINESS while the sight is up.
            if (_hudReticle == null)
            {
                var app = GameObject.Find("_APP_GHPC_");
                var canvas = app != null ? app.transform.Find("UIHUDCanvas") : null;
                if (canvas != null)
                    _hudReticle = canvas.Find("3P aim reticle");
            }
            if (_hudReticle != null && _hudReticle.gameObject.activeSelf == IsActive)
                _hudReticle.gameObject.SetActive(!IsActive);

            try
            {
                if (_snappiness == null)
                    _snappiness = typeof(GHPC.Camera.BufferedCameraFollow).GetField("SNAPPINESS",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
                if (_snappiness != null)
                {
                    if (IsActive && _savedSnap < 0f)
                    {
                        _savedSnap = (float)_snappiness.GetValue(null);
                        _snappiness.SetValue(null, 0.20f);
                    }
                    else if (!IsActive && _savedSnap >= 0f)
                    {
                        _snappiness.SetValue(null, _savedSnap);
                        _savedSnap = -1f;
                    }
                }
            }
            catch (Exception) { }

            // SetAimWorldPosition slaves the fire control to that point; release it as soon as the
            // sight starts moving again (or the player leaves the sight), otherwise the commander's
            // free look would keep dragging the turret around.
            if (!_aimingNow && _pushedAim)
            {
                _pushedAim = false;
                var w = PlayerInput.Instance != null ? PlayerInput.Instance.CurrentPlayerWeapon : null;
                if (w != null && w.Weapon != null && w.Weapon.FCS != null)
                {
                    try
                    {
                        var m = w.Weapon.FCS.GetType().GetMethod("ResetLrfAimPoint",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (m != null) m.Invoke(w.Weapon.FCS, null);
                        
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning("[Leo2A4M] CITV aim release failed: " + ex.Message);
                    }
                }
            }
        }

        private void Update()
        {
            if (camera == null)
                camera = CameraManager.Instance != null ? CameraManager.Instance.CameraFollow : null;
            if (camera == null)
                return;

            if (camera.commanderHead == null || camera.commanderHead.GetComponent<CITV>() == null)
            {
                IsActive = false;
                if (model != null)
                    model.SetActive(true);
                camera.aimSensitivity3P = 5f;
                return;
            }

            if (camera.commanderHead.gameObject.GetInstanceID() != id)
            {
                IsActive = false;
                if (model != null)
                    model.SetActive(true);
                return;
            }

            IsActive = true;
            curr_sens = 1.25f * (nods.CurrentFov / 60f) * CitvFovFix.Sensitivity();
            camera.aimSensitivity3P = curr_sens;
            if (model != null)
                model.SetActive(false);

            // The commander sight only drives the gun while the commander has *taken over* the
            // weapon (GHPC's commander override -- `BufferedCameraFollow.IsCommanderMode`,
            // right mouse by default).  Outside that the sight is a free observation optic and
            // nothing is written, so panning it does not drag the turret.
            // While the override is held the fire control gets the CITV's own aim point, so a
            // lase + shot lands on the crosshair instead of a fixed offset below it.
            bool looking = CameraManager.Instance != null && CameraManager.Instance._currentCamSlot == nods;
            float moved = Vector3.Angle(nods.transform.forward, _lastSightFwd);
            _lastSightFwd = nods.transform.forward;
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
            _sightRate = moved / dt;                       // degrees per second

            bool alwaysSlave = Leo2A4MMod.CitvSlaveTurret != null && Leo2A4MMod.CitvSlaveTurret;
            // Input.GetMouseButton is the only reliable "left mouse / right mouse" reading here;
            // IsCommanderMode turned out to be true for the whole commander station (it never
            // goes false while the sight is up), which is why the turret followed the sight even
            // with the override released.  "Smooth Aim" is kept as the second candidate because
            // that is the action the 2A5 mod binds for the same job.
            bool rmb = Input.GetMouseButton(1);
            bool smoothAim = InputUtil.MainPlayer != null && InputUtil.MainPlayer.GetButton("Smooth Aim");
            // Working mode is the one that shoots straight: the commander sight drives the fire
            // control every frame (0.1.17 semantics), so the turret follows the sight and the shot
            // is solved for the line the crosshair sits on.  Free look is a temporary release of
            // that link, on its own key, so it never fights the commander override.

            // M1A2C control model: free observation by default, the sight takes the gun over while
            // the commander override button (right mouse) is held.  The push is what makes the shot
            // land on the crosshair, so it must run for the whole time the button is down.
            bool overrideHeld = looking && (Input.GetMouseButton(1) || (InputUtil.MainPlayer != null && InputUtil.MainPlayer.GetButton("Smooth Aim")));
            bool aiming = looking && (alwaysSlave || overrideHeld);
            _aimingNow = aiming;
            if (aiming)
            {
                var w = PlayerInput.Instance != null ? PlayerInput.Instance.CurrentPlayerWeapon : null;
                if (w != null && w.Weapon != null && w.Weapon.FCS != null)
                {
                    Vector3 pt = CitvAimPoint;
                    _lastPushDist = (pt - nods.transform.position).magnitude;

                    // The game's commander override lays the *bore* straight at the commander's
                    // sight line, with no ballistic compensation -- measured shots land low by the
                    // flat-fire drop for that range.  Push a point raised by that drop instead, so
                    // the override's own laying becomes correct.  The preference scales it (1 =
                    // physical drop at the nominal muzzle velocity, 0 = off).
                    float comp = Leo2A4MMod.CitvOverrideComp != null ? Leo2A4MMod.CitvOverrideComp : 1f;
                    float boreDrop = Leo2A4MMod.CitvBoreOffset != null ? Leo2A4MMod.CitvBoreOffset : 0.55f;
                    _lastDrop = 0f;
                    if (comp > 0f && _lastPushDist > 50f)
                    {
                        const float v0 = 1650f;                       // 120 mm L/44 class muzzle velocity
                        float t = _lastPushDist / v0;
                        // flight drop + the bore sits ~0.55 m below the sight line, and the override
                        // points the *bore* at the sight ray, so that offset lands short too
                        _lastDrop = 0.5f * 9.81f * t * t * comp + boreDrop;
                        pt += Vector3.up * _lastDrop;
                    }

                    w.Weapon.FCS.SetAimWorldPosition(pt);
                    _pushedAim = true;
                }
            }
        }
        private float _diagTimer;
        private Vector3 _lastSightFwd;
        private float _stillTime;
        private float _sightRate;
        private float _lastPushDist;
        private float _lastDrop;
        private float _lastAimVsCam;
        private bool _aimingNow;
        private Transform _diagGps, _diagOptic, _diagMantlet;

        private static Transform FindChildNamed(Transform root, string name)
        {
            if (root == null)
                return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != null && t.name == name)
                    return t;
            return null;
        }

        private Vehicle _unit;
        private static System.Reflection.FieldInfo _maxAimDistance;

        /// <summary>Where the CITV crosshair points.  Same recipe as the M1A2C mod: raycast from
        /// the commander camera along CurrentAimVector, using the game's laser rangefinder layer
        /// mask so our own hull/turret colliders are excluded and the range is the real one.</summary>
        private Vector3 CitvAimPoint
        {
            get
            {
                var follow = CameraManager.Instance != null ? CameraManager.Instance.CameraFollow : null;
                if (follow == null || follow.BufferedCamera == null)
                    return Vector3.zero;

                Vector3 origin = follow.BufferedCamera.transform.position;
                // The crosshair marks the *optic's optical axis*, so the ray has to be the camera's
                // own forward.  CurrentAimVector (what the M1A2C port used) is the stabilised aim
                // vector, which is not the same line as the commander optics -- using it put the
                // impact down and to the right of the crosshair instead of on it.
                Vector3 dir = follow.BufferedCamera.transform.forward;
                if (dir.sqrMagnitude < 0.5f)
                    dir = nods != null ? nods.transform.forward : Vector3.forward;
                _lastAimVsCam = Vector3.Angle(dir, follow.CurrentAimVector);

                float max = 4000f;
                try
                {
                    if (_maxAimDistance == null)
                        _maxAimDistance = typeof(GHPC.Camera.BufferedCameraFollow).GetField(
                            "MAX_AIM_DISTANCE",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                    if (_maxAimDistance != null)
                    {
                        object v = null;
                        if (_maxAimDistance.IsStatic) v = _maxAimDistance.GetValue(null);
                        else
                        {
                            try { v = _maxAimDistance.GetValue(follow); }
                            catch (Exception) { v = _maxAimDistance.GetValue(follow.BufferedCamera); }
                        }
                        if (v is float) max = (float)v;
                    }
                }
                catch (Exception) { }

                int mask = ~0;
                try { mask = GHPC.ConstantsAndInfoManager.Instance.LaserRangefinderLayerMask.value; }
                catch (Exception) { }

                RaycastHit hit;
                if (Physics.Raycast(new Ray(origin, dir), out hit, max, mask, QueryTriggerInteraction.Ignore))
                    return hit.point;
                return origin + dir * max;
            }
        }
    }
}
