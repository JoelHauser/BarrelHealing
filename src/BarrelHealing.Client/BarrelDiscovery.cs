using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BarrelHealing.Client
{
    /// <summary>
    /// Finds the burning barrels once, when the raid starts.
    ///
    /// There is no burning-barrel class to look for -- nothing in Assembly-CSharp
    /// describes one, so a barrel is a mesh, a particle system and a light with no
    /// script of its own. docs/barrels.md's AssetRipper research claimed the shipped
    /// assets never call it `barrel` -- that was wrong, just under-sampled: the first
    /// raid test (2026-09-14, Shoreline) found the real, actually-instantiated root
    /// named `barrel_fire_wfire`, under a parent folder literally called `barrels`.
    /// `bonfire`/`brazier` are kept in the pattern since they came from a real (if
    /// unconfirmed-live) asset dump, but `barrel_fire` is the one seen working.
    ///
    /// **Starting from the particle systems and lights is what filters out the unlit
    /// ones.** A lit bonfire owns `TorchFire`, `barrel_fire_smoke` and
    /// `barrel_fire_heat` children; an unlit one is `model`, `model_lod` and `shadow`
    /// and nothing else, so it has no emitter to be found by and never reaches this
    /// list. Searching the scene for the name instead would collect both and heal the
    /// player at a cold barrel.
    /// </summary>
    internal static class BarrelDiscovery
    {
        internal static List<Vector3> Find()
        {
            var log = BarrelHealingPlugin.Log;
            var found = new List<Vector3>();

            Regex pattern;
            try
            {
                pattern = new Regex(BarrelHealingPlugin.BarrelNamePattern.Value, RegexOptions.IgnoreCase);
            }
            catch (System.ArgumentException ex)
            {
                log.LogError("[BarrelHealing] barrel name pattern will not compile, finding nothing: " + ex.Message);
                return found;
            }

            var claimed = new HashSet<Transform>();

            // includeInactive: true is load-bearing, not defensive. EFT culls these fire props
            // until the player is near one, and FindObjectsOfType excludes inactive objects by
            // default -- so this one-shot raid-start scan ran with every fire on the map
            // deactivated and found nothing at all (first raid test, 2026-09-14). An inactive
            // particle system still has a valid transform.position, which is all this needs.
            foreach (var particles in UnityEngine.Object.FindObjectsOfType<ParticleSystem>(true))
            {
                Consider(particles.transform, pattern, claimed, found);
            }

            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>(true))
            {
                Consider(light.transform, pattern, claimed, found);
            }

            log.LogInfo($"[BarrelHealing] found {found.Count} fire object(s) in this raid");
            return found;
        }

        private static void Consider(Transform transform, Regex pattern, HashSet<Transform> claimed, List<Vector3> found)
        {
            var match = TransformNameMatch.MatchingAncestor(transform, pattern);

            if (match == null)
            {
                return;
            }

            // One barrel is a light and several particle systems under a shared parent.
            // Claiming the matched ancestor collapses those into a single position.
            if (!claimed.Add(match))
            {
                return;
            }

            found.Add(match.position);
            BarrelHealingPlugin.Log.LogInfo($"[BarrelHealing]   {TransformNameMatch.PathOf(match)} @ {match.position}");
        }
    }
}
