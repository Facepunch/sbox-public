namespace Sandbox.UI.NodeGraph;

/// <summary>
/// An input: handle on the left, label after it.
/// </summary>
public class PlugInPanel : PlugPanel
{
	public new IPlugIn Inner => (IPlugIn)base.Inner;

	public override Vector2 ConnectionPosition => Node.Position + Position + new Vector2( handleSize * 0.5f, Size.y * 0.5f );

	public override bool IsConnected => Inner.ConnectedOutput is not null;

	public PlugInPanel( NodePanel node, IPlugIn plug ) : base( node, plug )
	{
	}

	public override void Layout()
	{
		base.Layout();
		Editor?.Place( new Rect( 15, 0, Size.x - 15, Size.y ) );
	}

	protected override void Paint( ref NodePaint paint )
	{
		var isTarget = DropTarget == this && Node.Graph.Preview is not null;
		var highlight = paint.HasMouseOver && DropTarget is null || isTarget;
		var unreachable = !highlight && !Node.Selected && !Inner.IsReachable;

		var rect = new Rect( 0, Size );

		var spacex = 4f;
		var config = HandleConfig;

		if ( isTarget )
		{
			config = Node.Graph.Preview.Output.HandleConfig;
		}
		else if ( Inner.ConnectedOutput is { } output && Node.Graph.FindPlug( output ) is { } outputPlug )
		{
			config = outputPlug.HandleConfig;
		}

		var color = config.Color;

		if ( !highlight )
		{
			color = color.Desaturate( 0.2f ).Darken( 0.3f );
		}

		if ( unreachable )
		{
			color = color.Desaturate( 0.5f ).Darken( 0.25f );
		}

		var handleRect = new Rect( 0, (Size.y - handleSize) * 0.5f, handleSize, handleSize ).Shrink( 2 );
		DrawHandle( ref paint, color, handleRect, config.Shape );
		DrawLabel( ref paint, new Rect( handleSize + spacex, 0, rect.Width - 4 - 10, rect.Size.y ), unreachable, TextFlag.LeftCenter );
	}
}
