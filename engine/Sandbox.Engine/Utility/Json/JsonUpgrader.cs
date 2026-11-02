using Sandbox.Internal;
using System.Text.Json.Nodes;

namespace Sandbox;

[SkipHotload]
internal static class JsonUpgrader
{
	private static (MethodDescription Method, JsonUpgraderAttribute Attribute)[] _methods;

	public static void UpdateUpgraders( TypeLibrary typeLibrary )
	{
		_methods = typeLibrary.GetMethodsWithAttribute<JsonUpgraderAttribute>()
			// A package's upgrader for an engine type would run for everyone loading that type, the menu included
			.Where( x => x.Attribute.Type is not null && (x.Method.MemberInfo?.DeclaringType?.Assembly == x.Attribute.Type.Assembly || IsolatedAssemblyContext.IsPackageType( x.Attribute.Type )) )
			.ToArray();
	}

	/// <summary>
	/// Runs through all upgraders that match its class where our version is lower than the specified version.
	/// </summary>
	/// <param name="version">The current version that's serialized in the json object</param>
	/// <param name="json"></param>
	/// <param name="targetType"></param>
	public static void Upgrade( int version, JsonObject json, Type targetType )
	{
		// This is normal, upgraders have not been initialized using UpdateUpgraders
		// it's fine to ignore this.
		if ( _methods is null )
			return;

		foreach ( var e in _methods
			.Where( x => x.Attribute.Type == targetType )
			.OrderBy( x => x.Attribute.Version )
			.Where( x => x.Attribute.Version > version ) )
		{
			try
			{
				e.Method.Invoke( null, new[] { json } );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"A component version upgrader ({e.Attribute.Type}, version {e.Attribute.Version}) threw an exception while trying to upgrade, so we halted the upgrade." );
				// Let's stop trying to upgrade because something is broken.
				return;
			}
			finally
			{
				// Update our serialized version step by step.
				json["__version"] = e.Attribute.Version;
			}
		}
	}
}
