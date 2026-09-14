using System.Linq;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using UnityEngine;

namespace BarrelHealing.Client
{
    /// <summary>
    /// Sets up an unlit bonfire's interaction collider, and does the actual lighting: clone
    /// the donor's fire children onto it and consume whatever ignition item was used.
    /// </summary>
    internal static class BonfireIgnition
    {
        // The unlit bonfire's own children (docs/barrels.md, via AssetRipper). Everything
        // else under a lit "_withfire" donor is fire: trigger_hurt_fire and the three
        // particle children. Cloning everything except these three carries the burn hazard
        // over for free -- no FlameDamageTrigger-specific code needed here.
        private static readonly string[] UnlitBaseChildren = { "model", "model_lod", "shadow" };

        // TemplateIds are matched with EqualsToString on EFT.MongoID, not string '==' -- see
        // CLAUDE.md for where these were read from (locales/global/en.json, cross-checked in
        // templates/items.json). Do not match on the shared parent category: it also contains
        // WD-40, propane, thermite and TNT.
        private static readonly string[] LighterIds =
        {
            "56742c2e4bdc2d95058b456d", // Zibbo
            "56742c284bdc2d98058b456d", // Crickent
            "5939a00786f7742fe8132936", // Golden Zibbo
            "5e2af37686f774755a234b65", // SurvL Survivor
        };

        private static readonly string[] MatchIds =
        {
            "57347b8b24597737dd42e192", // Classic matches
            "5e2af2bc86f7746d3f3c33fc", // Hunting matches
        };

        /// <summary>
        /// Adds the interaction collider and the Switch that shows the "Light" prompt.
        /// The collider sits on its own child so it does not disturb the bonfire's existing
        /// colliders or their layer -- a fresh Unity layer name lookup, not a hardcoded int,
        /// since LayersMaskController.DoorLayer resolves it at runtime the same way the game
        /// engine does.
        /// </summary>
        internal static BonfireSwitch Prepare(Transform unlitRoot, Transform donor)
        {
            var colliderObject = new GameObject("BarrelHealing_IgnitionCollider");
            colliderObject.transform.SetParent(unlitRoot, worldPositionStays: false);
            colliderObject.layer = LayersMaskController.DoorLayer;

            var collider = colliderObject.AddComponent<SphereCollider>();
            collider.radius = BarrelHealingPlugin.IgnitionColliderRadius.Value;

            // A trigger so this never blocks player movement -- EFT's own interactive/loot
            // colliders (what _interactiveLootMaskWPlayer is built to raycast) are triggers
            // for the same reason, and Physics.Raycast hits triggers by default.
            collider.isTrigger = true;

            var bonfireSwitch = unlitRoot.gameObject.AddComponent<BonfireSwitch>();
            bonfireSwitch.Donor = donor;
            return bonfireSwitch;
        }

        /// <summary>Whether the current player is carrying something that can light a fire.</summary>
        internal static bool HasIgnitionSource(InventoryController inventoryController)
        {
            return inventoryController != null && FindIgnitionItem(inventoryController, out _, out _);
        }

        internal static bool TryLight(BonfireSwitch target, Transform donor)
        {
            var log = BarrelHealingPlugin.Log;

            if (donor == null)
            {
                log.LogWarning("[BarrelHealing] ignition fired with no donor -- should not be reachable, Operatable should have been false");
                return false;
            }

            var player = Singleton<GameWorld>.Instance?.MainPlayer;
            var inventoryController = player?.InventoryController;

            if (inventoryController == null || !FindIgnitionItem(inventoryController, out var item, out var consumed))
            {
                log.LogInfo("[BarrelHealing] Light pressed with no lighter or matches on hand -- prompt should have been greyed out");
                return false;
            }

            foreach (Transform child in donor)
            {
                if (UnlitBaseChildren.Contains(child.name))
                {
                    continue;
                }

                Object.Instantiate(child.gameObject, target.transform, worldPositionStays: false);
            }

            if (consumed)
            {
                // ItemController.TryThrowItem -> ThrowItem -> ItemManipulator.Discard +
                // RemoveOperation: verified by decompiling EFT.InventoryLogic.ItemController,
                // and confirmed unoverridden the length of the InventoryController chain
                // (PlayerInventoryController, PlayerOwnerInventoryController,
                // SinglePlayerInventoryController, OfflineInventoryController). It does not
                // spawn a world LootItem -- that's a different, hands-visible throw path
                // entirely (grenades etc.), not this one. silent:true only suppresses the
                // anti-RMT discard-limit warning notification.
                inventoryController.TryThrowItem(item, null, true);
            }

            log.LogInfo($"[BarrelHealing] lit {target.name} with {(consumed ? "matches (consumed)" : "a lighter")}");
            return true;
        }

        private static bool FindIgnitionItem(InventoryController inventoryController, out Item item, out bool consumed)
        {
            Item lighter = null;
            Item match = null;

            foreach (var candidate in inventoryController.Inventory.GetPlayerItems(EPlayerItems.Equipment))
            {
                if (lighter == null && LighterIds.Any(id => candidate.TemplateId.EqualsToString(id)))
                {
                    lighter = candidate;
                }
                else if (match == null && MatchIds.Any(id => candidate.TemplateId.EqualsToString(id)))
                {
                    match = candidate;
                }
            }

            // Lighters are preferred and not consumed; matches are the fallback and are.
            if (lighter != null)
            {
                item = lighter;
                consumed = false;
                return true;
            }

            if (match != null)
            {
                item = match;
                consumed = true;
                return true;
            }

            item = null;
            consumed = false;
            return false;
        }
    }
}
