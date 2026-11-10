using NativeEngine;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Engine;

/// <summary>Engine configuration lifecycle and persistence of archived native console variables.</summary>
[SkipHotload]
internal static class ConsoleConfig
{
	const string JsonFile = "cfg/machine_convars.json";
	const string LegacyFile = "cfg/machine_convars.vcfg";
	static bool initialized;
	static VariableStorage _variables;

	/// <summary>
	/// Load binding defaults and user configuration, then attempt JSON migration. Console applications skip user configuration.
	/// </summary>
	internal static bool Initialize( bool bindingsEnabled, bool developer, bool console )
	{
		if ( !KeyBindings.Initialize( bindingsEnabled, developer ) ) return false;
		if ( console ) return true;
		if ( Application.IsUnitTest )
		{
			KeyBindings.ResetToDefaults();
			return true;
		}
		initialized = true;
		KeyBindings.Load();

		_variables = new VariableStorage( EngineFileSystem.Root.CreateSubSystem( "core" ) );
		LoadVariables( EngineFileSystem.CoreContent, "cfg/machine_convars_default.vcfg", legacy: true );
		_variables.Initialize( ConVarSystem.ArchivedNativeVariables );
		return true;
	}

	internal static void ExecuteAutoexec()
	{
		if ( initialized ) ConsoleScripts.Exec( "autoexec.cfg" );
	}

	[ConCmd( "host_writeconfig", ConVarFlags.Protected, Help = "Save user bindings and archived engine variables." )]
	internal static void Save()
	{
		if ( !initialized || Application.IsUnitTest ) return;
		KeyBindings.Save();
		_variables?.Save( ConVarSystem.ArchivedNativeVariables );
	}

	internal static void Shutdown()
	{
		try
		{
			Save();
		}
		finally
		{
			initialized = false;
			_variables?.Dispose();
			_variables = null;
			KeyBindings.Reset();
		}
	}

	/// <summary>Owns one configuration filesystem and preserves the load result across subsequent saves.</summary>
	internal sealed class VariableStorage( BaseFileSystem files ) : IDisposable
	{
		bool _canSave;

		internal bool Initialize( IEnumerable<Command> commands )
		{
			_canSave = LoadUserVariables( files, out var migrated );
			if ( _canSave && migrated ) Save( commands );
			return _canSave;
		}

		internal bool Save( IEnumerable<Command> commands ) => _canSave && SaveVariables( files, commands );

		public void Dispose()
		{
			_canSave = false;
			files.Dispose();
		}
	}

	/// <summary>
	/// Validate the document before applying saved variables. Missing files succeed; malformed or unreadable files return false.
	/// </summary>
	internal static bool LoadVariables( BaseFileSystem fs, string path, bool legacy = false )
		=> LoadVariables( fs, path, legacy, out _ );

	/// <summary>Preserve malformed JSON for recovery; unreadable files continue to block saving.</summary>
	internal static bool LoadUserVariables( BaseFileSystem fs )
		=> LoadUserVariables( fs, out _ );

	static bool LoadUserVariables( BaseFileSystem fs, out bool migrated )
	{
		migrated = false;
		try
		{
			if ( !fs.FileExists( JsonFile ) )
			{
				migrated = fs.FileExists( LegacyFile );
				return LoadVariables( fs, LegacyFile, legacy: true );
			}
			if ( LoadVariables( fs, JsonFile, false, out var malformed ) ) return true;
			if ( !malformed ) return false;
			var backup = JsonFile + ".bad";
			if ( fs.FileExists( backup ) ) backup += $".{Guid.NewGuid():N}";
			fs.MoveFile( JsonFile, backup );
			Log.Warning( $"Preserved invalid console configuration as '{backup}'. Using defaults." );
			return true;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Couldn't recover console configuration: {e.Message}" );
			return false;
		}
	}

	static bool LoadVariables( BaseFileSystem fs, string path, bool legacy, out bool malformed )
	{
		malformed = false;
		try
		{
			if ( !fs.FileExists( path ) ) return true;
			var text = fs.ReadAllText( path );
			if ( legacy ) text = EngineGlue.KeyValuesToJson( text );
			if ( text is null ) throw new JsonException( "Expected a configuration document." );
			var root = JsonNode.Parse( text )?.AsObject();
			if ( root?["convars"] is not JsonObject values ) throw new JsonException( "Expected a convars object." );
			// Validate the whole document before applying any values.
			var entries = values.ToDictionary( x => x.Key, x => x.Value?.GetValueKind() switch
			{
				JsonValueKind.String => x.Value.GetValue<string>(),
				JsonValueKind.Number when legacy => x.Value.ToJsonString(),
				_ => throw new JsonException( "Convar values must be strings." )
			} );
			foreach ( var (name, value) in entries )
			{
				var command = ConVarSystem.Find( name );
				if ( command is not { IsVariable: true, IsSaved: true } ) continue;
				if ( command.IsCheat && !Game.CheatsEnabled ) continue;
				command.Value = value;
			}
			return true;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException )
		{
			malformed = e is JsonException or InvalidOperationException or ArgumentException;
			Log.Warning( $"Couldn't load console configuration '{path}': {e.Message}" );
			return false;
		}
	}

	/// <summary>
	/// Write archived variables as JSON, then delete the legacy VCFG. Returns false on a filesystem failure.
	/// </summary>
	internal static bool SaveVariables( BaseFileSystem fs, IEnumerable<Command> commands )
	{
		try
		{
			var values = new JsonObject();
			foreach ( var command in commands.OrderBy( x => x.Name, StringComparer.OrdinalIgnoreCase ) )
				values[command.Name] = command.Value;
			fs.WriteAllTextAtomic( JsonFile, new JsonObject { ["convars"] = values }.ToJsonString( new JsonSerializerOptions { WriteIndented = true } ) );
			if ( fs.FileExists( LegacyFile ) ) fs.DeleteFile( LegacyFile );
			return true;
		}
		catch ( Exception e ) when ( e is IOException or UnauthorizedAccessException )
		{
			Log.Warning( $"Couldn't save console configuration: {e.Message}" );
			return false;
		}
	}
}
