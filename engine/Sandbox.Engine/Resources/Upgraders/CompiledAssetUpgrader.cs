using Editor;
using NativeEngine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Sandbox.Internal;

/// <summary>
/// Runs built-in upgrades on decoded native resources before their dependencies are loaded.
/// </summary>
internal static class CompiledAssetUpgrader
{
	// Resources can load before the editor or game TypeLibrary exists.
	static readonly Dictionary<string, Action<KeyValues3>[]> upgraders = Discover( typeof( CompiledAssetUpgrader ).Assembly );

	internal static Dictionary<string, Action<KeyValues3>[]> Discover( Assembly assembly )
	{
		try
		{
			var methods = assembly.GetTypes()
				.SelectMany( FindUpgraders )
				.OrderBy( x => x.Attribute.Order )
				.ThenBy( x => x.Method.DeclaringType.FullName, StringComparer.Ordinal )
				.ThenBy( x => x.Method.Name, StringComparer.Ordinal );

			var result = new Dictionary<string, Action<KeyValues3>[]>( StringComparer.OrdinalIgnoreCase );
			foreach ( var group in methods.GroupBy( x => "." + x.Attribute.Extension, StringComparer.OrdinalIgnoreCase ) )
			{
				result[group.Key] = group.Select( x => x.Upgrade )
					.Distinct()
					.ToArray();
			}

			return result;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't discover compiled asset upgraders in '{assembly.FullName}'." );
			return new Dictionary<string, Action<KeyValues3>[]>( StringComparer.OrdinalIgnoreCase );
		}
	}

	static IEnumerable<(MethodInfo Method, AssetUpgraderAttribute Attribute, Action<KeyValues3> Upgrade)>
		FindUpgraders( Type type )
	{
		try
		{
			const BindingFlags flags = BindingFlags.Static | BindingFlags.Public |
				BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
			return type.GetMethods( flags )
				.SelectMany( method => method.GetCustomAttributes<AssetUpgraderAttribute>()
					.Where( attribute => attribute.Target == AssetUpgradeTarget.Compiled )
					.Select( attribute => (method, attribute) ) )
				.Where( x => IsCompiledUpgrader( x.method ) )
				.Select( x => (x.method, x.attribute, x.method.CreateDelegate<Action<KeyValues3>>()) )
				.ToArray();
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't discover compiled asset upgrader '{type.FullName}'." );
			return [];
		}
	}

	static bool IsCompiledUpgrader( MethodInfo method )
	{
		if ( method.ReturnType != typeof( void ) || method.ContainsGenericParameters )
			return false;

		var parameters = method.GetParameters();
		return parameters.Length == 1 && parameters[0].ParameterType == typeof( KeyValues3 );
	}

	internal static void UpgradeCompiled( string path, KeyValues3 data )
	{
		if ( data.IsNull || string.IsNullOrEmpty( path ) )
			return;

		var extension = Path.GetExtension( path );
		if ( extension.EndsWith( "_c", StringComparison.OrdinalIgnoreCase ) )
			extension = extension[..^2];

		if ( !upgraders.TryGetValue( extension, out var upgrades ) )
			return;

		try
		{
			foreach ( var upgrade in upgrades )
				upgrade( data );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Couldn't upgrade compiled asset '{path}'." );
		}
	}
}
