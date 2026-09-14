using System;
using EFT;
using EFT.Interactive;
using UnityEngine;

namespace BarrelHealing.Client
{
    /// <summary>
    /// A "Light" prompt bolted onto an unlit bonfire at runtime.
    ///
    /// Subclassing <see cref="WorldInteractiveObject"/> directly does not work:
    /// EFT.InteractionContextHelper.GetAvailableActions dispatches on concrete type with an
    /// explicit is-chain (Door, Trunk, LootableContainer, Switch, StationaryWeapon, ...) and
    /// throws for anything it does not recognise. Switch is the narrowest recognised type that
    /// does not need a Door reference -- its own Open/Close/Lock/Unlock/NextSwitches plumbing
    /// is simply never reached here, because <see cref="Interact"/> is overridden completely
    /// and never calls base.Interact.
    ///
    /// The vanilla prompt only appears when DoorState == EDoorState.Shut and Operatable is
    /// true (same GetAvailableActions overload) -- both are set explicitly in
    /// <see cref="OnAwake"/> since a component added at runtime has no prefab to inherit
    /// serialized defaults from. Operatable is kept in sync with "is the player carrying a
    /// lighter or matches" by BarrelHeartbeat's poll -- the same polling-not-patching pattern
    /// as the rest of this mod, not a per-frame Update() on every instance.
    /// </summary>
    internal sealed class BonfireSwitch : Switch
    {
        internal Transform Donor;
        internal Action OnLit;
        internal bool Lit { get; private set; }

        public override void OnAwake()
        {
            // WorldInteractiveObject.Awake() is not virtual -- it calls OnAwake(), so this is
            // the one safe place to add setup without silently losing the base class's own
            // Awake work (wiring _handle/LockHandle sound callbacks, both null here anyway).
            base.OnAwake();

            Id = "BarrelHealing_" + GetInstanceID();
            DoorState = EDoorState.Shut;
            ContextMenuTip = "Light";
        }

        public override void Interact(InteractionResult interactionResult)
        {
            if (Lit || interactionResult.InteractionType != EInteractionType.Open)
            {
                return;
            }

            Lit = BonfireIgnition.TryLight(this, Donor);

            if (Lit)
            {
                Operatable = false;
                OnLit?.Invoke();
            }
        }
    }
}
