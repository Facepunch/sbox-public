using System.Text.Json;
using System.Text.Json.Nodes;
using NativeEngine;

namespace Sandbox.Engine;

/// <summary>Active key bindings, defaults and console commands. Owns configuration persistence and legacy migration.</summary>
[SkipHotload]
internal static partial class KeyBindings
{
	const string Unbound = "<unbound>";
	static readonly Dictionary<string, string> defaults = new( StringComparer.OrdinalIgnoreCase );
	static readonly Binding[] bindings = new Binding[(int)ButtonCode.BUTTON_CODE_COUNT];
	static JsonObject config = new();

	readonly record struct Binding( string Command, bool User );

	internal static void Reset()
	{
		_storage = null;
		defaults.Clear();
		Array.Clear( bindings );
		config = new();
	}

	static JsonObject Parse( string json ) => JsonNode.Parse( json, new JsonNodeOptions { PropertyNameCaseInsensitive = true } ).AsObject();

	internal static void LoadDefaults( string json )
	{
		if ( Parse( json )["bindings"] is not JsonObject section ) return;
		foreach ( var (name, value) in section )
			if ( value is JsonValue ) defaults[name] = value.ToString();
	}

	internal static void SetBinding( ButtonCode code, string command, bool user = true )
	{
		if ( (uint)code >= bindings.Length ) return;
		bindings[(int)code] = new( command ?? "", user );
	}

	internal static string GetBinding( ButtonCode code ) => (uint)code < bindings.Length ? bindings[(int)code].Command ?? "" : null;

	static void Apply( string name, string command, bool user )
	{
		var code = KeyTranslation.StringToButtonCode( name );
		if ( code == ButtonCode.BUTTON_CODE_INVALID )
		{
			Log.Warning( $"Encountered unknown key name \"{name}\"!" );
			return;
		}
		SetBinding( code, string.Equals( command, Unbound, StringComparison.OrdinalIgnoreCase ) ? "" : command, user );
	}

	internal static void UnbindAll()
	{
		Array.Fill( bindings, new Binding( "", true ) );
	}

	internal static void ResetToDefaults()
	{
		UnbindAll();
		foreach ( var (name, command) in defaults ) Apply( name, command, false );
		config = new();
	}

	internal static bool Read( string json )
	{
		JsonObject saved;
		KeyValuePair<string, string>[] entries;
		try
		{
			saved = JsonNode.Parse( json, new JsonNodeOptions { PropertyNameCaseInsensitive = true } ) as JsonObject;
			if ( saved is null ) throw new JsonException( "Expected a binding configuration object." );
			ValidateObjects( saved );
			if ( saved.ContainsKey( "bindings" ) && saved["bindings"] is not JsonObject )
				throw new JsonException( "Expected a bindings object." );
			entries = (saved["bindings"] as JsonObject)?.Select( x => new KeyValuePair<string, string>( x.Key,
				x.Value is JsonValue ? x.Value.ToString() : throw new JsonException( "Expected a binding value." ) ) ).ToArray() ?? [];
		}
		catch ( Exception e ) when ( e is JsonException or ArgumentException or InvalidOperationException )
		{
			Log.Warning( $"Error reading key bindings: {e.Message}" );
			return false;
		}

		ResetToDefaults();
		config = saved;
		foreach ( var (name, value) in entries ) Apply( name, value, true );
		return true;
	}

	// JsonObject detects duplicate names lazily. Materialize every object before changing live bindings.
	static void ValidateObjects( JsonNode node )
	{
		if ( node is JsonObject obj )
			foreach ( var entry in obj ) ValidateObjects( entry.Value );
		else if ( node is JsonArray array )
			foreach ( var value in array ) ValidateObjects( value );
	}

	internal static string Write()
	{
		if ( config["bindings"] is not JsonObject section )
			config["bindings"] = section = new JsonObject( new JsonNodeOptions { PropertyNameCaseInsensitive = true } );

		for ( var i = 1; i < bindings.Length; i++ )
		{
			var name = KeyTranslation.CodeToString( (ButtonCode)i );
			var binding = bindings[i];
			var empty = string.IsNullOrEmpty( binding.Command );
			if ( binding.User && (!empty || defaults.ContainsKey( name )) )
				section[name] = binding.Command ?? "";
			else
				section.Remove( name );
		}

		return config.ToJsonString();
	}

	[ConCmd( "bind", ConVarFlags.Protected, Help = "Bind a key." )]
	internal static void Bind( params string[] args )
	{
		if ( args.Length == 0 )
		{
			Log.Info( "bind <key> [command] : attach a command/inputvalue to a key" );
			return;
		}
		var code = KeyTranslation.StringToButtonCode( args[0] );
		if ( code == ButtonCode.BUTTON_CODE_INVALID )
		{
			Log.Info( $"bind: \"{args[0]}\" isn't a valid key" );
			return;
		}
		if ( args.Length == 1 ) Log.Info( $"bind: \"{KeyTranslation.CodeToString( code )}\" = \"{GetBinding( code )}\"" );
		else SetBinding( code, string.Join( " ", args.Skip( 1 ) ) );
	}

	[ConCmd( "unbind", ConVarFlags.Protected, Help = "Unbind a key." )]
	internal static void Unbind( params string[] args )
	{
		if ( args.Length is < 1 or > 2 )
		{
			Log.Info( "unbind <key> : remove commands from a key" );
			return;
		}
		var code = KeyTranslation.StringToButtonCode( args[0] );
		if ( code == ButtonCode.BUTTON_CODE_INVALID ) Log.Info( $"\"{args[0]}\" isn't a valid key" );
		else SetBinding( code, "" );
	}

	[ConCmd( "unbindall", ConVarFlags.Protected, Help = "Unbind all keys." )]
	static void UnbindAllCommand( params string[] args )
	{
		if ( args.Length != 0 ) Log.Info( "unbindall : unbind all commands" );
		else UnbindAll();
	}

	[ConCmd( "binddefaults", ConVarFlags.Protected, Help = "Bind all keys to their default values." )]
	static void BindDefaults( params string[] args )
	{
		if ( args.Length > 1 ) Log.Info( "binddefaults : bind all commands to default settings" );
		else ResetToDefaults();
	}

	[ConCmd( "writekeybindings", ConVarFlags.Protected, Help = "Saves current key bindings to disk." )]
	static void SaveCommand() => Save();

	[ConCmd( "key_listboundkeys", Help = "List bound keys with bindings." )]
	static void List( params string[] args )
	{
		if ( args.Length > 1 ) Log.Info( "usage: key_listboundkeys" );
		else PrintMatches( "" );
	}

	[ConCmd( "key_findbinding", Help = "Find key bound to specified command string." )]
	static void Find( params string[] args )
	{
		if ( args.Length is < 1 or > 2 || string.IsNullOrEmpty( args[0] ) ) Log.Info( "usage: key_findbinding <substring>" );
		else PrintMatches( args[0] );
	}

	static void PrintMatches( string substring )
	{
		for ( var i = 0; i < bindings.Length; i++ )
		{
			var command = bindings[i].Command;
			if ( string.IsNullOrEmpty( command ) || !command.Contains( substring, StringComparison.Ordinal ) ) continue;
			Log.Info( $"\"{KeyTranslation.CodeToString( (ButtonCode)i )}\" = \"{command}\"" );
		}
	}
}
