using System.Text.Json.Nodes;

namespace Editor.ShaderGraph;

[AttributeUsage( AttributeTargets.Method )]
public class SGJsonUpgraderAttribute : Attribute
{
	/// <summary>
	/// The version of this upgrade.
	/// </summary>
	public int Version { get; }

	/// <summary>
	/// The type we're targeting for this upgrade.
	/// </summary>
	public Type Type { get; }

	public SGJsonUpgraderAttribute( Type type, int version )
	{
		Type = type;
		Version = version;
	}
}

// I could probably use JsonUpgrader but that's marked as internal at the moment... :(
[SkipHotload]
internal static class SGJsonUpgrader
{
	private static (MethodDescription Method, SGJsonUpgraderAttribute Attribute)[] _methods;

	static SGJsonUpgrader()
	{
		Update();
	}

	[Event( "hotloaded" )]
	static void Update()
	{
		_methods = EditorTypeLibrary.GetMethodsWithAttribute<SGJsonUpgraderAttribute>().ToArray();
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
		if ( _methods == null )
		{
			return;
		}

		foreach ( var item2 in from x in _methods
							   where x.Attribute.Type == targetType
							   orderby x.Attribute.Version
							   where x.Attribute.Version > version
							   select x )
		{
			try
			{
				MethodDescription item = item2.Method;
				item.Invoke( null, [ json ] );
			}
			catch ( Exception exception )
			{
				Log.Warning( exception, $"A type version upgrader ( {item2.Attribute.Type}, version {item2.Attribute.Version}) threw an exception while trying to upgrade, so we halted the upgrade." );
				break;
			}
			finally
			{
				json["__version"] = item2.Attribute.Version;
			}
		}
	}
}
