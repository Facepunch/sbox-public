# NPCs

How to make an NPC. The smallest working example is [`Examples/ExampleNpc.cs`](Examples/ExampleNpc.cs) — copy it.

## The idea

An NPC **thinks** once per tick:

1. **Senses** gather what it perceives (visible/audible things, disturbances) into an `Awareness` flag set.
2. **`SelectSchedule()`** picks what it wants to do — the highest-priority behaviour that applies.
3. The active **schedule** runs a sequence of **tasks** (move here, look at that, fire, wait).

A higher-priority schedule automatically interrupts a lower one when awareness changes, so an idle NPC drops what it's doing the moment it sees a threat — you don't poll for it.

## Making one

Subclass `BaseNpc` and override three things:

```csharp
using Sandbox.Npcs;
using Sandbox.Npcs.Schedules;

public sealed class CuriousNpc : BaseNpc
{
    public override string Faction => Factions.Citizen;

    protected override void SetupRelationships()
    {
        SetRelationship( Factions.Enemy, Disposition.Fearful );
        SetRelationship( Factions.Monster, Disposition.Fearful );
    }

    protected override BaseSchedule SelectSchedule()
    {
        if ( Senses?.Disturbance is { } disturbance )
        {
            var investigate = GetSchedule<InvestigateSchedule>();
            investigate.Target = disturbance.Position;
            return investigate;
        }

        return GetSchedule<WanderSchedule>();
    }
}
```

That's the whole contract. Everything else (senses, navigation, animation, speech, preemption) is provided. An NPC with its own brain instead of schedules overrides `Think()`.

### Factions & relationships

An NPC's `Faction` is its identity (`Factions.Player/Ally/Enemy/Citizen/Monster`, or your own string). In `SetupRelationships` declare how it regards *other* factions with `SetRelationship(faction, disposition)` (`Hostile`, `Fearful` or `Friendly`). Anything unlisted is neutral (ignored). For a one-off ("this cop now hates *that* player"), call `SetRelationship(other, Disposition.Hostile)` at runtime.

### Schedules & tasks

- A **schedule** (`BaseSchedule`) builds a list of **tasks** in `OnStart` via `AddTask(...)`, and has a `Priority`. The engine provides reusable ones in `Sandbox.Npcs.Schedules` (`WanderSchedule`, `InvestigateSchedule`, `FollowSchedule`).
- A **task** (`BaseTask`) is one step that returns `Running` / `Success` / `Failed` each tick. The engine provides reusable ones in `Sandbox.Npcs.Tasks` (`MoveToTask`, `LookAtTask`, `WaitTask`, `FireWeaponTask`, ...).
- `GetSchedule<T>()` returns a cached, reused instance — **set its inputs every time you return it** (e.g. `investigate.Target = ...`).

### Reacting to the world

- `Senses` gives you `FindBestTarget()` (highest-priority hostile), `FindNearestVisible(disposition)`, `Disturbance` (the nearest heard gunshot/death), and more.
- Broadcast events for other NPCs to hear with `EmitStimulus(StimulusKinds.Gunshot)`. They sense it as `Senses.Disturbance`.
- To shape a hit before it lands, override `OnDamaging(IDamageable.DamageEvent)`; to react to one, override `OnDamaged(IDamageable.DamageEvent)`; for death, override `OnDeath(IDamageable.DeathEvent)` and call `base.OnDeath(...)`.

### Health & damage

`Health` drops by the damage taken and the NPC dies below 1. `MaxHealth` is raised to `Health` when the NPC starts. Damage and death are raised through the engine's `Component.IDamageable.IEvents` (`OnDamaging`, `OnDamaged`, `OnDied`), so game rules (headshot multipliers, blood, what happens to the ragdoll) belong in the game's listeners, not in the NPC. Impact damage from being thrown carries the surface normal in `DamageInfo.Normal`.

## The prefab

The Animation layer is added automatically. Add the Senses, Navigation and Speech layers the NPC uses (each is optional; tasks that need a missing one fail), and the components they drive. Configure `SightRange`, `HearingRange`, `FieldOfView`, `QuietSpeed` and `Targets` on `SensesLayer`; disable its `Enabled` property to stop sensing:

- **CitizenNpcAnimator**, if the model uses the citizen animgraph; it drives the graph from the Animation layer.
- **NavMeshAgent** — required to move (an NPC without one warns and can't path; a purely physics-driven NPC like the rollermine skips it).
- **SkinnedModelRenderer** — the model, wired to the `Renderer` field.
- **Collider** — a solid body collider so it can be shot and (if pressable) used. Don't tag it `playercontroller`.
- **Rigidbody** — for physics interactions (physgun, ragdoll).

Copy an existing prefab under `Assets/entities/sents/npc/` rather than building from scratch.
