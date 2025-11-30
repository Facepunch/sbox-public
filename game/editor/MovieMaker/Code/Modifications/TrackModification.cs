using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Sandbox.MovieMaker;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// Performs some kind of edit on a track given a time range selection.
/// </summary>
public interface ITrackModification;

public interface IModificationOptions;

public interface ITranslatableOptions : IModificationOptions
{
	MovieTime Offset { get; }

	ITranslatableOptions WithOffset( MovieTime offset );
}

public interface ITrackModification<T> : ITrackModification
{
	/// <summary>
	/// Performs the modification on a set of blocks from a track, returning the modified blocks.
	/// </summary>
	/// <param name="original">Blocks from the source track to modify.</param>
	/// <param name="selection">Time envelope to apply the modification to.</param>
	/// <param name="options">Modification-specific options.</param>
	IEnumerable<PropertyBlock<T>> Apply( IReadOnlyList<PropertyBlock<T>> original, TimeSelection selection, IModificationOptions options );
}

/// <inheritdoc cref="ITrackModification"/>
public interface ITrackModification<TValue, in TOptions> : ITrackModification<TValue>
	where TOptions : IModificationOptions
{
	IEnumerable<PropertyBlock<TValue>> Apply( IReadOnlyList<PropertyBlock<TValue>> original, TimeSelection selection,
		TOptions options );

	IEnumerable<PropertyBlock<TValue>> ITrackModification<TValue>.Apply( IReadOnlyList<PropertyBlock<TValue>> original,
		TimeSelection selection, IModificationOptions options ) =>
		Apply( original, selection, (TOptions)options );
}

/// <summary>
/// When added to a class that derives from <see cref="IMovieModification"/>, adds a button to the
/// toolbar that starts performing that modification when pressed.
/// </summary>
/// <param name="title"></param>
[AttributeUsage( AttributeTargets.Class )]
public sealed class MovieModificationAttribute( string title ) : Attribute
{
	/// <summary>
	/// Tooltip title text.
	/// </summary>
	public string Title { get; } = title;

	/// <summary>
	/// Button icon.
	/// </summary>
	public string Icon { get; init; } = "edit";

	/// <summary>
	/// Button group heading.
	/// </summary>
	public string? Group { get; init; }

	/// <summary>
	/// Tooltip description text.
	/// </summary>
	public string? Description { get; init; }

	/// <summary>
	/// Button sort order, defaults to <c>0</c>.
	/// </summary>
	public int Order { get; init; }
}

public record ModificationSnapshot( Type Type, IModificationOptions Options, ImmutableDictionary<Guid, ITrackModification>? Tracks = null );

/// <summary>
/// Describes an edit being made to one or more tracks in a movie.
/// </summary>
public interface IMovieModification
{
	/// <summary>
	/// Contains any state about this modification that we'd want to store in the undo stack.
	/// </summary>
	IModificationOptions Options { get; set; }

	bool HasChanges { get; }
	MovieTimeRange? SourceTimeRange { get; }

	bool CanStart( TrackListView trackList, TimeSelection selection );

	/// <summary>
	/// Called after this instance was created to perform any initialization.
	/// </summary>
	void Initialize( MotionEditMode editMode );

	/// <summary>
	/// Add any custom controls that modify <see cref="Options"/> to <paramref name="group"/>.
	/// </summary>
	void AddControls( ToolBarGroup group );

	/// <summary>
	/// Called when this modification's toolbar button was pressed.
	/// </summary>
	void Start( TrackListView trackList, TimeSelection selection );

	bool UpdatePreview( TimeSelection selection );
	bool UpdatePreview( TimeSelection selection, IProjectTrack track );
	void ClearPreview();

	bool Commit( TimeSelection selection );

	public ModificationSnapshot Snapshot() => new( GetType(), Options );

	public void Restore( ModificationSnapshot snapshot ) => Options = snapshot.Options;
}

/// <summary>
/// A <see cref="IMovieModification"/> with a particular option type <typeparamref name="T"/>.
/// </summary>
/// <typeparam name="T">Option type, contains any state about this modification that we'd want to store in the undo stack.</typeparam>
public interface IMovieModification<T> : IMovieModification
	where T : IModificationOptions
{
	/// <inheritdoc cref="IMovieModification.Options"/>
	new T Options { get; set; }

	IModificationOptions IMovieModification.Options
	{
		get => Options;
		set => Options = (T)value;
	}
}

/// <summary>
/// A <see cref="IMovieModification"/>
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="defaultOptions"></param>
/// <param name="autoCreate"></param>
public abstract class PerTrackModification<T>( T defaultOptions, bool autoCreate ) : IMovieModification<T>
	where T : IModificationOptions
{
	private Dictionary<IProjectPropertyTrack, ITrackModificationPreview> TrackPreviews { get; } = new();

	public MotionEditMode EditMode { get; private set; } = null!;

	private T _options = defaultOptions;
	private TimeSelection? _lastSelection;

	public T Options
	{
		get => _options;
		set
		{
			_options = value;

			if ( _lastSelection is { } selection )
			{
				UpdatePreview( selection );
			}
		}
	}

	public bool HasChanges => TrackPreviews.Values.Any( x => x.Modification is not null );

	public virtual MovieTimeRange? SourceTimeRange => null;

	public virtual bool CanStart( TrackListView trackList, TimeSelection selection ) =>
		trackList.EditablePropertyTracks.Any( x => x.Track is IProjectPropertyTrack );

	public void Initialize( MotionEditMode editMode )
	{
		EditMode = editMode;

		OnInitialize( editMode );
	}

	protected virtual void OnInitialize( MotionEditMode editMode ) { }

	public virtual void AddControls( ToolBarGroup group ) { }

	public virtual void Start( TrackListView trackList, TimeSelection selection ) { }

	protected ITrackModificationPreview? GetTrackModificationPreview( IProjectPropertyTrack track )
	{
		return TrackPreviews.GetValueOrDefault( track );
	}

	protected ITrackModificationPreview GetOrCreateTrackModificationPreview( IProjectPropertyTrack track )
	{
		if ( GetTrackModificationPreview( track ) is { } state ) return state;

		var type = typeof(TrackModificationPreview<>).MakeGenericType( track.TargetType );
		TrackPreviews.Add( track, state = (ITrackModificationPreview)Activator.CreateInstance( type, EditMode, track )! );

		return state;
	}

	public virtual bool UpdatePreview( TimeSelection selection )
	{
		var changed = false;

		if ( autoCreate )
		{
			foreach ( var view in EditMode.Session.TrackList.EditablePropertyTracks )
			{
				GetOrCreateTrackModificationPreview( (IProjectPropertyTrack)view.Track );
			}
		}

		foreach ( var (track, state) in TrackPreviews )
		{
			state.Modification ??= CreateModification( track );

			changed |= state.Update( selection, Options );
		}

		_lastSelection = selection;

		return changed;
	}

	public bool UpdatePreview( TimeSelection selection, IProjectTrack track )
	{
		if ( track is IProjectPropertyTrack propertyTrack && GetTrackModificationPreview( propertyTrack ) is { } preview )
		{
			preview.Update( selection, Options );
			return true;
		}

		return false;
	}

	public virtual void ClearPreview()
	{
		foreach ( var preview in TrackPreviews.Values )
		{
			preview.Clear();
		}

		TrackPreviews.Clear();

		_lastSelection = null;
	}

	protected ITrackModification? CreateModification( IProjectPropertyTrack track )
	{
		var method = GetType()
			.GetMethod( nameof(OnCreateModification), BindingFlags.NonPublic | BindingFlags.Instance )!
			.MakeGenericMethod( track.TargetType );

		return (ITrackModification?)method.Invoke( this, [track] );
	}

	protected virtual ITrackModification<TValue>? OnCreateModification<TValue>( IPropertyTrack<TValue> track ) => null;

	public virtual bool Commit( TimeSelection selection )
	{
		foreach ( var (_, preview) in TrackPreviews )
		{
			preview.Commit( selection, Options );
		}

		TrackPreviews.Clear();

		return true;
	}

	public ModificationSnapshot Snapshot() => new ( GetType(), Options,
		TrackPreviews
			.Where( x => x.Value.Modification is not null )
			.ToImmutableDictionary(
				x => x.Key.Id,
				x => x.Value.Modification! ) );

	public void Restore( ModificationSnapshot snapshot, MovieProject project )
	{
		Options = (T)snapshot.Options;

		ClearPreview();

		if ( snapshot.Tracks is not { } tracks ) return;

		foreach ( var (id, modification) in tracks )
		{
			if ( project.GetTrack( id ) is not { } track ) continue;
			if ( track is not IProjectPropertyTrack propertyTrack ) continue;

			GetOrCreateTrackModificationPreview( propertyTrack ).Modification = modification;
		}
	}
}
