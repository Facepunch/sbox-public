using Editor.NodeEditor;
using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Selects the Animation Clip whose remaining root motion best matches a target's position and orientation.
/// <br/><br/><b>Use when:</b>&#160;Choosing the animation that best reaches a positional and rotational target.
/// <br/><b>Important:</b>&#160;Options must be Animation Clips with usable root motion. Position and orientation scores are weighted independently, and selection remains fixed until shutdown.
/// </summary>
[Title( "Target Selector" ), Group( "Flow" ), Icon( "my_location" )]
public class TargetSelectorNode : VariationDataNode<TargetSelectorNode.Data>
{
	public sealed class Data : VariationDataNode.VariationData
	{
		[Property, Editor( "animgraph.bone" )]
		public string AlignmentBone { get; set; } = "";
	}

	public class Option
	{
		/// <summary>
		/// Optional label for this option's pin.
		/// </summary>
		[Property, KeyProperty]
		public string Label { get; set; } = "";

		[Hide]
		public NodeInput Pose { get; set; }
	}

	[Input( typeof( TargetLink ) )]
	public NodeInput Target { get; set; }

	[Property]
	public List<Option> Options { get; set; } =
	[
		new(),
		new(),
	];

	/// <summary>
	/// How much the end orientation of each clip counts towards its score.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float OrientationScoreWeight { get; set; } = 1.0f;

	/// <summary>
	/// How much the end position of each clip counts towards its score.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float PositionScoreWeight { get; set; } = 1.0f;

	[Property]
	public bool IsWorldSpaceTarget { get; set; } = false;

	/// <summary>
	/// Skip options whose clip failed to compile instead of failing the whole node.
	/// </summary>
	[Property]
	public bool IgnoreInvalidOptions { get; set; } = false;

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Color.Parse( "#e5c07b" ).Value;

	private readonly List<IPlugIn> _optionPlugs = new();

	protected override IEnumerable<IPlugIn> DynamicInputs
	{
		get
		{
			while ( _optionPlugs.Count > Options.Count )
				_optionPlugs.RemoveAt( _optionPlugs.Count - 1 );

			while ( _optionPlugs.Count < Options.Count )
			{
				var i = _optionPlugs.Count;
				var info = new AnimPlugInfo( $"Option{i}",
					() => i < Options.Count && !string.IsNullOrEmpty( Options[i].Label ) ? Options[i].Label : $"Option {i + 1}",
					_ => i < Options.Count ? Options[i].Pose : default,
					( _, value ) => { if ( i < Options.Count ) Options[i].Pose = value; } );

				_optionPlugs.Add( new AnimPlugIn( this, info, typeof( PoseLink ) ) );
			}

			return _optionPlugs;
		}
	}

	public override Menu CreateContextMenu( NodeUI node )
	{
		var menu = new Menu( "Target Selector" );
		menu.AddOption( "Add Option", "add", () => { Options.Add( new Option() ); Update(); } );

		if ( Options.Count > 0 )
			menu.AddOption( "Remove Last Option", "remove", () => { Options.RemoveAt( Options.Count - 1 ); Update(); } );

		return menu;
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );
		if ( Options.Count == 0 )
		{
			compiler.Warn( "Target Selector has no options" );
			return compiler.ReferencePose();
		}

		var orientationWeight = OrientationScoreWeight;
		if ( orientationWeight <= 0.0f && PositionScoreWeight <= 0.0f )
		{
			compiler.Warn( "Target Selector needs at least one non-zero score weight" );
			orientationWeight = 1.0f;
		}

		var target = compiler.EmitValue( this, Target );
		if ( target is null )
		{
			compiler.Warn( "Target Selector's target input isn't connected" );
			target = compiler.NullTarget();
		}

		var options = new List<GraphBuilder.Node>();
		foreach ( var option in Options )
		{
			var index = compiler.EmitPose( this, option.Pose, "Target Selector" );
			if ( index.IsClip() )
				options.Add( index );
			else
				compiler.Warn( "Target Selector options must be animation clips" );
		}

		if ( options.Count == 0 )
			return compiler.ReferencePose();

		return compiler.Builder.TargetSelector(
			target,
			options,
			new GraphBuilder.TargetSelectorSettings
			{
				PositionWeight = PositionScoreWeight,
				OrientationWeight = orientationWeight,
				WorldSpace = IsWorldSpaceTarget,
				IgnoreInvalidOptions = IgnoreInvalidOptions,
				AlignmentBone = data.AlignmentBone,
			} );
	}

}
