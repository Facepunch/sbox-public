using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Defines a state-machine layer whose active state supplies the layer weight, root-motion weight, and bone mask.
/// <br/><br/><b>Use when:</b>&#160;Layering state-driven animation through a Layer Blend node.
/// <br/><b>Important:</b>&#160;Requires a State Machine input and must connect to Layer Blend. It continues evaluating at zero weight so state transitions can progress.
/// </summary>
[Title( "State Machine Layer" ), Group( "Layers" ), Icon( "layers" )]
public class StateMachineLayerNode : Node
{
	private const float DebugHeight = 24;

	[Input( typeof( PoseLink ) ), Title( "State Machine" )]
	public NodeInput StateMachine { get; set; }

	[Property]
	public GraphBuilder.LayerBlendMode BlendMode { get; set; } = GraphBuilder.LayerBlendMode.Overlay;

	[Property]
	public bool IsSynchronized { get; set; }

	[Property]
	public bool IgnoreEvents { get; set; }

	[Output( typeof( LayerLink ) )]
	[JsonIgnore]
	public object Layer => default;

	public override Color PrimaryColor => Color.Parse( "#ff6347" ).Value;

	[JsonIgnore]
	internal bool HasLiveLayerWeight { get; private set; }

	[JsonIgnore]
	internal float LiveLayerWeight { get; private set; }

	public StateMachineLayerNode()
	{
		ExpandSize = new Vector2( 0, DebugHeight + 8 );
	}

	public override void OnPaint( Rect rect )
	{
		var area = new Rect( rect.Left + 8, rect.Bottom - DebugHeight - 8, rect.Width - 16, DebugHeight );

		Paint.ClearPen();
		Paint.SetBrush( Theme.WidgetBackground );
		Paint.DrawRect( area, 4 );

		Paint.SetDefaultFont( 7 );
		Paint.SetPen( Theme.Text );
		Paint.DrawText( area, HasLiveLayerWeight ? LiveLayerWeight.ToString( "0.00" ) : "-", TextFlag.Center );
	}

	public bool SetLiveLayerWeight( bool hasWeight, float weight = 0.0f )
	{
		if ( HasLiveLayerWeight == hasWeight && LiveLayerWeight == weight )
			return false;

		HasLiveLayerWeight = hasWeight;
		LiveLayerWeight = weight;
		return true;
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		compiler.Warn( "State Machine Layer must be connected to a Layer Blend node", this );
		return compiler.ReferencePose();
	}

}
