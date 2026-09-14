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
                var timer = 0f;

                while (InRaid())
                {
                    yield return tick;

                    if (!InRaid())
                    {
                        break;
                    }

                    timer = HealingTick.Update(barrels, timer, TickInterval);
                }

                // Raid over: the cache and the timer go out of scope with this iteration.
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
    }
}
