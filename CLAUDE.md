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
| SPT install | `C:\HUH` (yes, really -- read out of `spt-installer.log`, not a typo) |
| SPT version | 4.1.3 |
| Client | EFT `0.16.9.5.40743`, **Mono**, not IL2CPP |
| Unity | 2022.3.43f1 |
| Live game (ignore it) | `C:\Battlestate Games\Escape from Tarkov` -- separate IL2CPP install, useless for modding |

```
dotnet build src/BarrelHealing.Client/BarrelHealing.Client.csproj -c Release -p:SPTPath=C:\HUH
scripts/pack.ps1 -SPTPath C:\HUH     # builds, then copies the DLL into BepInEx/plugins/BarrelHealing
```

**Run those through PowerShell, not Bash.** A backslash path mangled through
Git Bash arrives as `C:HUH` and the build stops with "is not an SPT install
root", which reads like a missing install rather than a quoting problem. Same
trap as SPT-Casino.

## How it is put together

```
BarrelHealingPlugin.cs   BepInPlugin entry, config, starts the heartbeat
BarrelHeartbeat.cs       waits for a raid, discovers once, polls every 0.5s, drops state at raid end
BarrelDiscovery.cs       one scene scan, name-matched, logs every hit
LimbPriority.cs          picks the next limb to heal
HealingTick.cs           range + line-of-sight gate, delay timer, the Heal() call
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

v0.1 is built, installed to `C:\HUH\BepInEx\plugins\BarrelHealing`, committed
and pushed. Untested in a raid.

**Next, and approved but not started: lightable bonfires (v0.2).** Walk up to an
unlit bonfire, get a **Light** prompt, and light it so it joins the fires that
heal you. Decisions already made, so they do not need relitigating:

- The button is greyed out unless the player carries a lighter or matches.
- A player-lit fire **burns you**, like a vanilla one -- so `trigger_hurt_fire`
  gets cloned along with the three particle children.
- **Matches are consumed, lighters are not.** Prefer the lighter when the player
  has both.
- On a map with no lit bonfire to copy fire from, the prompt does not appear and
  the log says why.

Ignition items, IDs read from `SPT_Data\database\locales\global\en.json` and
cross-checked in `templates\items.json`. All are `StackMaxSize = 1`, so
consuming means removing the whole item, not decrementing a stack:

| Item | Template ID | Consumed |
| --- | --- | --- |
| Zibbo lighter | `56742c2e4bdc2d95058b456d` | no |
| Crickent lighter | `56742c284bdc2d98058b456d` | no |
| Golden Zibbo lighter | `5939a00786f7742fe8132936` | no |
| SurvL Survivor Lighter | `5e2af37686f774755a234b65` | no |
| Classic matches | `57347b8b24597737dd42e192` | yes |
| Hunting matches | `5e2af2bc86f7746d3f3c33fc` | yes |

**Do not match on their shared parent category** (`57864e4c24597754843f8723`) --
it also contains WD-40, propane, thermite and TNT.

Plumbing for it, all verified to exist:
`player.InventoryController.Inventory.GetPlayerItems(EPlayerItems.Equipment)`
to see what is carried, `item.TemplateId.EqualsToString(string)` to identify it,
and `Object.Instantiate(donorChild, targetRoot, false)` to clone the fire --
`worldPositionStays: false` keeps each piece's local offset so the flame lands
in the barrel and not at the world origin. Unlit barrels are found cheaply with
`FindObjectsOfType<LODGroup>()` (every bonfire root has one, lit or not) filtered
by name, then split on whether a `ParticleSystem` exists in the subtree.

**The one genuinely unsolved piece is consuming the matches.** There is no
`DestroyItem` on `InventoryController`. The candidate is
`TryThrowItem(item, callback, silent: true)` -- but a *throw* may spawn a
pickup-able `LootItem`, which would drop the matchbox at the player's feet
instead of consuming it. Verify that in a raid before trusting it; the fallback
is destroying the resulting `LootItem`, or moving consumption to a server route.
Whether a client-side removal survives to the stash through SPT's end-of-raid
profile save is also unverified.
