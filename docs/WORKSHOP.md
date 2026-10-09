# The workshop environment

The holographic desk now sits in a real engineer's workshop instead of a black void. Every
prop is a photo-scanned **CC0** model from [Poly Haven](https://polyhaven.com), free for any use
(credits: `Assets/Cypher/ThirdParty/PolyHaven/CREDITS.md`).

```
                      FRONT WALL (z = +8.5)
   drill press · welding cart · propane · extinguisher  [roller-shutter door]  metal desk + toolbox  TRASH
  ┌────────────────────────────────────────────────────────────────────────────┐
  │ power box                                              crates · boxes      │
  │ toolbox      ╭─ hanging lamps ─╮        holograms                          │
  │ WORKBENCH    vise · drill · tools       ╭──────╮        STEEL SHELVES      │
  │ (left wall)  multimeter · circuit board │ DESK │        crates · paint     │
  │ wall lamps   desk lamp · stool          ╰──────╯        tool chest         │
  │                                         (you)                              │
  │ shelves · boxes            pipes                       crate              │
  └────────────────────────────────────────────────────────────────────────────┘
                      BACK WALL (z = −6)
```

- **Room:** worn garage-concrete floor, concrete-block walls with corrugated steel above,
  corrugated ceiling with painted steel I-beams and columns, yellow safety lines.
- **Light:** warm tungsten pools from five hanging industrial lamps (two cast shadows), wall
  lamps over the bench, cool fluorescent fill, and the cyan hologram glow at the center. A
  reflection probe gives the metal props believable reflections.
- **Atmosphere:** lighter haze (fog), brighter indoor ambient light, floating dust.

## Rebuilding / tweaking

- Run **Cypher → Build Workshop Scene** after any change. It rebuilds the room from
  `Assets/Cypher/Editor/WorkshopEnvironment.cs`. Each prop is one line, like
  `Prop("bench_vice_01", zone, new Vector3(x, y, z), yawDegrees, expectedHeight)`, so moving or
  rotating something is just a number change. Props are placed by their measured size: they
  stand on the floor or bench and slide flush against walls automatically.
- Materials are generated into `ThirdParty/PolyHaven/Materials/` (URP Lit with proper
  metallic/smoothness maps). You can adjust their colors or smoothness in the Inspector.
- Want more props? Pick any model at polyhaven.com/models, add its id to `MODELS` in
  `tools/fetch_polyhaven.py`, run `python3 tools/fetch_polyhaven.py`, then add a `Prop(...)` line.

## Performance (MacBook Air)

Around 35 mid-poly models, about 12 realtime lights and 3 shadowed lights, all static-batched.
If the frame rate drops (Game view → **Stats**), the cheapest wins are:
1. Turn off shadows on the two shadowed hanging lamps (*Workshop → Ceiling Lamps → Lamp Light → Shadow Type: No Shadows*).
2. Lower *Project Settings → Quality* one level.
