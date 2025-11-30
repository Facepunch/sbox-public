using System;
using System.Collections;
using System.Runtime.CompilerServices;
using Sandbox.MovieMaker.Properties;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Controls which <see cref="ITrackTarget"/>s from a scene are controlled by which <see cref="ITrack"/> from a <see cref="IMovieClip"/>.
/// Can be serialized to save which tracks are bound to which targets.
/// </summary>
public sealed partial class TrackBinder( Scene? scene = null ) : IEnumerable<KeyValuePair<Guid, IValid?>>
{
	private readonly ConditionalWeakTable<ITrack, ITrackTarget> _cache = new();

	/// <summary>
	/// The scene this binder is targeting.
	/// </summary>
	public Scene Scene { get; } = scene ?? Game.ActiveScene ?? throw new Exception( "No active scene!" );

	public void Add( IReferenceTrack track, IValid? target ) => Get( track ).Bind( target );

	/// <summary>
	/// Gets or creates a target that maps to the given <paramref name="track"/>.
	/// The target might not be bound to anything in the scene yet, use <see cref="ITrackTarget.IsBound"/> to check.
	/// </summary>
	public ITrackTarget Get( ITrack track )
	{
		if ( _cache.TryGetValue( track, out var target ) )
		{
			return target;
		}

		// Recurse to get parent ITarget, if this track has a parent

		var parent = track.Parent;
		var parentTarget = parent is not null ? Get( parent ) : null;

		// Create target for this track

		target = CreateTarget( track, parentTarget );

		// Update cache

		_cache.AddOrUpdate( track, target );

		return target;
	}

	/// <inheritdoc cref="Get(ITrack)"/>
	public ITrackReference Get( IReferenceTrack track ) => (ITrackReference)Get( (ITrack)track );

	/// <inheritdoc cref="Get(ITrack)"/>
	public ITrackReference<T> Get<T>( IReferenceTrack<T> track ) where T : class, IValid => (ITrackReference<T>)Get( (ITrack)track );

	/// <inheritdoc cref="Get(ITrack)"/>
	public ITrackProperty Get( IPropertyTrack track ) => (ITrackProperty)Get( (ITrack)track );

	/// <inheritdoc cref="Get(ITrack)"/>
	public ITrackProperty<T> Get<T>( IPropertyTrack<T> track ) => (ITrackProperty<T>)Get( (ITrack)track );

	private ITrackTarget CreateTarget( ITrack track, ITrackTarget? parent = null )
	{
		return track switch
		{
			// GameObject reference
			IReferenceTrack refTrack when track.TargetType == typeof( GameObject ) =>
				new GameObjectReference( parent as ITrackReference<GameObject>, track.Name, this, refTrack.Id, refTrack.ReferenceId ),

			// Component reference
			IReferenceTrack refTrack =>
				CreateComponentReference( parent as ITrackReference<GameObject>, track.TargetType, this, refTrack.Id ),

			// Member property within another track
			IPropertyTrack =>
				TrackProperty.Create( parent ?? throw new ArgumentNullException( nameof( parent ) ),
					track.Name, track.TargetType ),

			_ => throw new NotImplementedException()
		};
	}

	/// <summary>
	/// Get all reference targets for tracks in the given <paramref name="clip"/>.
	/// </summary>
	/// <typeparam name="T">Reference type, for example <see cref="GameObject"/> or a <see cref="Component"/> type.</typeparam>
	/// <param name="clip">Movie clip to find track bindings for.</param>
	public IEnumerable<ITrackReference<T>> GetReferences<T>( IMovieClip clip )
		where T : Component
	{
		return clip.Tracks
			.OfType<IReferenceTrack<T>>()
			.Select( Get );
	}

	/// <summary>
	/// Get all property targets for tracks in the given <paramref name="clip"/>.
	/// </summary>
	/// <typeparam name="T">Property value type.</typeparam>
	/// <param name="clip">Movie clip to find track bindings for.</param>
	public IEnumerable<ITrackProperty<T>> GetProperties<T>( IMovieClip clip )
	{
		return clip.Tracks
			.OfType<IPropertyTrack<T>>()
			.Select( Get );
	}

	/// <summary>
	/// Get all bound component references for tracks in the given <paramref name="clip"/>.
	/// </summary>
	/// <typeparam name="T">Component type.</typeparam>
	/// <param name="clip">Movie clip to find track bindings for.</param>
	public IEnumerable<T> GetComponents<T>( IMovieClip clip )
		where T : Component
	{
		return clip.Tracks
			.OfType<IReferenceTrack<T>>()
			.Select( x => Get( x ).Value )
			.OfType<T>();
	}

	private static ConditionalWeakTable<Scene, TrackBinder> DefaultBinders { get; } = new();

	/// <summary>
	/// Gets the default binder for the active scene.
	/// </summary>
	public static TrackBinder Default
	{
		get
		{
			if ( Game.ActiveScene is not { } scene )
			{
				throw new InvalidOperationException( "No active scene!" );
			}

			if ( DefaultBinders.TryGetValue( scene, out var binder ) )
			{
				return binder;
			}

			DefaultBinders.Add( scene, binder = new TrackBinder( scene ) );

			return binder;
		}
	}

	public IEnumerator<KeyValuePair<Guid, IValid?>> GetEnumerator()
	{
		foreach ( var (guid, gameObject) in _gameObjectMap )
		{
			yield return new KeyValuePair<Guid, IValid?>( guid, gameObject );
		}

		foreach ( var (guid, component) in _componentMap )
		{
			yield return new KeyValuePair<Guid, IValid?>( guid, component );
		}
	}

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
