namespace Sandbox.UI.NodeGraph;

/// <summary>
/// A reroute in a <see cref="NodeGraphPanel"/>: a dot a wire passes through, with its
/// <see cref="IRerouteNode.Comment"/> in a bubble above. Its input and output both sit under the
/// dot, so pressing it pulls a wire - out of the output once something feeds the input, else into
/// the input. Ctrl or Shift drags it.
/// </summary>
public class ReroutePanel : NodePanel
{
	/// <summary>
	/// Width and height of the dot, in canvas units.
	/// </summary>
	public const float Diameter = 16f;

	const float PlugSize = 14f;

	public IRerouteNode Reroute => (IRerouteNode)Node;

	public ReroutePanel( NodeGraphPanel graph, INode node, bool isPreview = false ) : base( graph, node, isPreview )
	{
		AddClass( "reroute" );
	}

	public override bool HasTooltip => false;

	/// <summary>
	/// Whether an input is fed, so a press should pull a wire out of the output.
	/// </summary>
	bool PreferOutput => Inputs.Any( x => x.IsConnected );

	protected override void Layout()
	{
		Size = new Vector2( Diameter );
		Style.Width = Diameter;
		Style.Height = Diameter;

		var preferOutput = PreferOutput;
		var offset = (Diameter - PlugSize) * 0.5f;

		foreach ( var input in Inputs )
		{
			PlacePlug( input, offset, preferOutput ? 0 : 2 );
		}

		foreach ( var output in Outputs )
		{
			PlacePlug( output, offset, preferOutput ? 2 : 0 );
		}
	}

	static void PlacePlug( PlugPanel plug, float offset, int zIndex )
	{
		plug.Size = new Vector2( PlugSize );
		plug.Position = new Vector2( offset );
		plug.Visible = false;
		plug.OnTooltip = null;
		plug.Style.ZIndex = zIndex;
		plug.Layout();
	}

	public override void Tick()
	{
		base.Tick();

		// Which plug a press should take changes as wires come and go
		var preferOutput = PreferOutput;
		if ( Outputs.FirstOrDefault() is { } output && (output.Style.ZIndex == 2) != preferOutput ) Layout();
	}

	protected internal override PlugPanel GetPlugAt( Vector2 canvasPosition )
	{
		if ( !new Rect( Position, Size ).IsInside( canvasPosition ) ) return null;

		return PreferOutput
			? (PlugPanel)Outputs.FirstOrDefault() ?? Inputs.FirstOrDefault()
			: (PlugPanel)Inputs.FirstOrDefault() ?? Outputs.FirstOrDefault();
	}

	public override void OnDraw( Painter painter )
	{
		DrawWires( painter );

		var paint = new NodePaint( this, painter ) { HasMouseOver = HasHovered };
		var color = Outputs.FirstOrDefault()?.HandleConfig.Color ?? Graph.DefaultHandleConfig.Color;

		if ( !paint.HasMouseOver )
		{
			color = color.Desaturate( 0.2f ).Darken( 0.3f );
		}

		paint.SetPen( NodeTheme.Canvas, 2f );
		paint.SetBrush( Selected ? Graph.SelectionColor : color );
		paint.DrawRect( new Rect( 0f, Size ), Diameter * 0.5f );

		DrawComment( ref paint );
	}

	/// <summary>
	/// The comment in a bubble centred above the dot, pointing down at it.
	/// </summary>
	void DrawComment( ref NodePaint paint )
	{
		if ( string.IsNullOrWhiteSpace( Reroute.Comment ) ) return;

		var text = Reroute.Comment;
		var center = Diameter * 0.5f;

		paint.SetDefaultFont( 10f );

		var width = MathF.Floor( MathF.Min( paint.MeasureText( text ).x + 20f, 500f ) );
		var bubble = new Rect( center - width * 0.5f, center - 40f, width, 25f );
		var tip = center - 10f;

		paint.ClearPen();
		paint.SetBrush( NodeTheme.FieldBackground.WithAlpha( 0.8f ) );
		paint.DrawRect( bubble, 2f );
		paint.DrawPolygon( new Vector2( center - 5f, bubble.Bottom ), new Vector2( center + 5f, bubble.Bottom ), new Vector2( center, tip ) );

		paint.SetPen( NodeTheme.Text );
		paint.DrawText( bubble, text, TextFlag.Center );
	}
}
