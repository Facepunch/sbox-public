namespace Sandbox.Npcs;

public partial class BaseNpc : Component
{
	/// <summary>
	/// Broadcast a stimulus from this NPC's position for nearby NPCs to sense (a gunshot when
	/// it fires, a death when it dies, and so on).
	/// </summary>
	public void EmitStimulus( string kind, float radius = 1024f, float lifetime = 1f )
	{
		Scene?.GetSystem<NpcSystem>()?.EmitStimulus( kind, WorldPosition, GameObject, radius, lifetime );
	}
}
