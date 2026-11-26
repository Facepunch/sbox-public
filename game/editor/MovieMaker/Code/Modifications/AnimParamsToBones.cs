using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;
using System.Linq;

namespace Editor.MovieMaker;

#nullable enable

[MovieModification( "AnimGraph Parameters to Bones",
	Description = "Bake AnimGraph-controlled animation to raw bone track data.",
	Group = "Animation",
	Icon = "personal_injury" )]
public class AnimParamsToBones : BlendModification
{
	public override bool CanStart( TrackListView trackList, TimeSelection selection )
	{
		return trackList.GetSkinnedModelRendererTracksWithParameters( selection.TotalTimeRange ).Any();
	}

	public override void Start( TrackListView trackList, TimeSelection selection )
	{
		var timeRange = selection.TotalTimeRange;
		var compiledTracks = new List<ICompiledPropertyTrack>();

		foreach ( var rendererTrackView in trackList.GetSkinnedModelRendererTracksWithParameters( timeRange ) )
		{
			if ( rendererTrackView.Target.Value is not SkinnedModelRenderer renderer ) continue;
			if ( renderer.Model is not { BoneCount: > 0 } ) continue;

			var compiledRootTrack = MovieClip.RootGameObject( rendererTrackView.Name, rendererTrackView.Parent!.Track.Id );
			var compiledRendererTrack = compiledRootTrack.Component<SkinnedModelRenderer>( rendererTrackView.Track.Id );

			var animParamTracks = rendererTrackView.GetAnimParamTracks()
				.Select( x => x.Track )
				.OfType<IProjectPropertyTrack>()
				.ToArray();

			var graph = renderer.Animation.Graph;

			// Trigger tracks are true for as long as a pulse was pending, so only trigger when one becomes true

			var activeTriggers = new HashSet<string>();

			var options = new BakeAnimationsOptions(
				SampleRate: EditMode.Project.SampleRate,
				ParentTrack: compiledRendererTrack,
				IncludeRootMotion: false,
				OnInitialize: model =>
				{
					model.UseAnimGraph = true;

					// Bake with the renderer's graph, it might not be the model's default

					if ( graph is not null )
					{
						model.Animation.Graph = graph;
					}

					ApplyAnimParamTracks( animParamTracks, activeTriggers, model, timeRange.Start );
				},
				OnUpdate: ( model, _, time ) =>
				{
					ApplyAnimParamTracks( animParamTracks, activeTriggers, model, timeRange.Start + time );
				}
			);

			var boneTracks = renderer.BakeAnimation( timeRange, options );

			compiledTracks.AddRange( boneTracks );
		}

		SetFromTracks( compiledTracks, timeRange, MovieTime.Zero, isAdditive: false );
	}

	private void ApplyAnimParamTracks( IEnumerable<IProjectPropertyTrack> tracks, HashSet<string> activeTriggers, SceneModel model, MovieTime time )
	{
		var parameters = model.Animation.Parameters;

		foreach ( var track in tracks )
		{
			if ( track.Parent?.TargetType == typeof( GraphTriggerAccessor ) )
			{
				if ( track is not ProjectPropertyTrack<bool> triggerTrack ) continue;

				if ( triggerTrack.TryGetValue( time, out var isPending ) && isPending )
				{
					if ( activeTriggers.Add( track.Name ) )
					{
						parameters.TryTrigger( track.Name );
					}
				}
				else
				{
					activeTriggers.Remove( track.Name );
				}

				continue;
			}

			if ( track.Parent?.TargetType == typeof( Sandbox.Animation.GraphParameters ) )
			{
				_ = track switch
				{
					ProjectPropertyTrack<bool> boolTrack when boolTrack.TryGetValue( time, out var value ) => parameters.TrySet( track.Name, value ),
					ProjectPropertyTrack<float> floatTrack when floatTrack.TryGetValue( time, out var value ) => parameters.TrySet( track.Name, value ),
					ProjectPropertyTrack<Vector3> vectorTrack when vectorTrack.TryGetValue( time, out var value ) => parameters.TrySet( track.Name, value ),
					ProjectPropertyTrack<string> stringTrack when stringTrack.TryGetValue( time, out var value ) => parameters.TrySet( track.Name, value ?? "" ),
					_ => false
				};

				continue;
			}

			if ( track is ProjectPropertyTrack<float> floatTrack2 && floatTrack2.TryGetValue( time, out var legacyValue ) )
			{
#pragma warning disable CS0618
				model.SetAnimParameter( track.Name, legacyValue );
#pragma warning restore CS0618
			}

			// TODO: other legacy types
		}
	}
}
