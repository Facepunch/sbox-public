using Sandbox.MovieMaker;
using Sandbox.MovieMaker.Properties;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using Sandbox.MovieMaker.Compiled;

namespace Editor.MovieMaker;

#nullable enable

/// <summary>
/// A track hierarchy preset to make it easier to add many sub-tracks. A track preset can only be used
/// to create tracks if all properties described in its hierarchy exist, with <paramref name="Root"/> representing
/// the root property of the hierarchy.
/// </summary>
public sealed partial record TrackPreset( TrackPresetMetadata Meta, TrackPresetNode Root )
{
	/// <summary>
	/// Number of tracks contained in this preset, not counting the root node.
	/// </summary>
	public int AvailableTrackCount( IProjectTrack rootTrack, TrackBinder binder ) => Root.AvailableTrackCount( rootTrack.Compile( true ), binder ) - 1;

	/// <summary>
	/// Counts how many tracks in the hierarchy rooted by <paramref name="rootTrack"/> match this preset's hierarchy.
	/// If this matches <see cref="AvailableTrackCount"/>, then this preset has been fully created.
	/// </summary>
	public int MatchingTrackCount( IProjectTrack rootTrack ) => Root.MatchingTrackCount( rootTrack ) - 1;

	/// <summary>
	/// Tests if the given <paramref name="rootTrack"/> has sub-tracks matching the track hierarchy described
	/// by this preset node.
	/// </summary>
	public bool AllTracksExist( IProjectTrack rootTrack, TrackBinder binder ) =>
		AvailableTrackCount( rootTrack, binder ) == MatchingTrackCount( rootTrack );
}

/// <summary>
/// Describes how a <see cref="TrackPreset"/> is presented in menus.
/// </summary>
public sealed record TrackPresetMetadata( string Title, string Category = "Custom", string Icon = "playlist_add", string? Description = null );

/// <summary>
/// A track in a <see cref="TrackPresetNode"/>'s hierarchy with a given <paramref name="PropertyName"/> and <paramref name="PropertyType"/>.
/// Can include sub-tracks, found in <paramref name="Children"/>.
/// </summary>
/// <param name="PropertyName">Property name to match.</param>
/// <param name="PropertyType">Property type to match.</param>
/// <param name="AllChildren">If true, auto-add all child properties when applying this preset and ignore <paramref name="Children"/>.</param>
/// <param name="Children">Sub-properties to match.</param>
[method: JsonConstructor]
public sealed record TrackPresetNode(
	string PropertyName,
	Type PropertyType,
	bool AllChildren = false,
	params ImmutableArray<TrackPresetNode> Children )
{
	public TrackPresetNode( string propertyName, Type propertyType, params ImmutableArray<TrackPresetNode> children )
		: this( propertyName, propertyType, false, children )
	{

	}

	/// <summary>
	/// Total number of tracks in this node's hierarchy, including this node's track.
	/// </summary>
	public int AvailableTrackCount( ICompiledTrack track, TrackBinder binder )
	{
		return AllChildren
			? 1 + TrackProperty.GetAll( binder.Get( track ) ).Count()
			: 1 + Children.Sum( x => x.AvailableTrackCount( track.Child( x.PropertyName, x.PropertyType ), binder ) );
	}

	/// <summary>
	/// Tests if a given <paramref name="target"/> contains this node's track hierarchy. True if the target has a
	/// matching type, and all <see cref="Children"/> nodes have matching targets.
	/// </summary>
	public bool Matches( ITrackTarget target )
	{
		return target.TargetType.IsAssignableTo( PropertyType ) && Children.All( childPreset => childPreset.IsChildOf( target ) );
	}

	/// <summary>
	/// True if this preset node has a matching child property inside <paramref name="parent"/>, including with
	/// recursively matching sub-children.
	/// </summary>
	private bool IsChildOf( ITrackTarget parent )
	{
		return FindMatchingChild( parent ) is { } child && Matches( child );
	}

	/// <summary>
	/// Looks for a child target inside <paramref name="parent"/> with a name and value type that matches this node.
	/// Returns null if not found.
	/// </summary>
	private ITrackTarget? FindMatchingChild( ITrackTarget parent )
	{
		if ( parent.TargetType == typeof( GameObject ) )
		{
			// If the parent is a game object reference, we might be looking for:
			// * a child game object reference
			// * a component reference
			// * a property of the game object (position / rotation / enabled)

			if ( parent is not ITrackReference<GameObject> { Value: { } go } parentRef )
			{
				return null;
			}

			if ( PropertyType == typeof( GameObject ) )
			{
				var childObj = go.Children.FirstOrDefault( x => x.Name == PropertyName );

				return childObj is not null
					? new GameObjectReference( childObj, parentRef )
					: null;
			}

			if ( PropertyType.IsAssignableTo( typeof( Component ) ) )
			{
				var component = go.Components.FirstOrDefault( PropertyType.IsInstanceOfType );
				var refType = typeof(ComponentReference<>).MakeGenericType( PropertyType );

				return component is not null
					? (ITrackReference)Activator.CreateInstance( refType, [component, parent] )!
					: null;
			}
		}

		// Default case, we're looking for a sub-property instead of a reference target

		var property = TrackProperty.Create( parent, PropertyName );

		return property?.TargetType.IsAssignableTo( PropertyType ) ?? false
			? property
			: null;
	}

	/// <summary>
	/// Counts how many tracks in the hierarchy rooted by <paramref name="rootTrack"/> match this preset's hierarchy.
	/// If this matches <see cref="AvailableTrackCount"/>, then this preset has been fully created.
	/// </summary>
	public int MatchingTrackCount( IProjectTrack rootTrack )
	{
		if ( !rootTrack.TargetType.IsAssignableTo( PropertyType ) ) return 0;

		if ( AllChildren )
		{
			return rootTrack.Children.Count + 1;
		}

		var count = 1;

		foreach ( var childPreset in Children )
		{
			if ( rootTrack.Children.FirstOrDefault( x => x.Name == childPreset.PropertyName ) is not { } childTrack ) continue;

			count += childPreset.MatchingTrackCount( childTrack );
		}

		return count;
	}

	private bool PrintMembers( StringBuilder builder )
	{
		builder.Append( $"{nameof( PropertyName )} = {PropertyName}, " );
		builder.Append( $"{nameof( PropertyType )} = {PropertyType.ToSimpleString( false )}, " );
		builder.Append( $"{nameof( Children )} = [ {string.Join( ", ", Children )} ]" );

		return true;
	}

	/// <summary>
	/// Helper to create a track preset node describing the track hierarchy with the given <paramref name="root"/> track.
	/// </summary>
	public static TrackPresetNode FromTrackView( TrackView root )
	{
		return new TrackPresetNode( root.Track.Name, root.Track.TargetType,
			[.. root.Children.Where( x => !x.IsLocked ).Select( FromTrackView )] );
	}
}

/// <summary>
/// Helper target used internally by <see cref="TrackPresetNode.FindMatchingChild"/>.
/// </summary>
file abstract record DummyReference<T>( T Value, ITrackReference<GameObject>? Parent ) : ITrackReference<T>
	where T : class, IValid
{
	public abstract string Name { get; }
	public abstract Guid Id { get; }

	public bool IsActive => true;

	Type ITrackTarget.TargetType => Value.GetType();

	private NotSupportedException NotSupportedInTrackBinder() =>
		new( "This helper is used internally by TrackPresetNode, and should never be used in a TrackBinder." );

	void ITrackReference.Reset() => throw NotSupportedInTrackBinder();
	void ITrackReference<T>.Bind( T? value ) => throw NotSupportedInTrackBinder();
}

file sealed record GameObjectReference( GameObject Value, ITrackReference<GameObject>? Parent ) : DummyReference<GameObject>( Value, Parent )
{
	public override string Name => Value.Name;
	public override Guid Id => Value.Id;
}

file sealed record ComponentReference<T>( T Value, ITrackReference<GameObject>? Parent ) : DummyReference<T>( Value, Parent )
	where T : Component
{
	public override string Name => Value.GetType().Name;
	public override Guid Id => Value.Id;
}
