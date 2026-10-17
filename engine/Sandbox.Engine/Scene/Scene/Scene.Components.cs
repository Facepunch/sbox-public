using Sandbox.Utility;

namespace Sandbox;

public partial class Scene : GameObject
{
	internal HashSetEx<Component> pendingStartComponents = new();

	/// <summary>
	/// Called for every enabled component
	/// </summary>
	internal void RegisterComponent( Component c )
	{
		AddObjectToDirectory( c );

		if ( !c.StartCalled )
			pendingStartComponents.Add( c );
	}

	internal void UnregisterComponent( Component c )
	{
		RemoveObjectFromDirectory( c );

		pendingStartComponents.Remove( c );
	}

	/// <summary>
	/// Get all components of type. This can include interfaces.
	/// This function can only find enabled/active components.
	/// </summary>
	[Pure]
	public IEnumerable<T> GetAllComponents<T>()
	{
		return GetAll<T>();
	}

	/// <summary>
	/// Get all components of type. This can include interfaces.
	/// This function can only find enabled/active components.
	/// </summary>
	[Pure]
	public IEnumerable<Component> GetAllComponents( Type type )
	{
		if ( !objectIndex.TryGetValue( type, out var set ) )
			yield break;

		foreach ( var e in set.EnumerateLocked() )
		{
			var c = e as Component;
			if ( !c.IsValid() ) continue;
			yield return c;
		}
	}
}
