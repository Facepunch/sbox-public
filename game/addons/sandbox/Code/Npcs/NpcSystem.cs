namespace Sandbox.Npcs;

/// <summary>Shared NPC coordination for a scene.</summary>
public sealed partial class NpcSystem : GameObjectSystem<NpcSystem>
{
	private readonly HashSet<Npc> _npcs = [];

	/// <summary>NPCs currently registered in the scene.</summary>
	public IReadOnlyCollection<Npc> Npcs => _npcs;

	/// <summary>Creates the scene NPC coordinator.</summary>
	public NpcSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, UpdateSpeech, "UpdateNpcSpeech" );
	}

	/// <summary>Registers an NPC with the scene.</summary>
	internal void Register( Npc npc )
	{
		if ( npc.IsValid() )
			_npcs.Add( npc );
	}

	/// <summary>Removes an NPC and any speech reservation it owns.</summary>
	internal void Unregister( Npc npc )
	{
		_npcs.Remove( npc );
	}
}
