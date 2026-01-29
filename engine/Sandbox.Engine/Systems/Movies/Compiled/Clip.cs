using System.Collections.Immutable;

using static Sandbox.Internal.GlobalGameNamespace;

namespace Sandbox.MovieMaker.Compiled;

#nullable enable

/// <summary>
/// An immutable compiled <see cref="IMovieClip"/> designed to be serialized.
/// </summary>
public sealed partial class MovieClip : IMovieClip
{
	/// <summary>
	/// A clip with no tracks.
	/// </summary>
	public static MovieClip Empty { get; } = FromTracks();

	private readonly ImmutableDictionary<Guid, ICompiledReferenceTrack> _referenceTracks;

	/// <inheritdoc cref="IMovieClip.Tracks"/>
	public ImmutableArray<ICompiledTrack> Tracks { get; }

	public MovieTime Duration { get; }

	private MovieClip( IReadOnlySet<ICompiledTrack> tracks )
	{
		// ReSharper disable once UseCollectionExpression
		Tracks = tracks
			.Order()
			.ToImmutableArray();

		_referenceTracks = tracks
			.OfType<ICompiledReferenceTrack>()
			.ToImmutableDictionary( x => x.Id, x => x );

		Duration = tracks
			.OfType<ICompiledBlockTrack>()
			.Select( x => x.TimeRange.End )
			.DefaultIfEmpty()
			.Max();
	}

	/// <inheritdoc cref="IMovieClip.GetTrack"/>
	public ICompiledReferenceTrack? GetTrack( Guid trackId )
	{
		return _referenceTracks.GetValueOrDefault( trackId );
	}

	IEnumerable<ITrack> IMovieClip.Tracks => Tracks.CastArray<ITrack>();
	IReferenceTrack? IMovieClip.GetTrack( Guid trackId ) => GetTrack( trackId );

	public static MovieClip FromTracks( params ICompiledTrack[] tracks ) =>
		FromTracks( tracks.AsEnumerable() );

	public static MovieClip FromTracks( IEnumerable<ICompiledTrack> tracks )
	{
		var allTracks = new HashSet<ICompiledTrack>();

		// Find all root tracks

		foreach ( var track in tracks )
		{
			var parent = track;

			while ( parent is not null && allTracks.Add( parent ) )
			{
				parent = parent.Parent;

				// No cycles!

				if ( parent == track )
				{
					throw new ArgumentException( "Track hierarchy must not have cycles.", nameof( Tracks ) );
				}
			}
		}

		var referenceTracks = new Dictionary<Guid, ICompiledReferenceTrack>();

		// IDs must be unique

		foreach ( var track in allTracks.OfType<ICompiledReferenceTrack>() )
		{
			if ( !referenceTracks.TryAdd( track.Id, track ) )
			{
				throw new ArgumentException( "Tracks must have unique IDs.", nameof( Tracks ) );
			}
		}

		return new MovieClip( allTracks );
	}

	/// <summary>
	/// Create a root <see cref="ICompiledReferenceTrack"/> that targets a <see cref="Sandbox.GameObject"/> with
	/// the given <paramref name="name"/>. To create a nested track, use <see cref="CompiledClipExtensions.GameObject"/>.
	/// </summary>
	public static CompiledReferenceTrack<GameObject> RootGameObject( string name, Guid? id = null, Guid? referenceId = null ) => new( id ?? Guid.NewGuid(), name, ReferenceId: referenceId );

	/// <summary>
	/// Create a root <see cref="ICompiledReferenceTrack"/> that targets a <see cref="Sandbox.Component"/> with
	/// the given <paramref name="type"/>. To create a nested track, use <see cref="CompiledClipExtensions.Component"/>.
	/// </summary>
	public static ICompiledReferenceTrack RootComponent( Type type, Guid? id = null ) =>
		TypeLibrary.GetType( typeof( CompiledReferenceTrack<> ) )
			.CreateGeneric<ICompiledReferenceTrack>( [type], [id ?? Guid.NewGuid(), type.Name, null] );

	/// <summary>
	/// Create a root <see cref="ICompiledReferenceTrack"/> that targets a <see cref="Sandbox.Component"/> with
	/// the type <typeparamref name="T"/>. To create a nested track, use <see cref="CompiledClipExtensions.Component{T}"/>.
	/// </summary>
	public static CompiledReferenceTrack<T> RootComponent<T>( Guid? id = null )
		where T : Component => new( id ?? Guid.NewGuid(), typeof( T ).Name );
}
