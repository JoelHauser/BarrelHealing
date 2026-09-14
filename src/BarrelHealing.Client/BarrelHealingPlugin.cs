using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace BarrelHealing.Client
{
    /// <summary>
    /// Standing near a burning barrel slowly patches you up, one limb at a time.
    ///
    /// No Harmony patches: the raid is noticed by polling Singleton&lt;GameWorld&gt;
    /// rather than by patching OnGameStarted, so this mod collides with nothing.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class BarrelHealingPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.joelhauser.barrelhealing";
        public const string PluginName = "Barrel Healing";
        public const string PluginVersion = "0.2.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<float> HealRadius;
        internal static ConfigEntry<float> DelayBeforeHealing;
        internal static ConfigEntry<float> HealRatePerSecond;
        internal static ConfigEntry<string> BarrelNamePattern;
        internal static ConfigEntry<int> EnvironmentLayerMask;

        internal static ConfigEntry<string> LightableBarrelNamePattern;
        internal static ConfigEntry<float> IgnitionColliderRadius;

        private void Awake()
        {
            Log = Logger;

            HealRadius = Config.Bind(
                "Healing",
                "Heal radius",
                3f,
                "How close to the barrel you have to be, in metres, before it does anything for you.");

            DelayBeforeHealing = Config.Bind(
                "Healing",
                "Delay before healing",
                2f,
                "Seconds of standing in range, in the open, before the healing starts. Stepping "
                + "out of range or behind cover puts this back to zero -- it does not accumulate "
                + "across visits.");

            HealRatePerSecond = Config.Bind(
                "Healing",
                "Heal rate",
                3f,
                "Health per second, applied to one limb at a time: the most hurt limb is filled "
                + "up first, then the next. A destroyed limb is skipped entirely and is not "
                + "brought back. Bleeds and fractures are not touched.");

            BarrelNamePattern = Config.Bind(
                "Discovery",
                "Barrel name pattern",
                "bonfire|brazier",
                "Case-insensitive regex matched against the name of every particle system and "
                + "light in the raid, and up to three parents above it. These two names were read "
                + "out of the shipped assets -- see docs/barrels.md. 'barrel' is deliberately not "
                + "here: it matches 41,000 weapon parts and no scenery at all.");

            EnvironmentLayerMask = Config.Bind(
                "Discovery",
                "Line-of-sight layer mask",
                -1,
                "Layers the wall check raycasts against, as a bitmask. -1 is every layer, which "
                + "is deliberately blunt: narrow it once you know which layer the map geometry "
                + "is actually on.");

            LightableBarrelNamePattern = Config.Bind(
                "Ignition",
                "Lightable barrel name pattern",
                "bonfire",
                "Case-insensitive regex for which unlit fires get a Light prompt. Deliberately "
                + "narrower than the healing pattern above: the one 'brazier' prop inspected has "
                + "no fire/light children and no known lit variant to clone from, so offering a "
                + "Light prompt there would be a prompt for nothing.");

            IgnitionColliderRadius = Config.Bind(
                "Ignition",
                "Ignition collider radius",
                0.6f,
                "Radius, in metres, of the interaction collider placed on an unlit bonfire so the "
                + "vanilla look-and-press prompt has something to raycast against.");

            StartCoroutine(BarrelHeartbeat.Run());

            Log.LogInfo("[BarrelHealing] loaded");
        }
    }
}
