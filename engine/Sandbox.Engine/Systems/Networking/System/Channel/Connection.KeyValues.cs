using Sandbox.Network;

namespace Sandbox;

public abstract partial class Connection
{
	/// <summary>
	/// Key-value store for connections. Not to be mistaken with <see cref="Info"/> which is sent from the client and can be used for things like avatar customization.
	/// This is not shared to other connections.
	/// </summary>
	internal Dictionary<string, object> KeyValues { get; } = new();

	/// <summary>
	/// Get a typed value from the connection's key-value store.
	/// </summary>
	internal T Get<T>( string key, T defaultValue = default )
	{
		if ( KeyValues.TryGetValue( key, out var val ) && val is T typed )
			return typed;

		return defaultValue;
	}

	/// <summary>
	/// Set a typed value in the connection's key-value store.
	/// </summary>
	internal void Set<T>( string key, T value )
	{
		KeyValues[key] = value;
	}
}
