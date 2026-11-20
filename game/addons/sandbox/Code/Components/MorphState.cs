/// <summary>
/// Persists morph values on a GameObject so they survive over the network and dupes. 
/// </summary>
[Title( "Morph State" )]
[Category( "Rendering" )]
public sealed class MorphState : Component
{
	/// <summary>
	/// Serialized morphs. Persisted for dupes/saves — not synced over the network.
	/// </summary>
	[Property]
	public string SerializedMorphs { get; set; }

	protected override void OnStart()
	{
		Apply();
	}

	/// <summary>
	/// Apply a partial morph batch on the host, then broadcast to all clients.
	/// </summary>
	public void ApplyBatch( string morphsJson )
	{
		var smr = GameObject.GetComponentInChildren<SkinnedModelRenderer>();
		if ( !smr.IsValid() ) return;

		var morphs = Json.Deserialize<Dictionary<string, float>>( morphsJson );
		if ( morphs is null ) return;

		foreach ( var (name, val) in morphs )
			smr.SceneModel.Morphs.Set( name, val );

		Capture( smr );
		BroadcastBatch( morphsJson );
	}

	/// <summary>
	/// Apply a full morph preset on the host (resets all first), then broadcast to all clients.
	/// </summary>
	public void ApplyPreset( string morphsJson )
	{
		var smr = GameObject.GetComponentInChildren<SkinnedModelRenderer>();
		if ( !smr.IsValid() ) return;

		var morphs = Json.Deserialize<Dictionary<string, float>>( morphsJson );
		if ( morphs is null ) return;

		foreach ( var name in smr.Morphs.Names )
			smr.SceneModel.Morphs.Reset( name );

		foreach ( var (name, val) in morphs )
			smr.SceneModel.Morphs.Set( name, val );

		Capture( smr );
		BroadcastPreset( morphsJson );
	}

	[Rpc.Broadcast]
	private void BroadcastBatch( string morphsJson )
	{
		if ( Networking.IsHost ) return;

		var smr = GameObject.GetComponentInChildren<SkinnedModelRenderer>();
		if ( !smr.IsValid() ) return;

		var morphs = Json.Deserialize<Dictionary<string, float>>( morphsJson );
		if ( morphs is null ) return;

		foreach ( var (name, val) in morphs )
			smr.SceneModel.Morphs.Set( name, val );
	}

	[Rpc.Broadcast]
	private void BroadcastPreset( string morphsJson )
	{
		if ( Networking.IsHost ) return;

		var smr = GameObject.GetComponentInChildren<SkinnedModelRenderer>();
		if ( !smr.IsValid() ) return;

		var morphs = Json.Deserialize<Dictionary<string, float>>( morphsJson );
		if ( morphs is null ) return;

		foreach ( var name in smr.Morphs.Names )
			smr.SceneModel.Morphs.Reset( name );

		foreach ( var (name, val) in morphs )
			smr.SceneModel.Morphs.Set( name, val );
	}

	/// <summary>
	/// Snapshot all current morph values from <paramref name="smr"/> into <see cref="SerializedMorphs"/>.
	/// </summary>
	public void Capture( SkinnedModelRenderer smr )
	{
		SerializedMorphs = Json.Serialize( smr.Morphs.Names.ToDictionary( n => n, n => smr.SceneModel?.Morphs.Get( n ) ?? 0f ) );
	}

	/// <summary>
	/// Apply the stored <see cref="SerializedMorphs"/> to the first <see cref="SkinnedModelRenderer"/> we find.
	/// Called on spawn/dupe restore.
	/// </summary>
	public void Apply()
	{
		if ( string.IsNullOrEmpty( SerializedMorphs ) ) return;

		var smr = GameObject.GetComponentInChildren<SkinnedModelRenderer>();
		if ( !smr.IsValid() ) return;

		var morphs = Json.Deserialize<Dictionary<string, float>>( SerializedMorphs );
		if ( morphs is null ) return;

		foreach ( var name in smr.Morphs.Names )
			smr.SceneModel.Morphs.Reset( name );

		foreach ( var (name, val) in morphs )
			smr.SceneModel.Morphs.Set( name, val );
	}
}
