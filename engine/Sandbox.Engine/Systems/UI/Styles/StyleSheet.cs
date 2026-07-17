using Sandbox.Engine;

namespace Sandbox.UI;

public class StyleSheet
{
	public static List<StyleSheet> Loaded { get; internal set; } = new List<StyleSheet>();

	/// <summary>
	/// Between sessions we clear the stylesheets, so one gamemode can't accidentally
	/// use cached values from another.
	/// </summary>
	internal static void ResetStyleSheets()
	{
		// Only reset sheets belonging to the current context, so we don't stomp another context (eg the menu)
		var context = GlobalContext.Current;

		for ( int i = Loaded.Count - 1; i >= 0; i-- )
		{
			var sheet = Loaded[i];
			if ( sheet == null || !ReferenceEquals( sheet.Context, context ) )
				continue;

			sheet.Release();
			Loaded.RemoveAt( i );
		}
	}

	public List<StyleBlock> Nodes { get; set; } = new List<StyleBlock>();

	// Rules bucketed by the class their subject needs, so a panel only tests rules for its own classes
	// instead of every rule. Rules without a class subject (element/id/*) go in _other. Built once when
	// parsing finishes (main thread); only read during the threaded style build.
	Dictionary<string, List<StyleBlock>> _byClass;
	List<StyleBlock> _other;

	public string FileName { get; internal set; }
	internal FileWatch Watcher { get; private set; }
	public List<string> IncludedFiles { get; set; } = new List<string>();
	public Dictionary<string, string> Variables;
	public Dictionary<string, KeyFrames> KeyFrames = new Dictionary<string, KeyFrames>( StringComparer.OrdinalIgnoreCase );
	public Dictionary<string, MixinDefinition> Mixins = new Dictionary<string, MixinDefinition>( StringComparer.OrdinalIgnoreCase );

	internal GlobalContext Context { get; private set; }

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
		var context = GlobalContext.Current;

		var alreadyLoaded = Loaded.FirstOrDefault( x => x.FileName == filename && ReferenceEquals( x.Context, context ) );
		if ( alreadyLoaded != null )
			return alreadyLoaded;

		var sheet = new StyleSheet();
		sheet.Context = context;
		sheet.UpdateFromFile( filename, failSilently, context );

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
			return StyleParser.ParseSheet( styles, filename, variables, recover: true );
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

			using ( new GlobalContext.GlobalContextScope( ctx ) )
			{
				return UpdateFromString( text, name, failSilently );
			}
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
			// Keep any variables that were injected from outside (eg inherited from a parent) so a
			// reparse doesn't lose them
			var injected = Variables?.Select( x => (x.Key, x.Value) ).ToArray();

			var sheet = FromString( text, filename, injected );

			Nodes = sheet.Nodes;
			Variables = sheet.Variables;
			KeyFrames = sheet.KeyFrames;
			Mixins = sheet.Mixins;
			_byClass = sheet._byClass;
			_other = sheet._other;

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
		var context = Context ?? GlobalContext.Current;

		Watcher = context.FileMount.Watch();
		Watcher.OnChanges += x =>
		{
			UpdateFromFile( name, true, context );

			// Watch any files that got @import'd during this reparse so editing them hotloads too
			foreach ( var file in IncludedFiles )
				Watcher?.AddFile( file );

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
		return defaultValue;
	}

	public string ReplaceVariables( string str )
	{
		if ( !str.Contains( '$' ) ) return str; // fast exit

		// Match whole $variable tokens only, so $col doesn't stomp $color and a literal $ (eg $5.00) is left alone
		return System.Text.RegularExpressions.Regex.Replace( str, @"\$[A-Za-z_][A-Za-z0-9_-]*", m =>
		{
			if ( Variables != null && Variables.TryGetValue( m.Value, out var value ) )
				return value;

			throw new Exception( $"Unknown variable '{m.Value}'" );
		} );
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

	/// <summary>
	/// Build the class index from Nodes. Call after Nodes is finalised (parse/hotload).
	/// </summary>
	internal void BuildIndex()
	{
		_byClass = new Dictionary<string, List<StyleBlock>>( StringComparer.OrdinalIgnoreCase );
		_other = new List<StyleBlock>();

		foreach ( var block in Nodes )
		{
			if ( block.Selectors == null )
				continue;

			foreach ( var sel in block.Selectors )
			{
				// Bucket by the subject's first class. A panel must have that class to match, so this is
				// a safe narrowing - rules without a class subject always get tested.
				if ( sel.Classes != null && sel.Classes.Length > 0 )
				{
					var key = sel.Classes[0];
					if ( !_byClass.TryGetValue( key, out var list ) )
						_byClass[key] = list = new List<StyleBlock>();

					list.Add( block );
				}
				else
				{
					_other.Add( block );
				}
			}
		}
	}

	/// <summary>
	/// Add the rules that could match a target with these classes to <paramref name="output"/>. The exact
	/// TestBroadphase still runs, so the result is the same as testing every rule - just cheaper.
	/// </summary>
	internal void GatherCandidates( HashSet<string> classes, IStyleTarget target, HashSet<StyleBlock> seen, List<StyleBlock> output )
	{
		if ( _byClass == null )
		{
			Take( Nodes, target, seen, output ); // not indexed - test everything
			return;
		}

		Take( _other, target, seen, output );

		if ( classes == null )
			return;

		foreach ( var c in classes )
		{
			if ( _byClass.TryGetValue( c, out var list ) )
				Take( list, target, seen, output );
		}
	}

	static void Take( List<StyleBlock> blocks, IStyleTarget target, HashSet<StyleBlock> seen, List<StyleBlock> output )
	{
		foreach ( var block in blocks )
		{
			if ( seen.Add( block ) && block.TestBroadphase( target ) )
				output.Add( block );
		}
	}
}
