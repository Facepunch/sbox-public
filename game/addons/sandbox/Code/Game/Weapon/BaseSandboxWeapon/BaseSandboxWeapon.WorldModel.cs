using Sandbox.Citizen;

public partial class BaseSandboxWeapon
{
	public interface IEvent : ISceneEvent<IEvent>
	{
		public void OnCreateWorldModel() { }
		public void OnDestroyWorldModel() { }
	}

	// WorldModelPrefab, HoldBone and HoldType are inherited from the engine BaseCombatWeapon.
	[Property, Feature( "WorldModel" )] public GameObject DroppedGameObject { get; set; }

	protected override void CreateWorldModel()
	{
		var player = GetComponentInParent<PlayerController>();
		if ( player?.Renderer is null ) return;

		CreateWorldModel( player.Renderer );
	}

	
	/// <summary>
	/// True while this weapon is loose in the world as a pickup rather than held. Host authoritative -
	/// the change callback toggles the pickup components (physics, the standalone model) on every peer.
	/// </summary>
	[Sync( SyncFlags.FromHost ), Change( nameof( OnIsDroppedChanged ) )]
	public bool IsDropped { get; set; } = true;

	private void OnIsDroppedChanged( bool oldValue, bool newValue ) => ApplyDroppedState( newValue );

	private void ApplyDroppedState( bool dropped )
	{
		// Carried weapons follow their parent exactly; dropped ones are physics objects that want
		// interpolated network transforms.
		GameObject.Network.Interpolation = dropped;

		var rb = GetComponent<Rigidbody>( true );
		if ( rb.IsValid() ) rb.Enabled = dropped;

		var col = GetComponent<ModelCollider>( true );
		if ( col.IsValid() ) col.Enabled = dropped;

		if ( DroppedGameObject.IsValid() ) DroppedGameObject.Enabled = dropped;
	}

	/// <summary>
	/// Creates and attaches the world model to the given renderer's bone.
	/// Use this overload when the weapon is held by something other than a player (e.g. an NPC).
	/// </summary>
	public void CreateWorldModel( SkinnedModelRenderer renderer )
	{
		if ( renderer is null ) return;

		if ( Networking.IsHost )
			IsDropped = false;

		var worldModel = WorldModelPrefab?.Clone( new CloneConfig
		{
			Parent = renderer.GetBoneObject( HoldBone ) ?? GameObject,
			StartEnabled = true,
			Transform = global::Transform.Zero
		} );
		if ( worldModel.IsValid() )
		{
			worldModel.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;

			// NPCs don't eject brass
			foreach ( var model in worldModel.GetComponentsInChildren<BaseWeaponModel>( true ) )
				model.EjectBrass = null;

			WorldModel = worldModel;
			IEvent.PostToGameObject( WorldModel, x => x.OnCreateWorldModel() );
		}
	}

	protected override void DestroyWorldModel()
	{
		if ( WorldModel.IsValid() )
			IEvent.PostToGameObject( WorldModel, x => x.OnDestroyWorldModel() );

		WorldModel?.Destroy();
		WorldModel = default;

		if ( Networking.IsHost )
			IsDropped = true;
	}
}
