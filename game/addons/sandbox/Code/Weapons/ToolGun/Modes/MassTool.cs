
[Icon( "🍔" )]
[Title( "#tool.name.mass" )]
[ClassName( "mass" )]
[Group( "#tool.group.tools" )]
public sealed class MassTool : ToolMode
{
	[Sync, Property, Title( "#tool.setting.mass_kg" ), Range( 1, 250 ), Step( 0.5f )]
	public float Value { get; set; } = 100.0f;

	public override string Description => "#tool.hint.mass.description";

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.mass.set", OnSetMass );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.mass.copy", OnCopyMass );
		RegisterAction( ToolInput.Reload, () => "#tool.hint.mass.reset", OnResetMass );
	}

	void OnSetMass()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var rb = select.GameObject.GetComponent<Rigidbody>();
		if ( !rb.IsValid() ) return;

		SetMass( rb, Value, ToolInput.Primary );
		ShootEffects( select );
	}

	void OnCopyMass()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var rb = select.GameObject.GetComponent<Rigidbody>();
		if ( !rb.IsValid() ) return;

		CopyMass( rb );
		ShootEffects( select );
	}

	void OnResetMass()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var rb = select.GameObject.GetComponent<Rigidbody>();
		if ( !rb.IsValid() ) return;

		SetMass( rb, 0.0f, ToolInput.Reload );
		ShootEffects( select );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void SetMass( Rigidbody rb, float mass, ToolInput input )
	{
		if ( !FireToolAction( input ) ) return;

		if ( !rb.IsValid() || rb.IsProxy ) return;

		if ( mass <= 0f )
		{
			var existing = rb.GetComponent<PhysicalProperties>();
			if ( existing.IsValid() )
			{
				// Keep the component if it still carries a gravity override or health
				existing.Mass = 0f;
				if ( existing.GravityScale == 1f && existing.Health <= 0f )
					existing.Destroy();
			}

			rb.MassOverride = 0f;
			return;
		}

		var mo = rb.GetOrAddComponent<PhysicalProperties>();
		mo.Mass = mass;
		mo.Apply();

		FirePostToolAction( input );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void CopyMass( Rigidbody rb )
	{
		if ( !FireToolAction( ToolInput.Secondary ) ) return;

		if ( !rb.IsValid() || rb.IsProxy ) return;

		var mo = rb.GetComponent<PhysicalProperties>();
		Value = mo.IsValid() ? mo.Mass : rb.Mass;

		FirePostToolAction( ToolInput.Secondary );
	}
}
