using System.ComponentModel;

namespace Sandbox;

/// <summary>
/// Dispatches <see cref="ChangeAttribute"/> callbacks - the code generator wraps the setter of a
/// [Change] property to come through here. Never called manually.
/// </summary>
[EditorBrowsable( EditorBrowsableState.Never )]
public static class ChangeCallback
{
	/// <summary>
	/// Sets the property and invokes its [Change] callback when the value differs.
	/// </summary>
	public static void OnPropertySet<T>( in WrappedPropertySet<T> p )
	{
		var attribute = p.GetAttribute<ChangeAttribute>();
		var property = Game.TypeLibrary.GetMemberByIdent( p.MemberIdent ) as PropertyDescription;

		// The type system doesn't know this property - a private one needs [Expose] for the change
		// callback to resolve. Still set the value, just skip the callback.
		if ( property is null )
		{
			Log.Warning( $"[Change] property {p.PropertyName} isn't in the type library - is it private without [Expose]?" );
			p.Setter( p.Value );
			return;
		}

		var type = property.TypeDescription;
		var functionName = attribute.Name ?? $"On{property.Name}Changed";
		var isStatic = p.IsStatic;

		var method = type.Methods.FirstOrDefault( x =>
			x.IsNamed( functionName ) &&
			x.IsStatic == isStatic &&
			x.Parameters.Length == 2 &&
			x.Parameters[0].ParameterType == typeof( T ) &&
			x.Parameters[1].ParameterType == typeof( T ) );

		var methodWithoutParams = method is not null ? null : type.Methods.FirstOrDefault( x =>
			x.IsNamed( functionName ) &&
			x.IsStatic == isStatic &&
			x.Parameters.Length == 0 );

		var oldValue = property.GetValue( p.Object );
		var isTheSame = Equals( p.Value, oldValue );

		p.Setter( p.Value );

		if ( isTheSame )
			return;

		if ( p.Object is Component component && IsLoading( component ) )
		{
			// Values loaded from disk are the initial state, so there's nothing to call back about.
			// A synced value arriving while a networked object spawns is a real change from that
			// state though - hold on to it until the object has finished spawning.
			if ( Sandbox.Network.NetworkTable.IsReadingChanges )
				Defer( component, property, method, methodWithoutParams, functionName, oldValue );

			return;
		}

		// We're calling back about the latest value now, so a deferred callback would repeat it
		if ( _deferred.Count > 0 && p.Object is Component c )
			_deferred.RemoveAll( d => d.Target == c && d.Property == property );

		Invoke( p.Object, property, method, methodWithoutParams, functionName, oldValue, p.Value );
	}

	static bool IsLoading( Component component )
	{
		if ( component.Flags.HasFlag( ComponentFlags.Deserializing ) )
			return true;

		var go = component.GameObject;
		return go.IsValid()
			   && (go.Flags.HasFlag( GameObjectFlags.Deserializing )
				   || go.Flags.HasFlag( GameObjectFlags.Loading ));
	}

	static void Invoke( object target, PropertyDescription property, MethodDescription method, MethodDescription methodWithoutParams, string functionName, object oldValue, object newValue )
	{
		try
		{
			if ( method is not null )
				method.Invoke( target, new[] { oldValue, newValue } );
			else if ( methodWithoutParams is not null )
				methodWithoutParams.Invoke( target );
			else
				Log.Warning(
					$"{property.TypeDescription.Name}.{property.Name} has [Change] but we can not find {functionName}( {property.PropertyType} oldValue, {property.PropertyType} newValue )" );
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	record struct DeferredCallback( Component Target, PropertyDescription Property, MethodDescription Method, MethodDescription MethodWithoutParams, string FunctionName, object OldValue );

	static readonly List<DeferredCallback> _deferred = new();

	static void Defer( Component target, PropertyDescription property, MethodDescription method, MethodDescription methodWithoutParams, string functionName, object oldValue )
	{
		// Keep the first old value if the same property is written more than once while spawning
		foreach ( var d in _deferred )
		{
			if ( d.Target == target && d.Property == property )
				return;
		}

		_deferred.Add( new DeferredCallback( target, property, method, methodWithoutParams, functionName, oldValue ) );
	}

	/// <summary>
	/// Invoke [Change] callbacks for synced values that were received while their object was
	/// being spawned from the network. Called once the spawned objects in <paramref name="scene"/>
	/// are fully set up - anything still loading, or in another scene, stays queued.
	/// </summary>
	internal static void FlushDeferred( Scene scene )
	{
		_deferred.RemoveAll( d => !d.Target.IsValid() );

		if ( _deferred.Count == 0 )
			return;

		var pending = _deferred.Where( d => d.Target.Scene == scene && !IsLoading( d.Target ) ).ToArray();
		_deferred.RemoveAll( pending.Contains );

		foreach ( var d in pending )
		{
			var newValue = d.Property.GetValue( d.Target );
			if ( Equals( newValue, d.OldValue ) )
				continue;

			Invoke( d.Target, d.Property, d.Method, d.MethodWithoutParams, d.FunctionName, d.OldValue, newValue );
		}
	}
}
