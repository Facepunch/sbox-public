using System.Text.Json;

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
	public static Contents Read( JsonElement objects )
	{
		var contents = new Contents();
		if ( objects.ValueKind != JsonValueKind.Array ) return contents;

		foreach ( var go in objects.EnumerateArray() )
			Read( go, contents, 0 );

		return contents;
	}

	static void Read( JsonElement go, Contents contents, int depth )
	{
		if ( go.ValueKind != JsonValueKind.Object ) return;
		if ( depth > MaxDepth )
		{
			contents.TooDeep = true;
			return;
		}

		if ( go.TryGetProperty( "Components", out var components ) && components.ValueKind == JsonValueKind.Array )
		{
			foreach ( var component in components.EnumerateArray() )
			{
				if ( component.ValueKind != JsonValueKind.Object ) continue;
				if ( !component.TryGetProperty( "__type", out var type ) || type.ValueKind != JsonValueKind.String ) continue;
				if ( !type.ValueEquals( FullName ) && !type.ValueEquals( ClassName ) ) continue;

				contents.Count++;
				if ( TryGetKey( component, out var key ) ) contents.Keys.Add( key );
			}
		}

		if ( go.TryGetProperty( "Children", out var children ) && children.ValueKind == JsonValueKind.Array )
		{
			foreach ( var child in children.EnumerateArray() )
				Read( child, contents, depth + 1 );
		}
	}

	static bool TryGetKey( JsonElement component, out BuilderShapeKey key )
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

	static T Value<T>( JsonElement json, string name ) => json.TryGetProperty( name, out var value ) && value.ValueKind != JsonValueKind.Null ? Json.Deserialize<T>( value.GetRawText() ) : default;
}
