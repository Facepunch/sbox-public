using NativeEngine;
using Sandbox.Engine;
using System;

namespace Sandbox;

public static partial class MenuUtility
{
	/// <summary>
	/// Allows to menu addon to interact with input configuration
	/// </summary>
	public static class Input
	{
		public static IReadOnlyList<InputAction> GetCommonInputs() => Sandbox.Engine.Input.CommonInputs;

		public static string GetBind( string group, string actionName, int slot, out bool isDefault )
		{
			isDefault = false;

			var collection = Sandbox.Engine.InputBinds.FindCollection( group );
			if ( collection != null )
			{
				var buttons = collection.Get( actionName, slot );
				if ( !string.IsNullOrWhiteSpace( buttons ) ) return buttons;
			}

			return null;
		}

		public static void ResetBinds( string group )
		{
			var collection = InputBinds.FindCollection( group );
			collection.ResetToDefaults();
		}

		public static void SetBind( string group, string actionName, string buttonName, int slot )
		{
			var collection = InputBinds.FindCollection( group );
			collection.Set( actionName, slot, buttonName );
		}

		public static void SaveBinds( string group )
		{
			var collection = InputBinds.FindCollection( group );
			collection.SaveToDisk();
		}

		/// <summary>
		/// For binding reasons, get the buttons pressed until one of them is released. Called with no buttons if escape backs out.
		/// </summary>
		public static void TrapButtons( Action<string[]> callback )
		{
			Game.InputContext.StartTrapping( callback );
		}
	}
}
