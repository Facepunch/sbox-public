﻿[Icon( "🧨" )]
[Title( "#tool.name.remover" )]
[ClassName( "remover" )]
[Group( "#tool.group.tools" )]
public sealed class RemoverTool : ToolMode
{
	public override bool TraceHitboxes => true;
	public override string Description => "#tool.hint.remover.description";

	protected override void OnStart()
	{
		base.OnStart();

		RegisterAction( ToolInput.Primary, () => "#tool.hint.remover.remove", OnRemove );
	}

	bool CanDestroy( GameObject go )
	{
		if ( !go.IsValid() ) return false;

		// gibs can always go
		if ( go.Tags.Contains( "debris" ) ) return true;

		if ( !go.Tags.Contains( "removable" ) ) return false;

		return true;
	}

	void OnRemove()
	{
		var select = TraceSelect();
		if ( !select.IsValid() ) return;

		var target = select.GameObject?.Network?.RootGameObject;
		if ( !target.IsValid() ) return;
		if ( !CanDestroy( target ) ) return;

		Remove( target );
		ShootEffects( select );
	}

	[Rpc.Host( NetFlags.OwnerOnly )]
	public void Remove( GameObject go )
	{
		if ( !FireToolAction( ToolInput.Primary ) ) return;

		go = go?.Network?.RootGameObject;

		if ( !CanDestroy( go ) ) return;
		if ( go.IsProxy ) return;

		go.Destroy();

		FirePostToolAction( ToolInput.Primary );
	}

}
