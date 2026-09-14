using System;
using System.Collections;
using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace BarrelHealing.Client
{
    /// <summary>
    /// One raid's worth of state, and the loop that notices raids starting and ending.
    ///
    /// Polling rather than patching is the same choice SPT-Casino's task bar made: a
    /// poll survives a method being renamed by the next obfuscator pass, and this mod
    /// only ever needs to read state that is already public.
    /// </summary>
    internal static class BarrelHeartbeat
    {
        private const float TickInterval = 0.5f;
        private const float IdleInterval = 1f;

        internal static IEnumerator Run()
        {
            var idle = new WaitForSeconds(IdleInterval);
            var tick = new WaitForSeconds(TickInterval);

            while (true)
            {
                if (!InRaid())
                {
                    yield return idle;
                    continue;
                }

                List<Vector3> barrels = BarrelDiscovery.Find();
                List<BonfireSwitch> unlit = PrepareIgnition(barrels);
                var timer = 0f;

                while (InRaid())
                {
                    yield return tick;

                    if (!InRaid())
                    {
                        break;
                    }

                    timer = HealingTick.Update(barrels, timer, TickInterval);
                    RefreshIgnitionPrompts(unlit);
                }

                // Raid over: the caches and the timer go out of scope with this iteration.
            }
        }

        private static bool InRaid()
        {
            if (!Singleton<GameWorld>.Instantiated)
            {
                return false;
            }

            var world = Singleton<GameWorld>.Instance;

            // Unity's ==, not a plain reference check: a torn-down GameWorld is not null
            // to the latter, and Instantiated keeps reporting it as present.
            if (world == null)
            {
                return false;
            }

            // The hideout and the narrated scenes are GameWorlds without being raids.
            // Not an `is` check: HideoutGameWorld and NarrateGameWorld are internal as of
            // EFT 0.16.9.5 build 40743, so the names cannot be written here at all and
            // the base chain has to be walked at runtime instead.
            var worldType = world.GetType();
            return !DerivesFromNamed(worldType, "HideoutGameWorld")
                && !DerivesFromNamed(worldType, "NarrateGameWorld");
        }

        private static bool DerivesFromNamed(Type type, string name)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.Name == name)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// One-time-per-raid setup: find the unlit bonfires, give each a Light prompt, and
        /// wire it so lighting one drops its position straight into the same list HealingTick
        /// already polls -- a freshly lit fire starts healing on the very next tick, no
        /// separate discovery pass needed.
        /// </summary>
        private static List<BonfireSwitch> PrepareIgnition(List<Vector3> barrels)
        {
            var discovery = BonfireDiscovery.Find();
            var switches = new List<BonfireSwitch>();

            if (discovery.Donor == null)
            {
                return switches;
            }

            foreach (var unlitRoot in discovery.Unlit)
            {
                var bonfireSwitch = BonfireIgnition.Prepare(unlitRoot, discovery.Donor);
                bonfireSwitch.OnLit = () => barrels.Add(bonfireSwitch.transform.position);
                switches.Add(bonfireSwitch);
            }

            return switches;
        }

        /// <summary>
        /// Keeps each unlit bonfire's Operatable flag in sync with whether the player is
        /// currently carrying a lighter or matches -- the button is greyed out otherwise.
        /// A poll, not a per-instance Update(): there are at most a handful of these per map.
        /// </summary>
        private static void RefreshIgnitionPrompts(List<BonfireSwitch> unlit)
        {
            if (unlit.Count == 0)
            {
                return;
            }

            var inventoryController = Singleton<GameWorld>.Instance?.MainPlayer?.InventoryController;
            var hasSource = BonfireIgnition.HasIgnitionSource(inventoryController);

            foreach (var bonfireSwitch in unlit)
            {
                if (bonfireSwitch == null || bonfireSwitch.Lit)
                {
                    continue;
                }

                bonfireSwitch.Operatable = hasSource;
            }
        }
    }
}
