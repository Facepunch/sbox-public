using Sandbox.Audio;

[Icon( "water" )]
public partial class WaterVolume : Component, Component.ITriggerListener
{
	[Property, Group( "Sound" )] private SoundEvent SoundEnter { get; set; }
		= ResourceLibrary.Get<SoundEvent>( "sounds/water_enter.sound" );

	[Property, Group( "Sound" )] private SoundEvent SoundExit { get; set; }
		= ResourceLibrary.Get<SoundEvent>( "sounds/water_exit.sound" );

	[RequireComponent] private BoxCollider Collider { get; set; }

	/// <summary>
	/// Roots of objects currently in the water.
	/// Handy for playing sounds only once per object, even if it has multiple colliders (like ragdolls)
	/// </summary>
	private HashSet<GameObject> Objects = new();
	private List<Rigidbody> Bodies = new();

	private BBox Bounds => BBox.FromPositionAndSize( WorldTransform.PointToWorld( Collider.Center ), WorldScale * Collider.Scale );

	protected override void OnFixedUpdate()
	{
		if ( Bodies is null || !Collider.IsValid() )
			return;

		var bbox = Bounds;
		var waterSurface = bbox.Center + Vector3.Up * bbox.Extents.z;
		var waterPlane = new Plane( waterSurface, Vector3.Up );

		for ( int i = Bodies.Count - 1; i >= 0; i-- )
		{
			var body = Bodies[i];
			if ( !body.IsValid() )
			{
				Bodies.RemoveAt( i );
				continue;
			}

			body.ApplyBuoyancy( waterPlane, Time.Delta );
		}
	}

	bool _wasCamUnderwater;
	protected override void OnUpdate()
	{
		if ( !Collider.IsValid() )
			return;

		var camera = Scene.Camera;
		bool isCamUnderwater = camera.IsValid() && Bounds.Contains( camera.WorldPosition );
		if ( isCamUnderwater != _wasCamUnderwater )
		{
			if ( isCamUnderwater ) OnCameraEnter();
			else OnCameraExit();
		}

		_wasCamUnderwater = isCamUnderwater;
	}

	DspProcessor _dsp;
	void OnCameraEnter()
	{
		var gameMixer = Mixer.FindMixerByName( "Game" );
		if ( gameMixer is null ) return;
		
		_dsp ??= new DspProcessor( "water.small" );
		gameMixer.AddProcessor( _dsp );
	}

	void OnCameraExit()
	{
		var gameMixer = Mixer.FindMixerByName( "Game" );
		if ( gameMixer is null ) return;

		gameMixer.RemoveProcessor( _dsp );
		_dsp = null;
	}

	void ITriggerListener.OnTriggerEnter( Collider other )
	{
		var body = other.Rigidbody;
		if ( !body.IsValid() || Bodies.Contains( body ) )
			return;

		Bodies.Add( body );

		var root = other.GameObject.Root;
		if ( Objects.Add( root ) )
		{
			if ( SoundEnter.IsValid() )
			{
				other.GameObject.PlaySound( SoundEnter );
			}
		}
	}

	void ITriggerListener.OnTriggerExit( Collider other )
	{
		var body = other.Rigidbody;
		if ( !body.IsValid() ) return;

		Bodies.Remove( body );

		var root = other.GameObject.Root;
		if ( Objects.Remove( root ) )
		{
			if ( SoundExit.IsValid() )
			{
				other.GameObject.PlaySound( SoundExit );
			}
		}
	}
}

public sealed partial class GameManager : ISceneLoadingEvents
{
	void ISceneLoadingEvents.AfterLoad( Scene scene )
	{
		var waterVolumes = scene.GetAll<Collider>().Where( x => x.Tags.Has( "water" ) );
		if ( waterVolumes.Count() < 1 ) return;

		foreach ( var volume in waterVolumes )
		{
			volume.Surface ??= Surface.FindByName( "water" );
			volume.GetOrAddComponent<WaterVolume>();
		}
	}
}
