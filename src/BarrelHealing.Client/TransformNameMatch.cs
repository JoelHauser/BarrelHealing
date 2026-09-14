using System.Text.RegularExpressions;
using UnityEngine;

namespace BarrelHealing.Client
{
    /// <summary>
    /// Shared by BarrelDiscovery (lit fires, for healing) and BonfireDiscovery (lit/unlit
    /// pairs, for ignition): walk up from a matched component's transform to the ancestor
    /// whose name matches a barrel/bonfire pattern, so a light+two-particle-systems fire
    /// collapses to one claimed position instead of three.
    /// </summary>
    internal static class TransformNameMatch
    {
        private const int AncestorDepth = 3;

        internal static Transform MatchingAncestor(Transform transform, Regex pattern)
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

        internal static string PathOf(Transform transform)
        {
            var path = transform.name;

            for (var parent = transform.parent; parent != null; parent = parent.parent)
            {
                path = parent.name + "/" + path;
            }

            return path;
        }
    }
}
