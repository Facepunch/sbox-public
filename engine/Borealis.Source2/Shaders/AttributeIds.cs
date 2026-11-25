namespace Borealis.Source2.Shaders;

/// <summary>
/// Attribute names as numbers, the same for every spelling that compares equal ignoring case (as the engine's attribute
/// tokens do): what <c>ShaderAttributes</c> stores values by, and what a shader's variables keep for the name they bind
/// (<see cref="ShaderVariable.BindingId"/>), so a draw finds each value without hashing its name. Ids are never reused.
/// </summary>
public static class AttributeIds
{
	static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> ids = new( StringComparer.OrdinalIgnoreCase );
	static readonly object adding = new();
	static string[] names = new string[256];
	static int count;

	/// <summary>The id of <paramref name="name"/>, made the first time it's asked for.</summary>
	public static int Of( string name )
	{
		ArgumentNullException.ThrowIfNull( name );
		if ( ids.TryGetValue( name, out var id ) ) return id;

		lock ( adding )
		{
			if ( ids.TryGetValue( name, out id ) ) return id;
			id = count;
			if ( id == names.Length ) Array.Resize( ref names, names.Length * 2 );
			names[id] = name;
			System.Threading.Volatile.Write( ref count, id + 1 );
			ids[name] = id;
			return id;
		}
	}

	/// <summary>The name an id was made for, as first spelled.</summary>
	public static string NameOf( int id ) => (uint)id < (uint)System.Threading.Volatile.Read( ref count ) ? names[id] : null;

	/// <summary>How many ids there are: every id is below it.</summary>
	public static int Count => System.Threading.Volatile.Read( ref count );
}
