using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Watches some object or property in the scene, capturing
/// its state whenever <see cref="Capture"/> is called.
/// </summary>
public interface IMovieTrackRecorder
{
	/// <summary>
	/// Describes the track this recorder is recording to.
	/// </summary>
	ITrack Track { get; }

	/// <summary>
	/// Currently recorded data for this track.
	/// </summary>
	IEnumerable<IPropertyBlock> Blocks { get; }

	/// <summary>
	/// Gets or creates a recorder for the named sub-property.
	/// </summary>
	/// <param name="name">Property name.</param>
	IMovieTrackRecorder Property( string name );

	/// <summary>
	/// Write the current state of the recorded object or property to the owning <see cref="MovieRecorder"/>.
	/// </summary>
	void Capture();

	/// <summary>
	/// Compiles captured values for this property and all sub-properties into tracks.
	/// </summary>
	IEnumerable<ICompiledTrack> Compile( MovieTimeRange timeRange );
}

internal interface IMovieTrackRecorderInternal : IMovieTrackRecorder
{
	MovieRecorder MovieRecorder { get; }
	new ICompiledTrack Track { get; }

	/// <summary>
	/// Binder targeting the prefab source of this object (if it's a prefab instance), so
	/// we can find the default values of each track.
	/// </summary>
	TrackBinder? PrefabSourceBinder { get; }

	/// <summary>
	/// A recorded <see cref="BindingReference{T}"/> refers to this track, so it must be compiled even if nothing in
	/// it was recorded.
	/// </summary>
	void MarkReferenced();

	ITrack IMovieTrackRecorder.Track => Track;
}

file sealed class UnknownTrackRecorder : IMovieTrackRecorder
{
	public static UnknownTrackRecorder Instance { get; } = new();

	public ITrack Track { get; } = new UnknownTrack();
	public IEnumerable<IPropertyBlock> Blocks => [];
	public IMovieTrackRecorder Property( string name ) => Instance;

	public void Capture() { }

	public IEnumerable<ICompiledTrack> Compile( MovieTimeRange timeRange ) => [];
}

file sealed class UnknownTrack : ITrack
{
	public string Name => "Unknown";
	public Type TargetType => typeof( Unknown );
	public ITrack? Parent => null;
}

internal abstract class MovieTrackRecorder<TTrack, TTarget> : IMovieTrackRecorderInternal
	where TTrack : ICompiledTrack
	where TTarget : ITrackTarget
{
	private readonly List<IMovieTrackRecorder> _children = new();
	private readonly Dictionary<string, IMovieTrackRecorder> _properties = new();

	private MovieTime _lastRecordedFrame = MovieTime.MinValue;

	public IEnumerable<IMovieTrackRecorder> Children => _children;

	public MovieRecorder MovieRecorder { get; }
	public IMovieTrackRecorderInternal? Parent { get; }
	public TTrack Track { get; }
	public virtual TrackBinder? PrefabSourceBinder => Parent?.PrefabSourceBinder;

	public TTarget Target { get; }
	public string Name => Track.Name;

	protected MovieTrackRecorder( MovieRecorder recorder, IMovieTrackRecorderInternal? parent, TTrack track )
	{
		MovieRecorder = recorder;
		Parent = parent;
		Track = track;
		Target = (TTarget)recorder.Binder.Get( track );
	}

	protected void AddChild( IMovieTrackRecorderInternal child )
	{
		_children.Add( child );
	}

	private bool _isReferenced;

	public void MarkReferenced() => _isReferenced = true;

	public virtual IEnumerable<IPropertyBlock> Blocks => [];

	public IMovieTrackRecorder Property( string name )
	{
		if ( _properties.TryGetValue( name, out var recorder ) ) return recorder;

		var target = TrackProperty.Create( Target, name );

		if ( target is null )
		{
			return UnknownTrackRecorder.Instance;
		}

		IMovieTrackRecorderInternal property;

		if ( target.Name == nameof( GameObject.Enabled ) && target.TargetType == typeof( bool ) && this is MovieGameObjectTrackRecorder or IMovieComponentTrackRecorder )
		{
			// Enabled tracks have their own special behaviour, so get special recorders.

			property = new MovieEnabledPropertyTrackRecorder( this );
		}
		else
		{
			var recorderType = typeof( MoviePropertyTrackRecorder<> ).MakeGenericType( target.TargetType );
			property = (IMovieTrackRecorderInternal)Activator.CreateInstance( recorderType, this, name )!;
		}

		AddChild( property );

		return _properties[name] = property;
	}

	public void Capture()
	{
		var time = MovieRecorder.Time;

		if ( time <= _lastRecordedFrame ) return;

		_lastRecordedFrame = time;

		if ( Target.IsActive )
		{
			OnCapture();
		}
		else
		{
			OnSkip();
		}
	}

	/// <summary>
	/// Called when target is active, and time has progressed since the last capture / skip.
	/// </summary>
	protected abstract void OnCapture();

	/// <summary>
	/// Called when target isn't active (disabled / destroyed), but time has progressed since the last capture / skip.
	/// </summary>
	protected virtual void OnSkip() { }

	protected virtual ICompiledTrack? OnCompile( MovieTimeRange timeRange ) => Track;

	public IEnumerable<ICompiledTrack> Compile( MovieTimeRange timeRange )
	{
		var compiled = OnCompile( timeRange );
		var children = _children
			.SelectMany( x => x.Compile( timeRange ) )
			.ToArray();

		if ( compiled is null )
		{
			return children;
		}

		if ( ReferenceEquals( compiled, Track ) && children.Length == 0 && !_isReferenced )
		{
			// This track and its children have no data for this time range, and nothing references it

			return [];
		}

		return [compiled, .. children];
	}

	ICompiledTrack IMovieTrackRecorderInternal.Track => Track;
}
