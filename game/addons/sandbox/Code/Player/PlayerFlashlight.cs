public sealed class PlayerFlashlight : Component
{
	[Property, RequireComponent] public SpotLight Light { get; set; }

	/// <summary>
	/// Optional point light placed in front of the player, softly lighting what's right around them.
	/// </summary>
	[Property, Group( "Fill Light" )] public PointLight FillLight { get; set; }

	/// <summary>
	/// How far in front of the eyes the fill light sits. Pulled back if something is in the way.
	/// </summary>
	[Property, Group( "Fill Light" )] public float FillDistance { get; set; } = 24f;

	[Property, Group( "Sound" )] public SoundEvent ToggleOnSound { get; set; }
	[Property, Group( "Sound" )] public SoundEvent ToggleOffSound { get; set; }
	[Sync, Change( nameof( UpdateLight ) )] public bool IsOn { get; set; } = false;

	private Player _player;
	private Transform _localOffset;

	protected override void OnStart()
	{
		_player = GetComponentInParent<Player>();
		_localOffset = LocalTransform;
		UpdateLight();
	}

	protected override void OnUpdate()
	{
		if ( !_player.IsValid() ) return;

		if ( !IsProxy && (IsOn || Input.Pressed( "Flashlight" )) )
		{
			// once something else has the key - getting into a car we're driving - the flashlight goes off, or there's
			// no way to turn it off again
			if ( IsKeyTaken() )
			{
				if ( IsOn ) Toggle();
			}
			else if ( Input.Pressed( "Flashlight" ) )
			{
				Toggle();
			}
		}

		var eye = _player.EyeTransform;
		WorldTransform = eye.ToWorld( _localOffset );

		if ( IsOn && FillLight.IsValid() )
		{
			FillLight.WorldPosition = GetFillPosition( eye );
		}
	}

	/// <summary>
	/// Ask whether anything else wants the flashlight key right now, like the headlights of a car we're driving.
	/// </summary>
	private bool IsKeyTaken()
	{
		var e = new PlayerFlashlightEvent { Player = _player };
		Local.IPlayerEvents.PostToGameObject( _player.GameObject, x => x.OnFlashlight( e ) );
		Global.IPlayerEvents.Post( x => x.OnPlayerFlashlight( e ) );
		return e.Cancelled;
	}

	private Vector3 GetFillPosition( Transform eye )
	{
		var target = eye.Position + eye.Forward * FillDistance;
		var tr = Scene.Trace.FromTo( eye.Position, target ).Radius( 4f ).WithoutTags( "ragdoll", "effect" ).IgnoreGameObjectHierarchy( _player.GameObject.Root ).Run();

		return tr.Hit ? tr.EndPosition : target;
	}

	private void Toggle()
	{
		BroadcastToggle( !IsOn );

		var sound = IsOn ? ToggleOnSound : ToggleOffSound;
		if ( sound.IsValid() )
		{
			Sound.Play( sound, WorldPosition );
		}
	}

	[Rpc.Broadcast]
	private void BroadcastToggle( bool value )
	{
		IsOn = value;
	}

	private void UpdateLight()
	{
		if ( Light.IsValid() )
			Light.Enabled = IsOn;

		if ( FillLight.IsValid() )
			FillLight.Enabled = IsOn;
	}
}
