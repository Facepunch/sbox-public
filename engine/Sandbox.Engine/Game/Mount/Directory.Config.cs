using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Mounting;

//
// Per-mount settings, stored by ident in config/mounts.json:
//
// {
//   "rust": { "Enabled": true },
//   "hl2": { "Enabled": true, "Path": "/srv/games/hl2" }
// }
//
public static partial class Directory
{
	static readonly JsonSerializerOptions ConfigJsonOptions = new( JsonSerializerOptions.Default )
	{
		WriteIndented = true,
		DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault
	};

	const string ConfigFileName = "mounts.json";

	/// <summary>
	/// Read mounts.json into the host's settings, writing it out if it's missing or in the old format.
	/// Called before any mount initializes, they read their settings then.
	/// </summary>
	static void LoadConfig()
	{
		// No config filesystem (content worker), nothing to load
		if ( EngineFileSystem.Config is null )
			return;

		var settings = _system.Settings;

		// Write it out so server hosts have a file to fill in
		if ( !EngineFileSystem.Config.FileExists( ConfigFileName ) )
		{
			WriteConfig();
			return;
		}

		JsonNode root;
		try
		{
			root = JsonNode.Parse( EngineFileSystem.Config.ReadAllText( ConfigFileName ) );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"Couldn't parse {ConfigFileName}" );
			return;
		}

		if ( root is JsonArray array )
		{
			// Old format, just a list of idents. Convert to the new format.

			foreach ( string ident in array )
			{
				if ( string.IsNullOrWhiteSpace( ident ) ) continue;
				settings[ident] = new MountSettings { Enabled = true };
			}

			WriteConfig();
			return;
		}

		if ( root is not JsonObject obj )
		{
			Log.Warning( $"{ConfigFileName} should be an object of mount settings by ident" );
			return;
		}

		foreach ( var (ident, node) in obj )
		{
			try
			{
				var entry = node?.Deserialize<MountSettings>();
				if ( entry is not null )
					settings[ident] = entry;
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Invalid entry for mount '{ident}' in {ConfigFileName}" );
			}
		}
	}

	/// <summary>
	/// Mount everything enabled in mounts.json.
	/// </summary>
	internal static async Task MountFromConfig()
	{
		if ( _system is null ) return;

		var idents = _system.Settings.Where( x => x.Value.Enabled ).Select( x => x.Key ).ToArray();

		foreach ( var ident in idents )
		{
			await Mount( ident );
		}
	}

	/// <summary>
	/// Write which mounts are enabled to mounts.json, keeping every other setting.
	/// </summary>
	internal static void SaveConfig()
	{
		if ( _system is null ) return;

		// Loaded mounts share their entries with the host, the rest are kept as they were
		foreach ( var mount in _system.All )
		{
			mount.Config.Enabled = mount.IsMounted;
		}

		WriteConfig();
	}

	static void WriteConfig()
	{
		if ( EngineFileSystem.Config is null ) return;

		// Every registered mount has an entry, only write the ones with something set.
		// Default values aren't serialized, so an entry with nothing set comes out empty.
		var root = new JsonObject();
		foreach ( var (ident, settings) in _system.Settings )
		{
			if ( JsonSerializer.SerializeToNode( settings, ConfigJsonOptions ) is JsonObject { Count: > 0 } node )
				root[ident] = node;
		}

		EngineFileSystem.Config.WriteAllText( ConfigFileName, root.ToJsonString( ConfigJsonOptions ) );
	}
}
