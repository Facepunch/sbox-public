namespace Sandbox;

public sealed partial class DebugOverlaySystem : GameObjectSystem<DebugOverlaySystem>
{
	bool inFixedUpdate;

	public DebugOverlaySystem( Scene scene ) : base( scene )
	{
		Listen( Stage.StartUpdate, -10000, StartUpdate, "BuildDebugOverlays" );
		Listen( Stage.StartFixedUpdate, -10000, StartFixedUpdate, "BuildDebugOverlays" );
		Listen( Stage.FinishFixedUpdate, 10000, EndFixedUpdate, "BuildDebugOverlays" );
	}

	void RemoveExpired()
	{
		for ( int i = 0; i < entries.Count; i++ )
		{
			if ( entries[i].SingleFrame ) continue;
			entries[i].life -= Time.Delta;

			if ( entries[i].life < 0 )
			{
				entries[i].Dispose();
				entries.RemoveAt( i ); // move to end and remove?
				i--;
			}
		}
	}

	void RemoveSingleFrame( bool createdDuringFixed )
	{
		for ( int i = 0; i < entries.Count; i++ )
		{
			if ( !entries[i].SingleFrame ) continue;
			if ( entries[i].CreatedDuringFixed != createdDuringFixed ) continue;

			entries[i].Dispose();
			entries.RemoveAt( i );
			i--;
		}

		// don't force the scene world to exist just to flush deletes
		if ( Scene.HasSceneWorld )
		{
			Scene.SceneWorld.DeletePendingObjects();
		}
	}

	void StartUpdate()
	{
		RemoveExpired();
		RemoveSingleFrame( false );
	}

	void StartFixedUpdate()
	{
		RemoveSingleFrame( true );
		inFixedUpdate = true;
	}

	void EndFixedUpdate()
	{
		inFixedUpdate = false;
	}
}
