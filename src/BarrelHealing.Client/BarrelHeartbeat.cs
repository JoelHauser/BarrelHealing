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

        // How often the local scan runs. This is an OverlapSphere over a few metres, not a
        // scene sweep -- see BarrelDiscovery for why that distinction cost a rewrite.
        private const float ScanInterval = 1f;

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

                BarrelDiscovery.Reset();
                HealingTick.Reset();

                var barrels = new List<Vector3>();
                var unlitNearby = new List<Transform>();
                var prompts = new List<BonfireSwitch>();
                var timer = 0f;
                var scanTimer = ScanInterval;

                while (InRaid())
                {
                    yield return tick;

                    if (!InRaid())
                    {
                        break;
                    }

                    var player = Singleton<GameWorld>.Instance?.MainPlayer;

                    if (player == null)
                    {
                        continue;
                    }

                    scanTimer += TickInterval;

                    if (scanTimer >= ScanInterval)
                    {
                        scanTimer = 0f;
                        BarrelDiscovery.ScanNear(player.Position, barrels, unlitNearby);
                        PrepareIgnition(unlitNearby, prompts, barrels);
                    }

                    timer = HealingTick.Update(barrels, timer, TickInterval);
                    RefreshIgnitionPrompts(prompts);
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
        /// Gives every newly streamed-in unlit bonfire a Light prompt, and wires it so
        /// lighting one drops its position straight into the same list HealingTick already
        /// polls -- a freshly lit fire starts healing on the very next tick.
        ///
        /// Runs on every rediscovery pass, not once: unlit bonfires appear as the player
        /// approaches them, exactly like the lit ones (see BarrelDiscovery). Already-prepared
        /// roots are skipped by checking for the component this added last time.
        /// </summary>
        private static void PrepareIgnition(List<Transform> unlitNearby, List<BonfireSwitch> switches, List<Vector3> barrels)
        {
            var donor = BarrelDiscovery.LastLitRoot;

            // No burning fire seen yet this raid, so there is nothing to copy flames from.
            if (donor == null)
            {
                return;
            }

            foreach (var unlitRoot in unlitNearby)
            {
                if (unlitRoot.GetComponent<BonfireSwitch>() != null)
                {
                    continue;
                }

                var bonfireSwitch = BonfireIgnition.Prepare(unlitRoot, donor);
                bonfireSwitch.OnLit = () => barrels.Add(bonfireSwitch.transform.position);
                switches.Add(bonfireSwitch);
                BarrelHealingPlugin.Log.LogInfo(
                    $"[BarrelHealing] Light prompt attached to {TransformNameMatch.PathOf(unlitRoot)} @ {unlitRoot.position}");
            }
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
