using Sandbox.Internal;
using System;

namespace Editor;

/// <summary>
/// Interface for editors to open code files.
/// Any class that implements this interface is automatically added to the list.
/// An editor is only enabled if <see cref="IsInstalled"/> returns true.
///
/// Decorate your implementation with a <see cref="TitleAttribute"/>.
/// </summary>
public interface ICodeEditor
{
	/// <summary>
	/// Opens a file in the editor, optionally at a line and column.
	/// </summary>
	public void OpenFile( string path, int? line = null, int? column = null );

	/// <summary>
	/// Open the solution of all sandbox projects
	/// </summary>
	public void OpenSolution();

	/// <summary>
	/// Open given addon in the editor.
	/// </summary>
	public void OpenAddon( Project addon );

	/// <summary>
	/// Whether or not this editor is installed.
	/// </summary>
	public bool IsInstalled();
}

/// <summary>
/// For opening source code files in whatever code editor the user has selected.
/// </summary>
public static partial class CodeEditor
{
	private static ICodeEditor _current;
	/// <summary>
	/// The current code editor we're using.
	/// </summary>
	[Title( "Code Editor" )]
	public static ICodeEditor Current
	{
		get
		{
			if ( _current == null )
			{
				var editorName = EditorCookie.GetString( "CodeEditor", GetDefault() );

				if ( editorName != null && editorName.StartsWith( "Custom:" ) )
				{
					var customId = editorName.Substring( 7 );
					_current = TryGetCustomEditor( customId );
					if ( _current != null )
					{
						return _current;
					}

					editorName = GetDefault();
				}

				var editorType = EditorTypeLibrary.GetTypes<ICodeEditor>().FirstOrDefault( t => t.Name == editorName );

				// Check if our selected editor is still valid
				if ( editorType != null )
				{
					var editor = editorType.Create<ICodeEditor>();
					if ( !editor.IsInstalled() )
					{
						Log.Warning( $"Code editor '{editorName}' not installed, using default" );
						editorType = EditorTypeLibrary.GetTypes<ICodeEditor>().FirstOrDefault( t => t.Name == GetDefault() );
					}
				}

				// Check if our selected editor even exists as a type
				if ( editorType == null )
				{
					Log.Warning( $"Code editor '{editorName}' not found, using default" );
					editorType = EditorTypeLibrary.GetTypes<ICodeEditor>().FirstOrDefault( t => t.Name == GetDefault() );
				}

				// If you seriously have no code editor installed, _current can just be null
				if ( editorType != null )
				{
					EditorCookie.SetString( "CodeEditor", editorType.Name ); // Make sure the cookie gets reset if we're falling back
					_current = editorType.Create<ICodeEditor>();
				}
			}
			return _current;
		}
		set
		{
			_current = value;

			var typeName = value.GetType().Name;
			if ( typeName == "CustomCodeEditor" )
			{
				var customEditor = value as dynamic;
				var config = customEditor?.Config;
				if ( config != null )
				{
					EditorCookie.SetString( "CodeEditor", $"Custom:{config.Id}" );
					return;
				}
			}

			EditorCookie.SetString( "CodeEditor", typeName );
		}
	}

	/// <summary>
	/// Tries to get a custom code editor by its unique ID.
	/// </summary>
	/// <param name="customId">The unique ID of the custom editor.</param>
	/// <returns>Returns the custom editor if found and is valid, otherwise returns null.</returns>
	private static ICodeEditor TryGetCustomEditor( string customId )
	{
		var customEditorType = EditorTypeLibrary.GetTypes<ICodeEditor>()
			.FirstOrDefault( t => t.Name == "CustomCodeEditor" );

		if ( customEditorType == null ) return null;

		var registryType = customEditorType.TargetType.Assembly.GetType( "Editor.CodeEditors.CustomCodeEditorRegistry" );
		if ( registryType == null ) return null;

		var getMethod = registryType.GetMethod( "GetEditorById", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static );
		if ( getMethod == null ) return null;

		var editor = getMethod.Invoke( null, new object[] { customId } ) as ICodeEditor;
		if ( editor != null && editor.IsInstalled() ) return editor;
		return null;
	}

	/// <summary>
	/// Clears out the current cached editor, forcing a re-evaluation on next cycle.
	/// Call this when custom editor configurations change.
	/// </summary>
	public static void InvalidateCache()
	{
		_current = null;
	}

	/// <summary>
	/// Tries to find an editor type with a matching name, and checks to see if it's installed on our system.
	/// </summary>
	/// <param name="editorName"></param>
	/// <returns></returns>
	private static TypeDescription GetEditorDescription( string editorName )
	{
		return EditorTypeLibrary.GetTypes<ICodeEditor>().Where( x => !x.IsInterface && x.Name == editorName && x.Create<ICodeEditor>().IsInstalled() ).FirstOrDefault();
	}

	/// <summary>
	/// Decides on a default code editor to use, defaults to Visual Studio or Visual Studio Code if not installed.
	/// Since code editors are made in addon space, we don't have their types ready to use.
	/// </summary>
	/// <returns></returns>
	private static string GetDefault()
	{
		// Default to Visual Studio
		var editorType = GetEditorDescription( "VisualStudio" );
		if ( editorType != null ) return editorType.Name;

		// Default to Visual Studio Code
		editorType = GetEditorDescription( "VisualStudioCode" );
		if ( editorType != null ) return editorType.Name;

		// Find any available code editor..
		return EditorTypeLibrary.GetTypes<ICodeEditor>()
			.Where( x => !x.IsInterface && x.Create<ICodeEditor>().IsInstalled() )
			.FirstOrDefault()
			?.Name ?? null;
	}

	/// <summary>
	/// Friendly name for our current code editor.
	/// </summary>
	public static string Title
	{
		get
		{
			if ( CodeEditor.Current != null )
			{
				if ( CodeEditor.Current.GetType().Name == "CustomCodeEditor" )
				{
					var customEditor = CodeEditor.Current as dynamic;
					var config = customEditor?.Config;
					if ( config != null && !string.IsNullOrEmpty( config.Name ) )
					{
						return config.Name;
					}

				}

				var displayInfo = DisplayInfo.ForType( CodeEditor.Current.GetType(), true );
				if ( !string.IsNullOrEmpty( displayInfo.Name ) )
					return displayInfo.Name;
			}

			return "Code Editor";
		}
	}

	private static void OpenFile( string path, int? line, int? column, string memberName )
	{
		ArgumentNullException.ThrowIfNullOrEmpty( path );

		if ( AssetSystem.FindByPath( path ) is { } asset )
		{
			if ( !IAssetEditor.OpenInEditor( asset, out var editor ) )
			{
				return;
			}

			if ( memberName is null || editor is null )
			{
				return;
			}

			editor.SelectMember( memberName );
			return;
		}

		if ( !System.IO.Path.IsPathRooted( path ) )
		{
			foreach ( var project in EditorUtility.Projects.GetAll() )
			{
				if ( !project.Active ) continue;
				if ( !project.HasCompiler ) continue;

				var codePath = project.GetCodePath();
				var fullPath = System.IO.Path.Combine( codePath, path );

				if ( System.IO.File.Exists( fullPath ) )
				{
					path = fullPath;
					goto found;
				}
			}

			Log.Error( $"Couldn't resolve relative path: '{path}'" );
			return;
		}

		found:

		if ( AssertCodeEditor() )
			Current?.OpenFile( path, line, column );
	}

	public static void OpenFile( ISourcePathProvider location )
	{
		OpenFile(
			location.Path,
			(location as ISourceLineProvider)?.Line,
			(location as ISourceColumnProvider)?.Column,
			(location as IMemberNameProvider)?.MemberName );
	}

	/// <summary>
	/// Opens a file in the current editor, optionally at a line and column.
	/// </summary>
	public static void OpenFile( string path, int? line = null, int? column = null )
	{
		OpenFile( path, line, column, null );
	}

	/// <summary>
	/// Returns true if the file exists and can be opened by the current code editor.
	/// </summary>
	/// <param name="path"></param>
	/// <returns></returns>
	public static bool CanOpenFile( string path )
	{
		if ( string.IsNullOrWhiteSpace( path ) ) return false;

		if ( AssetSystem.FindByPath( path ) is { } asset )
		{
			return true;
		}

		if ( !System.IO.Path.IsPathRooted( path ) )
		{
			foreach ( var project in EditorUtility.Projects.GetAll() )
			{
				if ( !project.Active ) continue;
				if ( !project.HasCompiler ) continue;
				var codePath = project.GetCodePath();
				var fullPath = System.IO.Path.Combine( codePath, path );
				if ( System.IO.File.Exists( fullPath ) )
				{
					return true;
				}
			}
			return false;
		}

		return false;
	}

	/// <summary>
	/// Open the solution of all s&amp;box projects
	/// </summary>
	public static void OpenSolution()
	{
		if ( AssertCodeEditor() )
			Current?.OpenSolution();
	}

	public static void OpenAddon( Project addon )
	{
		if ( AssertCodeEditor() )
			Current.OpenAddon( addon );
	}

	private static bool AssertCodeEditor()
	{
		if ( Current == null )
		{
			EditorUtility.DisplayDialog( "No Code Editor", "No code editor found, you'll want something like Visual Studio." );
			return false;
		}
		return true;
	}

	/// <summary>
	/// Finds a .sln this path belongs to, this is pretty much entirely for internal usage to open engine slns
	/// </summary>
	public static string FindSolutionFromPath( string path )
	{
		if ( path == null || path.Length < 5 )
			throw new Exception( $"Couldn't find solution file from path \"{path}\"" );

		var addonFile = System.IO.Path.Combine( path, ".sbproj" );
		if ( System.IO.File.Exists( addonFile ) )
		{
			return AddonSolutionPath();
		}

		var solutions = System.IO.Directory.EnumerateFiles( path, "*.slnx" ).ToArray();
		if ( solutions.Length > 0 )
		{
			return string.Join( ";", solutions );
		}

		return FindSolutionFromPath( System.IO.Directory.GetParent( path ).FullName );
	}

	public static string AddonSolutionPath()
	{
		return $"{Project.Current.GetRootPath()}/{Project.Current.Config.Ident}.slnx";
	}
}
