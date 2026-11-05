namespace Editor.NodeEditor;

public class PlugIn : Plug
{
	public override Vector2 ConnectionPosition => ToScene( new Vector2( handleSize * 0.5f, Size.y * 0.5f ) );

	public Connection Connection { get; private set; }

	public override bool IsConnected => Connection.IsValid();

	public new IPlugIn Inner => (IPlugIn)base.Inner;

	public PlugIn( NodeUI node, IPlug plug ) :
		base( node, plug )
	{
		Cursor = CursorShape.Finger;
	}

	/// <summary>
	/// Should only be called from <see cref="NodeEditor.Connection"/>.
	/// </summary>
	internal void SetConnectionInternal( Connection value )
	{
		Connection = value;

		Node.MarkNodeChanged();

		if ( Editor.IsValid() )
		{
			Editor.Enabled = !value.IsValid();
			Editor.Update();
		}
	}

	public override void Layout()
	{
		if ( !Editor.IsValid() )
			return;

		Editor.Position = new Vector2( 15, 0 );
		Editor.Size = new Vector2( Size.x - 15, Size.y );
	}

	protected override void OnPaint()
	{
		var isTarget = DropTarget == this && Node.Graph.Preview is not null;
		var highlight = Paint.HasMouseOver && !DropTarget.IsValid() || isTarget;
		var unreachable = !highlight && !Node.Selected && !Inner.IsReachable;

		var rect = new Rect();
		rect.Size = Size;

		var spacex = 4f;
		var config = HandleConfig;

		if ( isTarget )
		{
			config = Node.Graph.Preview.Output.HandleConfig;
		}
		else if ( Connection.IsValid() )
		{
			config = Connection.Output.HandleConfig;
		}

		var color = isTarget && Node.Graph.Preview.IsInvalidPreview
			? Theme.Red
			: config.Color;

		if ( !highlight )
		{
			color = color.Desaturate( 0.2f ).Darken( 0.3f );
		}

		if ( unreachable )
		{
			color = color.Desaturate( 0.5f ).Darken( 0.25f );
		}

		var handleRect = new Rect( 0, (Size.y - handleSize) * 0.5f, handleSize, handleSize ).Shrink( 2 );
		DrawHandle( color, handleRect, config.Shape );
		DrawLabel( new Rect( handleSize + spacex, 0, rect.Width - 4 - 10, rect.Size.y ), unreachable, TextFlag.LeftCenter );
	}
}
