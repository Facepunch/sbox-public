using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Samples one static pose from an animation at a fixed or driven time without advancing playback or sampling events and root motion.
/// <br/><br/><b>Use when:</b>&#160;Selecting an exact pose by normalized time or frame.
/// <br/><b>Important:</b>&#160;Time can be remapped into 0-1, interpreted as frames, and is clamped to the animation range. An unassigned animation outputs the reference pose.
/// </summary>
[Title( "Animation Pose" ), Group( "Animation" ), Icon( "person" )]
public class AnimationPoseNode : VariationDataNode<AnimationPoseNode.Data>
{
	/// <summary>
	/// The parts of an animation pose node that can differ per variation.
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

		/// <summary>
		/// Overrides Fixed Time for this variation. Negative values use Fixed Time instead.
		/// Uses the node's frame or normalized-time mode; a connected Time input takes precedence.
		/// </summary>
		[Property]
		public float TimeOverride { get; set; } = -1.0f;

		internal float ResolveFixedTime( float fixedTime ) => TimeOverride >= 0 ? TimeOverride : fixedTime;

		public override VisualState GetVisualState()
		{
			return string.IsNullOrWhiteSpace( AnimationName ) ? VisualState.HasUnsetData : VisualState.None;
		}
	}

	[Input( typeof( FloatLink ) )]
	public NodeInput Time { get; set; }

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

	/// <summary>
	/// Remaps the input time value from this range into 0-1.
	/// </summary>
	[Property]
	public float RemapRangeStart { get; set; } = 0.0f;

	[Property]
	public float RemapRangeEnd { get; set; } = 1.0f;

	/// <summary>
	/// Time to sample at when neither the input nor the variation supplies a time.
	/// </summary>
	[Property]
	public float FixedTime { get; set; } = 0.0f;

	/// <summary>
	/// Interpret the input time in frames instead of a 0-1 percentage.
	/// </summary>
	[Property]
	public bool UseFramesAsInput { get; set; } = false;

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
				info.Name = $"Pose: {AnimationName}";
			return info;
		}
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );

		if ( string.IsNullOrWhiteSpace( data.AnimationName ) )
		{
			compiler.Warn( "Animation Pose has no animation selected" );
			return compiler.ReferencePose();
		}

		var time = compiler.EmitValue( this, Time );
		return compiler.Builder.Pose(
			data.AnimationReference,
			new GraphBuilder.PoseSettings
			{
				Time = time,
				RemapRangeStart = RemapRangeStart,
				RemapRangeEnd = RemapRangeEnd,
				FixedTime = data.ResolveFixedTime( FixedTime ),
				UseFramesAsInput = UseFramesAsInput,
			} );
	}

}
