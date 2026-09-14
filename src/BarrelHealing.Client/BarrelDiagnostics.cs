using System.Linq;
using Comfort.Common;
using EFT;
using UnityEngine;

namespace BarrelHealing.Client
{
    /// <summary>
    /// Temporary: BarrelDiscovery found 0 fire objects on a map with a bonfire the player was
    /// standing at (2026-09-14), so the "bonfire"/"brazier" name-match or the 3-ancestor-level
    /// walk is wrong for at least this map. Rather than guess again from AssetRipper's one
    /// dev-scene sample, this dumps every ParticleSystem/Light's real name and full transform
    /// path within range of the player, no ancestor-depth or name assumptions at all -- whatever
    /// the truth is, it shows up here. Delete this file once BarrelNamePattern is fixed for real.
    /// </summary>
    internal static class BarrelDiagnostics
    {
        private const float DumpInterval = 5f;

        internal static void Tick(ref float timer, float tickInterval)
        {
            timer += tickInterval;

            if (timer < DumpInterval)
            {
                return;
            }

            timer = 0f;

            var player = Singleton<GameWorld>.Instance?.MainPlayer;

            if (player == null)
            {
                return;
            }

            var origin = player.Position;
            var radius = BarrelHealingPlugin.DiagnosticDumpRadius.Value;
            var log = BarrelHealingPlugin.Log;

            var hits = Object.FindObjectsOfType<ParticleSystem>(true)
                .Select(p => ("ParticleSystem", p.transform))
                .Concat(Object.FindObjectsOfType<Light>(true).Select(l => ("Light", l.transform)))
                .Select(entry => (entry.Item1, entry.Item2, Distance: Vector3.Distance(origin, entry.Item2.position)))
                .Where(entry => entry.Distance <= radius)
                .OrderBy(entry => entry.Distance)
                .ToList();

            log.LogInfo($"[BarrelHealing] [diag] {hits.Count} ParticleSystem/Light within {radius}m of player:");

            foreach (var (kind, transform, distance) in hits)
            {
                log.LogInfo($"[BarrelHealing] [diag]   {kind} d={distance:F1} {TransformNameMatch.PathOf(transform)}");
            }
        }
    }
}
