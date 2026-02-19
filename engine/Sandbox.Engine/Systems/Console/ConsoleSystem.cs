using Sandbox.Engine;

namespace Sandbox;

/// <summary>
/// A library to interact with the Console System.
/// </summary>
public static partial class ConsoleSystem
{
	/// <summary>
	/// Try to set a console variable. You will only be able to set variables that you have permission to set.
	/// </summary>
	public static void SetValue( string name, object value )
	{
		// Menu is allowed to access engine ConVars for settings, games can not
		ConVarSystem.SetValue( name, value?.ToString(), Game.IsMenu );
	}

	/// <summary>
	/// Get a console variable's value as a string.
	/// </summary>
	public static string GetValue( string name, string defaultValue = null )
	{
		// Menu is allowed to access engine ConVars for settings, games can not
		return ConVarSystem.GetValue( name, defaultValue, Game.IsMenu );
	}

	/// <summary>
	/// Invoke a method when a property with [Change] is changed.
	/// </summary>
	public static void OnChangePropertySet<T>( in WrappedPropertySet<T> p )
	{
		var attribute = p.GetAttribute<ChangeAttribute>();
		var property = Game.TypeLibrary.GetMemberByIdent( p.MemberIdent ) as PropertyDescription;
		var type = property.TypeDescription;
		var functionName = attribute.Name ?? $"On{property.Name}Changed";
		var isStatic = p.IsStatic;

		var method = type.Methods.FirstOrDefault( x =>
			x.IsNamed( functionName ) &&
			x.IsStatic == isStatic &&
			x.Parameters.Length <= 2 &&
			x.Parameters.All( p => p.ParameterType == typeof( T ) ) );

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
				method.Invoke( p.Object, [oldValue, p.Value] );
			else
				Log.Warning(
					$"{type.Name}.{property.Name} has [Change] but we can not find {functionName}( {property.PropertyType} oldValue, {property.PropertyType} newValue )" );
		}
		catch ( Exception e )
		{
			Log.Error( e );
		}
	}

	/// <summary>
	/// When we update a ConVar in code, call the ConsoleSystem.
	/// </summary>
	public static void OnWrappedSet<T>( in WrappedPropertySet<T> p )
	{
		var previous = p.Getter();

		if ( Equals( previous, p.Value ) )
			return;

		p.Setter( p.Value );
		var value = p.Getter();

		var convar = p.GetAttribute<ConVarAttribute>();
		if ( convar is null ) return;

		ConVarSystem.OnConVarChanged( convar.Name ?? p.PropertyName, value, previous );
	}

	/// <summary>
	/// When we query a convar property
	/// </summary>
	public static T OnWrappedGet<T>( in WrappedPropertyGet<T> p )
	{
		var convar = p.GetAttribute<ConVarAttribute>();

		// no convar found
		if ( convar is null )
			return p.Value;

		// not replicated
		if ( !convar.Flags.Contains( ConVarFlags.Replicated ) )
			return p.Value;

		var convarName = convar.Name ?? p.PropertyName;

		//
		// We have a replicated value in the string table, use it
		//
		if ( IGameInstanceDll.Current.TryGetReplicatedVarValue( convarName, out var replicatedValue ) )
		{
			return (T)replicatedValue.ToType( typeof( T ) );
		}

		return p.Value;
	}
}
