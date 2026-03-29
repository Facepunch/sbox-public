using System.Runtime.CompilerServices;
using Sandbox.MovieMaker.Compiled;
using Sandbox.Utility;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// <para>
/// Records properties in a scene to tracks ready for use in a <see cref="MoviePlayer"/>. You can use this for in-game demo recording
/// of a whole scene, or only specific properties, configured using <see cref="MovieRecorderOptions"/>.
/// </para>
/// <para>
/// You can manually call <see cref="Advance"/> to move the recording time along, then <see cref="Capture"/> to write all recorded properties
/// to tracks. Alternatively, call <see cref="Start"/> to automatically advance and capture every fixed update, and <see cref="Stop"/> to finish recording.
/// </para>
/// <para>
/// Convert the recording to a <see cref="MovieClip"/> by calling <see cref="ToClip"/>. This clip can then be
/// played back immediately, or serialized to later use.
/// </para>
/// </summary>
public sealed partial class MovieRecorder
{
	/// <summary>
	/// Configuration deciding which properties are captured, and at what sample rate.
	/// </summary>
	public MovieRecorderOptions Options { get; }

	/// <summary>
	/// Maps tracks to objects and properties in the scene.
	/// </summary>
	public TrackBinder Binder { get; }

	/// <summary>
	/// Scene we're recording. Will match <see cref="TrackBinder.Scene"/>.
	/// </summary>
	public Scene Scene => Binder.Scene;

	private MovieTime? _firstCaptureTime;

	private readonly HashSet<IMovieTrackRecorder> _recordedThisFrame = new();

	/// <summary>
	/// Recorded time range, spanning from the first capture to the current value of <see cref="Time"/>.
	/// </summary>
	public MovieTimeRange TimeRange => (_firstCaptureTime ?? default, Time);

	private List<MovieGameObjectTrackRecorder> RootTrackRecorders { get; } = new();

	private Dictionary<Type, IReadOnlyList<IComponentCapturer>> CapturerCache { get; } = new();

	private ConditionalWeakTable<GameObject, MovieGameObjectTrackRecorder?> GameObjectTrackRecorderCache { get; } = new();
	private ConditionalWeakTable<Component, IMovieComponentTrackRecorder?> ComponentTrackRecorderCache { get; } = new();

	/// <summary>
	/// Which <see cref="IMovieTrackRecorder"/>s recorded anything during the last call to <see cref="Capture"/>.
	/// </summary>
	public IEnumerable<IMovieTrackRecorder> RecordedThisFrame => _recordedThisFrame;

	/// <summary>
	/// Current recording time, increased by calling <see cref="Advance"/>.
	/// </summary>
	public MovieTime Time { get; private set; }

	internal MovieTime LastCaptureTime { get; private set; }

	internal event Action<MovieRecorder>? Stopped;

	/// <summary>
	/// Create a new <see cref="MovieRecorder"/>, recording the given <paramref name="scene"/> with the given <paramref name="options"/>.
	/// </summary>
	/// <param name="scene">Scene to record.</param>
	/// <param name="options">Optional configuration, defaults to <see cref="MovieRecorderOptions.Default"/>.</param>
	public MovieRecorder( Scene scene, MovieRecorderOptions? options = null )
		: this( new TrackBinder( scene ), options )
	{

	}

	/// <summary>
	/// Create a new <see cref="MovieRecorder"/> with the given <paramref name="binder"/> and <paramref name="options"/>.
	/// </summary>
	/// <param name="binder">Binder to map tracks to objects in a scene.</param>
	/// <param name="options">Optional configuration, defaults to <see cref="MovieRecorderOptions.Default"/>.</param>
	public MovieRecorder( TrackBinder binder, MovieRecorderOptions? options = null )
	{
		Binder = binder;
		Options = options ?? MovieRecorderOptions.Default;
	}

	/// <summary>
	/// Gets a <see cref="IMovieTrackRecorder"/> for the given <paramref name="gameObject"/>, creating one if it doesn't
	/// exist. If <see cref="MovieRecorderOptions.Filters"/> reject this game object, returns null instead.
	/// </summary>
	/// <param name="gameObject">Object in the scene to record.</param>
	public IMovieTrackRecorder? GetTrackRecorder( GameObject? gameObject ) => GetGameObjectTrackRecorderInternal( gameObject );

	public IMovieTrackRecorder? GetTrackRecorder( IValid? gameObjectOrComponent )
	{
		return gameObjectOrComponent switch
		{
			GameObject go => GetTrackRecorder( go ),
			Component cmp => GetTrackRecorder( cmp ),
			_ => null
		};
	}

	private MovieGameObjectTrackRecorder? GetGameObjectTrackRecorderInternal( GameObject? gameObject )
	{
		// Don't record invalid stuff!

		if ( !gameObject.IsValid() || gameObject.IsDestroyed ) return null;

		// Must be in this scene!

		if ( gameObject.Scene != Scene ) return null;

		if ( GameObjectTrackRecorderCache.TryGetValue( gameObject, out var recorder ) ) return recorder;

		// Don't record scenes!

		if ( gameObject is Scene ) return null;

		// Don't record hidden objects!

		if ( (gameObject.Flags & GameObjectFlags.Hidden) != 0 ) return null;

		// Check filters

		foreach ( var filter in Options.Filters )
		{
			if ( !filter( gameObject ) )
			{
				GameObjectTrackRecorderCache.AddOrUpdate( gameObject, null );
				return null;
			}
		}

		if ( gameObject.Parent is not Sandbox.Scene and not null )
		{
			var parentTrack = GetGameObjectTrackRecorderInternal( gameObject.Parent );

			// If parent isn't recordable, don't record this object either!

			recorder = parentTrack?.Child( gameObject );
			GameObjectTrackRecorderCache.AddOrUpdate( gameObject, recorder );

			return recorder;
		}

		// Look for a recorder that currently targets this object, or is unbound and can target it

		recorder = RootTrackRecorders.FirstOrDefault( x => x.CanTarget( gameObject ) );

		if ( recorder is null )
		{
			// Create a new root recorder for this GameObject

			var track = MovieClip.RootGameObject( gameObject.Name, metadata: new TrackMetadata( gameObject.Id, gameObject.PrefabInstanceSource ) );

			recorder = new MovieGameObjectTrackRecorder( this, track );

			RootTrackRecorders.Add( recorder );
		}

		recorder.Target.Bind( gameObject );
		GameObjectTrackRecorderCache.AddOrUpdate( gameObject, recorder );

		return recorder;
	}

	/// <summary>
	/// <para>
	/// Gets a <see cref="IMovieTrackRecorder"/> for the given <paramref name="component"/>, creating one if it doesn't
	/// exist. If <see cref="MovieRecorderOptions.Filters"/> reject the component's game object, returns null instead.
	/// </para>
	/// <para>
	///	Calling <see cref="Capture"/> on the returned recorder will use <see cref="IComponentCapturer"/>s to decide
	/// which properties to capture. These handlers are configured using <see cref="MovieRecorderOptions.ComponentCapturers"/>.
	/// </para>
	/// </summary>
	/// <param name="component">Component in the scene to record.</param>
	public IMovieTrackRecorder? GetTrackRecorder( Component? component )
	{
		// Don't record invalid stuff!

		if ( !component.IsValid() ) return null;

		if ( ComponentTrackRecorderCache.TryGetValue( component, out var recorder ) ) return recorder;

		// Don't record hidden components!

		if ( (component.Flags & ComponentFlags.Hidden) != 0 ) return null;

		recorder = GetGameObjectTrackRecorderInternal( component.GameObject )?.Component( component );

		ComponentTrackRecorderCache.AddOrUpdate( component, recorder );

		return recorder;
	}

	/// <summary>
	/// Gets a <see cref="IMovieTrackRecorder"/> for the given <paramref name="track"/>, creating one if it doesn't
	/// exist. If <see cref="MovieRecorderOptions.Filters"/> reject whatever game object the track is bound to,
	/// returns null instead.
	/// </summary>
	/// <param name="track">Track to record.</param>
	public IMovieTrackRecorder? GetTrackRecorder( ITrack track )
	{
		if ( track.Parent is not { } parentTrack )
		{
			// Expecting root tracks to be GameObject references

			if ( track is not IReferenceTrack<GameObject> refTrack ) return null;

			// Look for an existing recorder

			var recorder = RootTrackRecorders.FirstOrDefault( x => x.Track.Id == refTrack.Id );

			if ( recorder is not null ) return recorder;

			// Create a new root recorder for this GameObject

			var rootTrack = MovieClip.RootGameObject( refTrack.Name, refTrack.Id, metadata: refTrack.Metadata );

			recorder = new MovieGameObjectTrackRecorder( this, rootTrack );

			RootTrackRecorders.Add( recorder );

			return recorder;
		}

		var parentRecorder = GetTrackRecorder( parentTrack );

		if ( parentRecorder is null ) return null;

		if ( track is IPropertyTrack propertyTrack )
		{
			return parentRecorder.Property( propertyTrack.Name );
		}

		// Child GameObject or Component references must have a GameObject parent

		if ( parentRecorder is not MovieGameObjectTrackRecorder parentGoRecorder ) return null;

		return track switch
		{
			IReferenceTrack<GameObject> refTrack => parentGoRecorder.Child( refTrack ),
			IReferenceTrack refTrack => parentGoRecorder.Component( refTrack ),
			_ => null
		};
	}

	/// <summary>
	/// Starts recording the scene.
	/// Stop recording by calling <see cref="Stop"/>, or disposing the returned object.
	/// Recording will automatically stop when the recorded scene is being destroyed.
	/// </summary>
	public IDisposable Start()
	{
		return MovieRecorderSystem.Current.Start( this );
	}

	/// <summary>
	/// Stop recording the scene. Does nothing if you haven't called <see cref="Start"/>.
	/// </summary>
	public void Stop()
	{
		if ( MovieRecorderSystem.Current.Stop( this ) )
		{
			Stopped?.Invoke( this );
		}
	}

	/// <summary>
	/// Moves recording ahead by the given <paramref name="deltaTime"/>.
	/// This will happen automatically if you've called <see cref="Start"/>.
	/// </summary>
	public void Advance( MovieTime deltaTime )
	{
		ArgumentOutOfRangeException.ThrowIfLessThan( deltaTime, MovieTime.Zero );

		Time += deltaTime;
		_recordedThisFrame.Clear();
	}

	/// <summary>
	/// Runs all actions in <see cref="MovieRecorderOptions.CaptureActions"/>.
	/// This will happen automatically if you've called <see cref="Start"/>.
	/// </summary>
	public void Capture()
	{
		_firstCaptureTime ??= Time;

		foreach ( var action in Options.CaptureActions )
		{
			action.Invoke( this );
		}

		LastCaptureTime = Time;
	}

	internal void PropertyCaptured( IMovieTrackRecorder recorder )
	{
		_recordedThisFrame.Add( recorder );
	}

	/// <summary>
	/// Convert the current recording to a <see cref="MovieClip"/> that can be serialized or played back.
	/// </summary>
	public MovieClip ToClip() => MovieClip.FromTracks( RootTrackRecorders.SelectMany( x => x.Compile() ) );

	/// <summary>
	/// Convert the current recording to a <see cref="IMovieResource"/> that can be saved as a .movie asset.
	/// </summary>
	public IMovieResource ToResource() => ToClip().ToResource();

	internal IReadOnlyList<IComponentCapturer> GetCapturers<T>() => GetCapturers( typeof( T ) );

	internal IReadOnlyList<IComponentCapturer> GetCapturers( Type componentType )
	{
		if ( CapturerCache.TryGetValue( componentType, out var recorders ) )
		{
			return recorders;
		}

		return CapturerCache[componentType] =
		[
			..Options.ComponentCapturers.Where( x => x.SupportsType( componentType ) )
		];
	}
}

/// <summary>
/// Ticks all <see cref="MovieRecorder"/>s for the current scene.
/// </summary>
internal sealed class MovieRecorderSystem : GameObjectSystem<MovieRecorderSystem>
{
	private readonly HashSet<MovieRecorder> _activeRecorders = new();

	/// <summary>
	/// If true, only the host or editor sessions can use the <c>movie</c> command.
	/// </summary>
	[Property]
	public bool DisableClientRecording { get; set; }

	public bool CanUseMovieCommand => !DisableClientRecording || Game.IsEditor || Networking.IsHost;

	public MovieRecorderSystem( Scene scene ) : base( scene )
	{
		Listen( Stage.FinishFixedUpdate, 1000, () =>
		{
			foreach ( var recorder in _activeRecorders )
			{
				recorder.Advance( scene.FixedDelta );
				recorder.Capture();
			}
		}, "CaptureMovieRecorders" );
	}

	public IDisposable Start( MovieRecorder recorder )
	{
		if ( _activeRecorders.Add( recorder ) )
		{
			recorder.Capture();
		}

		return new DisposeAction( recorder.Stop );
	}

	public bool Stop( MovieRecorder recorder )
	{
		return _activeRecorders.Remove( recorder );
	}

	public override void Dispose()
	{
		foreach ( var recorder in _activeRecorders.ToArray() )
		{
			recorder.Stop();
		}

		base.Dispose();
	}
}
