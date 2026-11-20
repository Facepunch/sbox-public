using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

public sealed class ToolPreset
{
	public Guid Id { get; set; }
	public string Name { get; set; }
	public JsonObject Values { get; set; } = new();
}

/// <summary>Personal presets in the game's local data folder. No RPCs or workshop storage.</summary>
public static class ToolPresetStore
{
	public sealed class PresetFile
	{
		public int Version { get; set; } = 1;
		public List<ToolPreset> Presets { get; set; } = new();
	}

	public static int Revision { get; private set; }
	public static string Key( ToolMode tool ) => $"tool-presets/{tool.CookiePrefix}";

	static PresetFile Read( string key )
	{
		if ( !LocalData.Has( key ) ) return new();
		// Do not silently replace a corrupt or newer-format file with an empty list.
		var file = FileSystem.Data.ReadJson<PresetFile>( $"{key}.json" );
		if ( file is null || file.Version != 1 || file.Presets is null
			|| file.Presets.Any( p => p is null || p.Id == Guid.Empty || p.Values is null || string.IsNullOrWhiteSpace( p.Name ) )
			|| file.Presets.Select( p => p.Id ).Distinct().Count() != file.Presets.Count )
			throw new InvalidOperationException( "#ui.tool_presets.read_error" );
		return file;
	}

	public static List<ToolPreset> Load( string key ) => Read( key ).Presets
		.OrderBy( p => p.Name, StringComparer.CurrentCultureIgnoreCase ).ToList();

	public static ToolPreset Save( string key, Guid id, string name, JsonObject values )
	{
		name = name?.Trim();
		if ( string.IsNullOrEmpty( name ) || name.Length > 64 )
			throw new ArgumentException( "#ui.tool_presets.invalid_name" );
		ArgumentNullException.ThrowIfNull( values );
		var file = Read( key );
		if ( file.Presets.Any( p => p.Id != id && string.Equals( p.Name, name, StringComparison.OrdinalIgnoreCase ) ) )
			throw new ArgumentException( "#ui.tool_presets.duplicate_name" );

		var preset = id == Guid.Empty ? new ToolPreset { Id = Guid.NewGuid() } : file.Presets.FirstOrDefault( p => p.Id == id );
		if ( preset is null ) throw new InvalidOperationException( "#ui.tool_presets.missing" );
		preset.Name = name;
		preset.Values = (JsonObject)values.DeepClone();
		if ( id == Guid.Empty ) file.Presets.Add( preset );
		LocalData.Set( key, file );
		Revision++;
		return preset;
	}

	public static void Delete( string key, Guid id )
	{
		var file = Read( key );
		if ( file.Presets.RemoveAll( p => p.Id == id ) == 0 ) return;
		LocalData.Set( key, file );
		Revision++;
	}

	public static string ErrorText( Exception exception )
	{
		Log.Warning( exception, "Tool preset operation failed" );
		return Game.Language.GetPhrase( exception.Message.StartsWith( "#ui.tool_presets." )
			? exception.Message.TrimStart( '#' ) : "ui.tool_presets.storage_error" );
	}
}

/// <summary>Only editable tool settings are saved; runtime objects, actions and hidden state are excluded.</summary>
public static class ToolPresetSettings
{
	public static bool IsSetting( SerializedProperty property ) =>
		!property.IsMethod && property.IsEditable && UISheet.PropertiesAndButtons( property )
		&& !property.HasAttribute<JsonIgnoreAttribute>() && IsSettingType( property.PropertyType );

	static bool IsSettingType( Type type ) => type is not null
		&& !type.IsAssignableTo( typeof( Component ) ) && !type.IsAssignableTo( typeof( GameObject ) )
		&& !type.IsAssignableTo( typeof( Delegate ) );

	static IEnumerable<PropertyDescription> Properties( ToolMode tool ) =>
		Game.TypeLibrary.GetType( tool.GetType() ).Properties.Where( p =>
			!p.IsStatic && p.CanRead && p.CanWrite && p.HasAttribute<PropertyAttribute>()
			&& !p.HasAttribute<HideAttribute>() && !p.HasAttribute<JsonIgnoreAttribute>() && IsSettingType( p.PropertyType ) );

	public static JsonObject Capture( ToolMode tool )
	{
		var values = new JsonObject();
		foreach ( var property in Properties( tool ) )
			values[property.Name] = Json.ToNode( property.GetValue( tool ), property.PropertyType );
		return values;
	}

	/// <summary>
	/// The settings a freshly made copy of the tool has, read from a draft so nothing live is touched.
	/// </summary>
	public static JsonObject Defaults( ToolMode tool )
	{
		using var draft = new ToolPresetDraft( tool, new JsonObject() );
		return Capture( draft.Tool );
	}

	public static void Apply( ToolMode tool, JsonObject values )
	{
		// Resolve all values before changing the target. Unknown/removed settings are ignored.
		var changes = Properties( tool ).Where( p => values.ContainsKey( p.Name ) )
			.Select( p => (Property: p, Value: Json.FromNode( values[p.Name], p.PropertyType )) ).ToList();
		foreach ( var change in changes ) change.Property.SetValue( tool, change.Value );
	}
}

/// <summary>A disabled tool in its own scene: normal property controls, without touching live tools or cookies.</summary>
public sealed class ToolPresetDraft : IDisposable
{
	readonly Scene _scene;
	public ToolMode Tool { get; }

	public ToolPresetDraft( ToolMode source, JsonObject values )
	{
		_scene = new Scene { WantsSystemScene = false };
		try
		{
			using ( _scene.Push() )
			{
				var go = new GameObject( false, "Tool preset draft" );
				Tool = (ToolMode)go.Components.Create( Game.TypeLibrary.GetType( source.GetType() ), false );
				ToolPresetSettings.Apply( Tool, values );
			}
		}
		catch { _scene.Destroy(); throw; }
	}

	public void Dispose() => _scene.Destroy();
}
