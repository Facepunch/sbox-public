using System;
using Sandbox.MovieMaker.Compiled;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// Helper methods for working with <see cref="IMovieClip"/> and <see cref="ITrack"/>.
/// </summary>
public static class ClipExtensions
{
	/// <summary>
	/// How deeply are we nested? Root tracks have depth <c>0</c>.
	/// </summary>
	public static int GetDepth( this ITrack track ) => track.Parent is null ? 0 : track.Parent.GetDepth() + 1;

	public static (IReferenceTrack ReferenceTrack, IReadOnlyList<string> PropertyNames) GetPath( this ITrack track, bool full = true )
	{
		var names = new List<string>();

		while ( track.Parent is not null && (full || track is not IReferenceTrack) )
		{
			names.Add( track.Name );

			track = track.Parent!;
		}

		names.Reverse();

		return (track as IReferenceTrack ?? throw new Exception( "Expected root tracks to be reference tracks." ), names);
	}

	public static string GetPathString( this ITrack track, bool full = true )
	{
		var path = track.GetPath( full );
		string[] propertyNames = [path.ReferenceTrack.Name, .. path.PropertyNames];
		return string.Join( " \u2192 ", propertyNames );
	}

	/// <summary>
	/// Searches <paramref name="clip"/> for a track with the given <paramref name="path"/>,
	/// starting from the root level of the clip.
	/// </summary>
	public static ITrack? GetTrack( this IMovieClip clip, params string[] path )
	{
		return clip.Tracks.FirstOrDefault( x => x.HasMatchingFullPath( path ) );
	}

	/// <inheritdoc cref="GetTrack(IMovieClip,string[])"/>
	public static ICompiledTrack? GetTrack( this MovieClip clip, params string[] path )
	{
		return (ICompiledTrack)((IMovieClip)clip).GetTrack( path )!;
	}

	/// <summary>
	/// Searches <paramref name="clip"/> for a track with the given <paramref name="path"/>,
	/// starting from the root level of the clip.
	/// </summary>
	public static IReferenceTrack<T>? GetReference<T>( this IMovieClip clip, params string[] path )
		where T : class, IValid
	{
		return clip.Tracks
			.OfType<IReferenceTrack<T>>()
			.FirstOrDefault( x => x.HasMatchingFullPath( path ) );
	}

	/// <inheritdoc cref="GetReference{T}(IMovieClip,string[])"/>
	public static CompiledReferenceTrack<T>? GetReference<T>( this MovieClip clip, params string[] path )
		where T : class, IValid
	{
		return (CompiledReferenceTrack<T>)((IMovieClip)clip).GetReference<T>( path )!;
	}

	/// <summary>
	/// Searches <paramref name="clip"/> for a property track with the given <paramref name="path"/>,
	/// starting from the root level of the clip.
	/// </summary>
	/// <typeparam name="T">Property value type.</typeparam>
	public static IPropertyTrack<T>? GetProperty<T>( this IMovieClip clip, params string[] path )
	{
		return clip.Tracks
			.OfType<IPropertyTrack<T>>()
			.FirstOrDefault( x => x.HasMatchingFullPath( path ) );
	}

	public static IPropertyTrack<T>? GetProperty<T>( this IMovieClip clip, Guid refTrackId, IReadOnlyList<string> path )
	{
		if ( clip.GetTrack( refTrackId ) is not { } refTrack ) return null;

		return clip.Tracks
			.OfType<IPropertyTrack<T>>()
			.FirstOrDefault( x => x.HasMatchingFullPath( refTrack, path ) );
	}

	/// <inheritdoc cref="GetProperty{T}(IMovieClip,string[])"/>
	public static CompiledPropertyTrack<T>? GetProperty<T>( this MovieClip clip, params string[] path )
	{
		return (CompiledPropertyTrack<T>?)((IMovieClip)clip).GetProperty<T>( path );
	}

	public static CompiledPropertyTrack<T>? GetProperty<T>( this MovieClip clip, Guid refTrackId, IReadOnlyList<string> path )
	{
		return (CompiledPropertyTrack<T>?)((IMovieClip)clip).GetProperty<T>( refTrackId, path );
	}

	private static bool HasMatchingFullPath( this ITrack track, IReadOnlyList<string> path )
	{
		if ( track.GetDepth() != path.Count - 1 ) return false;

		var parent = track;

		for ( var i = path.Count - 1; i >= 0 && parent is not null; --i )
		{
			var name = path[i];

			if ( parent.Name != name ) return false;

			parent = parent.Parent;
		}

		return true;
	}

	private static bool HasMatchingFullPath( this ITrack track, IReferenceTrack refTrack, IReadOnlyList<string> propertyPath )
	{
		if ( track.GetDepth() - refTrack.GetDepth() != propertyPath.Count ) return false;

		var parent = track;

		for ( var i = propertyPath.Count - 1; i >= 0 && parent is not null; --i )
		{
			var name = propertyPath[i];

			if ( parent.Name != name ) return false;

			parent = parent.Parent;
		}

		return parent == refTrack;
	}

	/// <summary>
	/// For each track in the given <paramref name="clip"/> that we have a mapped property for,
	/// set the property value to whatever value is stored in that track at the given <paramref name="time"/>.
	/// </summary>
	public static bool Update( this IMovieClip clip, MovieTime time, TrackBinder? binder = null )
	{
		var anyChanges = false;

		foreach ( var track in clip.GetTracks( time ).OfType<IPropertyTrack>() )
		{
			anyChanges |= track.Update( time, binder );
		}

		return anyChanges;
	}

	/// <summary>
	/// If we have a mapped property for <paramref name="track"/>, set the property value to whatever value
	/// is stored in the track at the given <paramref name="time"/>.
	/// </summary>
	public static bool Update( this IPropertyTrack track, MovieTime time, TrackBinder? binder = null )
	{
		binder ??= TrackBinder.Default;

		return binder.Get( track ).Update( track, time );
	}

	/// <inheritdoc cref="Update(IPropertyTrack,MovieTime,TrackBinder?)"/>
	public static bool Update<T>( this IPropertyTrack<T> track, MovieTime time, TrackBinder? binder = null )
	{
		binder ??= TrackBinder.Default;

		binder.Get( track ).Update( track, time );

		return true;
	}
}
