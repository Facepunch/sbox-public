using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Plays an animation clip whose playback time is driven directly by a float input (a percentage through the clip), with optional reverse playback, root motion, and graph events.
/// <br/><br/><b>Use when:</b>&#160;Scrubbing an animation from a parameter, such as aim sweeps, lever positions, or door openings.
/// <br/><b>Important:</b>&#160;The <b>Time</b> input must be connected. Time controlled clips ignore synchronization from parent blends or layers. Moving the time backwards while playing forwards is treated as a loop for event and root-motion sampling.
/// </summary>
[Title( "Time Controlled Animation Clip" ), Group( "Animation" ), Icon( "history_toggle_off" )]
public class TimeControlledAnimationClipNode : VariationDataNode<TimeControlledAnimationClipNode.Data>
{
	/// <summary>
	/// The parts of a time controlled animation clip node that can differ per variation.
	/// </summary>
	public sealed class Data : VariationDataNode.VariationData
	{
		[Property, Title( "Animation" ), ResourceType( "anim" ), JsonIgnore]
		public string AnimationName
		{
			get => AnimationReference.ResolvePath() ?? "";
			set => AnimationReference = ResourceId.FromPath( value );
		}

		[Hide, JsonPropertyName( "AnimationName" )]
		public ResourceId AnimationReference { get; set; }

		public override VisualState GetVisualState()
		{
			return string.IsNullOrWhiteSpace( AnimationName ) ? VisualState.HasUnsetData : VisualState.None;
		}
	}

	/// <summary>
	/// The animation of the variation being edited.
	/// </summary>
	[Hide, JsonIgnore]
	public string AnimationName
	{
		get => SelectedData.AnimationName;
		set => SelectedData.AnimationName = value;
	}

	[Hide, JsonPropertyName( "AnimationName" )]
	public string LegacyAnimationName
	{
		set => DefaultVariationData.AnimationName = value;
	}

	[Property]
	public bool SampleRootMotion { get; set; } = true;

	/// <summary>
	/// Graph event IDs fired while this clip plays.
	/// </summary>
	[Property]
	public List<string> GraphEvents { get; set; } = [];

	[Input( typeof( FloatLink ) ), Title( "Time" )]
	public NodeInput Time { get; set; }

	[Input( typeof( BoolLink ) ), Title( "Play In Reverse" )]
	public NodeInput PlayInReverse { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Green;

	protected override DisplayInfo DefaultDisplayInfo
	{
		get
		{
			var info = TypeDisplayInfo;
			if ( !string.IsNullOrWhiteSpace( AnimationName ) )
				info.Name = AnimationName;
			return info;
		}
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );

		if ( string.IsNullOrWhiteSpace( data.AnimationName ) )
		{
			compiler.Warn( "Time Controlled Animation Clip has no animation selected" );
			return compiler.ReferencePose();
		}

		if ( GetConnectedNode( Time ) is null )
		{
			compiler.Warn( "Time Controlled Animation Clip requires a Time input", this );
			return compiler.ReferencePose();
		}

		var time = compiler.EmitValue( this, Time );
		var playInReverse = compiler.EmitValue( this, PlayInReverse );
		var graphEvents = GraphEvents?.Where( id => !string.IsNullOrWhiteSpace( id ) ) ?? [];

		return compiler.Builder.TimeControlledClip(
			data.AnimationReference,
			time,
			new GraphBuilder.TimeControlledClipSettings
			{
				PlayInReverse = playInReverse,
				SampleRootMotion = SampleRootMotion,
				GraphEvents = graphEvents,
			} );
	}

}
