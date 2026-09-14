using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace BarrelHealing.Client
{
    /// <summary>
    /// Finds the burning barrels once, when the raid starts.
    ///
    /// There is no burning-barrel class to look for -- nothing in Assembly-CSharp
    /// describes one, so a barrel is a mesh, a particle system and a light with no
    /// script of its own. Names are all there is to go on, which is why every match
    /// is logged: the first raid on a map is how the pattern gets tuned.
    /// </summary>
    internal static class BarrelDiscovery
    {
        private const int AncestorDepth = 3;

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

            foreach (var particles in UnityEngine.Object.FindObjectsOfType<ParticleSystem>())
            {
                Consider(particles.transform, pattern, claimed, found);
            }

            foreach (var light in UnityEngine.Object.FindObjectsOfType<Light>())
            {
                Consider(light.transform, pattern, claimed, found);
            }

            log.LogInfo($"[BarrelHealing] found {found.Count} fire object(s) in this raid");
            return found;
        }

        private static void Consider(Transform transform, Regex pattern, HashSet<Transform> claimed, List<Vector3> found)
        {
            var match = MatchingAncestor(transform, pattern);

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
            BarrelHealingPlugin.Log.LogInfo($"[BarrelHealing]   {PathOf(match)} @ {match.position}");
        }

        private static Transform MatchingAncestor(Transform transform, Regex pattern)
        {
            var current = transform;

            for (var depth = 0; depth <= AncestorDepth && current != null; depth++)
            {
                if (pattern.IsMatch(current.name))
                {
                    return current;
                }

                current = current.parent;
            }

            return null;
        }

        private static string PathOf(Transform transform)
        {
            var path = new StringBuilder(transform.name);

            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                path.Insert(0, parent.name + "/");
            }

            return path.ToString();
        }
    }
}
