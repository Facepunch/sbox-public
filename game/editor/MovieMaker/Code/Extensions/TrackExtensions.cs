using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;
using System.Linq;
using System.Reflection;

namespace Editor.MovieMaker;

#nullable enable

public record BakeAnimationsOptions(
	int SampleRate = MovieProject.DefaultSampleRate,
	bool IncludeRootMotion = false,
	Action<SceneModel>? OnInitialize = null,
	Action<SceneModel, float, MovieTime>? OnUpdate = null,
	CompiledReferenceTrack<SkinnedModelRenderer>? ParentTrack = null );

public static class TrackExtensions
{
	public static ProjectReferenceTrack<GameObject>? GetGameObjectTrack( this IProjectTrack? track )
	{
		while ( track is not null )
		{
			if ( track is ProjectReferenceTrack<GameObject> match )
			{
				return match;
			}

			track = track.Parent;
		}

		return null;
	}

	public static IEnumerable<TrackView> GetLocalPositionTracks( this TrackListView listView, MovieTimeRange timeRange )
	{
		return listView.UnlockedTracks
			.Where( x => x is { Track: ProjectReferenceTrack<GameObject> }
				&& x.Find( nameof(GameObject.LocalPosition) ) is { Track: ProjectPropertyTrack<Vector3> posTrack }
				&& posTrack.GetBlocks( timeRange ).Any() )
			.Select( x => x.Find( nameof(GameObject.LocalPosition) )! );
	}

	public static IEnumerable<TrackView> GetSkinnedModelRendererTracksWithParameters( this TrackListView listView, MovieTimeRange timeRange )
	{
		return listView.UnlockedTracks
			.Where( x => x is { Track: ProjectReferenceTrack<SkinnedModelRenderer> } )
			.Where( x => x.Find( nameof(SkinnedModelRenderer.Parameters) )?.Children.Any(
				y => y.Track is IProjectPropertyTrack propertyTrack && propertyTrack.GetBlocks( timeRange ).Any() ) ?? false );
	}

	public static IReadOnlyList<ICompiledPropertyTrack> BakeAnimation( this SkinnedModelRenderer renderer, MovieTimeRange timeRange, BakeAnimationsOptions? options = null )
	{
		options ??= new BakeAnimationsOptions();

		var compiledRootTrack = options.ParentTrack?.Parent ?? MovieClip.RootGameObject( renderer.GameObject.Name );
		var compiledRendererTrack = options.ParentTrack ?? compiledRootTrack.Component<SkinnedModelRenderer>();

		var sampleRate = options.SampleRate;
		var samplePeriod = 1f / sampleRate;

		var dummyWorld = new SceneWorld();

		var compiledTracks = new List<ICompiledPropertyTrack>();
		var sourceModel = renderer.Model;

		try
		{
			var dummyModel = new SceneModel( dummyWorld, renderer.Model, Transform.Zero );

			options.OnInitialize?.Invoke( dummyModel );

			dummyModel.Update( 0f );

			var sampleCount = timeRange.Duration.GetFrameCount( sampleRate );

			var boneTrackData = Enumerable.Range( 0, sourceModel.BoneCount )
				.Select( _ => new List<Transform>( sampleCount ) )
				.ToArray();

			var rootMotionData = new List<Transform>();
			var prevRootMotion = Transform.Zero;

			for ( var i = 0; i <= sampleCount; ++i )
			{
				var time = MovieTime.FromFrames( i, sampleRate );

				options.OnUpdate?.Invoke( dummyModel, samplePeriod, time );

				for ( var boneIndex = 0; boneIndex < boneTrackData.Length; ++boneIndex )
				{
					boneTrackData[boneIndex].Add( dummyModel.GetParentSpaceBone( boneIndex ) );
				}

				prevRootMotion = dummyModel.RootMotion.ToWorld( prevRootMotion );

				rootMotionData.Add( prevRootMotion );

				dummyModel.Update( samplePeriod );
			}

			var compiledBonesTrack = compiledRendererTrack.Property<BoneAccessor>( "Bones" );

			for ( var boneIndex = 0; boneIndex < boneTrackData.Length; ++boneIndex )
			{
				var boneName = sourceModel.GetBoneName( boneIndex );
				var samples = boneTrackData[boneIndex];

				var track = compiledBonesTrack
					.Property<Transform>( boneName );

				var first = samples[0];

				track = samples.All( sample => sample.AlmostEqual( first ) )
					? track.WithConstant( timeRange, first )
					: track.WithSamples( timeRange, sampleRate, [.. samples] );

				compiledTracks.Add( track );
			}

			if ( !options.IncludeRootMotion )
			{
				return compiledTracks;
			}

			var baseTransform = renderer.LocalTransform;

			for ( var i = 0; i < rootMotionData.Count; ++i )
			{
				rootMotionData[i] = baseTransform.ToWorld( rootMotionData[i] );
			}

			var compiledPositionTrack = compiledRootTrack
				.Property<Vector3>( nameof( GameObject.LocalPosition ) )
				.WithSamples( timeRange, sampleRate, rootMotionData.Select( x => x.Position ) );

			var compiledRotationTrack = compiledRootTrack
				.Property<Rotation>( nameof( GameObject.LocalRotation ) )
				.WithSamples( timeRange, sampleRate, rootMotionData.Select( x => x.Rotation ) );

			compiledTracks.Add( compiledPositionTrack );
			compiledTracks.Add( compiledRotationTrack );

			return compiledTracks;
		}
		finally
		{
			dummyWorld.Delete();
		}
	}
}
