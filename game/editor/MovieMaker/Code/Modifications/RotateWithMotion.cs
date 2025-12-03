
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

[MovieModification( "Rotate with Motion",
	Description = "Create LocalRotation track data that rotates objects in the direction of movement.",
	Group = "Animation",
	Icon = "transfer_within_a_station" )]
public class RotateWithMotionBuilder : BlendModification
{
	public override bool CanStart( TrackListView trackList, TimeSelection selection )
	{
		return trackList.GetLocalPositionTracks( selection.TotalTimeRange ).Any();
	}

	public override void Start( TrackListView trackList, TimeSelection selection )
	{
		var sampleRate = EditMode.Project.SampleRate;
		var sampleCount = selection.TotalTimeRange.Duration.GetFrameCount( sampleRate );
		
		var compiledTracks = new List<CompiledPropertyTrack<Rotation>>();
		var samples = new Rotation[sampleCount];

		foreach ( var localPosTrackView in trackList.GetLocalPositionTracks( selection.TotalTimeRange ) )
		{
			var track = (ProjectPropertyTrack<Vector3>)localPosTrackView.Track;

			var prev = track.GetLastValue( selection.TotalStart );
			var firstMovement = sampleCount;

			for ( var i = 0; i < sampleCount; ++i )
			{
				var time = selection.TotalTimeRange.Start + MovieTime.FromFrames( i, sampleRate );
				var next = track.GetLastValue( time );

				if ( !prev.AlmostEqual( next ) )
				{
					samples[i] = Rotation.LookAt( next - prev );
					firstMovement = Math.Min( firstMovement, i );
				}
				else if ( i > 0 )
				{
					samples[i] = samples[i - 1];
				}

				prev = next;
			}

			// Skip if the object wasn't moving

			if ( firstMovement >= sampleCount ) continue;

			// Fill in any invalid at the start

			Array.Fill( samples, samples[firstMovement], 0, firstMovement );

			var goTrack = (IReferenceTrack<GameObject>)track.Parent!;
			var compiledGoTrack = MovieClip.RootGameObject( goTrack.Name, goTrack.Id );
			var compiledLocalRotTrack = compiledGoTrack.Property<Rotation>( nameof( GameObject.LocalRotation ) )
				.WithSamples( selection.TotalTimeRange, sampleRate, samples );

			compiledTracks.Add( compiledLocalRotTrack );
		}

		SetFromTracks( compiledTracks, selection.TotalTimeRange, MovieTime.Zero, isAdditive: false );
	}
}
