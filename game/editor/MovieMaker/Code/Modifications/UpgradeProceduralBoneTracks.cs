using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using System.Linq;
using Sandbox.MovieMaker.Properties;

namespace Editor.MovieMaker;

#nullable enable

[MovieModification( "Upgrade Procedural Bone Tracks",
	Description = "Convert legacy procedural bone object tracks to IK-controllable tracks.",
	Group = "Animation",
	Icon = "upgrade" )]
public class UpgradeProceduralBoneTracks : BlendModification
{
	public override bool CanStart( TrackListView trackList, TimeSelection selection )
	{
		return GetProceduralBoneTracks( trackList, selection.TotalTimeRange ).Any();
	}

	private readonly record struct ProceduralBoneTrack(
		ProjectReferenceTrack<GameObject> BoneObjectTrack,
		ProjectReferenceTrack<GameObject> RendererObjectTrack,
		Model Model,
		ProjectPropertyTrack<Vector3>? PositionTrack,
		ProjectPropertyTrack<Rotation>? RotationTrack,
		ProjectPropertyTrack<Vector3>? ScaleTrack )
	{
		public IEnumerable<IProjectPropertyTrack> PropertyTracks
		{
			get
			{
				if ( PositionTrack is { } posTrack ) yield return posTrack;
				if ( RotationTrack is { } rotTrack ) yield return rotTrack;
				if ( ScaleTrack is { } sclTrack ) yield return sclTrack;
			}
		}
	}

	private static IEnumerable<ProceduralBoneTrack> GetProceduralBoneTracks( TrackListView trackList, MovieTimeRange timeRange )
	{
		var boneObjectTracks = trackList.AllTracks
			.Where( x => x is { Track: ProjectReferenceTrack<GameObject>, IsBoneObject: true } );

		return boneObjectTracks.Select( x =>
			{
				var boneObjectTrack = (ProjectReferenceTrack<GameObject>)x.Track;
				var rendererObjectTrackView = GetRendererObjectTrackView( x );

				return new ProceduralBoneTrack( boneObjectTrack,
					(ProjectReferenceTrack<GameObject>)rendererObjectTrackView!.Track,
					(rendererObjectTrackView.Target.Value as GameObject)?.GetComponent<SkinnedModelRenderer>()?.Model!,
					(ProjectPropertyTrack<Vector3>?)boneObjectTrack.GetChild( nameof( GameObject.LocalPosition ) ),
					(ProjectPropertyTrack<Rotation>?)boneObjectTrack.GetChild( nameof( GameObject.LocalRotation ) ),
					(ProjectPropertyTrack<Vector3>?)boneObjectTrack.GetChild( nameof( GameObject.LocalScale ) ) );
			} )
			.Where( x => (ProjectReferenceTrack<GameObject>?)x.RendererObjectTrack is not null )
			.Where( x => (Model?)x.Model is not null )
			.Where( x => HasTrackData( timeRange, x.PropertyTracks ) );
	}

	private static TrackView? GetRendererObjectTrackView( TrackView boneObjectTrackView )
	{
		var parentTrackView = boneObjectTrackView.Parent;

		while ( parentTrackView is not null && parentTrackView.IsBoneObject )
		{
			parentTrackView = parentTrackView.Parent;
		}

		return parentTrackView;
	}

	private static bool HasTrackData( MovieTimeRange timeRange, IEnumerable<IProjectPropertyTrack> tracks )
	{
		foreach ( var track in tracks )
		{
			if ( track.GetBlocks( timeRange ).Any() ) return true;
		}

		return false;
	}

	public override void Start( TrackListView trackList, TimeSelection selection )
	{
		var timeRange = selection.TotalTimeRange;
		var sampleRate = EditMode.Project.SampleRate;

		var compiledTracks = new List<ICompiledPropertyTrack>();
		var boneAccessorTracks = new Dictionary<ProjectReferenceTrack<GameObject>, CompiledPropertyTrack<BoneAccessor>>();

		foreach ( var source in GetProceduralBoneTracks( trackList, timeRange ) )
		{
			// Get / create BoneAccessor track that would be the parent of this bone track

			if ( !boneAccessorTracks.TryGetValue( source.RendererObjectTrack, out var compiledAccessorTrack ) )
			{
				var rendererTrack = source.RendererObjectTrack.GetChild( nameof( SkinnedModelRenderer ) ) as ProjectReferenceTrack<SkinnedModelRenderer>;

				var compiledRootTrack = MovieClip.RootGameObject( source.RendererObjectTrack.Name, source.RendererObjectTrack.Id );
				var compiledRendererTrack = compiledRootTrack.Component<SkinnedModelRenderer>( rendererTrack?.Id );

				compiledAccessorTrack = boneAccessorTracks[source.RendererObjectTrack] = compiledRendererTrack.Property<BoneAccessor>( "Bones" );
			}

			// Create bone transform track

			var compiledBoneTrack = compiledAccessorTrack.Property<Transform>( source.BoneObjectTrack.Name );

			// Get time ranges where there will be transform data for this bone

			var blockTimeRanges = source.PropertyTracks
				.Select( x => x.GetBlocks( timeRange ).Select( y => y.TimeRange ) )
				.Aggregate( Enumerable.Empty<MovieTimeRange>(), ( s, x ) => x.Union( s ) )
				.Select( x => x.Clamp( timeRange ) )
				.Where( x => x.Duration.IsPositive );

			var bone = source.Model.Bones.GetBone( source.BoneObjectTrack.Name );
			var parentBindPose = bone.Parent?.LocalTransform ?? Transform.Zero;
			var bindPose = parentBindPose.ToLocal( bone.LocalTransform );

			foreach ( var blockTimeRange in blockTimeRanges )
			{
				// TODO: try to preserve keyframes?

				var sampleCount = blockTimeRange.Duration.GetFrameCount( sampleRate );
				var samples = new Transform[sampleCount];

				Log.Info( $"{source.BoneObjectTrack.Name}: {bindPose}" );

				for ( var i = 0; i < sampleCount; ++i )
				{
					var sample = bindPose;
					var time = MovieTime.FromFrames( i, sampleRate );

					if ( source.PositionTrack?.TryGetValue( time, out var pos ) is true )
					{
						sample.Position = pos;
					}

					if ( source.RotationTrack?.TryGetValue( time, out var rot ) is true )
					{
						sample.Rotation = rot;
					}

					if ( source.ScaleTrack?.TryGetValue( time, out var scale ) is true )
					{
						sample.Scale = scale;
					}

					samples[i] = sample;
				}

				compiledBoneTrack = samples.All( x => x.AlmostEqual( samples[0] ) )
					? compiledBoneTrack.WithConstant( blockTimeRange, samples[0] )
					: compiledBoneTrack.WithSamples( blockTimeRange, sampleRate, samples );
			}

			compiledTracks.Add( compiledBoneTrack );
		}

		SetFromTracks( compiledTracks, timeRange, MovieTime.Zero, isAdditive: false );
	}
}
