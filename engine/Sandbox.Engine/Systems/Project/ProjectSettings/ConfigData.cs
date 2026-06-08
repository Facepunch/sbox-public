using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Project configuration data is derived from this class
/// </summary>
public abstract class ConfigData
{
	[JsonPropertyName( "__guid" ), Hide]
	public Guid Guid { get; set; } = Guid.NewGuid();

	/// <summary>
	/// Whether this config was loaded from a file on disk, or created with code defaults.
	/// </summary>
	[JsonIgnore, Hide]
	public bool LoadedFromDisk { get; internal set; }

	[JsonIgnore, Hide]
	public virtual int Version => 1;

	public JsonObject Serialize()
	{
		OnValidate();

		var obj = Json.SerializeAsObject( this );
		obj["__schema"] = "configdata";
		obj["__type"] = GetType().Name;
		obj["__version"] = Version;

		return obj;
	}

	public void Deserialize( string json )
	{
		var jso = Json.ParseToJsonObject( json );
		if ( jso is null ) return;

		// read schema, version etc, upgrade if needed
		var serializedVersion = (int)(jso["__version"] ?? 1);
		if ( serializedVersion < Version )
		{
			// Log.Warning( $"{this} needs an API update, running upgraders (from version {serializedVersion} to {Version})" );
			JsonUpgrader.Upgrade( serializedVersion, jso, GetType() );
		}

		Json.DeserializeToObject( this, jso );

		OnValidate();
	}

	/// <summary>
	/// Called after deserialization, and before serialization. A place to error check and make sure everything is fine.
	/// </summary>
	protected virtual void OnValidate()
	{
		// nothing
	}
}
