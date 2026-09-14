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

        internal static ConfigEntry<float> ScanRadius;

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
                "bonfire|brazier|barrel_fire",
                "Case-insensitive regex matched against the name of every particle system and "
                + "light in the raid, and up to three parents above it. 'bonfire'/'brazier' were "
                + "read out of AssetRipper's asset dump (docs/barrels.md); 'barrel_fire' is what "
                + "the actually-instantiated prop on Shoreline turned out to be named in the "
                + "first raid test (2026-09-14) -- AssetRipper's static sample dismissed 'barrel' "
                + "as 41,000 weapon parts without checking this one. Matching 'barrel' bare is "
                + "still avoided since this pattern is also matched against ancestor names up to "
                + "3 levels up, where a stray weapon-part parent could coincidentally qualify.");

            EnvironmentLayerMask = Config.Bind(
                "Discovery",
                "Line-of-sight layer mask",
                0,
                "Layers the wall check raycasts against, as a bitmask. 0 means ask the game for "
                + "its own terrain+high-poly collision mask, which is what EFT uses for sight "
                + "checks and is almost certainly what you want. The old default of -1 meant "
                + "every layer, so a trigger volume, a loot collider or the fire's own heat "
                + "volume counted as a wall and blocked healing at a barrel in plain sight.");

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

            ScanRadius = Config.Bind(
                "Discovery",
                "Scan radius",
                8f,
                "How far around the player to look for fires, in metres, once a second. This is "
                + "a physics overlap query rather than a scene-wide search, so its cost scales "
                + "with what is actually nearby -- keep it near the heal radius. Raising it a "
                + "long way makes the query return more and eventually costs frame time, which "
                + "is exactly what the scene-wide version used to do.");

            StartCoroutine(BarrelHeartbeat.Run());

            Log.LogInfo("[BarrelHealing] loaded");
        }
    }
}
