
[Icon( "🪂" )]
[Title( "#tool.name.gravity" )]
[ClassName( "gravity" )]
[Group( "#tool.group.tools" )]
public sealed class GravityTool : ToolMode
{
	[Sync, Property, Title( "#tool.setting.gravity_scale" ), Range( -1, 4 ), Step( 0.05f )]
	public float Value { get; set; } = 1.0f;

	public override string Description => "#tool.hint.gravity.description";

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.gravity.set", OnSetGravity );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.gravity.copy", OnCopyGravity );
		RegisterAction( ToolInput.Reload, () => "#tool.hint.gravity.reset", OnResetGravity );
	}

	void OnSetGravity()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var target = ResolveTarget( select.GameObject );
		if ( !target.IsValid() ) return;

		SetGravity( target, Value, ToolInput.Primary );
		ShootEffects( select );
	}

	void OnCopyGravity()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var target = ResolveTarget( select.GameObject );
		if ( !target.IsValid() ) return;

		CopyGravity( target );
		ShootEffects( select );
	}

	void OnResetGravity()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var target = ResolveTarget( select.GameObject );
		if ( !target.IsValid() ) return;

		SetGravity( target, 1.0f, ToolInput.Reload );
		ShootEffects( select );
	}

	/// <summary>
	/// Check if we're hitting an object with model physics or regular rigidbody prop
	/// </summary>
	static GameObject ResolveTarget( GameObject hit )
	{
		var modelPhysics = hit.GetComponentInParent<ModelPhysics>( true );
		if ( modelPhysics.IsValid() ) return modelPhysics.GameObject;

		var rb = hit.GetComponent<Rigidbody>();
		return rb.IsValid() ? rb.GameObject : null;
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void SetGravity( GameObject target, float scale, ToolInput input )
	{
		if ( !FireToolAction( input ) ) return;

		if ( !target.IsValid() || target.IsProxy ) return;

		scale = scale.Clamp( -4f, 4f );

		var props = target.GetOrAddComponent<PhysicalProperties>();
		props.GravityScale = scale;
		props.Apply();

		// nothing left to persist, don't leave an empty component behind
		if ( scale == 1f && props.Mass <= 0f && props.Health <= 0f )
		{
			props.Destroy();
		}

		FirePostToolAction( input );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void CopyGravity( GameObject target )
	{
		if ( !FireToolAction( ToolInput.Secondary ) ) return;

		if ( !target.IsValid() || target.IsProxy ) return;

		var rb = target.GetComponentInChildren<Rigidbody>( true );
		if ( rb.IsValid() ) Value = rb.GravityScale;

		FirePostToolAction( ToolInput.Secondary );
	}
}
