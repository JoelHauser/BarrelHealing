# Where the fires actually are, and what they're made of

> **The verified section directly below supersedes everything after it.** The
> original survey further down was under-sampled and got the prop names wrong;
> it is kept for its method and its map counts, both still useful, with a
> correction notice attached.

## Verified prop geometry (2026-09-14, second AssetRipper pass)

A second pass against `H:\SPT4.1.X\EscapeFromTarkov_Data` resolved the prop
hierarchies properly, by following `m_Components` to each Transform and reading
child names and local offsets rather than searching by name. Everything in this
section is read off the real assets.

**There are two unrelated fire props, and this is the crux of the ignition bug.**

| | `bonfire` | `barrel_fire` |
| --- | --- | --- |
| What it is | stone ring around a low woodpile, on the ground | steel drum, waist height |
| State in shipped maps | **unlit** -- this is what gets a Light prompt | **lit** -- this is what heals you today |
| Children | `model`, `model_lod`, `shadow` | `barrel_metal`, `barrel_metal_lod`, `collider`, `shadow`, `shadow_lod`, `barrel_fire_heat`, `barrel_fire_smoke (1)` |
| Fire objects | **none, not even disabled** | `barrel_fire_heat`, `barrel_fire_smoke (1)`, both at local `(0.02, 0.52, 0.04)` |
| Colliders | `Bonfire_COLLIDER`, `Bonfire_BALLISTIC_woodthick`, `Bonfire_stones_COLLIDER`, `Bonfire_stones_BALLISTIC_stone` | `Barrel_fire_COLLIDER`, `Barrel_fire_BALLISTIC_metalthin`, `Barrel_fire_Trigger_hurt_fire` |
| Root carries | `LODGroup` (confirmed) | `LODGroup` |

`brazier` is a third prop again: `brazier_LOD0`/`LOD1`, `ballistic_set`,
`collider_set`, `shadow_LOD0`, and no fire/light/particle children in any
instance inspected. No lit variant has ever been found, which is why
`LightableBarrelNamePattern` deliberately excludes it.

**What this settles, and what it breaks:**

1. **Cold detection is sound.** An unlit `bonfire` has no `ParticleSystem`
   anywhere in its subtree, disabled or otherwise, so
   `GetComponentInChildren<ParticleSystem>(true) == null` really does mean cold.
   This was the assumption most likely to sink v0.2 and it holds.
2. **`BonfireIgnition.UnlitBaseChildren` is wrong for the real donor.** It
   excludes `model`/`model_lod`/`shadow` and clones everything else -- but the
   drum's non-fire children are `barrel_metal`, `barrel_metal_lod`, `collider`
   and `shadow_lod`, none of which are on that list. Lighting a woodpile would
   clone a **whole steel drum mesh and its collider** onto it. The exclusion list
   was written from `bonfire_withfire`, a dev-scene prop whose children genuinely
   are `model`/`model_lod`/`shadow`.
3. **The flame would float.** Both drum fire objects sit `0.52m` up, because that
   is the drum's rim. Cloned with `worldPositionStays: false` onto a ground-level
   bonfire, the fire ends up half a metre above the sticks.
4. **A naive allow-list drops the glow.** Copying only `barrel_fire_*` misses the
   point lights, which hang off `barrel_metal`, not off the fire objects.

The dev-scene `bonfire_withfire` is worth ignoring as a donor model: its
`model`/`model_lod`/`shadow` are *disabled*, `barrel_fire_heat` is disabled, and
its fire children sit at offsets like `(1.38, 0.63, 1.53)` -- over a metre to the
side. It is a design reference, not a shipped arrangement.

### Unlit bonfire locations on Shoreline

Logged live in a raid by the pre-rewrite build, which attached a Light prompt to
each. Village and Village_hut_01 are the easiest pair to reach together.

| Scene / parent | X | Y | Z |
| --- | --- | --- | --- |
| West — Village | 388.87 | -54.64 | -78.10 |
| West — Village_hut_01 | 449.48 | -54.52 | 164.05 |
| West — Outdoor | 48.84 | -21.79 | -122.01 |
| Sanatorium — Props | -216.06 | -5.13 | -153.30 |
| Middle — to_middle | -81.41 | -42.40 | 120.67 |
| North — Outdoor | -418.29 | -20.64 | -252.86 |
| East — PROPS | -558.44 | -19.11 | -338.77 |

### The 3D reference

The four LOD0 meshes (`Bonfire_LOD0`, `Bonfire_stones_LOD0`, `Barrel_fire_LOD0`,
`brazier_LOD0`) were exported to GLB and published as a private, orbitable viewer
alongside these findings:

**https://claude.ai/artifact/T6njQiRch48gVTXE8kSCv6**

That link is private to the repo owner's Claude account; it will not open for
anyone else. **The extracted meshes are deliberately not committed to this
repository** -- they are BSG's assets, and shipping ripped game geometry in a
public repo is a licensing problem no matter how small the file. Regenerate them
with the recipe below if the viewer is ever needed again.

### Regenerating the geometry export

The mesh export endpoint is not in the original recipe at the bottom of this file:

1. `winget install --id AssetRipper.AssetRipper` (2.0.0)
2. `AssetRipper.GUI.Free.exe --headless --port 8199`
3. `POST /LoadFolder` with `path=H:\SPT4.1.X\EscapeFromTarkov_Data` — takes about
   8 minutes and reads the whole game.
4. `GET /Search/View?q=Bonfire_LOD0` to find the asset, then
   `GET /Assets/Model.glb?<the Path query from its View link>` to export it.
   The GameObject itself 404s — `Model.glb` only accepts Mesh assets, so search
   for the `*_LOD0` mesh names rather than the prop names.
5. `GET /Assets/Json?Path=...` walks `m_Components` → Transform → `m_Children`,
   which is how the hierarchies above were read. `FileID` in a component
   reference is collection-relative, so cross-collection refs (like a MeshFilter's
   `m_Mesh`) are easier to resolve by searching the mesh name than by path maths.

Note AssetRipper **normalises vertex bounds on export** — every mesh returns in a
±1 box, so proportions within a mesh are true but absolute real-world dimensions
are not recoverable this way.

---

## Original survey (2026, first pass) — see correction

> **Correction, 2026-09-14 — read this before trusting anything below.** The first
> in-raid test contradicted this document twice.
>
> 1. **The name claim below is wrong.** "None of `barrel` ... appear anywhere in
>    the loaded asset names" and "'barrel' alone returns 41,426 `GameObject` hits,
>    but every one sampled is a weapon part" — the operative word was *sampled*.
>    The real, actually-instantiated props on Shoreline are named
>    **`barrel_fire_wfire`** and **`barrel_fire`** (Unity-suffixed `barrel_fire (3)`
>    etc.), sitting under a parent transform literally called **`barrels`**, in
>    scene `SBG_Shoreline_Light`. Both were found by logging live transform paths
>    in a raid, not by asset search. `bonfire`/`brazier` may well also exist, but
>    they are not what a player walks up to on Shoreline.
> 2. **The lit/unlit distinction below is unverified for these props.** The
>    `bonfire` vs `bonfire_withfire` pair was read out of `custom_DesignStuff`, an
>    internal dev scene. Both live props seen so far (`barrel_fire_wfire` and
>    `barrel_fire (3)`) are *lit* and own particle children, with slightly
>    different hierarchies from each other (the latter has no `TorchFire`, and its
>    `Point light`s hang off `barrel_metal`). No unlit live instance has been
>    inspected yet, so "the unlit one is missing the fire entirely" is still an
>    assumption carried over from the dev-scene sample.
>
> The per-map counts further down were never re-verified after this and should be
> treated as an AssetRipper artefact, not ground truth. The method at the bottom
> ("Reproducing this") is still sound; its conclusions were just under-sampled.
> Note also that the path in it (`C:\HUH`) is the old box -- use `H:\SPT4.1.X`.


Extracted statically with AssetRipper 2.0.0 (`winget install AssetRipper.AssetRipper`),
run headless (`--headless --port 8199`) and driven entirely through its REST/HTML
API (`POST /LoadFolder` with `C:\HUH\EscapeFromTarkov_Data`, then `/Search/View?q=...`,
`/Assets/View`, `/Assets/Json`) — no raid was launched for any of this. Unity
version confirmed by the tool: `2022.3.43f1`.

## The names are not what we guessed

None of `barrel`, `campfire`, `burning`, `bochka` (Russian for barrel),
`kostyor` (Russian for bonfire), or `firepit` appear anywhere in the loaded
asset names. `barrel` alone returns 41,426 `GameObject` hits, but every one
sampled is a weapon part (`barrel_uzi_pro_iwi_smg_170mm_9x19`,
`mount_12g_toni_system_barrel_clamp_rail`, etc.) — completely unrelated.

The real names are:

- **`bonfire`** (and `Bonfire`, `bonfire02`, `bonfire_withfire`, plus a couple
  of one-off variants like `Bonfire_COLLIDER`) — this is the STALKER-style
  barrel/receptacle fire. Confirmed by walking its child hierarchy (see below).
- **`brazier`** (`brazier_LOD0`/`brazier_LOD1`, plus `ballistic_set`,
  `collider_set`, `shadow_LOD0` siblings) — a second, differently-built prop.
  The one instance inspected in full (Woods, `woods_combined`) has **no**
  fire/light/particle children at all, just LODs and colliders. Whether any
  `brazier` instance elsewhere is ever lit is unconfirmed — worth checking
  again if this prop turns out to matter.

## `bonfire` vs `bonfire_withfire`: a real lit/unlit pair, not a toggle

Two instances compared directly, both fetched via `/Assets/Json` (Transform's
`m_Children`) then resolved back to names via `/Assets/View`:

**`bonfire_withfire`** (scene `custom_DesignStuff`, an internal dev/reference
scene, not a shipped map) has 7 children:
`trigger_hurt_fire`, `model`, `model_lod`, `shadow`, `barrel_fire_smoke (1)`,
`TorchFire`, `barrel_fire_heat`.

**Plain `bonfire`** (scene `Shopping_Mall_2` — a real, shipped map) has only 3:
`model`, `shadow`, `model_lod`.

The unlit one isn't a lit prefab with its fire disabled — it's missing the
fire entirely: no light, no particle system, no damage trigger, nothing to
flip on. **Lighting one would mean instantiating a copy of the fire/trigger
sub-hierarchy from a `_withfire` instance and parenting it onto the unlit
barrel's `Transform`** at runtime (`UnityEngine.Object.Instantiate`), not
toggling a component's `enabled` flag. That's a materially bigger feature
than the current proximity-heal prototype — worth deciding whether it's
wanted before starting it.

Note `trigger_hurt_fire` living right on the lit prefab: this is presumably
what `EFT.Interactive.FlameDamageTrigger` (found earlier via reflection) is
attached to. Confirms fire hazard and fire decoration are the same object,
which is also why "leave `FlameDamageTrigger` alone" (the earlier decision)
is the right call — it's part of this exact prop, not a separate system.

## Per-map counts

Counts are of matched `GameObject` rows per scene — for `bonfire` this
includes duplicate-suffixed instances (`Bonfire (1)`, `Bonfire (2)`, ...),
which is Unity's own disambiguation for multiple same-named siblings, so
these are almost certainly real per-scene instance counts and not one
prefab counted multiple times.

**Real, shipped maps** (scene names read directly off each asset, not
inferred):

| Map | Scene | bonfire | brazier |
| --- | --- | --- | --- |
| Shoreline | Shoreline_North | 1 | |
| Shoreline | Shoreline_East | 1 | |
| Shoreline | Shoreline_Middle | 1 | 33 |
| Shoreline | Shoreline_South | | 21 |
| Shoreline | shoreline_sanatorium | 1 | |
| Shoreline | Shoreline_West | 3 | 48 |
| Woods | woods_combined | 2 | 6 |
| Shopping Mall | Shopping_Mall_2 | 5 | |
| Reserve | Reserve_Base_Main_Checkpoint | 2 | |
| Reserve | Reserve_Base_MilitaryDorms | | 3 |
| Reserve | Reserve_Base_Mortar_Position | 1 | |
| Reserve | Reserve_Base_outside_BLOCK_4 | 1 | |
| Reserve | Reserve_Base_outside_BLOCK_10 | 1 | |
| Lighthouse | Lighthouse_Abadonned_pier | 1 | |
| Lighthouse | Lighthouse_Complex | | 3 |
| Lighthouse | Lighthouse_Fisher_Village | 1 | |
| Lighthouse | Lighthouse_Marina | 2 | 3 |
| Lighthouse | Lighthouse_SummerHotel | 1 | |
| Lighthouse | Lighthouse_SwampVillage | 2 | 30 |
| Lighthouse | Lighthouse_WaterStation | 2 | 3 |
| Streets (City) | City_NE_02_TD_Klimova_indoor | 1 | 3 |
| Streets (City) | City_SE_01_courtyard_B | | 12 |
| Streets (City) | City_SE_02_courtyard_A | | 3 |
| Streets (City) | City_SW_02_LexOs_blockpost | | 15 |
| Streets (City) | City_Roads_Razvedchikov | | 9 |
| Ground Zero | Sandbox_Area_05_courtyard | 1 | |

**Uncertain** — named plausibly but not confirmed against a known map:

| Scene | bonfire | Notes |
| --- | --- | --- |
| bunker_2 | 3 | No map prefix; could be Reserve's bunker or a Labyrinth-related scene |
| Custom_ChemicalFactory | 1 | Capitalized "Custom_" unlike the clearly-internal lowercase `custom_*` scenes below — genuinely unclear if this is shipped or a reference scene |

**Internal dev/design/lighting/sound scenes** — not shipped map geometry, so
these bonfire objects are almost certainly never seen by a player in a raid:
`custom_multiScene` (4), `custom_DesignStuff` (1, this is the `bonfire_withfire`
example above), `custom_Light` (7), `shoreline_DesignStuff` (1),
`Shopping_Mall_DesignStuff` (1), `Reserve_Base_DesignStuff` (1),
`Reserve_Base_Light` (14), `woods_design_stuff` (1), `woods_light` (7),
`Lighthouse_DesignStuff` (1), `Lighthouse_Light` (1), `Lighthouse_DesignMain`
(1, this is the `zone_bonfire` hit — likely an ambiance/trigger volume, not
a mesh, unconfirmed), `Woods_Sound` (7), `Custom_Sound` (1),
`Factory_Sound_Rework` (7), `custom_DesignMain` (1).

**Not found under either name**: Customs, Factory (only a sound-authoring
scene, no map geometry hit), Interchange, Labs/Laboratory. Absence of a hit
here is not proof of absence in the raid — it only means neither `bonfire`
nor `brazier` matched anything in those maps' loaded scenes. A different
prop name, or a scene not covered by this pass, would explain it just as
well.

## Reproducing this

1. `winget install --id AssetRipper.AssetRipper`
2. Run headless on a fixed port:
   `AssetRipper.GUI.Free.exe --headless --port 8199`
3. `POST http://127.0.0.1:8199/LoadFolder` with form field `path` =
   `C:\HUH\EscapeFromTarkov_Data` (this takes several minutes — it's reading
   the whole game).
4. `GET http://127.0.0.1:8199/Search/View?q=<term>` for a name search;
   `GET /Assets/View?Path=<encoded pointer>` and `/Assets/Json?Path=...` to
   inspect one object's components/children/parent by following the
   `m_Children` / `m_Father` pointers in the JSON.

Path IDs and collection indices are assigned per load and are **not stable**
across sessions — re-running this will get different numbers for the same
objects. Only the names, scene names, and counts above are durable.
