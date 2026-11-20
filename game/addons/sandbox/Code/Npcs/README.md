# NPCs

How to make an NPC. The smallest working example is [`Examples/ExampleNpc.cs`](Examples/ExampleNpc.cs) — copy it.

## The idea

An NPC **thinks** once per tick:

1. **Senses** gather what it perceives (visible/audible things, disturbances) into an `Awareness` flag set.
2. **`GetSchedule()`** picks what it wants to do — the highest-priority behaviour that applies.
3. The active **schedule** runs a sequence of **tasks** (move here, look at that, fire, wait).

A higher-priority schedule automatically interrupts a lower one when awareness changes, so an idle NPC drops what it's doing the moment it sees a threat — you don't poll for it.

## Making one

Subclass `Npc` and override three things:

```csharp
public sealed class GuardNpc : Npc
{
    // 1. Who am I? (drives how others treat me)
    public override string Faction => Factions.Ally;

    // 2. How do I feel about other factions?
    protected override void SetupRelationships()
    {
        Hates( Factions.Enemy, Factions.Monster );
        Likes( Factions.Player );
    }

    // 3. What do I want to do right now? (most important first)
    public override ScheduleBase GetSchedule()
    {
        var enemy = Senses.GetBestTarget();
        if ( enemy.IsValid() )
            return GetSchedule<CombatEngageSchedule>(); // configure & return

        return GetSchedule<WanderSchedule>();
    }
}
```

That's the whole contract. Everything else (senses, navigation, animation, speech, preemption) is provided.

### Factions & relationships

An NPC's `Faction` is its identity (`Factions.Player/Ally/Enemy/Citizen/Monster`, or your own string). In `SetupRelationships` declare how it regards *other* factions with `Hates` / `Fears` / `Likes`. Anything unlisted is neutral (ignored). Same faction is friendly by default. For a one-off ("this cop now hates *that* player"), call `SetDisposition(other, Disposition.Hostile)` at runtime.

### Schedules & tasks

- A **schedule** (`ScheduleBase`) builds a list of **tasks** in `OnStart` via `AddTask(...)`, and has a `Priority`. Reusable ones live in `Schedules/` (`WanderSchedule`, `InvestigateSchedule`, `FollowSchedule`).
- A **task** (`TaskBase`) is one step that returns `Running` / `Success` / `Failed` each tick. Reusable ones live in `Tasks/` (`MoveTo`, `LookAt`, `Wait`, `FireWeapon`, ...).
- `GetSchedule<T>()` returns a cached, reused instance — **set its inputs every time you return it** (e.g. `investigate.Target = ...`).

### Reacting to the world

- `Senses` gives you `GetBestTarget()` (highest-priority hostile), `GetNearestVisible(disposition)`, `Disturbance` (a heard gunshot/death), and more.
- Broadcast events for other NPCs to hear with `EmitStimulus(StimulusKind.Gunshot)`. They sense it as `Senses.Disturbance`.
- To react to being hit, override `OnHurt(in DamageInfo)`; for death, override `Die(in DamageInfo)` and call `base.Die(...)`.

## The prefab

The four AI layers are added automatically, but you must add the components they drive:

- **NavMeshAgent** — required to move (an NPC without one warns and can't path; a purely physics-driven NPC like the rollermine skips it).
- **SkinnedModelRenderer** — the model, wired to the `Renderer` field.
- **Collider** — a solid body collider so it can be shot and (if pressable) used. Don't tag it `playercontroller`.
- **Rigidbody** — for physics interactions (physgun, ragdoll).

Copy an existing prefab under `Assets/entities/sents/npc/` rather than building from scratch.
