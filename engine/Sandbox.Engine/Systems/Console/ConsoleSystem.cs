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
	/// Forwards to <see cref="ChangeCallback.OnPropertySet{T}"/> so already-compiled code still
	/// resolves.
	/// </summary>
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	[Obsolete( "Use ChangeCallback.OnPropertySet" )]
	public static void OnChangePropertySet<T>( in WrappedPropertySet<T> p ) => ChangeCallback.OnPropertySet( p );

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
