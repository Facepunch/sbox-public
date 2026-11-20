using System.Text.Json.Nodes;

namespace Sandbox;

/// <summary>
/// Reads building blocks out of serialized GameObjects (dupes), without creating anything,
/// so limits, permissions and model prewarming can be decided before a dupe spawns.
/// </summary>
public static class BuilderDupe
{
	/// <summary>Building blocks found in a set of serialized objects.</summary>
	public sealed class Contents
	{
		/// <summary>Every BuilderProp, including ones whose recipe is invalid.</summary>
		public int Count { get; internal set; }

		/// <summary>The distinct models those blocks need.</summary>
		public HashSet<BuilderShapeKey> Keys { get; } = new();

		/// <summary>
		/// Nested deeper than <see cref="MaxDepth"/>, so <see cref="Count"/> can't be trusted and the dupe should be refused.
		/// </summary>
		public bool TooDeep { get; internal set; }
	}

	/// <summary>Deeper than any real contraption; stops a crafted dupe from hiding blocks or recursing forever.</summary>
	public const int MaxDepth = 64;

	static readonly string FullName = typeof( BuilderProp ).FullName;
	static readonly string ClassName = nameof( BuilderProp );

	/// <summary>
	/// Walk <paramref name="objects"/> and their children for BuilderProp components.
	/// </summary>
	public static Contents Read( JsonArray objects )
	{
		var contents = new Contents();
		if ( objects is null ) return contents;

		foreach ( var node in objects )
			Read( node as JsonObject, contents, 0 );

		return contents;
	}

	static void Read( JsonObject go, Contents contents, int depth )
	{
		if ( go is null ) return;
		if ( depth > MaxDepth )
		{
			contents.TooDeep = true;
			return;
		}

		if ( go["Components"] is JsonArray components )
		{
			foreach ( var node in components )
			{
				if ( node is not JsonObject component ) continue;
				if ( component["__type"] is not JsonValue type || !type.TryGetValue<string>( out var name ) || (name != FullName && name != ClassName) ) continue;

				contents.Count++;
				if ( TryGetKey( component, out var key ) ) contents.Keys.Add( key );
			}
		}

		if ( go["Children"] is JsonArray children )
		{
			foreach ( var child in children )
				Read( child as JsonObject, contents, depth + 1 );
		}
	}

	static bool TryGetKey( JsonObject component, out BuilderShapeKey key )
	{
		key = default;

		try
		{
			var recipe = new BuilderRecipe(
				Value<string>( component, nameof( BuilderProp.PieceId ) ),
				Value<BuilderCatalog.Shape>( component, nameof( BuilderProp.Shape ) ),
				Value<Vector3>( component, nameof( BuilderProp.Size ) ),
				Value<string>( component, nameof( BuilderProp.Parameters ) ) );

			return recipe.TryGetKey( out key );
		}
		catch ( Exception )
		{
			return false;
		}
	}

	static T Value<T>( JsonObject json, string name ) => json[name] is { } node ? Json.FromNode<T>( node ) : default;
}
