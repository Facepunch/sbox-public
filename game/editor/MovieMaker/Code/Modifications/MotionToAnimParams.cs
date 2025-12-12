using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

[MovieModification( "Motion to AnimGraph Parameters",
	Description = "Create movement related AnimGraph parameter tracks to match the motion of objects.",
	Group = "Animation",
	Icon = "directions_run" )]
public class MotionToAnimParams : BlendModification
{
	public override bool CanStart( TrackListView trackList, TimeSelection selection )
	{
		return trackList.GetLocalPositionTracks( selection.TotalTimeRange ).Any();
	}

	public override void Start( TrackListView trackList, TimeSelection selection )
	{
		var sampleRate = EditMode.Project.SampleRate;
		var timeRange = selection.TotalTimeRange;
		var sampleCount = timeRange.Duration.GetFrameCount( sampleRate );

		if ( sampleCount < 2 ) return;

		var compiledTracks = new List<ICompiledPropertyTrack>();

		var transforms = new Transform[sampleCount];
		var velocities = new Vector3[sampleCount];
		var yawSpeeds = new float[sampleCount];
		var accels = new Vector3[sampleCount];

		foreach ( var localPosTrackView in trackList.GetLocalPositionTracks( timeRange ) )
		{
			var localPosTrack = (ProjectPropertyTrack<Vector3>)localPosTrackView.Track;
			var localRotTrack = localPosTrackView.Parent?.Find( nameof( GameObject.LocalRotation ) )?.Track as ProjectPropertyTrack<Rotation>;

			// Fetch transform at every frame

			for ( var i = 0; i < sampleCount; i++ )
			{
				var time = timeRange.Start + MovieTime.FromFrames( i, sampleRate );

				var pos = localPosTrack.GetLastValue( time );
				var rot = localRotTrack?.GetLastValue( time ) ?? Rotation.Identity;

				transforms[i] = new Transform( pos, rot );
			}

			// Calculate velocities

			for ( var i = 0; i < sampleCount - 1; i++ )
			{
				velocities[i] = (transforms[i + 1].Position - transforms[i].Position) * sampleRate;
				yawSpeeds[i] = MathX.DeltaDegrees( transforms[i + 1].Rotation.Yaw(), transforms[i].Rotation.Yaw() ) * sampleRate;
			}

			velocities[^1] = velocities[^2];

			// Calculate acceleration

			for ( var i = 0; i < sampleCount - 1; i++ )
			{
				accels[i] = velocities[i + 1] - velocities[i];
			}

			accels[^1] = 0f;

			// Make local to object rotation

			for ( var i = 0; i < sampleCount; i++ )
			{
				velocities[i] = transforms[i].Rotation.Inverse * velocities[i];
				accels[i] = transforms[i].Rotation.Inverse * accels[i];
			}

			var rootTrack = (ProjectReferenceTrack<GameObject>)localPosTrackView.Parent!.Track;
			var rendererTrack = (ProjectReferenceTrack<SkinnedModelRenderer>)EditMode.Session.GetOrCreateTrack( rootTrack, nameof(SkinnedModelRenderer) );

			var compiledRootTrack = MovieClip.RootGameObject( rootTrack.Name, rootTrack.Id );
			var compiledRendererTrack = compiledRootTrack.Component<SkinnedModelRenderer>( rendererTrack.Id );
			var compiledParamsTrack = compiledRendererTrack.Property<SkinnedModelRenderer.ParameterAccessor>( nameof(SkinnedModelRenderer.Parameters) );

			compiledTracks.Add( compiledParamsTrack.Property<float>( "move_x" )
				.WithSamples( timeRange, sampleRate, velocities.Select( x => x.x ) ) );
			compiledTracks.Add( compiledParamsTrack.Property<float>( "move_y" )
				.WithSamples( timeRange, sampleRate, velocities.Select( x => -x.y ) ) );
			compiledTracks.Add( compiledParamsTrack.Property<float>( "move_z" )
				.WithSamples( timeRange, sampleRate, velocities.Select( x => x.z ) ) );
			compiledTracks.Add( compiledParamsTrack.Property<float>( "move_direction" )
				.WithSamples( timeRange, sampleRate, velocities.Select( GetAngle ) ) );
			compiledTracks.Add( compiledParamsTrack.Property<float>( "move_speed" )
				.WithSamples( timeRange, sampleRate, velocities.Select( x => x.Length ) ) );
			compiledTracks.Add( compiledParamsTrack.Property<float>( "move_groundspeed" )
				.WithSamples( timeRange, sampleRate, velocities.Select( x => x.WithZ( 0f ).Length ) ) );
			compiledTracks.Add( compiledParamsTrack.Property<float>( "move_rotationspeed" )
				.WithSamples( timeRange, sampleRate, yawSpeeds ) );
			compiledTracks.Add( compiledParamsTrack.Property<float>( "skid_x" )
				.WithSamples( timeRange, sampleRate, accels.Select( x => x.x / 800f ) ) );
			compiledTracks.Add( compiledParamsTrack.Property<float>( "skid_y" )
				.WithSamples( timeRange, sampleRate, accels.Select( x => -x.y / 800f ) ) );
		}

		SetFromTracks( compiledTracks, timeRange, MovieTime.Zero, isAdditive: false );
	}

	private static float GetAngle( Vector3 localVelocity )
	{
		return MathF.Atan2( localVelocity.y, localVelocity.x ).RadianToDegree().NormalizeDegrees();
	}
}
