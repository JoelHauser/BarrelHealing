using System.Collections.Generic;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace BarrelHealing.Client
{
    /// <summary>
    /// One poll: are we stood at a barrel we can actually see, and if so, for long enough?
    /// </summary>
    internal static class HealingTick
    {
        // The wall check runs from roughly chest height so it does not clip the floor
        // the player is stood on.
        private const float ChestHeight = 1f;

        // A barrel has its own collider, and the ray ends inside it. Anything struck
        // this close to the far end is the barrel itself, not a wall in front of it.
        private const float EndpointSlack = 0.5f;

        /// <summary>Returns the new proximity timer.</summary>
        internal static float Update(List<Vector3> barrels, float timer, float tickInterval)
        {
            if (barrels == null || barrels.Count == 0)
            {
                return 0f;
            }

            var world = Singleton<GameWorld>.Instance;

            if (world == null)
            {
                return 0f;
            }

            var player = world.MainPlayer;

            if (player == null)
            {
                return 0f;
            }

            var health = player.ActiveHealthController;

            if (health == null)
            {
                return 0f;
            }

            if (!AtAVisibleBarrel(player.Position, barrels))
            {
                return 0f;
            }

            timer += tickInterval;

            if (timer < BarrelHealingPlugin.DelayBeforeHealing.Value)
            {
                return timer;
            }

            var limb = LimbPriority.PickNextLimb(health);

            if (limb.HasValue)
            {
                health.Heal(limb.Value, BarrelHealingPlugin.HealRatePerSecond.Value * tickInterval);
            }

            return timer;
        }

        private static bool AtAVisibleBarrel(Vector3 position, List<Vector3> barrels)
        {
            var radius = BarrelHealingPlugin.HealRadius.Value;
            var eye = position + Vector3.up * ChestHeight;

            foreach (var barrel in barrels)
            {
                if (Vector3.Distance(position, barrel) > radius)
                {
                    continue;
                }

                if (HasLineOfSight(eye, barrel))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            RaycastHit hit;

            if (!Physics.Linecast(from, to, out hit, BarrelHealingPlugin.EnvironmentLayerMask.Value))
            {
                return true;
            }

            return hit.distance >= Vector3.Distance(from, to) - EndpointSlack;
        }
    }
}
