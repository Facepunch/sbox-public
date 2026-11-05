using Sandbox.Engine;
using System.Text.Json.Serialization;

namespace Sandbox;

#nullable enable

/// <summary>
/// A serialized reference to a <see cref="Component"/> that can be resolved at runtime.
/// Components are referenced by their <see cref="Component.Id"/>, their containing object's
/// <see cref="GameObject.Id"/>, and their <see cref="TypeDescription.ClassName"/> if available.
/// </summary>
[ActionGraphExposeWhenCached]
internal readonly struct ComponentReference : IEquatable<ComponentReference>
{
	private const string ExpectedReferenceType = "component";

	/// <summary>
	/// Reference a given <see cref="Component"/>.
	/// </summary>
	public static ComponentReference FromInstance( Component component )
	{
		var t = GlobalContext.Current.TypeLibrary.GetType( component.GetType() );
		return new ComponentReference( ExpectedReferenceType, component.Id, component.GameObject.Id, t?.ClassName );
	}

	/// <summary>
	/// Converts a <see cref="ComponentReference"/> into a <see cref="GameObjectReference"/>, referencing the object
	/// that contains the component.
	/// </summary>
	public static explicit operator GameObjectReference( ComponentReference componentRef )
	{
		return GameObjectReference.FromId( componentRef.GameObjectId );
	}

	/// <summary>
	/// Expected to be <c>"component"</c> for a component reference.
	/// </summary>
	[JsonPropertyName( "_type" )]
	public string ReferenceType { get; }

	/// <summary>
	/// The <see cref="Component.Id"/> of the referenced component.
	/// </summary>
	[JsonPropertyName( "component_id" )]
	public Guid ComponentId { get; }

	/// <summary>
	/// The <see cref="GameObject.Id"/> of the object containing the referenced component.
	/// </summary>
	[JsonPropertyName( "go" )]
	public Guid GameObjectId { get; }

	/// <summary>
	/// If available, the <see cref="TypeDescription.ClassName"/> of the referenced component.
	/// </summary>
	[JsonPropertyName( "component_type" )]
	public string? ComponentTypeName { get; }

	[JsonConstructor]
	private ComponentReference( string referenceType, Guid componentId, Guid gameObjectId, string? componentTypeName = null )
	{
		ReferenceType = referenceType;
		ComponentId = componentId;
		GameObjectId = gameObjectId;
		ComponentTypeName = componentTypeName;
	}

	public bool Equals( ComponentReference other )
	{
		return ComponentId == other.ComponentId
			&& GameObjectId == other.GameObjectId
			&& string.Equals( ComponentTypeName, other.ComponentTypeName, StringComparison.Ordinal )
			&& string.Equals( ReferenceType, other.ReferenceType, StringComparison.Ordinal );
	}

	public override bool Equals( object? obj ) => obj is ComponentReference other && Equals( other );

	/// <summary>
	/// Leaves out <see cref="ReferenceType"/>: it's the same for every reference, so it adds nothing to the hash.
	/// </summary>
	public override int GetHashCode() => HashCode.Combine( ComponentId, GameObjectId );

	public static bool operator ==( ComponentReference left, ComponentReference right ) => left.Equals( right );
	public static bool operator !=( ComponentReference left, ComponentReference right ) => !left.Equals( right );

	/// <summary>
	/// Attempt to resolve <see cref="ComponentTypeName"/> into a <see cref="Type"/>. Returns <see langword="null"/> if not resolved.
	/// </summary>
	/// <param name="targetType">Optional base type / interface that the resolved type must derive from / implement. Defaults to <see cref="Component"/>.</param>
	public Type? ResolveComponentType( Type? targetType = null )
	{
		return GlobalContext.Current.TypeLibrary.GetType( targetType ?? typeof( Component ), ComponentTypeName, true )?.TargetType;
	}

	/// <summary>
	/// Attempt to resolve this reference in the current <see cref="Game.ActiveScene"/>. Returns <see langword="null"/> if
	/// the reference couldn't be resolved, and logs a warning.
	/// </summary>
	public Component? Resolve() => Resolve( Game.ActiveScene );

	/// <summary>
	/// Attempt to resolve this reference in the given <paramref name="scene"/>. Returns <see langword="null"/> if
	/// the reference couldn't be resolved.
	/// </summary>
	/// <param name="scene">Scene to attempt to resolve the reference in.</param>
	/// <param name="targetType">Optional base type / interface that the resolved instance must derive from / implement. Defaults to <see cref="Component"/>.</param>
	/// <param name="warn">If true, log a warning to the console if the reference couldn't be resolved.</param>
	public Component? Resolve( Scene scene, Type? targetType = null, bool warn = false )
	{
		if ( ReferenceType != ExpectedReferenceType )
			throw new( $"Tried to deserialize unknown type '{ReferenceType ?? "null"}' as Component" );

		// TODO: Do we want to throw when not found? We're not doing that in GameObjectReference.Resolve

		targetType ??= typeof( Component );

		Component? component = null;

		if ( ComponentId != Guid.Empty )
		{
			if ( !scene.IsValid() )
			{
				if ( warn ) Log.Warning( "Tried to read component - but active scene was null!" );

				return null;
			}

			component = scene.Directory.FindComponentByGuid( ComponentId );

			if ( component is not null )
				return component;
		}

		if ( GameObjectId != Guid.Empty )
		{
			if ( !scene.IsValid() )
			{
				if ( warn ) Log.Warning( "Tried to read component - but active scene was null!" );

				return null;
			}

			var go = scene.Directory.FindByGuid( GameObjectId );

			if ( !go.IsValid() )
			{
				// Conna: this is okay - we may be deserializing over the network and this is a property
				// referring to a GameObject that we don't know about. Let's just make the reference null.
				if ( warn ) Log.Warning( $"Unknown GameObject {GameObjectId}" );

				return null;
			}

			if ( !string.IsNullOrWhiteSpace( ComponentTypeName ) )
			{
				if ( ResolveComponentType( targetType ) is not { } resolvedType )
					throw new( $"Unable to find type '{ComponentTypeName}' with base type '{targetType.Name}'" );

				targetType = resolvedType;
			}

			component = go.Components.Get( targetType, FindMode.EverythingInSelf );

			if ( component is not null )
				return component;

			throw new( $"Component '{go}:{targetType}' was not found" );
		}

		throw new( $"Component '{ComponentId}' was not found" );
	}
}
