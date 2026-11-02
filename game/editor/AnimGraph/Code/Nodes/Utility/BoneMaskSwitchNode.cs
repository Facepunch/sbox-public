using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Selects the True Mask or False Mask from a boolean condition.
/// <br/><br/><b>Use when:</b>&#160;Toggling between two bone masks from a boolean state.
/// <br/><b>Important:</b>&#160;Selection occurs on initialization. Enable <b>Switch Dynamically</b> to react to later condition changes, optionally blending over time.
/// </summary>
[Title( "Bone Mask Switch" ), Group( "Bone Mask" ), Icon( "toggle_on" )]
public class BoneMaskSwitchNode : Node
{
	[Input( typeof( BoolLink ) )]
	public NodeInput Condition { get; set; }

	[Input( typeof( BoneMaskLink ) ), Title( "True Mask" )]
	public NodeInput TrueMask { get; set; }

	[Input( typeof( BoneMaskLink ) ), Title( "False Mask" )]
	public NodeInput FalseMask { get; set; }

	/// <summary>
	/// Blend to the new mask when the condition changes while active.
	/// </summary>
	[Property]
	public bool SwitchDynamically { get; set; } = false;

	[Property, ShowIf( nameof( SwitchDynamically ), true )]
	public float BlendTime { get; set; } = 0.1f;

	[Output( typeof( BoneMaskLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#d19a66" ).Value;


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var condition = compiler.EmitValue( this, Condition );
		if ( condition is null )
		{
			compiler.Warn( "Bone Mask Switch has no Condition connected" );
			condition = compiler.ConstBool( false );
		}

		var whenTrue = compiler.EmitMask( this, TrueMask );
		var whenFalse = compiler.EmitMask( this, FalseMask );
		return compiler.Builder.BoneMaskSwitch(
			condition,
			whenTrue,
			whenFalse,
			new GraphBuilder.BoneMaskSelectionSettings
			{
				SwitchDynamically = SwitchDynamically,
				BlendTime = BlendTime,
			} );
	}

}
