# BarrelHealing

A STALKER-style mechanic for SPT: stand near a lit burning barrel and it slowly
patches you up, one limb at a time.

Client-only BepInEx plugin. **No server mod, and no Harmony patches at all**, so
there is nothing here for another mod to collide with.

> **Status: the healing works and is confirmed in a raid. The "light a cold
> barrel" feature (v0.2) is written but has never successfully run.** See
> [What works](#what-works) before installing, so you know what you are getting.

## What it does

Walk up to a burning barrel, wait a couple of seconds, and your most damaged
limb starts refilling. When it tops out, the next-worst limb takes over. A green
rate arrow appears next to your health while it is working, the same way the game
shows hydration and energy draining.

- You have to be **within 3 metres and able to see the fire** -- a wall between
  you and it stops the healing.
- **Destroyed limbs are skipped** and never restored. That needs surgery, and a
  barrel is not a surgical kit.
- **Bleeds and fractures are not touched.** This raises raw limb HP and nothing
  else, so you still need to deal with the bleed that got you here.
- Everything is configurable: radius, warm-up delay, healing rate.

## Requirements

- SPT 4.1.x (developed against a 4.1.5 install, EFT `0.16.9.5.40743`, Mono)
- BepInEx, which SPT already ships with

## Install

Download the release DLL and drop it in:

```
<your SPT folder>\BepInEx\plugins\BarrelHealing\BarrelHealing.Client.dll
```

Launch the game once to generate the config file at
`BepInEx\config\com.joelhauser.barrelhealing.cfg`.

## Configuration

| Setting | Default | What it does |
| --- | --- | --- |
| Heal radius | `3` | How close to the fire you have to be, in metres. |
| Delay before healing | `2` | Seconds stood in range before healing starts. Leaving resets it. |
| Heal rate | `3` | HP per second, applied to one limb at a time. |
| Scan radius | `8` | How far to look for fires. Keep near the heal radius -- raising it a lot costs frame time. |
| Barrel name pattern | `bonfire\|brazier\|barrel_fire` | Regex for which props count as fires. |
| Line-of-sight layer mask | `0` | `0` asks the game for its own terrain/geometry mask. Only change this if you know why. |

## What works

**Confirmed in a raid:** proximity healing, limb priority, the line-of-sight
check, the HUD rate arrow, and discovery across maps.

**Not confirmed:** lighting cold barrels. The code is there -- walk up to an
unlit barrel carrying a lighter or matches, get a **Light** prompt, and it joins
the fires that heal you -- but the prompt has never been seen in a raid, and the
match-consumption path has never executed. Treat it as unfinished. Bug reports
on it are welcome and expected.

A note on how lighting is *meant* to work, if you try it: the mod only knows
about fires it has been near, so you need to have passed a burning barrel earlier
in the raid for there to be a flame to copy. A player-lit fire burns you, exactly
like a vanilla one. Matches are consumed; lighters are not.

## Building

```
dotnet build src/BarrelHealing.Client/BarrelHealing.Client.csproj -c Release -p:SPTPath=<your SPT folder>
```

Or build and install in one step:

```
scripts/pack.ps1 -SPTPath <your SPT folder>
```

Run those from PowerShell rather than Git Bash -- a Windows path mangled through
Git Bash arrives as `C:SPT` and the build stops with "is not an SPT install root",
which reads like a missing install rather than a quoting problem.

There are no NuGet dependencies. The project references the game's own assemblies
out of your SPT folder, which is why `SPTPath` is required.

## For modders

`CLAUDE.md` in this repo is the engineering log, and it is unusually blunt about
what went wrong. If you are doing anything with EFT's health system or with
runtime scene objects, two findings there will save you an evening:

- **`ActiveHealthController.Heal(EBodyPart, float)` is an empty stub.** It exists,
  it is public, it compiles, it runs, and it does nothing. Use `ChangeHealth`.
- **Scene props do not exist at raid start.** EFT streams them in as you approach,
  so a scan at spawn finds nothing no matter what you search for.

## Licence

Not yet chosen.
