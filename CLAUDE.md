# BarrelHealing -- working notes for Claude

A STALKER-style mechanic for SPT: stand near a lit burning barrel and it slowly
patches you up, one limb at a time. Client-only BepInEx plugin, no server mod,
**no Harmony patches at all**.

**Nothing in this repo has ever run in a raid.** Every claim below was verified
against the installed assemblies and shipped game assets by static analysis --
which is not the same as it working. The first raid is still the first test.

## The box this is built on

| | |
| --- | --- |
| SPT install | `H:\SPT4.1.X` (moved here from the original `C:\HUH` box; see git history) |
| SPT version | 4.1.3 |
| Client | EFT `0.16.9.5.40743`, **Mono**, not IL2CPP |
| Unity | 2022.3.43f1 |
| Live game (ignore it) | `C:\Battlestate Games\Escape from Tarkov` -- separate IL2CPP install, useless for modding |

```
dotnet build src/BarrelHealing.Client/BarrelHealing.Client.csproj -c Release -p:SPTPath=H:\SPT4.1.X
scripts/pack.ps1 -SPTPath H:\SPT4.1.X     # builds, then copies the DLL into BepInEx/plugins/BarrelHealing
```

**Run those through PowerShell, not Bash.** A backslash path mangled through
Git Bash arrives as `C:HUH` and the build stops with "is not an SPT install
root", which reads like a missing install rather than a quoting problem. Same
trap as SPT-Casino.

## How it is put together

```
BarrelHealingPlugin.cs   BepInPlugin entry, config, starts the heartbeat
BarrelHeartbeat.cs       waits for a raid, discovers once, polls every 0.5s, drops state at raid end
BarrelDiscovery.cs       one scene scan for lit fires, name-matched, logs every hit
LimbPriority.cs          picks the next limb to heal
HealingTick.cs           range + line-of-sight gate, delay timer, the Heal() call
TransformNameMatch.cs    shared ancestor-name-match/dedupe helper (BarrelDiscovery + BonfireDiscovery)
BonfireDiscovery.cs      v0.2: one scene scan for a lit donor + every unlit bonfire
BonfireSwitch.cs         v0.2: EFT.Interactive.Switch subclass -- the "Light" prompt itself
BonfireIgnition.cs       v0.2: collider/prompt setup, fire cloning, lighter/match consumption
```

**Polling, not patching.** The raid is noticed by watching
`Singleton<GameWorld>.Instantiated` from a coroutine rather than patching
`GameWorld.OnGameStarted`. A poll survives the next obfuscator pass renaming
something, and this mod only ever reads state that is already public. It also
means **zero Harmony patches and zero `spt-*` references**, so there is nothing
for another mod to collide with.

Config lives in `BepInEx/config/` after the first run: heal radius (3m), delay
before healing, heal rate, the barrel name pattern, and the line-of-sight layer
mask.

## What was read out of the game, and is therefore true

All by reflection against `C:\HUH\EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll`
(15,094 types resolved once a dependency resolver is attached -- without one you
get ~2,800 and silently miss almost everything).

- `EFT.HealthSystem.ActiveHealthController.Heal(EBodyPart, float)` -- and
  `Player.ActiveHealthController` is typed as the **concrete class**, not
  `IHealthController`, so no cast is needed. `Heal` is *not* on the interface.
- `IsBodyPartDestroyed(EBodyPart)`, `IsBodyPartBroken(EBodyPart)`, and
  `GetBodyPartHealth(EBodyPart, bool rounded = ...)` returning
  `EFT.HealthSystem.ValueStruct` (`Current`/`Maximum`/`Normalized`/`AtMaximum`).
  These are inherited from an obfuscated generic base whose name is unwritable,
  but they are public, so C# reaches them through the derived type fine.
- `EBodyPart` is in the **global namespace**, not `EFT`. Head, Chest, Stomach,
  LeftArm, RightArm, LeftLeg, RightLeg, Common.
- `EFT.GameWorld.MainPlayer` (field), `Player.Position`, `Player.CameraPosition`.
- `GameWorld.OnGameStarted()` / `OnDestroy()` / `Dispose()` exist, if a patch is
  ever wanted instead of the poll.

**Destroyed limbs are skipped and never restored** -- that needs
`RestoreBodyPart`, which a barrel is not. Bleeds and fractures are untouched:
`Heal` only raises raw body-part HP.

## Things that cost time, written down so they cost it once

**`HideoutGameWorld` and `NarrateGameWorld` are `internal`.** `world is
HideoutGameWorld` will not compile -- CS0122. The hideout *is* a GameWorld
(`HideoutGameWorld : ClientLocalGameWorld : ClientGameWorld : GameWorld`), so it
has to be excluded, and the only way is walking the base chain comparing
`Type.Name` strings at runtime. See `DerivesFromNamed` in `BarrelHeartbeat.cs`;
SPT-Casino hit this first and solved it identically.

**A destroyed Unity object is not null to a plain reference check.**
`Singleton<T>.Instantiated` is a raw `ldnull; cgt.un` comparison, so a torn-down
world still reports as present. Compare the instance with Unity's `==`.

**SPT's `PluginValidator` does not care about a plugin with no `spt-*`
references.** This was checked properly, by decompiling
`BepInEx/patchers/spt-prepatch.dll` with Mono.Cecil rather than guessing:
`GetSptReferenceVersion` does `AssemblyReferences.Where(spt-*).Select(version)
.OrderByDescending().FirstOrDefault()`, so no references means `null`, and
`GetMismatchedPlugins` has a `brfalse` that skips `null` outright. Referencing
nothing is safe. Referencing an `spt-*` DLL from the wrong SPT version is what
gets a plugin rejected.

**The scenery is not called "barrel".** `barrel` matches 41,426 GameObjects and
every one is a weapon part (`barrel_uzi_pro_iwi_smg_170mm_9x19`). The real names
are **`bonfire`** and **`brazier`**. `campfire`, `burning`, `firepit`, `bochka`
and `kostyor` match *nothing at all*. Full detail, including per-map counts, is
in `docs/barrels.md`.

**Discovery starts from particle systems on purpose.** A lit bonfire owns
`TorchFire`, `barrel_fire_smoke` and `barrel_fire_heat` children; an unlit one is
`model`, `model_lod`, `shadow` and nothing else. Starting the scan from emitters
is what makes it impossible to collect a cold barrel and heal the player at it.
Searching by name alone would collect both. There is **no `Light` component on
either** -- these fires are particles only.

**Tooling, on this box:** Git Bash chokes on large files -- a `grep -c` over a
17MB HTML file times out at 60s where PowerShell regex does it instantly. Use
PowerShell for anything big. And `$PID` is a read-only automatic variable in
PowerShell, so it cannot be a loop variable.

## Where this was left off

v0.1 and v0.2 are both built and installed to
`H:\SPT4.1.X\BepInEx\plugins\BarrelHealing` (moved here from the original
`C:\HUH` box). Neither has run in a raid yet.

**v0.2, lightable bonfires, is implemented** -- walk up to an unlit bonfire,
get a **Light** prompt, light it, and it joins `BarrelHeartbeat`'s healing list
immediately (no second discovery pass needed; see `BonfireSwitch.OnLit`).
Decisions from the original spec, kept:

- The button is greyed out unless the player carries a lighter or matches
  (`BarrelHeartbeat.RefreshIgnitionPrompts`, polled every 0.5s, not per-frame).
- A player-lit fire **burns you**, like a vanilla one -- `trigger_hurt_fire`
  is cloned along with the three particle children, since `BonfireIgnition`
  clones everything under the donor except its own `model`/`model_lod`/`shadow`.
- **Matches are consumed, lighters are not.** Lighter preferred when both carried.
- On a map with no lit bonfire to copy from, `BonfireDiscovery` finds no donor,
  no Switch is ever attached, and the log says why.

### How the "Light" prompt actually works (no Harmony, verified by decompile)

EFT's world-interaction system is generic at the raycast layer
(`EFT.GameWorld.FindInteractable`, called every frame from
`EFT.Player.InteractionRaycast`) but closed at the prompt-building layer:
`EFT.InteractionContextHelper.GetAvailableActions` dispatches on concrete type
with an explicit `is Door / is Trunk / is LootableContainer / is Switch / ...`
chain and **throws** for anything unrecognised. So `BonfireSwitch` subclasses
`EFT.Interactive.Switch` (the narrowest recognised type that needs no `Door`
reference), not `WorldInteractiveObject` directly.

The `Switch` overload of `GetAvailableActions` shows the prompt (text =
`ContextMenuTip.Localized()`) only when `Operatable` is true and
`DoorState == EDoorState.Shut` -- both default to "not shown" (`DoorState`
defaults to `EDoorState.None`) so a runtime-added component has to set them
explicitly, done in `BonfireSwitch.OnAwake`. Pressing the prompt calls
`Interact(new InteractionResult(EInteractionType.Open))`, which
`BonfireSwitch.Interact` overrides completely -- `Switch`'s own
Open/Close/Lock/Unlock/`NextSwitches`/`Door`/`Lamps` fields are never touched
and stay null/default harmlessly.

`WorldInteractiveObject.OnEnable()` auto-finds its interaction `Collider` by
scanning children for the first one on `LayersMaskController.DoorLayer`
(`LayerMask.NameToLayer("DoorLowPolyCollider")`). An unlit bonfire's existing
colliders are not on that layer, so `BonfireIgnition.Prepare` adds a dedicated
child `SphereCollider` there, `isTrigger = true` (matches how EFT's own
loot/interactive colliders work, and `Physics.Raycast` hits triggers by
default) -- **untested in a raid**, this is the one piece of the mechanism
that is inference from naming/convention rather than a decompiled guarantee.
If the prompt never appears on an unlit bonfire, this collider is the first
thing to check (try `isTrigger = false`, or confirm `DoorLowPolyCollider` is
actually in `_interactiveLootMaskWPlayer`).

### The matches-consumption question is resolved, and the earlier worry was wrong

`InventoryController.TryThrowItem` does **not** spawn a pickup-able `LootItem`.
Decompiled directly (`EFT.InventoryLogic.ItemController`, the base every
`InventoryController` variant in this game inherits from, unoverridden all the
way down through `PlayerInventoryController`, `PlayerOwnerInventoryController`,
`SinglePlayerInventoryController`, `OfflineInventoryController`):

```csharp
public virtual void ThrowItem(Item item, bool downDirection = false, Callback callback = null)
{
    OperationResult<DiscardResult> operationResult = ItemManipulator.Discard(item, this, simulate: true);
    if (operationResult.Failed) { callback?.Invoke(operationResult.ToResult()); }
    else { Execute(new RemoveOperation(GetAndIncrementNextOperationId(), this, operationResult.Value), callback); }
}
```

That's a straight inventory removal (`ItemManipulator.Discard` +
`RemoveOperation`), the same mechanism used elsewhere in the game for outright
item destruction -- no physics throw, no world-spawned pickup. The
hands-visible "throw" players see for grenades/flares is a different system
entirely. `BonfireIgnition.TryLight` calls
`inventoryController.TryThrowItem(item, null, true)` directly;
`silent: true` only suppresses the anti-RMT discard-limit warning notification.
Whether a client-side removal survives to the stash through SPT's end-of-raid
profile save is still unverified -- that part needs an actual raid.

### The first raid happened (2026-09-14, Shoreline), and found two real bugs

Nothing healed. Both causes are fixed but **the fix itself is not yet
raid-confirmed** -- that is the next thing to check.

**1. The prop names in `docs/barrels.md` were wrong.** Not subtly: the real
objects a player walks up to on Shoreline are `barrel_fire_wfire` and
`barrel_fire` (`barrel_fire (3)` after Unity's duplicate suffix), parented under
a transform called `barrels` in scene `SBG_Shoreline_Light`. The doc had
explicitly ruled `barrel` out after sampling a handful of the 41,426 name hits
and finding only weapon parts. `BarrelNamePattern` is now
`bonfire|brazier|barrel_fire`. That correction is recorded at the top of
docs/barrels.md too.

**2. The real bug: `FindObjectsOfType<T>()` excludes inactive objects.** EFT
deactivates these fire props until the player is near one. Discovery runs once,
at raid start, when the player is at spawn and therefore every fire on the map
is deactivated -- so it found nothing no matter what the name pattern said. The
log signature was unmistakable once the diagnostic was in: `0 ParticleSystem/
Light within 10m` for roughly fifty seconds while running from spawn, then six
hits the moment the player arrived at the barrel. Every `FindObjectsOfType` and
`GetComponentInChildren` in this mod now passes `includeInactive: true`; an
inactive particle system still has a valid `transform.position`, which is all
discovery reads.

This one is worth remembering beyond this mod: **any scene scan done at raid
start in EFT has to opt into inactive objects**, or it only sees whatever
happens to be near the player's spawn.

`BarrelDiagnostics.cs` is the temporary logger that found this -- it dumps every
ParticleSystem/Light near the player every 5s while zero fires were discovered.
Delete it once discovery is confirmed working.

**Still unchecked, in order:** does discovery now log `found N fire object(s)`
with N > 0 at raid start; does standing at a lit barrel actually heal; does the
Light prompt appear on an unlit one (no unlit live instance has been inspected
yet -- see the docs/barrels.md correction, the "unlit barrels have no fire
children at all" claim is still just an assumption from a dev scene); does
pressing it clone the fire somewhere sane; does a match leave the inventory and
stay gone through end-of-raid save.
