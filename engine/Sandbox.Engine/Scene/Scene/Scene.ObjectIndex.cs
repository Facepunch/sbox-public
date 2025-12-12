using Sandbox.Internal;
using Sandbox.Utility;

namespace Sandbox;

//
// We add objects like Components and GameObjectSystem to an object index, in which they're indexed for 
// fast lookup by type, basetypes and interfaces. This allows Scene.GetAll<T> to get interfaces etc very
// quickly compared to iterating individual items.
//
// Objects should be added when they're active and removed when they're inactive. For example, when a component
// or a component's GameObject is disabled, then it is removed from the object index. When it is enabled 
// again, it's re-added.
//

public partial class Scene : GameObject
{
	// objects collections indexed by type
	[SuppressNullKeyWarning]
	Dictionary<Type, HashSetEx<object>> objectIndex = new();

	// all objects in the index
	HashSet<object> objectsInIndex = new();

	// Component magic
	internal HashSetEx<Component> updateComponents = new();
	internal HashSetEx<Component> fixedUpdateComponents = new();
	internal HashSetEx<Component> preRenderComponents = new();

	/// <summary>
	/// Should only be called when destroying the scene. This here just to avoid unregistering
	/// all of the objects when we don't need to, because we're just quitting.
	/// </summary>
	void ClearObjectIndex()
	{
		foreach ( var t in objectIndex.Values )
		{
			t?.Clear();
		}

		objectsInIndex.Clear();
		objectIndex.Clear();
		updateComponents.Clear();
		fixedUpdateComponents.Clear();
		preRenderComponents.Clear();
	}

	/// <summary>
	/// When hotload occurs, the interfaces etc could have changed. So first of all we want to
	/// go through and remove any null entries, then we want to go through and re-add everything.
	/// </summary>
	void HotloadObjectIndex()
	{
		var ft = FastTimer.StartNew();

		// copy the list
		var temporaryHash = objectsInIndex.ToList();

		// clear the old list
		objectsInIndex.Clear();
		objectIndex.Clear();

		// We can't clear updateComponents etc because ActionGraph add to them manually

		// re-add everything
		foreach ( var obj in temporaryHash )
		{
			if ( obj is null ) continue;

			AddObjectToDirectory( obj );
		}

		if ( ft.ElapsedMilliSeconds > 10 )
		{
			Log.Warning( $"HotloadObjectIndex took {ft.ElapsedMilliSeconds:0.0}ms" );
		}
	}


	/// <summary>
	/// Adds object instance, indexed by type, to the directory so that its values are accessible by GetAll
	/// </summary>
	internal void AddObjectToDirectory( object obj )
	{
		if ( !objectsInIndex.Add( obj ) ) return;

		var t = obj.GetType();
		while ( t is not null )
		{
			// We stop short of adding these base types because right now they
			// have no value. I can see a use in the future for getting all components.
			if ( t == typeof( object ) ) break;
			if ( t == typeof( Component ) ) break;
			if ( t == typeof( GameObjectSystem ) ) break;

			objectIndex.GetOrCreate( t ).Add( obj );

			foreach ( var i in t.GetInterfaces() )
			{
				objectIndex.GetOrCreate( i ).Add( obj );
			}

			t = t.BaseType;
		}

		// little bit of magic for components
		if ( obj is Component c )
		{
			if ( c is IUpdateSubscriber || c.OnComponentUpdate is not null ) updateComponents.Add( c );
			if ( c is IFixedUpdateSubscriber || c.OnComponentFixedUpdate is not null ) fixedUpdateComponents.Add( c );
			if ( c is IPreRenderSubscriber ) preRenderComponents.Add( c );
		}
	}

	/// <summary>
	/// Adds object instance, indexed by type, to the directory so that its values are accessible by GetAll
	/// </summary>
	internal void RemoveObjectFromDirectory( object obj )
	{
		if ( !objectsInIndex.Remove( obj ) ) return;

		var t = obj.GetType();
		while ( t is not null )
		{
			if ( t == typeof( object ) ) break;
			if ( t == typeof( Component ) ) break;
			if ( t == typeof( GameObjectSystem ) ) break;

			objectIndex.GetOrCreate( t ).Remove( obj );

			foreach ( var i in t.GetInterfaces() )
			{
				objectIndex.GetOrCreate( i ).Remove( obj );
			}

			t = t.BaseType;
		}

		// little bit of magic for components
		if ( obj is Component c )
		{
			if ( c is IUpdateSubscriber || c.OnComponentUpdate is not null ) updateComponents.Remove( c );
			if ( c is IFixedUpdateSubscriber || c.OnComponentFixedUpdate is not null ) fixedUpdateComponents.Remove( c );
			if ( c is IPreRenderSubscriber ) preRenderComponents.Remove( c );
		}
	}

	/// <summary>
	/// Get all objects of this type. This could be a component or a GameObjectSystem, or other stuff in the future.
	/// </summary>
	[Pure]
	public IEnumerable<T> GetAll<T>()
	{
		if ( !objectIndex.TryGetValue( typeof( T ), out var set ) || set.Count == 0 )
			yield break;

		foreach ( var e in set.EnumerateLocked() )
		{
			T c = (T)e;
			if ( c is null ) continue;
			if ( c is IValid v && !v.IsValid ) continue;
			yield return c;
		}
	}

	/// <summary>
	/// Get all objects of this type. This could be a component or a GameObjectSystem, or other stuff in the future.
	/// </summary>
	[Pure]
	public void GetAll<T>( List<T> target )
	{
		if ( !objectIndex.TryGetValue( typeof( T ), out var set ) || set.Count == 0 )
			return;

		foreach ( var e in set.EnumerateLocked() )
		{
			T c = (T)e;
			if ( c is null ) continue;
			if ( c is IValid v && !v.IsValid ) continue;

			target.Add( c );
		}
	}

	/// <summary>
	/// Gets the first object found of this type. This could be a component or a GameObjectSystem, or other stuff in the future.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <returns></returns>
	[Pure]
	public T Get<T>()
	{
		if ( !objectIndex.TryGetValue( typeof( T ), out var set ) || set.Count == 0 )
			return default;

		foreach ( var e in set.EnumerateLocked() )
		{
			T c = (T)e;
			if ( c is null ) continue;
			if ( c is IValid v && !v.IsValid ) continue;
			return c;
		}

		return default;
	}

	/// <summary>
	/// Gets the first object found of this type. This could be a component or a GameObjectSystem, or other stuff in the future.
	/// </summary>
	/// <typeparam name="T"></typeparam>
	/// <param name="obj"></param>
	/// <returns>true if the Scene contained a valid object; otherwise, false.</returns>
	[Pure]
	public bool TryGet<T>( out T obj )
	{
		if ( objectIndex.TryGetValue( typeof( T ), out var set ) && set.Count > 0 )
		{
			foreach ( var e in set.EnumerateLocked() )
			{
				T c = (T)e;
				if ( c is null ) continue;
				if ( c is IValid v && !v.IsValid ) continue;

				obj = c;
				return true;
			}
		}

		obj = default;
		return false;
	}
}
