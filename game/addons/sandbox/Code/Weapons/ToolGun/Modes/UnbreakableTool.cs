
[Icon( "🛡️" )]
[Title( "#tool.name.unbreakable" )]
[ClassName( "unbreakable" )]
[Group( "#tool.group.tools" )]
public sealed class UnbreakableTool : ToolMode
{
	public override string Description => "#tool.hint.unbreakable.description";

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.unbreakable.set", OnSetUnbreakable );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.unbreakable.unset", OnUnsetUnbreakable );
	}

	void OnSetUnbreakable()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var prop = select.GameObject.GetComponent<Prop>();
		if ( !prop.IsValid() ) return;

		SetUnbreakable( prop, true );
		ShootEffects( select );
	}

	void OnUnsetUnbreakable()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var prop = select.GameObject.GetComponent<Prop>();
		if ( !prop.IsValid() ) return;

		SetUnbreakable( prop, false );
		ShootEffects( select );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	private void SetUnbreakable( Prop prop, bool unbreakable )
	{
		if ( !FireToolAction( unbreakable ? ToolInput.Primary : ToolInput.Secondary ) ) return;

		if ( !prop.IsValid() || prop.IsProxy ) return;

		prop.Health = unbreakable ? 0 : ( prop?.Model?.Data?.Health ?? 100 );

		FirePostToolAction( unbreakable ? ToolInput.Primary : ToolInput.Secondary );
	}
}
