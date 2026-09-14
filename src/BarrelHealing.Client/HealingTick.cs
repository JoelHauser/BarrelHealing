using System.Collections.Generic;
using Comfort.Common;
using EFT;
using EFT.Ballistics;
using EFT.HealthSystem;
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

        // A barrel has its own collider and the ray ends *inside* it -- many discovered
        // positions are the flame/heat particle system, which sits down in the drum. So the
        // ray reliably strikes the barrel's own shell before the endpoint. 0.5m was too tight
        // for that and rejected fires the player was standing right at; a drum is ~0.6m
        // across, so anything within a metre of the far end is the barrel, not a wall.
        private const float EndpointSlack = 1f;

        // How near a known fire the player has to be before the "why am I not healing" line
        // is worth printing at all.
        private const float ReportRange = 12f;
        private const float ReportInterval = 2f;

        private static float _reportTimer;

        // The heal rate currently registered with the health controller, in HP per *minute*
        // -- see SetHealRate. Kept so it can be handed back on deregistration.
        private static float _registeredRate;

        /// <summary>Clears per-raid state. The rate is registered on a health controller that
        /// dies with the raid, so only the local tracking needs resetting.</summary>
        internal static void Reset()
        {
            _reportTimer = 0f;
            _registeredRate = 0f;
        }

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
                if (timer > 0f)
                {
                    BarrelHealingPlugin.Log.LogInfo("[BarrelHealing] left the fire");
                }

                SetHealRate(health, 0f);
                ReportNearest(player.Position, barrels, tickInterval);
                return 0f;
            }

            _reportTimer = 0f;

            // timer == 0 means last tick was out of range, so this is the moment of arrival.
            if (timer == 0f)
            {
                BarrelHealingPlugin.Log.LogInfo(
                    $"[BarrelHealing] at a fire -- warming up for {BarrelHealingPlugin.DelayBeforeHealing.Value}s");
            }

            timer += tickInterval;

            if (timer < BarrelHealingPlugin.DelayBeforeHealing.Value)
            {
                // Still warming up, so nothing is being healed yet and the arrow should not
                // be promising otherwise.
                SetHealRate(health, 0f);
                return timer;
            }

            var limb = LimbPriority.PickNextLimb(health);

            if (!limb.HasValue)
            {
                // Nothing left that can be healed -- every limb either full or destroyed.
                SetHealRate(health, 0f);
                return timer;
            }

            SetHealRate(health, BarrelHealingPlugin.HealRatePerSecond.Value);

            // NOT ActiveHealthController.Heal(). That method exists, is public, takes exactly
            // the arguments you would want -- and its body is empty in this build. It compiled,
            // ran 46 times in a raid, and moved the player's health by nothing at all. Use
            // ChangeHealth: positive value heals, it clamps to the limb's maximum itself, and
            // it does the NetworkSyncBodyHealth/HealthChangedEvent work that makes the UI and
            // the rest of the game notice.
            health.ChangeHealth(
                limb.Value,
                BarrelHealingPlugin.HealRatePerSecond.Value * tickInterval,
                default(DamageInfo));

            var after = health.GetBodyPartHealth(limb.Value);
            BarrelHealingPlugin.Log.LogInfo(
                $"[BarrelHealing] healing {limb.Value} -> {after.Current:F0}/{after.Maximum:F0}");

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

        /// <summary>
        /// Drives the green up-arrow next to the health readout.
        ///
        /// That arrow is not produced by changing HP -- it reads a *rate* registered on the
        /// health controller, which is why healing worked for a while with no arrow at all.
        /// `ChangeHealth` moves the number; this declares "and it is currently moving at N".
        ///
        /// Two things worth knowing. The rate is stored **per minute**, not per second --
        /// `Effect.SetHealthRatesPerSecond` takes a per-second figure and does `health *= 60f`
        /// before handing it on, so the same conversion happens here and a 3 HP/s barrel
        /// correctly reads as 180/min. And this is display only: `HealRate` is read by exactly
        /// one property (`HealthRate`) and the network serialiser, and is never applied to a
        /// body part anywhere, so registering it cannot double-heal the player.
        ///
        /// `ChangeHealthRate(old, new)` subtracts the old figure and adds the new one, so the
        /// previously registered value has to be handed back to clear it -- hence _registeredRate.
        /// Leaving it set on exit would strand a permanent arrow on the HUD.
        /// </summary>
        private static void SetHealRate(ActiveHealthController health, float perSecond)
        {
            var perMinute = perSecond * 60f;

            if (Mathf.Approximately(_registeredRate, perMinute))
            {
                return;
            }

            health.ChangeHealthRate(_registeredRate, perMinute);
            _registeredRate = perMinute;
            health.NetworkSyncHealthRates();
        }

        private static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            RaycastHit hit;

            if (!Physics.Linecast(from, to, out hit, LineOfSightMask()))
            {
                return true;
            }

            return hit.distance >= Vector3.Distance(from, to) - EndpointSlack;
        }

        /// <summary>
        /// The wall check should test map geometry and nothing else. The old default of -1
        /// meant every layer, so the ray could be stopped by a trigger volume, a loot
        /// collider, or the fire's own heat/damage volume -- none of which are a wall.
        /// 0 means "ask the game": HighPolyWithTerrainMask is terrain plus high-poly
        /// collision, which is what EFT itself uses for this kind of sight test.
        /// </summary>
        private static int LineOfSightMask()
        {
            var configured = BarrelHealingPlugin.EnvironmentLayerMask.Value;
            return configured == 0 ? LayersMaskController.HighPolyWithTerrainMask : configured;
        }

        /// <summary>
        /// Says, at most every couple of seconds and only when a fire is actually nearby, why
        /// the player is stood next to one and not healing. Without this the failure mode is
        /// silence, which reads identically to the mod not being loaded.
        /// </summary>
        private static void ReportNearest(Vector3 position, List<Vector3> barrels, float tickInterval)
        {
            _reportTimer += tickInterval;

            if (_reportTimer < ReportInterval)
            {
                return;
            }

            var nearest = Vector3.zero;
            var nearestDistance = float.MaxValue;

            foreach (var barrel in barrels)
            {
                var distance = Vector3.Distance(position, barrel);

                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearest = barrel;
                }
            }

            if (nearestDistance > ReportRange)
            {
                return;
            }

            _reportTimer = 0f;

            var radius = BarrelHealingPlugin.HealRadius.Value;

            if (nearestDistance > radius)
            {
                BarrelHealingPlugin.Log.LogInfo(
                    $"[BarrelHealing] nearest fire {nearestDistance:F1}m away, need {radius:F1}m");
                return;
            }

            var eye = position + Vector3.up * ChestHeight;
            RaycastHit hit;
            var blocked = Physics.Linecast(eye, nearest, out hit, LineOfSightMask());
            var blocker = blocked ? $"{hit.collider.name} at {hit.distance:F1}m" : "nothing";

            BarrelHealingPlugin.Log.LogInfo(
                $"[BarrelHealing] fire {nearestDistance:F1}m away (in range) but no line of sight -- "
                + $"ray hit {blocker}, needs to reach {Vector3.Distance(eye, nearest) - EndpointSlack:F1}m");
        }
    }
}
