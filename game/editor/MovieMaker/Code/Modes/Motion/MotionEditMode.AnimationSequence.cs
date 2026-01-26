using Sandbox;
using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;

namespace Editor.MovieMaker;

#nullable enable

partial class MotionEditMode
{
	public void ImportAnimationSequence( SkinnedModelRenderer renderer, ProjectReferenceTrack<SkinnedModelRenderer> rendererTrack, TimeSelection timeSelection, Model sourceModel, string animName )
	{
		using var undoScope = Session.History.Push( $"Import Animation ({animName})" );

		var compiledRootTrack = MovieClip.RootGameObject( rendererTrack.Parent!.Name, rendererTrack.Parent!.Id );
		var compiledRendererTrack = compiledRootTrack.Component<SkinnedModelRenderer>( rendererTrack.Id );

		var options = new BakeAnimationsOptions(
			SampleRate: Project.SampleRate,
			ParentTrack: compiledRendererTrack,
			IncludeRootMotion: true,
			OnInitialize: model =>
			{
				model.UseAnimGraph = false;
				model.CurrentSequence.Name = animName;
			}
		);

		var compiledTracks = renderer.BakeAnimation( timeSelection.TotalTimeRange, options );

		SetModification<BlendModification>( timeSelection )
			.SetFromTracks( compiledTracks, timeSelection.TotalTimeRange, 0d, false );

		SelectionChanged();
		DisplayAction( "local_movies" );
	}
}
