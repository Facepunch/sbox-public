namespace Sandbox.Npcs;

/// <summary>Shared NPC coordination for a scene: who is in it, who may speak, and the stimuli NPCs sense.</summary>
public sealed partial class NpcSystem : GameObjectSystem<NpcSystem>
{
	private readonly HashSet<BaseNpc> _npcs = [];
	private readonly List<BaseNpc> _sensingNpcs = [];
	readonly NpcSensingSnapshot _sensingSnapshot = new();
	bool _scanningSenses;

	/// <summary>NPCs currently registered in the scene.</summary>
	public IReadOnlyCollection<BaseNpc> Npcs => _npcs;

	/// <summary>Creates the scene NPC coordinator.</summary>
	public NpcSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, 0, Update, "UpdateNpcs" );
	}

	private void Update()
	{
		UpdateSpeech();
		PruneStimuli();

		if ( !Networking.IsHost || !NpcConVars.Enabled ) return;
		ScanSenses();
	}

	internal void ScanSenses( bool force = false )
	{
		if ( _scanningSenses ) return;
		_scanningSenses = true;
		// Game callbacks can spawn or remove NPCs during a scan.
		_sensingNpcs.Clear();
		_sensingNpcs.AddRange( _npcs );
		try
		{
			var kinds = Layers.SensesLayer.SenseTargets.None;
			var anyDue = false;
			foreach ( var npc in _sensingNpcs )
			{
				if ( !npc.IsValid() || !npc.Senses.IsValid() || !npc.Senses.IsScanDue( npc, force ) ) continue;
				kinds |= npc.Senses.Targets;
				anyDue = true;
			}
			if ( !anyDue ) return;
			_sensingSnapshot.Capture( Scene, _sensingNpcs, kinds );
			foreach ( var npc in _sensingNpcs )
			{
				if ( !npc.IsValid() || !npc.Senses.IsValid() || !npc.Senses.IsScanDue( npc, force ) ) continue;
				try { npc.Senses.ScanEnvironment( _sensingSnapshot.Targets ); }
				catch ( Exception e ) { Log.Error( e, $"Exception sensing on {npc}" ); }
			}
		}
		finally
		{
			_sensingSnapshot.Clear();
			_sensingNpcs.Clear();
			_scanningSenses = false;
		}
	}

	/// <summary>Registers an NPC with the scene.</summary>
	internal void Register( BaseNpc npc )
	{
		if ( npc.IsValid() )
			_npcs.Add( npc );
	}

	/// <summary>Unregisters an NPC from the scene.</summary>
	internal void Unregister( BaseNpc npc )
	{
		_npcs.Remove( npc );
	}
}
