using Sandbox.Citizen;

public partial class BaseSandboxWeapon
{
	public interface IEvent : ISceneEvent<IEvent>
	{
		public void OnCreateWorldModel() { }
		public void OnDestroyWorldModel() { }
	}

	[Property, Feature( "WorldModel" )] public GameObject DroppedGameObject { get; set; }

	/// <summary>
	/// Creates the world model and attaches it to the holder's hold bone, whether a player or an NPC holds this.
	/// </summary>
	protected override void CreateWorldModel()
	{
		var renderer = HolderRenderer;
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

	/// <summary>
	/// True while this weapon is not out in someone's hands: loose in the world as a pickup, or put away. Host
	/// authoritative - the change callback shows the standalone model on every peer. The engine weapon switches
	/// the physics and the network interpolation itself, from whether it is held.
	/// </summary>
	[Sync( SyncFlags.FromHost ), Change( nameof( OnIsDroppedChanged ) )]
	public bool IsDropped { get; set; } = true;

	private void OnIsDroppedChanged( bool oldValue, bool newValue )
	{
		if ( DroppedGameObject.IsValid() ) DroppedGameObject.Enabled = newValue;
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
