using System.Text.Json.Serialization;

namespace Sandbox;

#nullable enable

/// <summary>
/// A serialized reference to a <see cref="GameObject"/> that can be resolved at runtime.
/// Can either have a <see cref="GameObjectId"/> for objects in a scene, or a <see cref="PrefabPath"/>
/// if we're referencing a prefab.
/// </summary>
[ActionGraphExposeWhenCached]
internal readonly struct GameObjectReference : IEquatable<GameObjectReference>
{
	private const string ExpectedReferenceType = "gameobject";

	/// <summary>
	/// Reference a <see cref="GameObject"/> in a scene by its <see cref="GameObject.Id"/>.
	/// </summary>
	public static GameObjectReference FromId( Guid id ) => new( ExpectedReferenceType, gameObjectId: id );

	/// <summary>
	/// Reference a prefab by its <see cref="Resource.ResourcePath"/>.
	/// </summary>
	public static GameObjectReference FromPrefabPath( string prefabPath ) => new( ExpectedReferenceType, prefabPath: prefabPath );

	/// <summary>
	/// Reference a given <see cref="GameObject"/>.
	/// </summary>
	public static GameObjectReference FromInstance( GameObject go )
	{
		// If we reference a prefab scene, use the prefab path instead of the ID.
		// But only if the prefab scene is not the active scene.
		// If the prefab is the active scene, we can use the ID, as users may want to reference the prefab root from within the prefab scene.
		if ( go is PrefabScene prefabRoot && go.Scene != Game.ActiveScene )
		{
			Assert.NotNull( prefabRoot.Source, "Prefab root should have a source" );
			return FromPrefabPath( prefabRoot.Source.ResourcePath );
		}

		return FromId( go.Id );
	}

	/// <summary>
	/// Expected to be <c>"gameobject"</c> for a game object reference.
	/// </summary>
	[JsonPropertyName( "_type" )]
	public string ReferenceType { get; }

	/// <summary>
	/// If we're referencing an object in a scene, this is its <see cref="GameObject.Id"/>.
	/// </summary>
	[JsonPropertyName( "go" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingDefault )]
	public Guid GameObjectId { get; }

	/// <summary>
	/// If we're referencing a prefab, this is its <see cref="Resource.ResourcePath"/>.
	/// </summary>
	[JsonPropertyName( "prefab" ), JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public string? PrefabPath { get; }

	[JsonConstructor]
	private GameObjectReference( string referenceType, Guid gameObjectId = default, string? prefabPath = null )
	{
		ReferenceType = referenceType;
		GameObjectId = gameObjectId;
		PrefabPath = prefabPath;
	}

	public bool Equals( GameObjectReference other )
	{
		return GameObjectId == other.GameObjectId
			&& string.Equals( PrefabPath, other.PrefabPath, StringComparison.Ordinal )
			&& string.Equals( ReferenceType, other.ReferenceType, StringComparison.Ordinal );
	}

	public override bool Equals( object? obj ) => obj is GameObjectReference other && Equals( other );

	/// <summary>
	/// Leaves out <see cref="ReferenceType"/>: it's the same for every reference, so it adds nothing to the hash.
	/// </summary>
	public override int GetHashCode() => HashCode.Combine( GameObjectId, PrefabPath );

	public static bool operator ==( GameObjectReference left, GameObjectReference right ) => left.Equals( right );
	public static bool operator !=( GameObjectReference left, GameObjectReference right ) => !left.Equals( right );

	/// <summary>
	/// Attempt to resolve this reference in the current <see cref="Game.ActiveScene"/>. Returns <see langword="null"/> if
	/// the reference couldn't be resolved, and logs a warning.
	/// </summary>
	public GameObject? Resolve() => Resolve( Game.ActiveScene );

	/// <summary>
	/// Attempt to resolve this reference in the given <paramref name="scene"/>. Returns <see langword="null"/> if
	/// the reference couldn't be resolved.
	/// </summary>
	/// <param name="scene">Scene to attempt to resolve the reference in.</param>
	/// <param name="warn">If true, log a warning to the console if the reference couldn't be resolved.</param>
	public GameObject? Resolve( Scene scene, bool warn = false )
	{
		if ( ReferenceType != ExpectedReferenceType )
			throw new( $"Tried to deserialize unknown type '{ReferenceType ?? "null"}' as GameObject" );

		if ( !string.IsNullOrWhiteSpace( PrefabPath ) )
		{
			var prefabFile = (PrefabFile)GameResource.GetPromise( typeof( PrefabFile ), PrefabPath );
			return SceneUtility.GetPrefabScene( prefabFile );
		}

		var go = scene.Directory.FindByGuid( GameObjectId );

		if ( !go.IsValid() )
		{
			// Conna: this is okay - we may be deserializing over the network and this is a property
			// referring to a GameObject that we don't know about. Let's just make the reference null.

			if ( warn ) Log.Warning( $"Unknown GameObject {GameObjectId}" );

			return null;
		}

		return go;
	}
}
