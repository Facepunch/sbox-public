namespace Sandbox.Npcs;

/// <summary>
/// Something that happened in the world that NPCs can sense -- a gunshot, an explosion, a
/// scream. Broadcast via <see cref="NpcSystem.EmitStimulus"/>; sensed by <see cref="Layers.SensesLayer"/>.
/// </summary>
public readonly record struct Stimulus( string Kind, Vector3 Position, GameObject Source, float Radius, float Expiry );

/// <summary>
/// Common stimulus kinds. Plain strings so UGC can add its own; this is just the shared set.
/// </summary>
public static class StimulusKinds
{
	public const string Gunshot = "gunshot";
	public const string Death = "death";
}
