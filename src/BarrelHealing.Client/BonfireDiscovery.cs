using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BarrelHealing.Client
{
    /// <summary>
    /// Finds one lit bonfire to copy fire from, and every unlit bonfire that could receive it.
    ///
    /// Deliberately separate from <see cref="BarrelDiscovery"/>, which finds fires that
    /// already heal using the broad "bonfire|brazier" pattern. This one narrows to
    /// <see cref="BarrelHealingPlugin.LightableBarrelNamePattern"/> (default "bonfire" only)
    /// -- the one brazier instance inspected (docs/barrels.md) has no fire children and no
    /// confirmed lit variant anywhere, so matching it here would offer a Light prompt with
    /// nothing to light it with.
    ///
    /// Unlit roots are found from LODGroup rather than a name search alone: every bonfire
    /// root -- lit or unlit -- carries one, so this cheaply narrows the scan before the name
    /// match runs, the same way BarrelDiscovery narrows by starting from ParticleSystem/Light.
    /// </summary>
    internal static class BonfireDiscovery
    {
        internal readonly struct Result
        {
            internal readonly Transform Donor;
            internal readonly List<Transform> Unlit;

            internal Result(Transform donor, List<Transform> unlit)
            {
                Donor = donor;
                Unlit = unlit;
            }
        }

        internal static Result Find()
        {
            var log = BarrelHealingPlugin.Log;

            Regex pattern;
            try
            {
                pattern = new Regex(BarrelHealingPlugin.LightableBarrelNamePattern.Value, RegexOptions.IgnoreCase);
            }
            catch (System.ArgumentException ex)
            {
                log.LogError("[BarrelHealing] lightable barrel pattern will not compile, ignition disabled: " + ex.Message);
                return new Result(null, new List<Transform>());
            }

            var donor = FindDonor(pattern);

            if (donor == null)
            {
                log.LogInfo("[BarrelHealing] no lit bonfire on this map to copy fire from -- ignition disabled this raid");
                return new Result(null, new List<Transform>());
            }

            var unlit = FindUnlit(pattern);
            log.LogInfo($"[BarrelHealing] ignition: donor {TransformNameMatch.PathOf(donor)}, {unlit.Count} unlit bonfire(s) found");
            return new Result(donor, unlit);
        }

        private static Transform FindDonor(Regex pattern)
        {
            var claimed = new HashSet<Transform>();
            Transform donor = null;

            foreach (var particles in Object.FindObjectsOfType<ParticleSystem>())
            {
                var match = TransformNameMatch.MatchingAncestor(particles.transform, pattern);

                if (match != null && claimed.Add(match) && donor == null)
                {
                    donor = match;
                }
            }

            return donor;
        }

        private static List<Transform> FindUnlit(Regex pattern)
        {
            var claimed = new HashSet<Transform>();
            var unlit = new List<Transform>();

            foreach (var lodGroup in Object.FindObjectsOfType<LODGroup>())
            {
                var match = TransformNameMatch.MatchingAncestor(lodGroup.transform, pattern);

                if (match == null || !claimed.Add(match))
                {
                    continue;
                }

                // Already has fire -- BarrelDiscovery already found this one for healing.
                if (match.GetComponentInChildren<ParticleSystem>() != null)
                {
                    continue;
                }

                unlit.Add(match);
                BarrelHealingPlugin.Log.LogInfo($"[BarrelHealing]   unlit: {TransformNameMatch.PathOf(match)} @ {match.position}");
            }

            return unlit;
        }
    }
}
