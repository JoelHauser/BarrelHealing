using EFT.HealthSystem;

namespace BarrelHealing.Client
{
    /// <summary>
    /// Which limb the barrel works on next: the most hurt one that can still be helped.
    /// </summary>
    internal static class LimbPriority
    {
        // EBodyPart lives in the global namespace, and Common is a bucket for skills
        // rather than a limb, so it is not in this list.
        private static readonly EBodyPart[] Limbs =
        {
            EBodyPart.Head,
            EBodyPart.Chest,
            EBodyPart.Stomach,
            EBodyPart.LeftArm,
            EBodyPart.RightArm,
            EBodyPart.LeftLeg,
            EBodyPart.RightLeg,
        };

        /// <summary>
        /// The limb to heal, or null when every limb is either full or destroyed.
        /// </summary>
        internal static EBodyPart? PickNextLimb(ActiveHealthController health)
        {
            EBodyPart? worst = null;
            var worstHealth = float.MaxValue;

            foreach (var limb in Limbs)
            {
                // A destroyed limb comes back through RestoreBodyPart and nothing else.
                // Heal() is not that, and a barrel is not a surgical kit.
                if (health.IsBodyPartDestroyed(limb))
                {
                    continue;
                }

                var value = health.GetBodyPartHealth(limb);

                if (value.AtMaximum)
                {
                    continue;
                }

                if (value.Normalized < worstHealth)
                {
                    worstHealth = value.Normalized;
                    worst = limb;
                }
            }

            return worst;
        }
    }
}
