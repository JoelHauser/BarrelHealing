using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BarrelHealing.Client
{
    /// <summary>
    /// Finds the fires near the player, cheaply, on a timer.
    ///
    /// **Three designs in, so the reasoning is worth keeping.** v0.1 scanned the whole scene
    /// once at raid start: found nothing, because EFT streams these props in as the player
    /// approaches and none of them exist yet at spawn. The fix for that rescanned the whole
    /// scene every two seconds, which worked and cost a visible frame spike every two seconds
    /// -- `FindObjectsOfType` walks every object in an EFT map, and an EFT map is enormous.
    ///
    /// The global scan was never needed. Healing happens within `Heal radius` (3m by default),
    /// so a fire 400m away is irrelevant until the player is next to it, and by then a local
    /// query will have found it. `Physics.OverlapSphereNonAlloc` is spatially indexed, returns
    /// a handful of colliders within a few metres, and allocates nothing.
    ///
    /// Positions, once found, are kept: the prop is still physically there even after it is
    /// culled away again, so a barrel stays a heal source once the player has been near it.
    /// </summary>
    internal static class BarrelDiscovery
    {
        // Two fires closer together than this are the same fire. Identity is by position, not
        // by Transform: a pooled prop can be destroyed and come back as a different object.
        private const float SamePlaceTolerance = 0.5f;

        // Reused between scans, so a scan allocates nothing at all. Sized well beyond what a
        // few metres of any real map returns; a saturated buffer is reported rather than
        // silently truncating the search.
        private const int MaxColliders = 512;
        private static readonly Collider[] Buffer = new Collider[MaxColliders];

        private static bool _warnedSaturated;

        /// <summary>
        /// The most recent lit fire seen, kept as the donor to clone flames from when the
        /// player lights a cold barrel. A consequence of scanning locally instead of globally:
        /// the mod only knows about fires it has been near, so lighting a barrel requires
        /// having passed a burning one earlier in the raid.
        /// </summary>
        internal static Transform LastLitRoot { get; private set; }

        internal static void Reset()
        {
            _warnedSaturated = false;
            LastLitRoot = null;
        }

        /// <summary>
        /// One spatial query, classifying everything it finds. Lit fires are appended to
        /// <paramref name="lit"/> (deduped against what is already known); unlit ones are
        /// written to <paramref name="unlit"/>, which is cleared first since those are only
        /// of interest while the player is stood near them.
        /// </summary>
        internal static void ScanNear(Vector3 origin, List<Vector3> lit, List<Transform> unlit)
        {
            unlit.Clear();

            Regex healPattern, lightPattern;

            if (!TryPattern(BarrelHealingPlugin.BarrelNamePattern.Value, out healPattern)
                || !TryPattern(BarrelHealingPlugin.LightableBarrelNamePattern.Value, out lightPattern))
            {
                return;
            }

            var radius = BarrelHealingPlugin.ScanRadius.Value;
            var count = Physics.OverlapSphereNonAlloc(origin, radius, Buffer, ~0, QueryTriggerInteraction.Collide);

            if (count >= MaxColliders && !_warnedSaturated)
            {
                _warnedSaturated = true;
                BarrelHealingPlugin.Log.LogWarning(
                    $"[BarrelHealing] scan buffer full ({MaxColliders}) -- a fire could be missed here. "
                    + "Lower the scan radius if this recurs.");
            }

            var claimed = new HashSet<Transform>();

            for (var i = 0; i < count; i++)
            {
                var collider = Buffer[i];

                if (collider == null)
                {
                    continue;
                }

                Consider(collider.transform, healPattern, lightPattern, claimed, lit, unlit);
            }
        }

        private static void Consider(
            Transform transform,
            Regex healPattern,
            Regex lightPattern,
            HashSet<Transform> claimed,
            List<Vector3> lit,
            List<Transform> unlit)
        {
            var match = TransformNameMatch.MatchingAncestor(transform, healPattern)
                ?? TransformNameMatch.MatchingAncestor(transform, lightPattern);

            if (match == null || !claimed.Add(match))
            {
                return;
            }

            // Lit or cold is decided by whether there is a fire under it, not by the name --
            // the same prop name covers both. includeInactive because a culled fire is still
            // a fire. This is a small local subtree walk, not a scene scan.
            var burning = match.GetComponentInChildren<ParticleSystem>(true) != null;

            if (!burning)
            {
                if (lightPattern.IsMatch(match.name))
                {
                    unlit.Add(match);
                }

                return;
            }

            LastLitRoot = match;

            var position = match.position;

            foreach (var seen in lit)
            {
                if (Vector3.Distance(seen, position) <= SamePlaceTolerance)
                {
                    return;
                }
            }

            lit.Add(position);
            BarrelHealingPlugin.Log.LogInfo($"[BarrelHealing] found fire: {TransformNameMatch.PathOf(match)} @ {position}");
        }

        // Compiled once and reused. This runs every second against every collider nearby, so
        // rebuilding a Regex per scan was pure waste; the cached copy is rebuilt only if the
        // config string is edited mid-raid.
        private static readonly Dictionary<string, Regex> PatternCache = new Dictionary<string, Regex>();

        private static bool TryPattern(string source, out Regex pattern)
        {
            if (PatternCache.TryGetValue(source, out pattern))
            {
                return pattern != null;
            }

            try
            {
                pattern = new Regex(source, RegexOptions.IgnoreCase | RegexOptions.Compiled);
            }
            catch (System.ArgumentException ex)
            {
                BarrelHealingPlugin.Log.LogError("[BarrelHealing] name pattern will not compile: " + ex.Message);
                pattern = null;
            }

            PatternCache[source] = pattern;
            return pattern != null;
        }
    }
}
