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
/// to tracks. Alternatively, call <see cref="Start"/> to automatically advance and capture every update, and <see cref="Stop"/> to finish recording.
/// </para>
/// <para>
/// Convert the recording to a <see cref="MovieClip"/> by calling <see cref="ToClip()"/>. This clip can then be
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
	/// <see cref="GameObject.Enabled"/> / <see cref="Component.Enabled"/> tracks left out until their target is seen disabled.
	/// </summary>
	private readonly List<MovieEnabledPropertyTrackRecorder> _omittedEnabledTracks = new();

	/// <summary>
	/// Ids of objects / components that existed when recording started, and whether they were
	/// <see cref="GameObject.Enabled"/> / <see cref="Component.Enabled"/> themselves. Capture actions usually only
	/// find active objects, so we can't tell from first capture whether something started disabled or was created.
	/// </summary>
	private readonly Dictionary<Guid, bool> _enabledAtStart = new();

	/// <summary>
	/// Recorded time range, spanning from the first capture to the current value of <see cref="Time"/>.
	/// </summary>
	public MovieTimeRange TimeRange => Options.BufferDuration is { } duration
		? (MovieTime.Max( _firstCaptureTime ?? default, Time - duration ), Time)
		: (_firstCaptureTime ?? default, Time);

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
	/// exist. If <see cref="MovieRecorderOptions.Filters"/> reject this game object, returns null instead. Will use
	/// <paramref name="gameObject"/>'s <see cref="GameObject.Name"/> as the track name.
	/// </summary>
	/// <param name="gameObject">Object in the scene to record.</param>
	public IMovieTrackRecorder? GetTrackRecorder( GameObject? gameObject ) => GetGameObjectTrackRecorderInternal( gameObject, null );

	/// <summary>
	/// Gets a <see cref="IMovieTrackRecorder"/> for the given <paramref name="gameObject"/>, creating one if it doesn't
	/// exist. If <see cref="MovieRecorderOptions.Filters"/> reject this game object, returns null instead. Will name
	/// the created track <paramref name="trackName"/>.
	/// </summary>
	/// <param name="gameObject">Object in the scene to record.</param>
	/// <param name="trackName">Name to use for the recorded track.</param>
	public IMovieTrackRecorder? GetTrackRecorder( GameObject? gameObject, string trackName ) => GetGameObjectTrackRecorderInternal( gameObject, trackName );

	public IMovieTrackRecorder? GetTrackRecorder( IValid? gameObjectOrComponent )
	{
		return gameObjectOrComponent switch
		{
			GameObject go => GetTrackRecorder( go ),
			Component cmp => GetTrackRecorder( cmp ),
			_ => null
		};
	}

	/// <summary>
	/// The object that <paramref name="gameObject"/>'s track is recorded under, or <see langword="null"/> for a root track.
	/// This is its parent, unless that's hidden: hidden objects aren't recorded, but things get parented to them at
	/// runtime, like impact decals stuck to a compiled map's merged world geometry. These are recorded under the
	/// nearest ancestor that isn't hidden, with their transforms relative to that.
	/// </summary>
	internal static GameObject? GetRecordedParent( GameObject gameObject )
	{
		var parent = gameObject.Parent;

		while ( parent is not null and not Sandbox.Scene && (parent.Flags & GameObjectFlags.Hidden) != 0 )
		{
			parent = parent.Parent;
		}

		return parent is Sandbox.Scene ? null : parent;
	}

	private MovieGameObjectTrackRecorder? GetGameObjectTrackRecorderInternal( GameObject? gameObject, string? trackName )
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

		if ( GetRecordedParent( gameObject ) is { } parent )
		{
			var parentTrack = GetGameObjectTrackRecorderInternal( parent, null );

			// If parent isn't recordable, don't record this object either!

			recorder = parentTrack?.Child( gameObject, trackName );
			GameObjectTrackRecorderCache.AddOrUpdate( gameObject, recorder );

			return recorder;
		}

		// Look for a recorder that currently targets this object, or is unbound and can target it

		recorder = RootTrackRecorders.FirstOrDefault( x => x.CanTarget( gameObject, trackName ) );

		if ( recorder is null )
		{
			// Create a new root recorder for this GameObject

			var metadata = new TrackMetadata(
				ReferenceId: gameObject.Id,
				PrefabSource: gameObject.PrefabInstanceSource,
				Order: 1_000 + RootTrackRecorders.Count,
				Flags: MovieGameObjectTrackRecorder.GetFlags( gameObject ) );

			var track = MovieClip.RootGameObject( trackName ?? gameObject.Name, metadata: metadata );

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

		recorder = GetGameObjectTrackRecorderInternal( component.GameObject, null )?.Component( component );

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
		if ( _firstCaptureTime is null )
		{
			_firstCaptureTime = Time;
			FindEnabledAtStart();
		}

		foreach ( var action in Options.CaptureActions )
		{
			action.Invoke( this );
		}

		// Capture actions usually only find active objects, so look for omitted Enabled tracks becoming disabled ourselves

		_omittedEnabledTracks.RemoveAll( x => !x.UpdateOmitted() );

		LastCaptureTime = Time;
	}

	internal void PropertyCaptured( IMovieTrackRecorder recorder )
	{
		_recordedThisFrame.Add( recorder );
	}

	internal void EnabledTrackOmitted( MovieEnabledPropertyTrackRecorder recorder )
	{
		_omittedEnabledTracks.Add( recorder );
	}

	private void FindEnabledAtStart()
	{
		foreach ( var go in Scene.GetAllObjects( false ) )
		{
			if ( go is Scene ) continue;

			_enabledAtStart[go.Id] = go.Enabled;

			foreach ( var component in go.Components.GetAll() )
			{
				_enabledAtStart[component.Id] = component.Enabled;
			}
		}
	}

	/// <summary>
	/// True if <paramref name="target"/> existed when recording started, and was enabled itself, ignoring its ancestors.
	/// </summary>
	internal bool WasEnabledAtStart( IValid target ) =>
		GetId( target ) is { } id && _enabledAtStart.GetValueOrDefault( id );

	/// <summary>
	/// True if <paramref name="target"/> existed when recording started. The scene always did.
	/// </summary>
	internal bool ExistedAtStart( IValid target ) =>
		target is Scene || GetId( target ) is { } id && _enabledAtStart.ContainsKey( id );

	private static Guid? GetId( IValid target ) => target switch
	{
		GameObject go => go.Id,
		Component component => component.Id,
		_ => null
	};

	/// <summary>
	/// Convert the current recording to a <see cref="MovieClip"/> that can be serialized or played back.
	/// </summary>
	public MovieClip ToClip()
	{
		var startTime = Options.BufferDuration is { } duration
			? MovieTime.Max( MovieTime.Zero, Time - duration )
			: MovieTime.Zero;

		return ToClip( (startTime, Time) );
	}

	public MovieClip ToClip( MovieTimeRange timeRange )
	{
		return MovieClip.FromTracks( RootTrackRecorders.SelectMany( x => x.Compile( timeRange ) ) );
	}

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
[Title( "Movie Recorder" )]
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
		// Capture every update, after components have updated, but before anim graphs are evaluated and reset any
		// auto-reset parameters. Values set for a single update (like b_jump) are still seen, and kept until the
		// next sample by MoviePropertyTrackRecorder.

		Listen( Stage.UpdateBones, -500, () =>
		{
			foreach ( var recorder in _activeRecorders )
			{
				recorder.Advance( Time.Delta );
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
