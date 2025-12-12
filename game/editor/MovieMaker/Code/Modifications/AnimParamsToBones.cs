using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
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

			var animParamTracks = rendererTrackView.Find( nameof( SkinnedModelRenderer.Parameters ) )
				?.Children
				.Select( x => x.Track )
				.OfType<IProjectPropertyTrack>()
				.ToArray() ?? [];

			var options = new BakeAnimationsOptions(
				SampleRate: EditMode.Project.SampleRate,
				ParentTrack: compiledRendererTrack,
				IncludeRootMotion: false,
				OnInitialize: model =>
				{
					model.UseAnimGraph = true;

					ApplyAnimParamTracks( animParamTracks, model, timeRange.Start );
				},
				OnUpdate: ( model, _, time ) =>
				{
					ApplyAnimParamTracks( animParamTracks, model, timeRange.Start + time );
				}
			);

			var boneTracks = renderer.BakeAnimation( timeRange, options );

			compiledTracks.AddRange( boneTracks );
		}

		SetFromTracks( compiledTracks, timeRange, MovieTime.Zero, isAdditive: false );
	}

	private void ApplyAnimParamTracks( IEnumerable<IProjectPropertyTrack> tracks, SceneModel model, MovieTime time )
	{
		foreach ( var track in tracks )
		{
			if ( track is ProjectPropertyTrack<float> floatTrack && floatTrack.TryGetValue( time, out var value ) )
			{
				model.SetAnimParameter( track.Name, value );
			}

			// TODO: other types
		}
	}
}
