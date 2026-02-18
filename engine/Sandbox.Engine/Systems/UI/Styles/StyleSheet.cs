using Sandbox.Engine;

namespace Sandbox.UI;

public class StyleSheet
{
	public static List<StyleSheet> Loaded { get; internal set; } = new List<StyleSheet>();

	/// <summary>
	/// Between sessions we clear the stylesheets, so one gamemode can't accidentally
	/// use cached values from another.
	/// </summary>
	internal static void InitStyleSheets()
	{
		foreach ( var sheet in Loaded )
		{
			sheet?.Release();
		}

		Loaded.Clear();
	}

	public List<StyleBlock> Nodes { get; set; } = new List<StyleBlock>();
	public string FileName { get; internal set; }
	internal FileWatch Watcher { get; private set; }
	public List<string> IncludedFiles { get; set; } = new List<string>();
	public Dictionary<string, string> Variables;
	public Dictionary<string, KeyFrames> KeyFrames = new Dictionary<string, KeyFrames>( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, MixinDefinition> Mixins = new Dictionary<string, MixinDefinition>( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Releases the filesystem watcher so we won't get file changed events.
	/// </summary>
	public void Release()
	{
		Watcher?.Dispose();
		Watcher = null;
	}

	public static StyleSheet FromFile( string filename, IEnumerable<(string key, string value)> variables = null, bool failSilently = false )
	{
		filename = BaseFileSystem.NormalizeFilename( filename );

		var alreadyLoaded = Loaded.FirstOrDefault( x => x.FileName == filename );
		if ( alreadyLoaded != null )
			return alreadyLoaded;

		var sheet = new StyleSheet();
		sheet.UpdateFromFile( filename, failSilently );

		sheet.AddVariables( variables );
		sheet.FileName = filename;
		sheet.AddWatcher( filename );

		Loaded.Add( sheet );

		return sheet;
	}

	internal void AddFilename( string filename )
	{
		IncludedFiles.Add( filename );
		Watcher?.AddFile( filename );
	}

	public static StyleSheet FromString( string styles, string filename = "none", IEnumerable<(string key, string value)> variables = null )
	{
		try
		{
			return StyleParser.ParseSheet( styles, filename, variables );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Error parsing stylesheet: {e.Message}\n{e.StackTrace}" );
			return new StyleSheet();
		}
	}

	internal bool UpdateFromFile( string name, bool failSilently = false, GlobalContext ctx = null )
	{
		ctx ??= GlobalContext.Current;

		if ( ctx.FileMount is null )
		{
			return false;
		}

		if ( failSilently && !ctx.FileMount.FileExists( name ) )
		{
			Nodes = new();
			return true;
		}

		try
		{
			var text = ctx.FileMount.ReadAllText( name );
			if ( text is null ) throw new System.IO.FileNotFoundException( "File not found", name );

			return UpdateFromString( text, name, failSilently );
		}
		catch ( Exception e )
		{
			if ( !failSilently )
			{
				Log.Warning( e, $"Error opening stylesheet: {name} ({e.Message})" );
			}

			Nodes = new();
		}

		return false;
	}

	internal bool UpdateFromString( string text, string filename = "none", bool failSilently = false )
	{
		try
		{
			var sheet = FromString( text, filename, null );

			Nodes = sheet.Nodes;
			Variables = sheet.Variables;
			KeyFrames = sheet.KeyFrames;
			Mixins = sheet.Mixins;

			// Don't overwrite the included files if the stylesheet
			// failed to load, because it won't be able to hotload
			if ( sheet.IncludedFiles.Any() )
			{
				IncludedFiles = sheet.IncludedFiles;
			}

			sheet.Release();

			return true;
		}
		catch ( Exception e )
		{
			if ( !failSilently )
			{
				Log.Warning( e, $"Error opening stylesheet: {filename} ({e.Message})" );
			}

			Nodes = new();
		}

		return false;
	}

	void AddWatcher( string name )
	{
		Watcher?.Dispose();
		Watcher = null;

		if ( GlobalContext.Current.FileMount is null )
			return;

		//
		// Store the current context to pass through to the watcher because
		// we might be in a different scope later, and won't be able to find the files
		//
		var context = GlobalContext.Current;

		Watcher = context.FileMount.Watch();
		Watcher.OnChanges += x =>
		{
			UpdateFromFile( name, true, context );
			context.UISystem.DirtyAllStyles();
		};

		foreach ( var file in IncludedFiles )
		{
			Watcher.AddFile( file );
		}
	}

	internal void SetVariable( string key, string value, bool isdefault = false )
	{
		Variables ??= new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );

		if ( isdefault && Variables.ContainsKey( key ) ) return;

		// If it's another variable, straight swap it
		value = ReplaceVariables( value );

		Variables[key] = value;
	}

	public string GetVariable( string name, string defaultValue = default )
	{
		if ( Variables == null ) return defaultValue;
		if ( Variables.TryGetValue( name, out var val ) ) return val;
		return null;
	}

	public string ReplaceVariables( string str )
	{
		if ( !str.Contains( '$' ) ) return str; // fast exit

		if ( Variables == null )
			throw new Exception( "Couldn't replace variables -- none set?" );

		var pairs = Variables.Where( x => str.Contains( x.Key ) ).ToArray();

		bool replaced = false;
		foreach ( var var in pairs.OrderByDescending( x => x.Key.Length ) ) // replace the longest first so $button won't stomp $button-bright
		{
			str = str.Replace( var.Key, var.Value );
			replaced = true;
		}

		if ( !replaced )
		{
			throw new Exception( $"Unknown variable '{str}'" );
		}

		return str;
	}

	internal void AddVariables( IEnumerable<(string key, string value)> variables )
	{
		if ( variables == null ) return;

		foreach ( var var in variables )
		{
			SetVariable( var.key, var.value );
		}
	}

	public void AddKeyFrames( KeyFrames frames )
	{
		KeyFrames[frames.Name] = frames;
	}

	/// <summary>
	/// Register a mixin definition.
	/// </summary>
	public void SetMixin( MixinDefinition mixin )
	{
		Mixins[mixin.Name] = mixin;
	}

	/// <summary>
	/// Try to get a mixin by name.
	/// </summary>
	public bool TryGetMixin( string name, out MixinDefinition mixin )
	{
		return Mixins.TryGetValue( name, out mixin );
	}

	/// <summary>
	/// Get a mixin by name or null if not found.
	/// </summary>
	public MixinDefinition GetMixin( string name )
	{
		Mixins.TryGetValue( name, out var mixin );
		return mixin;
	}
}
