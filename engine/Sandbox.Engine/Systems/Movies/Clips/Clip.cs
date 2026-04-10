using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Sandbox.MovieMaker;

#nullable enable

/// <summary>
/// A collection of <see cref="ITrack"/>s describing properties changing over time and actions being invoked.
/// </summary>
public interface IMovieClip
{
	/// <summary>
	/// All tracks within the clip.
	/// </summary>
	IEnumerable<ITrack> Tracks { get; }

	/// <summary>
	/// How long this clip takes to fully play.
	/// </summary>
	MovieTime Duration { get; }

	/// <summary>
	/// Attempts to get a reference track with the given <paramref name="trackId"/>.
	/// </summary>
	/// <returns>The matching track, or <see langword="null"/> if not found.</returns>
	IReferenceTrack? GetTrack( Guid trackId );

	/// <summary>
	/// Get tracks that are active at the given <paramref name="time"/>.
	/// </summary>
	public IEnumerable<ITrack> GetTracks( MovieTime time ) => Tracks;
}

/// <summary>
/// Maps to a <see cref="ITrackTarget"/> in a scene, and describes how it changes over time.
/// </summary>
public interface ITrack
{
	/// <summary>
	/// Property or object name, used when auto-binding this track in a scene.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// What type of object or property is this track targeting.
	/// </summary>
	Type TargetType { get; }

	/// <summary>
	/// Tracks can be nested, which means child tracks can auto-bind to targets in the scene
	/// if their parent is bound.
	/// </summary>
	ITrack? Parent { get; }
}

/// <summary>
/// Additional information used when editing or animating reference tracks.
/// </summary>
/// <param name="ReferenceId">ID of the <see cref="Component"/> or <see cref="GameObject"/> this track was created to target.</param>
/// <param name="PrefabSource">For <see cref="GameObject"/> tracks, the prefab path that the original target object was instantiated from.</param>
public sealed record TrackMetadata(
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	Guid? ReferenceId = null,
	[property: JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	string? PrefabSource = null );

/// <summary>
/// Maps to an <see cref="ITrackReference"/> in a scene, which binds to a <see cref="GameObject"/>
/// or <see cref="Component"/>.
/// </summary>
public interface IReferenceTrack : ITrack
{
	/// <summary>
	/// Identifier for this track. Must be unique in the containing <see cref="IMovieClip"/>,
	/// but different clips can share tracks as long as they have identical names, types,
	/// and parent tracks.
	/// </summary>
	Guid Id { get; }

	/// <inheritdoc cref="ITrack.Parent"/>
	new IReferenceTrack<GameObject>? Parent { get; }

	/// <summary>
	/// Additional information used when editing or animating this track.
	/// </summary>
	TrackMetadata? Metadata { get; }

	ITrack? ITrack.Parent => Parent;
}

/// <inheritdoc cref="IPropertyTrack"/>
/// <typeparam name="T">Reference value type, must match <see cref="ITrack.TargetType"/>.</typeparam>
public interface IReferenceTrack<T> : IReferenceTrack
	where T : class, IValid
{
	Type ITrack.TargetType => typeof( T );
}

/// <summary>
/// Unused, will describe running actions in the scene.
/// </summary>
public interface IActionTrack : ITrack
{
	new ITrack Parent { get; }

	ITrack ITrack.Parent => Parent;
}

/// <summary>
/// Controls an <see cref="ITrackProperty"/> in the scene. Defines what value that property should have
/// at each moment of time.
/// </summary>
public interface IPropertyTrack : ITrack
{
	/// <summary>
	/// For a given <paramref name="time"/>, does this track want to control its mapped property.
	/// If so, also outputs the desired property value.
	/// </summary>
	bool TryGetValue( MovieTime time, out object? value );

	/// <inheritdoc cref="ITrack.Parent"/>
	new ITrack Parent { get; }

	ITrack ITrack.Parent => Parent;
}

/// <inheritdoc cref="IPropertyTrack"/>
/// <typeparam name="T">Property value type.</typeparam>
public interface IPropertyTrack<T> : IPropertyTrack
{
	/// <summary>
	/// For a given <paramref name="time"/>, does this track want to control its mapped property.
	/// If so, also outputs the desired property value.
	/// </summary>
	bool TryGetValue( MovieTime time, [MaybeNullWhen( false )] out T value );

	Type ITrack.TargetType => typeof( T );

	bool IPropertyTrack.TryGetValue( MovieTime time, out object? value )
	{
		if ( TryGetValue( time, out var val ) )
		{
			value = val;
			return true;
		}

		value = null;
		return false;
	}
}

/// <summary>
/// A time region where something happens in a movie track.
/// </summary>
public interface ITrackBlock
{
	/// <summary>
	/// Start and end time of this block.
	/// </summary>
	MovieTimeRange TimeRange { get; }
}

/// <summary>
/// Describes a value that changes over time.
/// </summary>
public interface IPropertySignal
{
	/// <summary>
	/// What type of value does this signal describe?
	/// </summary>
	Type PropertyType { get; }

	/// <summary>
	/// What value does this signal have at the given time?
	/// </summary>
	object? GetValue( MovieTime time );
}

/// <inheritdoc cref="IPropertySignal{T}"/>
// ReSharper disable once TypeParameterCanBeVariant
public interface IPropertySignal<T> : IPropertySignal
{
	/// <inheritdoc cref="IPropertySignal.GetValue"/>
	new T GetValue( MovieTime time );

	object? IPropertySignal.GetValue( MovieTime time ) => GetValue( time );
	Type IPropertySignal.PropertyType => typeof( T );
}

/// <summary>
/// A <see cref="IPropertySignal"/> with a defined start and end time.
/// </summary>
public interface IPropertyBlock : ITrackBlock, IPropertySignal;

/// <summary>
/// A <see cref="IPropertySignal{T}"/> with a defined start and end time.
/// </summary>
// ReSharper disable once TypeParameterCanBeVariant
public interface IPropertyBlock<T> : IPropertyBlock, IPropertySignal<T>;

/// <summary>
/// A <see cref="ITrackBlock"/> that can change dynamically, usually for previewing edits / live recordings.
/// </summary>
public interface IDynamicBlock : ITrackBlock
{
	event Action<MovieTimeRange>? Changed;
}
