using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using GHPC;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace Leo2A4M
{
    /// <summary>
    /// The vehicle builds its armour list inside Awake, so add-on pieces must exist before that.
    /// Unity calls the MOST DERIVED Awake, which is why patching <c>Vehicle.Awake</c> alone never
    /// fired for this unit — every declared Awake in the Unit hierarchy is patched instead.
    /// </summary>
    internal static class SpawnHook
    {
        internal static bool Pending;
    }

    [HarmonyPatch]
    internal static class UnitAwakePatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var found = new List<MethodBase>();
            try
            {
                var asm = typeof(Unit).Assembly;
                foreach (var t in asm.GetTypes())
                {
                    if (!typeof(Unit).IsAssignableFrom(t) || t.IsAbstract)
                        continue;
                    for (var cur = t; cur != null && typeof(Unit).IsAssignableFrom(cur); cur = cur.BaseType)
                    {
                        var m = cur.GetMethod("Awake",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                        if (m != null && !found.Contains(m))
                            found.Add(m);
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] Awake scan failed: " + ex.Message);
            }
            return found;
        }

        private static void Prefix(Unit __instance)
        {
            if (!SpawnHook.Pending)
                return;
            var vic = __instance as Vehicle;
            if (vic == null)
                return;
            SpawnHook.Pending = false;
            try
            {
                MelonLogger.Msg("[Leo2A4M] pre-Awake armour build on " + vic.name);
                AddOnArmour.Build(vic);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning("[Leo2A4M] pre-Awake armour failed: " + ex.Message);
            }
        }
    }
}
