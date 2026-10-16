using Sandbox.Engine;
using Sandbox.Internal;
using System;

namespace Sandbox;

public static partial class MenuUtility
{
	/// <summary>
	/// One of the running game's own pause menu buttons, read off a <see cref="PauseActionAttribute"/>
	/// method - what to show, and how to press it.
	/// </summary>
	public readonly record struct PauseAction( string Title, string Icon, string Tooltip, bool IsEnabled, bool DismissOnPress, Action Press );

	/// <summary>
	/// The running game's own pause menu buttons, in order, into <paramref name="actions"/> - its static
	/// <see cref="PauseActionAttribute"/> methods, off its type library (which keeps the list). Nothing
	/// when there's no game.
	/// </summary>
	public static void GetPauseActions( List<PauseAction> actions )
	{
		actions.Clear();

		if ( IGameInstance.Current is null )
			return;

		using ( GlobalContext.GameScope() )
		{
			var found = GlobalGameNamespace.TypeLibrary.GetMethodsWithAttribute<PauseActionAttribute>();

			foreach ( var (method, attribute) in found.OrderBy( x => x.Attribute.Order ).ThenBy( x => x.Attribute.Title, StringComparer.Ordinal ) )
			{
				if ( string.IsNullOrWhiteSpace( attribute.Title ) || method.Parameters.Length > 0 )
					continue;

				actions.Add( new PauseAction( attribute.Title, attribute.Icon, attribute.Tooltip, true, attribute.DismissOnPress, () => Press( method ) ) );
			}
		}
	}

	/// <summary>
	/// Press one - back in the game's context, where its code expects to be.
	/// </summary>
	static void Press( MethodDescription method )
	{
		using ( GlobalContext.GameScope() )
		{
			// It's the game's code - one that throws shouldn't take the menu with it
			try
			{
				method.Invoke( null );
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Pause action {method.TypeDescription?.Name}.{method.Name} threw when pressed" );
			}
		}
	}
}
