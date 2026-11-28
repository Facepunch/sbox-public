using Editor.NodeEditor;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Editor.ShaderGraph;

internal static class ShaderGraphJsonUpgrader
{
	private static (MethodDescription Method, JsonUpgraderAttribute Attribute)[] _methods;

	[EditorEvent.Hotload]
	[Event( "shadergraph.created" )]
	private static void UpdateUpgraders()
	{
		_methods = EditorTypeLibrary.GetMethodsWithAttribute<JsonUpgraderAttribute>().ToArray();
	}

	/// <summary>
	/// Runs through all upgraders that match its class where our version is lower than the specified version.
	/// </summary>
	/// <param name="version">The current version that's serialized in the json object</param>
	/// <param name="json"></param>
	/// <param name="targetType"></param>
	/// <param name="options"></param>
	public static void Upgrade( int version, JsonObject json, Type targetType, JsonSerializerOptions options )
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
				e.Method.Invoke( null, new object[] { json, options } );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"A shader graph version upgrader ({e.Attribute.Type}, version {e.Attribute.Version}) threw an exception while trying to upgrade, so we halted the upgrade." );
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
