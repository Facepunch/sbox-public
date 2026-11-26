using Sandbox.MovieMaker.Compiled;
using Sandbox.MovieMaker.Properties;

namespace Sandbox.MovieMaker;

#nullable enable

internal sealed class MovieGameObjectTrackRecorder : MovieTrackRecorder<CompiledReferenceTrack<GameObject>, ITrackReference<GameObject>>
{
	private bool _firstCapture = true;
	private TrackBinder? _prefabSourceBinder;
	private readonly string _rootName;

	public override TrackBinder? PrefabSourceBinder => _prefabSourceBinder ?? base.PrefabSourceBinder;

	public MovieGameObjectTrackRecorder( MovieRecorder recorder, CompiledReferenceTrack<GameObject> track )
		: base( recorder, null, track )
	{
		_rootName = GetRootName();
	}

	public MovieGameObjectTrackRecorder( MovieGameObjectTrackRecorder parent, CompiledReferenceTrack<GameObject> track )
		: base( parent.MovieRecorder, parent, track )
	{
		_rootName = GetRootName();
	}

	private string GetRootName()
	{
		var name = Track.Name;

		if ( name.Length < 4 ) return name;
		if ( name[^1] != ')' ) return name;

		var bracketIndex = name.LastIndexOf( '(' );

		if ( bracketIndex < 2 ) return name;
		if ( name[bracketIndex - 1] != ' ' ) return name;

		var indexSpan = name.AsSpan( bracketIndex + 1, name.Length - bracketIndex - 2 );

		if ( !int.TryParse( indexSpan, out _ ) ) return name;

		return name.Substring( 0, bracketIndex - 1 );
	}

	private bool MatchesRootName( string name )
	{
		if ( name == _rootName ) return true;

		if ( name.Length < 4 ) return false;
		if ( name[^1] != ')' ) return false;

		var bracketIndex = name.LastIndexOf( '(' );

		if ( bracketIndex < 2 ) return false;
		if ( name[bracketIndex - 1] != ' ' ) return false;

		return name.AsSpan( 0, bracketIndex - 1 ).Equals( _rootName, StringComparison.Ordinal );
	}

	public bool CanTarget( GameObject gameObject, string? trackName )
	{
		if ( Target.IsBound )
		{
			return Target.Value == gameObject;
		}

		var prefabSource = gameObject.IsPrefabInstanceRoot ? gameObject.PrefabInstanceSource : null;

		return Track.Metadata?.PrefabSource == prefabSource && MatchesRootName( trackName ?? gameObject.Name );
	}

	public MovieGameObjectTrackRecorder Child( IReferenceTrack<GameObject> track )
	{
		Assert.AreEqual( Track.Id, track.Parent?.Id, "Expecting the given track to be a child of this recorder's track." );

		var recorder = Children
			.OfType<MovieGameObjectTrackRecorder>()
			.FirstOrDefault( x => x.Track.Id == track.Id );

		if ( recorder is not null ) return recorder;

		var compiledTrack = Track.GameObject( track.Name, track.Id, track.Metadata );

		recorder = new MovieGameObjectTrackRecorder( this, compiledTrack );

		AddChild( recorder );

		return recorder;
	}

	public MovieGameObjectTrackRecorder Child( GameObject gameObject, string? trackName = null )
	{
		Assert.True( MovieRecorder.GetRecordedParent( gameObject ) == Target.Value );

		// Look for a recorder that currently targets this object, or is unbound and can target it

		var recorder = Children
			.OfType<MovieGameObjectTrackRecorder>()
			.FirstOrDefault( x => x.CanTarget( gameObject, trackName ) );

		if ( recorder is null )
		{
			// Create a new child recorder for this GameObject

			var metadata = new TrackMetadata(
				ReferenceId: gameObject.Id,
				PrefabSource: gameObject.IsPrefabInstanceRoot ? gameObject.PrefabInstanceSource : null,
				Order: 1_000 + Children.OfType<MovieGameObjectTrackRecorder>().Count(),
				Flags: GetFlags( gameObject ) );

			var track = Track.GameObject( trackName ?? gameObject.Name, metadata: metadata );

			recorder = new MovieGameObjectTrackRecorder( this, track );

			AddChild( recorder );
		}

		recorder.Target.Bind( gameObject );

		return recorder;
	}

	public IMovieComponentTrackRecorder Component( IReferenceTrack track )
	{
		Assert.AreEqual( Track.Id, track.Parent?.Id, "Expecting the given track to be a child of this recorder's track." );
		Assert.True( track.TargetType.IsAssignableTo( typeof( Component ) ), "Expected track to be a component type." );

		var recorder = Children
			.OfType<IMovieComponentTrackRecorder>()
			.FirstOrDefault( x => x.Track is ICompiledReferenceTrack { Id: var id } && id == track.Id );

		if ( recorder is not null ) return recorder;

		var compiledTrack = Track.Component( track.TargetType, track.Id, track.Metadata );
		var recorderType = typeof( MovieComponentTrackRecorder<> ).MakeGenericType( track.TargetType );

		recorder = (IMovieComponentTrackRecorder)Activator.CreateInstance( recorderType, [this, compiledTrack] )!;

		AddChild( recorder );

		return recorder;
	}

	public IMovieComponentTrackRecorder Component( Component component )
	{
		Assert.True( component.GameObject == Target.Value );

		// Look for a recorder that currently targets this component, or is unbound and can target it

		var recorder = Children
			.OfType<IMovieComponentTrackRecorder>()
			.FirstOrDefault( x => x.CanTarget( component ) );

		if ( recorder is null )
		{
			// Create a new recorder for this Component

			var metadata = new TrackMetadata(
				ReferenceId: component.Id,
				FallbackType: GetFallbackType( component.GetType() ),
				Flags: GetFlags( component ) );

			var track = Track.Component( component.GetType(), metadata: metadata );
			var recorderType = typeof( MovieComponentTrackRecorder<> ).MakeGenericType( component.GetType() );

			recorder = (IMovieComponentTrackRecorder)Activator.CreateInstance( recorderType, [this, track] )!;

			AddChild( recorder );
		}

		recorder.Target.Bind( component );

		return recorder;
	}

	/// <summary>
	/// For a component type declared in game code, the most specific engine type it derives from. Used to load the
	/// track in projects that don't have the game's code. Null for engine types, and for types deriving straight from
	/// <see cref="Sandbox.Component"/>, which would bind to any component.
	/// </summary>
	private static Type? GetFallbackType( Type componentType )
	{
		if ( !IsDynamicType( componentType ) ) return null;

		for ( var baseType = componentType.BaseType; baseType is not null; baseType = baseType.BaseType )
		{
			if ( IsDynamicType( baseType ) ) continue;

			return baseType == typeof( Sandbox.Component ) ? null : baseType;
		}

		return null;
	}

	/// <summary>
	/// <see cref="TrackFlags.MapObject"/> for objects loaded by a <see cref="MapInstance"/>.
	/// </summary>
	internal static TrackFlags GetFlags( GameObject gameObject ) =>
		gameObject.IsMapInstanceRoot ? TrackFlags.MapObject : TrackFlags.None;

	/// <summary>
	/// <see cref="TrackFlags.MapObject"/> for a <see cref="MapInstance"/> that has loaded a scene map. Every object that
	/// map creates gets the flag too, so playback can tell them apart from objects created at runtime. Not for legacy
	/// maps, whose entities aren't marked as loaded from the map, or for maps that haven't loaded yet.
	/// </summary>
	private static TrackFlags GetFlags( Component component ) =>
		component is MapInstance { IsLoaded: true, LoadedVpkPath: null } ? TrackFlags.MapObject : TrackFlags.None;

	private static bool IsDynamicType( Type type ) =>
		Sandbox.Engine.GlobalContext.Current.TypeLibrary.GetType( type ) is { IsDynamicAssembly: true };

	/// <summary>
	/// Bones are posed by animation or physics, so we don't need to record their transforms. Procedural bones are
	/// posed by code instead, like a vehicle's wheels, so we do.
	/// </summary>
	private static bool IsAnimatedBone( GameObject gameObject )
	{
		if ( (gameObject.Flags & GameObjectFlags.PhysicsBone) != 0 ) return true;
		if ( (gameObject.Flags & GameObjectFlags.Bone) == 0 ) return false;

		return (gameObject.Flags & GameObjectFlags.ProceduralBone) == 0;
	}

	protected override void OnCapture()
	{
		if ( Target.Value is not { IsValid: true } gameObject ) return;

		if ( _firstCapture )
		{
			_firstCapture = false;
			FindPrefabSource( gameObject );
		}

		// Need to always record ancestors for transform to be correct

		Parent?.Capture();

		if ( IsAnimatedBone( gameObject ) ) return;

		Property( nameof( GameObject.Enabled ) ).Capture();

		if ( gameObject.Parent is { } parent && parent is not Scene && (parent.Flags & GameObjectFlags.Hidden) != 0 )
		{
			CaptureRelativeToRecordedParent( gameObject );
		}
		else
		{
			Property( nameof( GameObject.Parent ) ).Capture();
			Property( nameof( GameObject.LocalPosition ) ).Capture();
			Property( nameof( GameObject.LocalRotation ) ).Capture();
			Property( nameof( GameObject.LocalScale ) ).Capture();
		}

		Property( nameof( GameObject.Tags ) ).Capture( gameObject.Tags );
		Property( nameof( GameObject.Flags ) ).Property( nameof( GameObjectFlags.Absolute ) ).Capture();
	}

	/// <summary>
	/// The actual parent of <paramref name="gameObject"/> is hidden, so it won't get a track. Record the parent and
	/// transform as if it was parented to the nearest ancestor that isn't hidden. See <see cref="MovieRecorder.GetRecordedParent"/>.
	/// </summary>
	private void CaptureRelativeToRecordedParent( GameObject gameObject )
	{
		var recordedParent = MovieRecorder.GetRecordedParent( gameObject );
		var parentRecorder = recordedParent is null ? null : MovieRecorder.GetTrackRecorder( recordedParent ) as IMovieTrackRecorderInternal;

		parentRecorder?.MarkReferenced();

		var parentId = (parentRecorder?.Track as ICompiledReferenceTrack)?.Id;

		((MoviePropertyTrackRecorder<BindingReference<GameObject>>)Property( nameof( GameObject.Parent ) ))
			.Capture( new BindingReference<GameObject>( parentId ) );

		// Absolute objects ignore their parent's transform anyway

		var local = (gameObject.Flags & GameObjectFlags.Absolute) != 0 || recordedParent is null
			? gameObject.LocalTransform
			: recordedParent.WorldTransform.ToLocal( gameObject.WorldTransform );

		((MoviePropertyTrackRecorder<Vector3>)Property( nameof( GameObject.LocalPosition ) )).Capture( local.Position );
		((MoviePropertyTrackRecorder<Rotation>)Property( nameof( GameObject.LocalRotation ) )).Capture( local.Rotation );
		((MoviePropertyTrackRecorder<Vector3>)Property( nameof( GameObject.LocalScale ) )).Capture( local.Scale );
	}

	protected override void OnSkip()
	{
		Parent?.Capture();

		Property( nameof( GameObject.Enabled ) ).Capture();
	}

	private void FindPrefabSource( GameObject gameObject )
	{
		if ( !gameObject.IsPrefabInstanceRoot ) return;
		if ( GameObject.GetPrefab( gameObject.PrefabInstanceSource ) is not Scene prefabScene ) return;

		// TODO: does this work okay for nested prefabs?

		_prefabSourceBinder = new TrackBinder( prefabScene );
		_prefabSourceBinder.Get( Track ).Bind( prefabScene );
	}
}
