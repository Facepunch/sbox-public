/// <summary>
/// Toolgun mode for selecting objects in the world.
/// </summary>
[Icon( "arrow_selector_tool" )]
[Title( "#tool.name.select" )]
[ClassName( "select" )]
public sealed class SelectTool : ToolMode
{
	/// <summary>
	/// Explains what the Select tool edits.
	/// </summary>
	public override string Description => "#tool.hint.select.description";

	protected override IEnumerable<ToolHint> ExtraHints => [new( "#tool.hint.select.add", ["run", "attack1"] )];

	protected override void OnStart()
	{
		base.OnStart();
		RegisterAction( ToolInput.Primary, () => "#tool.hint.select.inspect", Inspect );
	}

	void Inspect()
	{
		if ( SpawnMenuHost.IsOpen && !InspectCursor.IsOpen ) return;

		var target = Inspector.GetSelectableRoot( TraceSelect().GameObject );
		Scene.Get<SpawnMenuHost>()?.SelectObject( target );
	}

	/// <summary>
	/// Right-clicking with Select in the context menu opens the target's object actions.
	/// </summary>
	public override void OnControl()
	{
		base.OnControl();

		if ( Input.Pressed( "attack2" ) && InspectCursor.IsOpen && SpawnMenuHost.GetActiveMode() is ContextMenuHost menu )
		{
			menu.Inspector?.OpenContextMenu( Inspector.GetSelectableRoot( TraceSelect().GameObject ) );
		}
	}
}
