[Icon( "🎨" )]
[Title( "#tool.name.colour" )]
[ClassName( "colour" )]
[Group( "#tool.group.render" )]
public sealed class ColourTool : ToolMode
{
	[Property, Sync, Title( "#tool.setting.colour" )]
	public Color Colour { get; set; } = Color.White;

	public override string Description => "#tool.hint.colour.description";

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.colour.apply", () => OnColour( Colour, ToolInput.Primary ) );
		RegisterAction( ToolInput.Secondary, () => "#tool.hint.colour.reset", () => OnColour( Color.White, ToolInput.Secondary ) );
	}

	void OnColour( Color colour, ToolInput input )
	{
		var select = TraceSelect();
		IsValidState = select.IsValid() && !select.IsWorld && !select.IsPlayer;
		if ( !IsValidState ) return;

		SetColour( select.GameObject, colour, input );
		ShootEffects( select );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void SetColour( GameObject target, Color colour, ToolInput input )
	{
		if ( !FireToolAction( input ) ) return;

		if ( !target.IsValid() ) return;

		var root = target.Network?.RootGameObject ?? target;
		if ( root.IsProxy || root.Tags.Has( "world" ) || root.Tags.Has( "player" ) ) return;

		foreach ( var renderer in root.GetComponentsInChildren<ModelRenderer>() )
		{
			var prop = renderer.GetComponent<Prop>();
			if ( prop.IsValid() )
				prop.Tint = colour;
			else
				renderer.Tint = colour;

			renderer.GameObject.Network?.Refresh();
		}

		FirePostToolAction( input );
	}
}
