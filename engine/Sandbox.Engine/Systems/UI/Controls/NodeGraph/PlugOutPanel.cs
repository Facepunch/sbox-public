namespace Sandbox.UI.NodeGraph;

/// <summary>
/// An output: label on the left, handle on the right edge.
/// </summary>
public class PlugOutPanel : PlugPanel
{
	public new IPlugOut Inner => (IPlugOut)base.Inner;

	public override Vector2 ConnectionPosition => Node.Position + Position + new Vector2( Size.x - handleSize * 0.5f, Size.y * 0.5f );

	public override bool IsConnected => Node.Graph.Graph?.Nodes
		.SelectMany( x => x.Inputs )
		.Any( x => Equals( x.ConnectedOutput, Inner ) ) ?? false;

	public PlugOutPanel( NodePanel node, IPlugOut plug ) : base( node, plug )
	{
	}

	public override void Layout()
	{
		base.Layout();
		Editor?.Place( new Rect( 0, 0, Size.x - 15, Size.y ) );
	}

	protected override void Paint( ref NodePaint paint )
	{
		var isTarget = DropTarget == this && Node.Graph.Preview is not null;
		var highlight = paint.HasMouseOver && DropTarget is null || isTarget;
		var unreachable = !highlight && !Node.Selected && !Inner.IsReachable;

		var rect = new Rect( 0, Size );

		var spacex = 4f;

		var config = HandleConfig;
		var color = config.Color;

		if ( !highlight )
		{
			color = color.Desaturate( 0.2f ).Darken( 0.3f );
		}

		if ( unreachable )
		{
			color = color.Desaturate( 0.5f ).Darken( 0.25f );
		}

		var handleRect = new Rect( rect.Width - handleSize, (rect.Height - handleSize) * 0.5f, handleSize, handleSize ).Shrink( 2 );
		DrawHandle( ref paint, color, handleRect, config.Shape, IsConnected || highlight );
		DrawLabel( ref paint, new Rect( spacex, 0, rect.Width - handleSize - spacex * 2, rect.Size.y ), unreachable, TextFlag.RightCenter );
	}
}
