namespace Sandbox;

/// <summary>Typed runtime values owned by a scripting context. Reuse a store to preserve values across script edits.</summary>
[Expose]
public sealed class ScriptStore
{
	readonly ScriptSystem system;
	internal readonly Breen.ScriptStore Instance = new();

	internal ScriptStore( ScriptSystem system ) => this.system = system;

	internal void CheckAccess( ScriptSystem owner )
	{
		system.CheckAccess();
		if ( system != owner ) throw new InvalidOperationException( "Stores must belong to the script's context." );
	}

	/// <summary>Reads an initialized value. The type must match exactly; missing entries throw.</summary>
	public T Get<T>( string name )
	{
		system.CheckAccess();
		return Instance.Get<T>( name );
	}

	/// <summary>Reads a value, initializing it with the supplied default if absent.</summary>
	public T Get<T>( string name, T initialValue )
	{
		system.CheckAccess();
		if ( Instance.TryGet<T>( name, out var value ) ) return value;
		Instance.Set( name, initialValue );
		return initialValue;
	}

	/// <summary>Writes a value. An existing entry's declared type cannot change.</summary>
	public void Set<T>( string name, T value )
	{
		system.CheckAccess();
		Instance.Set( name, value );
	}
}

/// <summary>Persists a script field in the current scripting context's global store. Initialized once by field name.</summary>
[Expose, Breen.Store( "global" ), AttributeUsage( AttributeTargets.Field )]
public sealed class GlobalAttribute : Attribute;

/// <summary>Persists a script field in its attached local store. Initialized once by field name.</summary>
[Expose, Breen.Store( "local" ), AttributeUsage( AttributeTargets.Field )]
public sealed class LocalAttribute : Attribute;
