using Sandbox.Audio;
using Sandbox.Volumes;

namespace Sandbox;

/// <summary>
/// Local water presentation from surface crossings and listener immersion.
/// </summary>
internal sealed class WaterEffectsSystem : GameObjectSystem<WaterEffectsSystem>
{
	readonly List<WaterBody> _waters = [];
	readonly List<Rigidbody> _bodies = [];
	Dictionary<Rigidbody, BodySample> _previous = [];
	Dictionary<Rigidbody, BodySample> _current = [];
	WaterBody _listenerWater;
	bool _listenerSampled;
	DspVolume _dsp;

	readonly record struct BodySample( BBox Bounds, Vector3 Position, WaterBody Water, float Travel, float NextSplash );

	public WaterEffectsSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishFixedUpdate, 0, UpdateSplashes, "Water splashes" );
		Listen( Stage.FinishUpdate, -1, UpdateListener, "Water sounds" );
	}

	void CollectWater()
	{
		_waters.Clear();
		Scene.GetAll( _waters );
		_waters.RemoveAll( water => !water.Active || !water.Effects );
	}

	void UpdateSplashes()
	{
		if ( Scene.IsEditor || Application.IsDedicatedServer || Time.Delta <= 0 )
			return;

		CollectWater();
		_current.Clear();
		if ( _waters.Count > 0 )
		{
			_bodies.Clear();
			Scene.GetAll( _bodies );
			foreach ( var body in _bodies )
			{
				if ( !body.Active || !body.PhysicsBody.IsValid() )
					continue;

				var player = body.GetComponent<PlayerController>();
				var sample = new BodySample( body.PhysicsBody.GetBounds(), body.WorldPosition, null, 0, 0 );
				if ( _previous.TryGetValue( body, out var previous ) )
					sample = UpdateBody( player, previous, sample );
				_current[body] = sample;
			}
			_bodies.Clear();
		}
		(_previous, _current) = (_current, _previous);
		_current.Clear();
		_waters.Clear();
	}

	BodySample UpdateBody( PlayerController player, BodySample previous, BodySample current )
	{
		WaterBody entry = null;
		Vector3 entryPosition = default;
		float entrySpeed = 0;
		float first = float.PositiveInfinity;
		foreach ( var water in _waters )
		{
			var normal = water.WorldRotation.Up;
			var start = previous.Bounds.Center - normal * Vector3.Dot( previous.Bounds.Extents, normal.Abs() );
			var end = current.Bounds.Center - normal * Vector3.Dot( current.Bounds.Extents, normal.Abs() );
			float speed = Vector3.Dot( start - end, normal ) / Time.Delta;
			if ( speed < 40 || !water.TraceSurface( start, end, out var point, out float fraction ) )
				continue;

			if ( fraction > first || (fraction == first && entry.IsValid() && (!entry.Ocean || water.Ocean)) )
				continue;

			entry = water;
			entryPosition = point;
			entrySpeed = speed;
			first = fraction;
		}
		if ( entry.IsValid() && Time.Now >= previous.NextSplash )
		{
			entry.CreateSplash( entryPosition, entrySpeed >= 300 ? 80 : entrySpeed >= 150 ? 64 : 32 );
			current = current with { NextSplash = Time.Now + 0.2f };
		}
		else
		{
			current = current with { NextSplash = previous.NextSplash };
		}

		if ( !player.IsValid() || !player.Active )
			return current;

		var head = player.WorldTransform.PointToWorld( new Vector3( 0, 0, player.CurrentHeight ) );
		WaterBody surface = null;
		foreach ( var water in _waters )
		{
			if ( !water.InFootprint( current.Position ) || water.WaveDistance( current.Position ) > 0 || water.WaveDistance( head ) < 0 )
				continue;

			if ( !surface.IsValid() || (surface.Ocean && !water.Ocean) )
				surface = water;
		}
		if ( !surface.IsValid() )
			return current;

		var movement = current.Position - previous.Position;
		var up = surface.WorldRotation.Up;
		float distance = (movement - up * Vector3.Dot( movement, up )).Length;
		float travel = distance / Time.Delta > 30 ? distance + (previous.Water == surface ? previous.Travel : 0) : 0;
		if ( travel >= 20 && Time.Now >= current.NextSplash )
		{
			surface.CreateSplash( current.Position );
			travel = 0;
			current = current with { NextSplash = Time.Now + 0.2f };
		}
		return current with { Water = surface, Travel = travel };
	}

	void UpdateListener()
	{
		if ( Scene.IsEditor || Application.IsDedicatedServer || Application.IsHeadless )
			return;

		if ( !Scene.Listener.IsValid() )
		{
			_listenerWater = null;
			_listenerSampled = false;
			UpdateDsp( null );
			return;
		}

		_waters.Clear();
		Scene.GetAll( _waters );
		var position = Scene.Listener.Transform.Position;
		WaterBody underwater = null;
		foreach ( var water in _waters )
		{
			if ( !water.ContainsView( position, includeSurface: false ) )
				continue;

			if ( !underwater.IsValid() || (underwater.Ocean && !water.Ocean) )
				underwater = water;
		}
		_waters.Clear();
		if ( _listenerSampled )
		{
			string sound = null;
			if ( underwater.IsValid() && !_listenerWater.IsValid() )
				sound = "in";
			else if ( !underwater.IsValid() && _listenerWater.IsValid() && _listenerWater.Active )
				sound = "out";

			if ( sound is not null && ListenerInBody( position ) )
			{
				var handle = Sound.Play( $"sounds/water/transition_{sound}.sound", position );
				if ( handle.IsValid() )
					handle.SpacialBlend = 0;
			}
		}
		_listenerWater = underwater;
		_listenerSampled = true;
		UpdateDsp( underwater );
	}

	/// <summary>
	/// Whether the listener is inside a simulated body's collision, such as a first-person player's head.
	/// A detached camera, like a third-person boom, crosses the surface without anything going under,
	/// so it keeps the underwater filter but plays no transition sound. Only runs on a crossing.
	/// </summary>
	internal bool ListenerInBody( Vector3 position )
	{
		return Scene.Trace.Sphere( 1, position, position ).IgnoreStatic().IgnoreKeyframed().Run().Hit;
	}

	/// <summary>
	/// Use the native volume system to filter the existing Game mixer while the scene listener is submerged.
	/// Never create or reconfigure mixers when a project has no Game mixer.
	/// </summary>
	void UpdateDsp( WaterBody water )
	{
		var mixer = Mixer.FindMixerByName( "Game" );
		bool enabled = water.IsValid() && mixer is not null;
		if ( enabled && !_dsp.IsValid() )
		{
			var go = new GameObject( Scene, false, "Water audio DSP" );
			go.Flags = GameObjectFlags.NotSaved | GameObjectFlags.Hidden | GameObjectFlags.NotNetworked;
			_dsp = go.Components.Create<DspVolume>();
			_dsp.SceneVolume = new SceneVolume { Type = SceneVolume.VolumeTypes.Infinite };
			_dsp.Dsp = "water.small";
			_dsp.Priority = 10;
		}
		if ( !_dsp.IsValid() )
			return;

		_dsp.TargetListener = Scene.Listener;
		if ( enabled )
			_dsp.TargetMixer = mixer;
		_dsp.GameObject.Enabled = enabled;
	}

	public override void Dispose()
	{
		_previous.Clear();
		_current.Clear();
		_waters.Clear();
		_bodies.Clear();
		_listenerWater = null;
		if ( _dsp.IsValid() )
		{
			_dsp.GameObject.Enabled = false;
			_dsp.GameObject.Destroy();
		}
		_dsp = null;
		base.Dispose();
	}
}
