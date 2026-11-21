# Buggy rig

So the idea is: the springs and the suspension need to be rigged to a wheel. Then we position the wheels in code.
Everything else just follows. No animations or animgraph, just a skeleton and skinning, export it like you would a
character.

Right now the game's running on a stand-in I hacked together from the current export (`buggy_rigged.fbx`). Yours
replaces it.

## What needs to change

- The suspension's all baked into the chassis mesh at the moment. The moving bits (arms, shocks, springs, knuckles,
  boots) need skinning, see below.
- The wheels are separate objects parented under the axles. They need to be skinned to bones too, all one model.
- Same with the steering wheel.
- There's no skeleton yet, so that's the main thing. Names below need to match. But we can change them if you want.

## Bones

`{c}` is the corner: `fl`, `fr`, `rl`, `rr`. Put everything where it sits with the car at rest on its wheels. Bone
orientation doesn't matter unless I say so.

| Bone | Parent | Where | What's skinned to it |
| --- | --- | --- | --- |
| `root` | | Origin | The body and anything that doesn't move |
| `wheel_{c}` | `root` | Where the arm meets the knuckle, on the steering axis. Rear is where the shaft meets the hub | Knuckle / hub carrier. Only goes up and down |
| `wheel_{c}_steer` | `wheel_{c}` | Same spot as `wheel_{c}`. Front only | Front axle / spindle. This is what steers |
| `wheel_{c}_spin` | `wheel_{c}_steer` (rear: `wheel_{c}`) | Dead centre of the tyre, with one axis along the axle | Tyre, rim, hub, nuts. This rolls |
| `steering_wheel` | `root` | Centre of the column | Steering wheel |

That's it, 3 a corner at the front and 2 at the back. The spin bone is where the game holds the wheel up, so it really
does need to be the middle of the tyre.

## Skinning

The wheels only ever go straight up and down, so the arms don't need bones, they're just a weight gradient:

- Arms and the rear shafts (boots included), 0% `wheel_{c}` at the pivot on the chassis up to 100% where they meet the
  knuckle / hub. The rest on `root`.
- Shocks (body, rod and spring), fade from `root` at the top mount to whatever the bottom's bolted to. At the back
  that's `wheel_{c}`. At the front it's on the arm, so it gets the same weights the arm has at that spot.
- The bar across the front between the knuckles. Middle stays on `root`, ends blend onto each side's `wheel_{c}`.
- Everything else is 100% on one bone.
