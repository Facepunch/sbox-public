using System.Text.Json.Nodes;

/// <summary>
/// Put on an object by the host while a player drives it (see <see cref="Player.Driving"/>)
/// </summary>
[Hide]
public sealed class DrivenProp : Component, IPhysgunEvent, ICarryEvent
{
	/// <summary>
	/// Who's driving this?
	/// </summary>
	[Sync( SyncFlags.FromHost )] public Player Driver { get; private set; }

	[Sync( SyncFlags.FromHost )] public bool HasPhysics { get; private set; }

	const float JumpSpeed = 350f;
	const float GroundControl = 10f;
	const float AirControl = 2f;

	bool _wasNoclip;

	/// <summary>
	/// Start driving this prop 
	/// </summary>
	internal static DrivenProp Begin( GameObject go, Player driver )
	{
		Assert.True( Networking.IsHost, "Only the host can start driving" );

		var driven = go.AddComponent<DrivenProp>();
		driven.Flags |= ComponentFlags.NotSaved;
		driven.Driver = driver;
		driven._wasNoclip = go.Tags.Has( "noclip" );
		driven.SetPhysics( driver.DrivesWithPhysics );

		return driven;
	}

	/// <summary>
	/// Let go of the prop
	/// </summary>
	internal void End()
	{
		if ( !IsValid ) return;

		var go = GameObject;
		go.Tags.Set( "noclip", _wasNoclip );
		Destroy();

		if ( go.Network.Active )
		{
			go.Network.Refresh();
		}
	}

	/// <summary>
	/// Take the noclip tag we put on driven objects out of <paramref name="json"/>, <paramref name="root"/> serialized,
	/// so saves and dupes get them the way they were before they were driven.
	/// </summary>
	internal static void RemoveDriveTags( GameObject root, JsonObject json )
	{
		foreach ( var driven in root.GetComponentsInChildren<DrivenProp>() )
		{
			if ( driven._wasNoclip ) continue;
			if ( FindSerialized( json, driven.GameObject.Id ) is not { } node ) continue;
			if ( node["Tags"] is not JsonValue value || !value.TryGetValue<string>( out var tags ) ) continue;

			node["Tags"] = string.Join( ",", tags.Split( ',' ).Where( x => x != "noclip" ) );
		}
	}

	static JsonObject FindSerialized( JsonObject json, Guid id )
	{
		if ( json["__guid"] is JsonValue value && (value.TryGetValue<Guid>( out var guid ) || value.TryGetValue<string>( out var text ) && Guid.TryParse( text, out guid )) && guid == id )
			return json;

		if ( json["Children"] is not JsonArray children )
			return null;

		return children.OfType<JsonObject>().Select( x => FindSerialized( x, id ) ).FirstOrDefault( x => x is not null );
	}

	void SetPhysics( bool physics )
	{
		HasPhysics = physics;
		GameObject.Tags.Set( "noclip", !physics || _wasNoclip );

		if ( physics )
		{
			if ( GetComponent<Rigidbody>() is { } body )
				body.MotionEnabled = true;
		}
		else
		{
			Freeze();
		}

		if ( GameObject.Network.Active )
		{
			GameObject.Network.Refresh();
		}
	}

	bool DriverInControl => HasPhysics && !Player.IsHeld( GameObject );

	internal void Jump()
	{
		if ( !DriverInControl ) return;

		var body = GetComponent<Rigidbody>();
		if ( !body.IsValid() || !body.MotionEnabled || !IsGrounded( body ) ) return;

		body.Velocity = body.Velocity.WithZ( MathF.Max( body.Velocity.z, 0f ) + JumpSpeed );
	}

	internal void ToggleFreeze()
	{
		if ( !DriverInControl ) return;
		if ( GetComponent<Rigidbody>() is not { } body ) return;

		SetFrozen( body.MotionEnabled );
	}

	internal void Unfreeze()
	{
		if ( !DriverInControl ) return;
		if ( GetComponent<Rigidbody>() is not { MotionEnabled: false } ) return;

		SetFrozen( false );
	}

	void SetFrozen( bool frozen )
	{
		if ( frozen )
			Freeze();
		else if ( GetComponent<Rigidbody>() is { } body )
			body.MotionEnabled = true;

		PlayFreezeEffect( frozen );
	}

	[Rpc.Broadcast( NetFlags.HostOnly )]
	void PlayFreezeEffect( bool frozen )
	{
		FreezeEffect.Play( GameObject, frozen, Driver.IsValid() && !Driver.IsProxy ? [GameObject] : null );
	}

	bool IsGrounded( Rigidbody body )
	{
		var tr = Scene.Trace.Body( body, body.WorldPosition + Vector3.Down * 4f ).IgnoreGameObjectHierarchy( GameObject ).Run();
		return tr.Hit || tr.StartedSolid;
	}

	protected override void OnUpdate()
	{
		if ( !Networking.IsHost ) return;

		// prop driver can die/switch to other prop/etc..
		if ( !Driver.IsValid() || Driver.Driving != GameObject )
		{
			End();
			return;
		}

		if ( !Player.CanKeepDriving( GameObject, Driver.Network.Owner ) )
		{
			Driver.ReleaseDriven();
			return;
		}

		// Not from under someone holding it, it would freeze in their hands
		if ( Driver.DrivesWithPhysics != HasPhysics && !Player.IsHeld( GameObject ) )
			SetPhysics( Driver.DrivesWithPhysics );

		if ( HasPhysics ) return;

		Freeze();

		if ( !Driver.TryGetDriveTransform( GameObject, out var position, out var rotation ) ) return;

		if ( Driver.IsProxy )
		{
			var fraction = 1f - MathF.Exp( -20f * Time.Delta );
			WorldPosition = WorldPosition.LerpTo( position, fraction );
			WorldRotation = Rotation.Slerp( WorldRotation, rotation, fraction );
		}
		else
		{
			WorldRotation = rotation;
			WorldPosition = position;
		}
	}

	protected override void OnFixedUpdate()
	{
		if ( !Networking.IsHost || !DriverInControl ) return;
		if ( !Driver.IsValid() || !Driver.TryGetDriveWish( GameObject, out var wish, out var rotation ) ) return;

		var body = GetComponent<Rigidbody>();
		if ( !body.IsValid() || !body.MotionEnabled ) return;

		var velocity = body.Velocity;
		var control = IsGrounded( body ) ? GroundControl : AirControl;
		var horizontal = velocity.WithZ( 0 ).LerpTo( wish, 1f - MathF.Exp( -control * Time.Delta ) );
		body.Velocity = horizontal.WithZ( velocity.z );

		body.SmoothRotate( rotation, 0.15f, Time.Delta );
	}

	void Freeze()
	{
		var body = GetComponent<Rigidbody>();
		if ( !body.IsValid() || !body.MotionEnabled ) return;

		body.Velocity = Vector3.Zero;
		body.AngularVelocity = Vector3.Zero;
		body.MotionEnabled = false;
	}

	void IPhysgunEvent.OnPhysgunGrab( IPhysgunEvent.GrabEvent e )
	{
		if ( !HasPhysics )
			e.Cancelled = true;
	}

	void ICarryEvent.OnCarryGrab( PlayerCarryEvent e )
	{
		if ( !HasPhysics )
			e.Cancelled = true;
	}
}
