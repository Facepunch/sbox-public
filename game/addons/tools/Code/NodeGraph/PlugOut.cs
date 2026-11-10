using Sandbox;

namespace Editor.NodeEditor;

public class PlugOut : Plug
{
	public override Vector2 ConnectionPosition => ToScene( new Vector2( Size.x - handleSize * 0.5f, Size.y * 0.5f ) );

	private readonly HashSet<Connection> _connections = new();

	public IEnumerable<Connection> Connections => _connections;

	public new IPlugOut Inner => (IPlugOut)base.Inner;

	public override bool IsConnected => Connections.Any( x => x.IsValid() );

	public PlugOut( NodeUI node, IPlug plug ) :
		base( node, plug )
	{
		Cursor = CursorShape.Finger;
	}

	/// <summary>
	/// Should only be called from <see cref="Connection"/>.
	/// </summary>
	internal void AddConnectionInternal( Connection value )
	{
		_connections.Add( value );
		Node.MarkNodeChanged();
	}

	/// <summary>
	/// Should only be called from <see cref="Connection"/>.
	/// </summary>
	internal void RemoveConnectionInternal( Connection value )
	{
		_connections.Remove( value );
		Node.MarkNodeChanged();
	}

	public override void Layout()
	{
		if ( !Editor.IsValid() )
			return;

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

		var handleRect = new Rect( rect.Width - handleSize, (rect.Height - handleSize) * 0.5f, handleSize, handleSize ).Shrink( 2 );
		DrawHandle( color, handleRect, config.Shape );
		DrawLabel( new Rect( spacex, 0, rect.Width - handleSize - spacex * 2, rect.Size.y ), unreachable, TextFlag.RightCenter );
	}
}
