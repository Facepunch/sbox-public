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

		if ( p.Object is Component component )
		{
			if ( component.Flags.HasFlag( ComponentFlags.Deserializing ) )
			{
				// Do nothing if the component is deserializing. This should be the first
				// time the property is loaded, so we don't want to invoke a callback.
				return;
			}

			var go = component.GameObject;
			if ( go.IsValid()
				 && (go.Flags.HasFlag( GameObjectFlags.Deserializing )
					 || go.Flags.HasFlag( GameObjectFlags.Loading )) )
			{
				// Do nothing if the component's GameObject is deserializing or
				// we're loading.
				return;
			}
		}

		try
		{
			if ( method is not null )
				method.Invoke( p.Object, new[] { oldValue, p.Value } );
			else if ( methodWithoutParams is not null )
				methodWithoutParams.Invoke( p.Object );
			else
				Log.Warning(
					$"{type.Name}.{property.Name} has [Change] but we can not find {functionName}( {property.PropertyType} oldValue, {property.PropertyType} newValue )" );
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}
}
