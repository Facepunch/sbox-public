namespace Sandbox.UI;

public partial class Panel
{
	string previouslyLoadedTemplateStylesheet;

	private void LoadStyleSheet()
	{
		if ( LoadStyleSheetFromInlineAttribute() )
			return;

		if ( LoadStyleSheetFromAttribute() )
			return;

		if ( LoadStyleSheetAuto() )
			return;
	}

	/// <summary>
	/// Loads a stylesheet from inline content in a [StyleSheet.Inline] attribute.
	/// </summary>
	/// <returns>True if the attribute exists and we loaded from it, otherwise false</returns>
	private bool LoadStyleSheetFromInlineAttribute()
	{
		var type = Game.TypeLibrary?.GetType( GetType() );
		var attr = type?.GetAttribute<UI.StyleSheet.InlineAttribute>( false );

		if ( attr == null )
			return false;

		var key = $"inline:{attr.Name}";

		if ( previouslyLoadedTemplateStylesheet != key && !string.IsNullOrWhiteSpace( previouslyLoadedTemplateStylesheet ) )
			StyleSheet.Remove( previouslyLoadedTemplateStylesheet );

		previouslyLoadedTemplateStylesheet = key;

		// Always runs, so a hotload that changed the styles reparses the shared sheet
		StyleSheet.AddInline( attr.Styles, key );
		return true;
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
