# Where the fires actually are, and what they're made of

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
