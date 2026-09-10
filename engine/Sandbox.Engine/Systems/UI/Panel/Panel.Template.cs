namespace Sandbox.UI;

public partial class Panel
{
	string previouslyLoadedTemplateStylesheet;
	InlineStyleSheet[] loadedInlineStylesheets;

	private void LoadStyleSheet()
	{
		// A control's inline styles belong to everything built on it, so these come from the whole
		// base chain and don't stand in for the panel's own sheet - a subclass keeps its own too
		var declaredItsOwn = LoadInlineStyleSheets();

		// A control that declares its own inline styles has said where its styles come from. Going
		// looking for a file as well would only find the .scss that used to sit beside it before
		// the styles moved inline, which no longer exists
		if ( declaredItsOwn )
			return;

		if ( LoadStyleSheetFromAttribute() )
			return;

		if ( LoadStyleSheetAuto() )
			return;
	}

	/// <summary>
	/// Loads the inline content of every [StyleSheet.Inline] on this panel's type and on the types
	/// it derives from.
	/// </summary>
	/// <returns>True if the panel's own type declared one, rather than only inheriting them.</returns>
	private bool LoadInlineStyleSheets()
	{
		var sheets = InlineStyleSheetsFor( GetType() );

		// A hotload can change what a type derives from, so anything that was ours and isn't now goes
		RemoveInlineStyleSheets( sheets );

		if ( sheets.Length == 0 )
			return false;

		loadedInlineStylesheets = sheets;

		// Always runs, so a hotload that changed the styles reparses the shared sheet
		foreach ( var sheet in sheets )
		{
			StyleSheet.AddInline( sheet.Attribute.Styles, sheet.Key );
		}

		// Base first, so the last one is this panel's own if it declared one
		return sheets[^1].DeclaredOnSelf;
	}

	readonly record struct InlineStyleSheet( UI.StyleSheet.InlineAttribute Attribute, string Key, bool DeclaredOnSelf );

	// Worked out once per type - every panel that gets built asks for this, and a type's attributes
	// can't change. A hotload makes new types, which miss the cache and get looked up again.
	static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, InlineStyleSheet[]> inlineStyleSheets = new();

	/// <summary>
	/// Every [StyleSheet.Inline] declared on <paramref name="type"/> and on the types it derives
	/// from, base first.
	/// </summary>
	private static InlineStyleSheet[] InlineStyleSheetsFor( Type type )
	{
		return inlineStyleSheets.GetOrAdd( type, static t => FindInlineStyleSheets( t ) );
	}

	/// <summary>
	/// The base chain is walked by hand rather than asked for as an inherited attribute, because
	/// the attribute is declared <c>Inherited = false</c> - reflection hands one back from the
	/// immediate base and no further, so a control two levels down would silently lose its styles.
	/// </summary>
	private static InlineStyleSheet[] FindInlineStyleSheets( Type type )
	{
		List<InlineStyleSheet> found = null;

		for ( var current = type; current is not null && current != typeof( object ); current = current.BaseType )
		{
			foreach ( var attribute in current.GetCustomAttributes( typeof( UI.StyleSheet.InlineAttribute ), false ) )
			{
				var inline = (UI.StyleSheet.InlineAttribute)attribute;

				found ??= new List<InlineStyleSheet>();
				found.Add( new InlineStyleSheet( inline, $"inline:{inline.Name}", current == type ) );
			}
		}

		if ( found is null )
			return Array.Empty<InlineStyleSheet>();

		// Walked derived to base, and the base's styles want to go on first
		found.Reverse();

		return found.ToArray();
	}

	/// <summary>
	/// Drop the inline sheets we loaded that aren't in <paramref name="keeping"/>.
	/// </summary>
	private void RemoveInlineStyleSheets( InlineStyleSheet[] keeping )
	{
		if ( loadedInlineStylesheets is null )
			return;

		foreach ( var loaded in loadedInlineStylesheets )
		{
			var keep = false;

			foreach ( var sheet in keeping )
			{
				if ( sheet.Key != loaded.Key ) continue;

				keep = true;
				break;
			}

			if ( !keep ) StyleSheet.Remove( loaded.Key );
		}

		loadedInlineStylesheets = null;
	}

	/// <summary>
	/// Loads a stylesheet from one specified within a [StyleSheet] attribute.
	/// </summary>
	/// <returns>True if the attribute exists and we loaded from it, otherwise false</returns>
	private bool LoadStyleSheetFromAttribute()
	{
		var type = Game.TypeLibrary?.GetType( GetType() );
		var attr = type?.GetAttribute<StyleSheetAttribute>( false );

		if ( attr == null )
		{
			if ( !string.IsNullOrWhiteSpace( previouslyLoadedTemplateStylesheet ) )
				StyleSheet.Remove( previouslyLoadedTemplateStylesheet );

			previouslyLoadedTemplateStylesheet = null;
			return false;
		}

		var path = attr?.Name;
		var classFileLocation = type?.GetAttributes<Internal.ClassFileLocationAttribute>()
			.MinBy( x => x.Path.Length );

		if ( path == null && classFileLocation == null )
		{
			Log.Warning( $"{this} has [StyleSheet] but ClassFileLocation wasn't generated!" );
		}

		var fullPath = GetFullPath( path, classFileLocation );
		return LoadStyleSheetFromPath( fullPath, false );
	}

	/// <summary>
	/// Loads a stylesheet from one based on the class name.
	/// </summary>
	/// <returns>True if the attribute exists and we loaded from it, otherwise false</returns>
	private bool LoadStyleSheetAuto()
	{
		var type = Game.TypeLibrary?.GetType( GetType() );

		// Get the shortest class file (incase we have MyPanel.SomeStuff.Blah)
		var classFileLocation = type?.GetAttributes<Internal.ClassFileLocationAttribute>()
			.MinBy( x => x.Path.Length );

		if ( classFileLocation == null )
		{
			// Couldn't find a stylesheet w/ the class name, but this isn't an error, fail silently.
			return false;
		}

		var fullPath = GetFullPath( null, classFileLocation );
		return LoadStyleSheetFromPath( fullPath, true );
	}

	/// <summary>
	/// Loads a stylesheet from the specified path.
	/// </summary>
	/// <returns>True if the stylesheet was loaded successfully, otherwise false</returns>
	private bool LoadStyleSheetFromPath( string path, bool failSilently )
	{
		path = BaseFileSystem.NormalizeFilename( path );

		// Nothing to do
		if ( previouslyLoadedTemplateStylesheet == path )
			return true;

		// Remove old sheet
		if ( !string.IsNullOrWhiteSpace( previouslyLoadedTemplateStylesheet ) )
			StyleSheet.Remove( previouslyLoadedTemplateStylesheet );

		// Add new one
		previouslyLoadedTemplateStylesheet = path;
		StyleSheet.Load( previouslyLoadedTemplateStylesheet, true, failSilently );

		return true;
	}

	private string GetFullPath( string path, Internal.ClassFileLocationAttribute classFileLocation )
	{
		if ( string.IsNullOrWhiteSpace( path ) && classFileLocation != null )
		{
			return classFileLocation.Path + ".scss";
		}
		else if ( classFileLocation != null && (!path.StartsWith( '/' ) && !path.StartsWith( '\\' )) )
		{
			var newpath = System.IO.Path.GetDirectoryName( classFileLocation.Path );
			newpath = System.IO.Path.Combine( newpath, path );
			return newpath;
		}

		return path;
	}

	/// <summary>
	/// TODO: Obsolete this and instead maybe we have something like [PanelSlot( "slotname" )] that 
	/// is applied on properties. Then when we find a slot="slotname" we chase up the heirachy and set the property.
	/// </summary>
	public virtual void OnTemplateSlot( Html.INode element, string slotName, Panel panel )
	{
		Parent?.OnTemplateSlot( element, slotName, panel );
	}
}
