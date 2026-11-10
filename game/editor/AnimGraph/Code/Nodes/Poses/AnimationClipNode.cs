using System.Text.Json.Serialization;

namespace Editor.AnimGraph.Nodes;

/// <summary>
/// Plays an animation clip with optional looping, root motion, reverse playback, time reset, speed scaling, sync-event offset, and graph events.
/// <br/><br/><b>Use when:</b>&#160;Playing a named animation as part of a pose graph.
/// <br/><b>Important:</b>&#160;An unassigned animation outputs the reference pose. <b>Play In Reverse</b> is ignored when a synchronized parent drives the clip's time.
/// </summary>
[Title( "Animation Clip" ), Group( "Animation" ), Icon( "movie" )]
public class AnimationClipNode : VariationDataNode<AnimationClipNode.Data>
{
	/// <summary>
	/// The parts of an animation clip node that can differ per variation.
	/// </summary>
	public sealed class Data : VariationDataNode.VariationData
	{
		[Property, Title( "Animation" ), ResourceType( "anim" )]
		public string AnimationName { get; set; } = "";

		[Property, Range( 0.01f, 5.0f )]
		public float SpeedMultiplier { get; set; } = 1.0f;

		[Property]
		public int StartSyncEventOffset { get; set; }

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

	/// <summary>
	/// The playback speed of the variation being edited.
	/// </summary>
	[Hide, JsonIgnore]
	public float SpeedMultiplier
	{
		get => SelectedData.SpeedMultiplier;
		set => SelectedData.SpeedMultiplier = value;
	}

	[Hide, JsonPropertyName( "AnimationName" )]
	public string LegacyAnimationName
	{
		set => DefaultVariationData.AnimationName = value;
	}

	[Hide, JsonPropertyName( "SpeedMultiplier" )]
	public float LegacySpeedMultiplier
	{
		set => DefaultVariationData.SpeedMultiplier = value;
	}

	[Hide, JsonPropertyName( "StartSyncEventOffset" )]
	public int LegacyStartSyncEventOffset
	{
		set => DefaultVariationData.StartSyncEventOffset = value;
	}

	[Property]
	public bool Loop { get; set; } = true;

	[Property]
	public bool SampleRootMotion { get; set; } = true;

	/// <summary>
	/// Graph event IDs fired while this clip plays.
	/// </summary>
	[Property]
	public List<string> GraphEvents { get; set; } = [];

	[Input( typeof( BoolLink ) ), Title( "Play In Reverse" )]
	public NodeInput PlayInReverse { get; set; }

	[Input( typeof( BoolLink ) ), Title( "Reset Time" )]
	public NodeInput ResetTime { get; set; }

	[Output( typeof( PoseLink ) )]
	[JsonIgnore]
	public object Result => default;

	public override Color PrimaryColor => Theme.Green;

	public override DisplayInfo DisplayInfo
	{
		get
		{
			var info = TypeDisplayInfo;
			if ( !string.IsNullOrWhiteSpace( SelectedData.AnimationName ) )
				info.Name = SelectedData.AnimationName;
			return info;
		}
	}


	internal override GraphBuilder.Node Compile( Compiler compiler )
	{
		var data = (Data)GetResolvedVariationData( compiler.VariationHierarchy, compiler.VariationID );

		if ( string.IsNullOrWhiteSpace( data.AnimationName ) )
		{
			compiler.Warn( "Animation Clip has no animation selected" );
			return compiler.ReferencePose();
		}

		var playInReverse = compiler.EmitValue( this, PlayInReverse );
		var resetTime = compiler.EmitValue( this, ResetTime );
		var graphEvents = GraphEvents?.Where( id => !string.IsNullOrWhiteSpace( id ) ) ?? [];

		return compiler.Builder.Clip(
			data.AnimationName,
			new GraphBuilder.ClipSettings
			{
				Looping = Loop,
				Speed = data.SpeedMultiplier,
				PlayInReverse = playInReverse,
				ResetTime = resetTime,
				SyncEventOffset = data.StartSyncEventOffset,
				SampleRootMotion = SampleRootMotion,
				GraphEvents = graphEvents,
			} );
	}

}
