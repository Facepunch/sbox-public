
[Icon( "🍄" )]
[Title( "#tool.name.resizer" )]
[ClassName( "resizer" )]
[Group( "#tool.group.tools" )]
public sealed class ResizerTool : ToolMode
{
	public override IEnumerable<string> TraceIgnoreTags => [];

	public override string Description => "#tool.hint.resizer.description";

	TimeSince timeSinceAction = 0;

	/// <summary>
	/// 1 while growing, -1 while shrinking, 0 when idle. Synced so everyone hears the loop.
	/// </summary>
	[Sync] int ResizeDirection { get; set; }
	int _pendingDirection;

	SoundHandle _resizeSound;
	int _resizeSoundDirection;

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.resizer.grow", OnGrow, InputMode.Down );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.resizer.shrink", OnShrink, InputMode.Down );
		RegisterAction( ToolInput.Reload, () => "#tool.hint.resizer.reset", OnReset );
	}

	public override void OnControl()
	{
		_pendingDirection = 0;
		base.OnControl();
		ResizeDirection = _pendingDirection;
	}

	protected override void OnUpdate()
	{
		base.OnUpdate();

		var toolgun = Toolgun;
		var player = toolgun?.Owner;
		var active = toolgun.IsValid() && player.IsValid()
			&& player.GetComponent<PlayerInventory>()?.ActiveWeapon == toolgun;

		if ( !active )
		{
			if ( !IsProxy ) ResizeDirection = 0;
			StopResizeSound();
			return;
		}

		if ( ResizeDirection == 0 )
		{
			StopResizeSound();
			return;
		}

		// Swap loops if direction changed
		if ( _resizeSound is not null && _resizeSoundDirection != ResizeDirection )
			StopResizeSound();

		var position = toolgun.GetMuzzleTransform().Position;
		if ( _resizeSound is null )
		{
			_resizeSound = Sound.Play( ResizeDirection > 0
				? "weapons/toolgun/sounds/shoot.resize.grow.sound"
				: "weapons/toolgun/sounds/shoot.resize.shrink.sound", position );
			_resizeSoundDirection = ResizeDirection;
		}

		_resizeSound?.Position = position;
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();
		if ( !IsProxy ) ResizeDirection = 0;
		StopResizeSound();
	}

	protected override void OnDestroy()
	{
		StopResizeSound();
		base.OnDestroy();
	}

	void StopResizeSound()
	{
		_resizeSound?.Stop( 0.1f );
		_resizeSound = null;
		_resizeSoundDirection = 0;
	}

	void OnGrow()
	{
		var select = TraceSelect();
		IsValidState = select.IsValid() && !select.IsWorld;
		if ( !IsValidState ) return;
		_pendingDirection = 1;
		if ( timeSinceAction < 0.03f ) return;

		Resize( select.GameObject, 0.033f );
		timeSinceAction = 0;
	}

	void OnShrink()
	{
		var select = TraceSelect();
		IsValidState = select.IsValid() && !select.IsWorld;
		if ( !IsValidState ) return;
		_pendingDirection = -1;
		if ( timeSinceAction < 0.03f ) return;

		Resize( select.GameObject, -0.033f );
		timeSinceAction = 0;
	}

	void OnReset()
	{
		var select = TraceSelect();
		IsValidState = select.IsValid() && !select.IsWorld;
		if ( !IsValidState ) return;

		ResetScale( select.GameObject );
		ShootEffects( select );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	private void ResetScale( GameObject go )
	{
		if ( !go.IsValid() ) return;
		if ( go.IsProxy ) return;
		if ( !FireToolAction( ToolInput.Reload ) ) return;

		go.WorldScale = Vector3.One;

		FirePostToolAction( ToolInput.Reload );
	}

	[Rpc.Broadcast( NetFlags.OwnerOnly )]
	private void Resize( GameObject go, float size )
	{
		if ( !go.IsValid() ) return;
		if ( go.IsProxy ) return;
		if ( !FireToolAction( size > 0 ? ToolInput.Primary : ToolInput.Secondary ) ) return;

		var newScale = go.WorldScale + size;
		
		if ( newScale.Length > 1000f ) return;

		var minimumScale = 0.01f;
		foreach ( var constraint in go.GetComponentsInChildren<IResizeConstraint>( true ) )
			minimumScale = MathF.Max( minimumScale, constraint.MinimumResizeScale );

		if ( minimumScale <= 0.01f && newScale.Length < 0.1f ) return;
		var scale = Vector3.Max( newScale, minimumScale );
		go.WorldScale = scale;

		FirePostToolAction( size > 0 ? ToolInput.Primary : ToolInput.Secondary );
	}
}
