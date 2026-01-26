
namespace Editor.MeshEditor;

class MoveModeToolBar : Widget
{
	MeshTool _tool;

	public MoveModeToolBar( Widget parent, MeshTool tool ) : base( parent )
	{
		_tool = tool;
		Layout = Layout.Row();

		foreach ( var t in EditorTypeLibrary.GetTypes<MoveMode>().OrderBy( x => x.Order ) )
		{
			if ( t.IsAbstract ) continue;

			Layout.Add( new MoveModeButton( t, tool, this ), 1 );
		}
	}

	void SetMode( string id )
	{
		_tool.MoveMode = EditorTypeLibrary.Create<MoveMode>( id );
		Update();
	}

	[Shortcut( "tools.position-tool", "w", typeof( SceneViewWidget ) )]
	public void ActivatePositionMode() => SetMode( "mesh.position.mode" );

	[Shortcut( "tools.rotate-tool", "e", typeof( SceneViewWidget ) )]
	public void ActivateRotateMode() => SetMode( "mesh.rotate.mode" );

	[Shortcut( "tools.scale-tool", "r", typeof( SceneViewWidget ) )]
	public void ActivateScaleMode() => SetMode( "mesh.scale.mode" );

	[Shortcut( "tools.pivot-tool", "t", typeof( SceneViewWidget ) )]
	public void ActivatePivotMode() => SetMode( "mesh.pivot.mode" );

	[Shortcut( "tools.resize-tool", "y", typeof( SceneViewWidget ) )]
	public void ActivateResizeMode() => SetMode( "mesh.resize.mode" );
}

file class MoveModeButton : Widget
{
	readonly TypeDescription _type;
	readonly MeshTool _tool;

	public MoveModeButton( TypeDescription type, MeshTool tool, Widget parent ) : base( parent )
	{
		_type = type;
		_tool = tool;

		FixedHeight = 32;
		Cursor = CursorShape.Finger;

		var title = _type.Title;
		if ( _type.GetAttribute<AliasAttribute>() is AliasAttribute alias && !string.IsNullOrEmpty( alias.Value.FirstOrDefault() ) )
		{
			var keys = EditorShortcuts.GetKeys( alias.Value.FirstOrDefault() );
			if ( !string.IsNullOrEmpty( keys ) )
			{
				title += $" ({keys.Trim().ToUpperInvariant()})";
			}
		}
		ToolTip = $"<b>{title}</b><br>{_type.Description}";
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			Activate();
		}
	}

	public void Activate()
	{
		if ( _type.TargetType == _tool.MoveMode?.GetType() ) return;

		_tool.MoveMode = _type.Create<MoveMode>();
	}

	protected override void OnPaint()
	{
		Paint.Antialiasing = true;
		Paint.TextAntialiasing = true;

		if ( _type.TargetType == _tool.MoveMode?.GetType() )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Blue );

			Paint.DrawRect( LocalRect.Shrink( 1 ), 4 );

			Paint.SetPen( Theme.TextButton );
		}
		else
		{
			Paint.ClearPen();
			Paint.SetPen( Theme.TextLight );
		}

		Paint.DrawIcon( LocalRect, _type.Icon, HeaderBarStyle.IconSize, TextFlag.Center );
	}
}
