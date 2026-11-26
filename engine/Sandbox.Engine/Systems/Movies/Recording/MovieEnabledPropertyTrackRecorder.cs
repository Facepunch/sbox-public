namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// We use <see cref="GameObject.Enabled"/> tracks to show or hide objects, and it
/// defaults to <see langword="false"/> outside of property blocks. This means we have
/// to record it slightly differently.
/// </summary>
internal sealed class MovieEnabledPropertyTrackRecorder : MoviePropertyTrackRecorder<bool>
{
	public MovieEnabledPropertyTrackRecorder( IMovieTrackRecorderInternal parent )
		: base( parent, nameof( GameObject.Enabled ) )
	{

	}

	/// <summary>
	/// We use Enabled to represent objects / components being destroyed / created,
	/// so we always include it for normal objects. We don't need to do that for map
	/// objects, or objects flagged with <see cref="GameObjectFlags.DontDestroyOnLoad"/>
	/// / <see cref="GameObjectFlags.Static"/>.
	/// </summary>
	private static bool AlwaysIncludeEnabledTrack( IMovieTrackRecorder parentRecorder )
	{
		if ( parentRecorder is MovieGameObjectTrackRecorder goRecorder )
		{
			return AlwaysIncludeEnabledTrack( goRecorder.Target.Value );
		}

		if ( parentRecorder is IMovieComponentTrackRecorder cmpRecorder )
		{
			return AlwaysIncludeEnabledTrack( ((Component?)cmpRecorder.Target.Value)?.GameObject );
		}

		return true;
	}

	private static bool AlwaysIncludeEnabledTrack( GameObject? go )
	{
		if ( go is null ) return true;

		if ( (go.Flags & GameObjectFlags.Static) != 0 ) return false;
		if ( (go.Flags & GameObjectFlags.DontDestroyOnLoad) != 0 ) return false;
		if ( go.GetComponentInParent<MapInstance>() is not null ) return false;

		return true;
	}

	protected override bool TryFindDefaultValue( out bool value )
	{
		value = true;

		if ( AlwaysIncludeEnabledTrack( Parent! ) )
		{
			return false;
		}

		// Started disabled, or created part way through, so needs to be hidden until it was first captured

		if ( Target.Parent.Value is IValid target && !CanOmit( target ) )
		{
			return false;
		}

		MovieRecorder.EnabledTrackOmitted( this );

		return true;
	}

	private bool CanOmit( IValid target )
	{
		// Enabled when recording started, so visible from the start anyway

		if ( MovieRecorder.WasEnabledAtStart( target ) ) return true;

		// Disabled when recording started

		if ( MovieRecorder.ExistedAtStart( target ) ) return false;

		// Created part way through: only the outermost created object needs an Enabled track to hide it,
		// so check if whatever contains ours was created part way through too

		var container = target switch
		{
			Component component => component.GameObject,
			GameObject go => MovieRecorder.GetRecordedParent( go ),
			_ => null
		};

		return container is not null && !MovieRecorder.ExistedAtStart( container );
	}

	protected override void OnSkip()
	{
		if ( IsDefaultValue && !Target.Value && !IsContainerDestroyed )
		{
			MarkChanged();
		}

		base.OnSkip();
	}

	/// <summary>
	/// Destroyed objects read as disabled, but destroying an object destroys everything inside it. Only the
	/// outermost destroyed object needs an Enabled track to hide it, so check if whatever contains ours is destroyed.
	/// </summary>
	private bool IsContainerDestroyed => Target.Parent.Parent is { IsBound: false };

	/// <summary>
	/// Skips capturing if the target is inactive, in case nothing else captured it this frame.
	/// Returns true if the track is still being left out.
	/// </summary>
	internal bool UpdateOmitted()
	{
		// Only skip: capturing an active target is up to MovieRecorderOptions.CaptureActions

		if ( !Target.IsActive )
		{
			Capture();
		}

		return IsDefaultValue;
	}
}
