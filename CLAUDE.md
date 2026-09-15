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

All by reflection against `EscapeFromTarkov_Data\Managed\Assembly-CSharp.dll`
(15,094 types resolved once a dependency resolver is attached -- without one you
get ~2,800 and silently miss almost everything). `ilspycmd` is installed and on
PATH, which is faster than a reflection harness for reading a method *body*.

> **`ActiveHealthController.Heal(EBodyPart, float)` IS AN EMPTY STUB. Do not use
> it.** Its entire body in this build is `{ }`. It exists, it is public, it takes
> exactly the arguments you want, it compiles, it runs -- and it changes nothing.
> An earlier version of this file listed it here as verified, because it had been
> confirmed to *exist*. Nobody read the body. It cost a raid where the log
> cheerfully printed `healing LeftLeg -> 63/65` forty-six times without the number
> ever moving.
>
> **Existence is not behavior.** For anything this mod actually depends on, read
> the decompiled body, not just the signature.

The real health API:

- **The HUD up-arrow is a third mechanism again.** `ChangeHealth` moves the
  number; it does not produce the green `+N` next to the health readout. That
  reads a *rate* registered through `ChangeHealthRate(oldDelta, newDelta)`,
  stored **per minute** -- `Effect.SetHealthRatesPerSecond` takes a per-second
  figure and does `health *= 60f`, so a 3 HP/s barrel correctly displays as
  `+180`. It is display-only: `HealRate` is read by exactly one property
  (`HealthRate`) and the network serialiser, and is never applied to a body
  part, so registering it cannot double-heal. Hand the previously registered
  value back to clear it (`HealingTick.SetHealRate`) or the arrow strands on
  the HUD forever. Call `NetworkSyncHealthRates()` after changing it.
  **Confirmed working in a raid, 2026-09-14.**
- **`ActiveHealthController.ChangeHealth(EBodyPart, float value, DamageInfo)`** --
  positive `value` heals. It clamps to the limb's maximum itself (the
  `HealthValue.Current` setter does a `Mathf.Clamp`), skips destroyed limbs and
  dead players on its own, and does the `NetworkSyncBodyHealth` +
  `HealthChangedEvent` work that makes the UI and the rest of the game notice.
  `DamageInfo` is `EFT.Ballistics.DamageInfo`, a struct, so `default` is fine --
  it is only passed through to the event.
- `FullRestoreBodyPart` / `RestoreBodyPart` exist for destroyed limbs.
  `BaseHealthController.BodyState` is a public
  `Dictionary<EBodyPart, BodyPartState>` if raw access is ever needed.
- `Player.ActiveHealthController` is typed as the **concrete class**, not
  `IHealthController`, so no cast is needed.
- `IsBodyPartDestroyed(EBodyPart)`, `IsBodyPartBroken(EBodyPart)`, and
  `GetBodyPartHealth(EBodyPart, bool rounded = ...)` returning
  `EFT.HealthSystem.ValueStruct` (`Current`/`Maximum`/`Normalized`/`AtMaximum`).
  These work -- confirmed in a raid, not just by signature.
- `EBodyPart` is in the **global namespace**, not `EFT`. Head, Chest, Stomach,
  LeftArm, RightArm, LeftLeg, RightLeg, Common.
- `EFT.GameWorld.MainPlayer` (field), `Player.Position`, `Player.CameraPosition`.
- `GameWorld.OnGameStarted()` / `OnDestroy()` / `Dispose()` exist, if a patch is
  ever wanted instead of the poll.

**Destroyed limbs are skipped and never restored** -- that needs
`RestoreBodyPart`, which a barrel is not. Bleeds and fractures are untouched:
`ChangeHealth` only raises raw body-part HP.

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

**Lit vs cold is decided by whether a `ParticleSystem` hangs under the prop**,
not by its name -- the same name covers both states. `GetComponentInChildren
<ParticleSystem>(true)` on the matched root, with `includeInactive: true`,
because a culled fire is still a fire. That check is a small local subtree walk,
which is why it is affordable inside the per-second scan.

**Never scan the scene to find them.** See the three-designs note in
`BarrelDiscovery.cs`: `FindObjectsOfType` over an EFT map costs a visible frame
spike, and it was never necessary, because healing only happens within a few
metres. `Physics.OverlapSphereNonAlloc` around the player does the same job for
almost nothing. Two consequences worth knowing: props are **not in the scene at
raid start at all** (EFT streams them in as the player nears, so a one-shot scan
at spawn finds zero no matter what), and the mod only knows about fires it has
been near -- which is why lighting a cold barrel needs a burning one to have
been passed earlier in the raid.

**Tooling, on this box:** Git Bash chokes on large files -- a `grep -c` over a
17MB HTML file times out at 60s where PowerShell regex does it instantly. Use
PowerShell for anything big. And `$PID` is a read-only automatic variable in
PowerShell, so it cannot be a loop variable.

## Where this was left off

**v0.1 healing WORKS, confirmed in a raid on 2026-09-14** -- Shoreline, health
climbing limb by limb, worst first, no frame cost. That took five raid tests and
four separate bugs; they are all written up under "Four bugs, and what each one
should teach" below, because every one of them was a wrong *assumption* that a
clean build and a plausible log had hidden.

The HUD heal-rate arrow works too, same raid -- see the health API section.

v0.2 (lightable bonfires) is implemented and installed but **has never had its
prompt seen in a raid.** No unlit barrel has come within scan range yet: in the
confirming raid `Light prompt attached` appeared in the log zero times, so none
of the ignition path -- detection, prompt, cloning, match consumption -- has
executed even once. Walk up to an unlit bonfire, get a **Light** prompt,
light it, and it joins the healing list immediately (`BonfireSwitch.OnLit`).
Decisions from the original spec, kept:

- The button is greyed out unless the player carries a lighter or matches
  (`BarrelHeartbeat.RefreshIgnitionPrompts`, polled every 0.5s, not per-frame).
- A player-lit fire **burns you**, like a vanilla one -- `trigger_hurt_fire`
  is cloned along with the particle children, since `BonfireIgnition` clones
  everything under the donor except its own `model`/`model_lod`/`shadow`.
- **Matches are consumed, lighters are not.** Lighter preferred when both carried.
- With no burning fire seen yet this raid there is no donor, so no Switch is
  attached and no prompt appears.

Note the **install is SPT 4.1.5**, not 4.1.3 as this file long claimed --
read out of `SPT_Runtime\user\logs\Launcher.log` (`server version: 4.1.5`).

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

## Four bugs, and what each one should teach

Getting v0.1 from "builds clean" to "actually heals" took five raid tests. Every
failure looked identical from inside the game -- nothing happens -- and every one
had a different cause. All four were wrong assumptions inherited from static
analysis, not coding mistakes, which is why they survived a clean build.

**1. The prop names in `docs/barrels.md` were wrong.** The real objects on
Shoreline are `barrel_fire_wfire` and `barrel_fire` (`barrel_fire (3)` after
Unity's duplicate suffix), under a transform called `barrels` in scene
`SBG_Shoreline_Light`. The doc had explicitly ruled `barrel` out after sampling a
handful of its 41,426 name hits, finding weapon parts, and generalising.
*Lesson: a name search returning tens of thousands of hits has not been checked
by sampling five of them.*

**2. The props do not exist at raid start.** EFT streams them in as the player
approaches. A one-shot scan at spawn finds zero, forever, whatever the pattern
says. `includeInactive: true` was tried first and did not fix it, which is what
ruled out "present but merely deactivated" and proved they are genuinely absent.
*Lesson: in EFT, "the scene at raid start" is not the scene.*

**3. Line of sight rejected every fire.** The layer mask was `-1`, every layer,
so the ray was stopped by trigger volumes, loot colliders and the fire's own heat
volume -- none of which are walls. Compounding it, many discovered positions are
the flame particle *inside* the drum, so the ray hits the barrel's own shell
before the endpoint and the 0.5m slack was too tight to forgive it. Now uses the
game's own terrain+high-poly mask (config `0` = ask the game) and 1m of slack.
*Lesson: "every layer" is not a neutral default, it is the most hostile one.*

**4. `ActiveHealthController.Heal()` is an empty stub.** Covered in full further
up. The log printed `healing LeftLeg -> 63/65` forty-six times while the number
never moved. Real API is `ChangeHealth(EBodyPart, float, DamageInfo)`.
*Lesson: existence is not behavior -- read the body.*

**And one performance mistake worth not repeating:** the fix for bug 2 was a
scene-wide `FindObjectsOfType` rescan every 2 seconds. It worked, and it cost a
visible frame spike every 2 seconds in a shipped-to-the-user build. The scan was
never needed at any range beyond the heal radius. See `BarrelDiscovery.cs`.

**The diagnostic that cracked it** logged every ParticleSystem/Light near the
player with full transform paths, every few seconds. Two of the four bugs were
invisible until live transform paths could be compared against the assumed ones.
It has been deleted now that discovery works, but *re-adding something like it is
the first move next time this mod "does nothing"* -- it converts silence into
evidence, which is the entire difficulty here.

### v0.2 is known-broken, and the geometry says why

A second AssetRipper pass (2026-09-14) resolved the real prop hierarchies. Full
detail, including the Shoreline coordinates of every cold bonfire and how to
regenerate the export, is at the **top of `docs/barrels.md`** -- read that before
touching `BonfireIgnition`. A private orbitable 3D viewer of the four meshes is
linked from there.

The one piece of good news: **cold detection is sound.** An unlit `bonfire` has
no `ParticleSystem` in its subtree at all, not even a disabled one, so the
lit/cold test works. That was the assumption most likely to sink the feature.

The bad news is that **`bonfire` and `barrel_fire` are unrelated props** -- a
stone-ringed woodpile on the ground versus a waist-height steel drum -- and the
ignition code assumes they are the same shape:

- **`UnlitBaseChildren` excludes the wrong names.** It skips
  `model`/`model_lod`/`shadow`, but the drum's non-fire children are
  `barrel_metal`, `barrel_metal_lod`, `collider`, `shadow_lod`. All four would be
  cloned, so lighting a woodpile spawns a steel drum mesh and collider on top of
  it. Fix: allow-list the fire objects instead of blocklisting the rest.
- **The flame would float 0.52m up**, the drum's rim height, preserved by
  `worldPositionStays: false` onto a ground-level prop. Fix: zero the Y offset.
- **The point lights hang off `barrel_metal`**, not off the fire objects, so a
  naive `barrel_fire_*` allow-list lights the fire without its glow.

Still unverified beyond that:

- **The ignition collider** (`isTrigger = true` on `DoorLowPolyCollider`) is
  inference from naming convention, not a decompiled guarantee. If no prompt
  appears, check this first: try `isTrigger = false`, and confirm that layer is
  really in `_interactiveLootMaskWPlayer`.
- **Whether a consumed match stays gone** through SPT's end-of-raid profile save.
