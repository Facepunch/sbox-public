using Facepunch.ActionGraphs;
using NativeEngine;
using Sandbox.Engine.Settings;
using Sandbox.Engine.Shaders;
using Sandbox.Internal;
using Sandbox.Utility;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Editor;

public static partial class EditorUtility
{
	public class OnInspectArgs : EventArgs
	{
		public bool Cancel { get; set; }

		public object Object { get; set; }
	}

	/// <summary>
	/// Called when InspectorObject changes
	/// </summary>
	public static event Action<OnInspectArgs> OnInspect;

	static object _inspectorObject;

	/// <summary>
	/// Set the object to be inspected by the inspector.
	/// </summary>
	public static object InspectorObject
	{
		get => _inspectorObject;
		set
		{
			if ( _inspectorObject == value ) return;

			if ( OnInspect == null )
			{
				_inspectorObject = value;
				return;
			}

			var eventArgs = new OnInspectArgs
			{
				Object = value,
				Cancel = false
			};

			OnInspect.Invoke( eventArgs );

			if ( eventArgs.Cancel == false )
			{
				_inspectorObject = eventArgs.Object;
			}
		}
	}

	public static void AddLogger( Action<LogEvent> logger )
	{
		Sandbox.Diagnostics.Logging.OnMessage += logger;
	}

	public static void RemoveLogger( Action<LogEvent> logger )
	{
		Sandbox.Diagnostics.Logging.OnMessage -= logger;
	}

	public static ConCmdAttribute.AutoCompleteResult[] AutoComplete( string text, int maxCount )
	{
		return ConVarSystem.GetAutoComplete( text, maxCount );
	}

	/// <summary>
	/// Get all the root panels.
	/// </summary>
	public static HashSet<IPanel> GetRootPanels() => IPanel.GetAllRootPanels();

	public static void SendToRecycleBin( string filename )
	{
		NativeEngine.EngineGlobal.Plat_SafeRemoveFile( filename );
	}

	/// <summary>
	/// Open a folder (or url)
	/// </summary>
	public static void OpenFolder( string path )
	{
		System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo()
		{
			FileName = path,
			UseShellExecute = true,
			Verb = "open",
		} );
	}

	/// <summary>
	/// Open a folder (or url)
	/// </summary>
	public static void OpenFile( string path )
	{
		try
		{
			System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo()
			{
				FileName = path,
				UseShellExecute = true,
				Verb = "open",
			} );
		}
		catch
		{
			// Show "Open With" dialog
			System.Diagnostics.Process.Start( new System.Diagnostics.ProcessStartInfo()
			{
				FileName = path,
				UseShellExecute = false,
				Verb = "openas",
			} );
		}
	}


	/// <summary>
	/// Open given file's folder in OS file explorer and select given file.
	/// </summary>
	public static void OpenFileFolder( string filepath )
	{
		filepath = System.IO.Path.GetFullPath( filepath );
		System.Diagnostics.Process.Start( "explorer.exe", string.Format( "/select,\"{0}\"", filepath ) );
	}

	private static string GetDestinationPath( string source, string directory )
	{
		var fileName = System.IO.Path.GetFileName( source );
		var destinationPath = System.IO.Path.Combine( directory, fileName );

		return destinationPath;
	}

	/// <summary>
	/// Copies a file to a directory
	/// </summary>
	internal static void CopyFileToDirectory( string filePath, string directory, bool overwrite = true )
	{
		var destinationPath = GetDestinationPath( filePath, directory );

		if ( !System.IO.File.Exists( filePath ) )
			throw new FileNotFoundException( $"File '{filePath}' doesn't exist" );

		if ( !System.IO.Directory.Exists( directory ) )
			throw new DirectoryNotFoundException( $"Directory '{directory}' doesn't exist" );

		System.IO.File.Copy( filePath, destinationPath, overwrite );
	}

	/// <summary>
	/// Moves an asset's source and compiled files to a directory (if they exist)
	/// </summary>
	public static void MoveAssetToDirectory( Asset asset, string directory, bool overwrite = true )
	{
		var currentPath = System.IO.Path.GetDirectoryName( asset.AbsolutePath );

		// fuck me c# - is this how we compare paths
		var dirA = System.IO.Path.GetFullPath( currentPath ).ToLower().Trim( '/', '\\' );
		var dirB = System.IO.Path.GetFullPath( directory ).ToLower().Trim( '/', '\\' );

		// already in this directory!
		if ( dirA == dirB )
			return;

		CopyAssetToDirectory( asset, directory );

		var absoluteSource = asset.GetSourceFile( true );
		var absoluteCompiled = asset.GetCompiledFile( true );

		if ( !string.IsNullOrEmpty( absoluteSource ) )
			System.IO.File.Delete( absoluteSource );

		if ( !string.IsNullOrEmpty( absoluteCompiled ) )
			System.IO.File.Delete( absoluteCompiled );
	}

	public static void RenameDirectory( string directory, string newDirectory, bool recursive = false )
	{
		if ( !System.IO.Directory.Exists( directory ) )
			return;

		// don't allow moving a folder inside a subfolder of itself!
		if ( newDirectory.StartsWith( directory, StringComparison.OrdinalIgnoreCase ) )
			return;

		if ( !System.IO.Directory.Exists( newDirectory ) )
			System.IO.Directory.CreateDirectory( newDirectory );


		var assets = AssetSystem.All.Where( x => x.AbsolutePath.StartsWith( directory.Replace( '\\', '/' ), StringComparison.OrdinalIgnoreCase ) && x.AbsolutePath.Substring( directory.Length ).Contains( '/' ) );
		foreach ( var asset in assets )
		{
			if ( !System.IO.Path.Exists( asset.AbsolutePath ) ) continue;
			MoveAssetToDirectory( asset, newDirectory );
		}

		var files = System.IO.Directory.GetFiles( directory, "*", System.IO.SearchOption.TopDirectoryOnly );
		foreach ( var file in files )
		{
			var newFile = file.Replace( directory, newDirectory );
			System.IO.File.Move( file, newFile );
		}

		var folders = System.IO.Directory.GetDirectories( directory, "*", System.IO.SearchOption.TopDirectoryOnly );
		foreach ( var folder in folders )
		{
			RenameDirectory( folder, folder.Replace( directory, newDirectory ), true );
		}

		if ( !recursive ) System.IO.Directory.Delete( directory, true );
	}

	/// <summary>
	/// Moves a file to the same directory but gives it a new name 
	/// </summary>
	/// <param name="asset"></param>
	/// <param name="newName"></param>
	public static bool RenameAsset( Asset asset, string newName )
	{
		if ( string.IsNullOrEmpty( newName ) )
			return false;

		newName = newName.Trim().GetFilenameSafe();
		if ( string.IsNullOrEmpty( newName ) )
			return false;

		var compiledPath = asset.GetCompiledFile( true );
		var newCompiledPath = compiledPath.Replace( asset.Name, newName );

		var sourcePath = asset.GetSourceFile( true );
		var newSourcePath = sourcePath.Replace( asset.Name, newName );

		if ( string.Equals( asset.Name, newName, StringComparison.OrdinalIgnoreCase ) )
		{
			// we've just changed the capitalisation
			// nothing's really changed for us as our asset system is case insensitive, so just do OS move
		}
		else
		{
			if ( System.IO.File.Exists( newSourcePath ) )
			{
				Log.Error( $"Cannot rename asset, '{asset.Name}' already exists!" );
				return false;
			}

			// if there's a compiled asset of this name already, but NOT a source file, just bin it (?)
			if ( System.IO.File.Exists( newCompiledPath ) )
			{
				System.IO.File.Delete( newCompiledPath );
			}

			// moving the asset will register another, so let's delete the old one
			asset.IsDeleted = true;
		}

		if ( !string.IsNullOrEmpty( compiledPath ) )
			System.IO.File.Move( compiledPath, newCompiledPath );

		if ( !string.IsNullOrEmpty( sourcePath ) )
			System.IO.File.Move( sourcePath, newSourcePath );

		return true;
	}

	/// <summary>
	/// Copies an asset's source and compiled files to a directory (if they exist)
	/// </summary>
	public static void CopyAssetToDirectory( Asset asset, string directory, bool overwrite = true )
	{
		var absoluteSource = asset.GetSourceFile( true );
		var absoluteCompiled = asset.GetCompiledFile( true );

		if ( !string.IsNullOrEmpty( absoluteCompiled ) && System.IO.Path.Exists( absoluteCompiled ) )
			CopyFileToDirectory( absoluteCompiled, directory, overwrite );

		if ( !string.IsNullOrEmpty( absoluteSource ) && System.IO.Path.Exists( absoluteSource ) )
			CopyFileToDirectory( absoluteSource, directory, overwrite );
	}

	public static Task<bool> PutAsync( Stream fileStream, string endpoint, Sandbox.Utility.DataProgress.Callback progress = null, CancellationToken token = default )
	{
		try
		{
			return Sandbox.Utility.Web.PutAsync( fileStream, endpoint, token, progress );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"PutAsync failed: {e.Message}" );
			return Task.FromResult( false );
		}
	}

	public static Task<bool> DownloadAsync( string url, string targetfile, Sandbox.Utility.DataProgress.Callback progress = null, CancellationToken token = default )
	{
		try
		{
			return Sandbox.Utility.Web.DownloadFile( url, targetfile, token, progress );
		}
		catch ( System.Exception e )
		{
			Log.Warning( e, $"DownloadAsync failed: {e.Message}" );
			return Task.FromResult( false );
		}
	}

	/// <summary>
	/// People are lazy and will be calling this all the time if we let them. Lets keep it hidden for as long as possible
	/// </summary>
	internal static void RedrawActiveWindow()
	{
		Native.QApp.redrawActiveWindow();
	}

	public static SceneWorld CreateSceneWorld()
	{
		return new SceneWorld() { IsTransient = false };
	}

	static SoundHandle lastSound;

	/// <summary>
	/// Stop a sound playing from an asset preview
	/// </summary>
	public static void StopAssetSound()
	{
		lastSound?.Stop();
		lastSound = null;
	}

	/// <summary>
	/// Plays an asset sound in 2d space
	/// </summary>
	public static bool PlayAssetSound( Asset asset )
	{
		StopAssetSound();

		if ( asset == null )
			return false;

		var compiledFile = asset.GetCompiledFile( false );
		if ( string.IsNullOrEmpty( compiledFile ) )
		{
			asset.Compile( false );
			return false;
		}

		if ( asset.AssetType == AssetType.SoundFile )
		{
			var foundFile = SoundFile.Load( compiledFile );
			if ( foundFile is null )
				return false;

			return PlayAssetSound( foundFile );
		}
		else if ( asset.AssetType == AssetType.SoundEvent )
		{
			var foundFile = SoundEvent.Load<SoundEvent>( compiledFile );
			if ( foundFile is null )
				return false;

			return PlayAssetSound( foundFile );
		}

		return false;
	}

	/// <summary>
	/// Plays an asset sound in 2d space
	/// </summary>
	public static bool PlayAssetSound( SoundEvent file )
	{
		StopAssetSound();

		if ( file is null )
			return false;

		lastSound = Sound.Play( file );

		if ( lastSound.IsValid() )
		{
			lastSound.ListenLocal = true;
			lastSound.Position = Vector3.Forward * 64.0f;
		}

		return lastSound.IsValid();
	}

	/// <summary>
	/// Plays an asset sound in 2d space
	/// </summary>
	public static bool PlayAssetSound( SoundFile file )
	{
		StopAssetSound();

		if ( file is null )
			return false;

		lastSound = Sound.PlayFile( file, 0.5f, 1.0f );

		if ( lastSound.IsValid() )
		{
			lastSound.ListenLocal = true;
			lastSound.Position = Vector3.Forward * 64.0f;
		}

		return lastSound.IsValid();
	}

	/// <summary>
	/// Plays a sound event
	/// </summary>
	public static SoundHandle PlaySound( string sound, float startTime = 0.0f )
	{
		var foundFile = SoundFile.Load( sound );
		if ( foundFile is null )
			return null;

		var s = Sound.PlayFile( foundFile );
		s.Time = startTime;
		s.ListenLocal = true;
		s.Position = Vector3.Forward * 64.0f;

		return s;
	}

	/// <summary>
	/// Plays a sound via the OS, which is the way you play a sound if you
	/// want it to be heard when the game is tabbed away
	/// </summary>
	public static bool PlayRawSound( string file )
	{
		var fullPath = EngineFileSystem.Assets.GetFullPath( file );
		if ( fullPath == null ) fullPath = EngineFileSystem.CoreContent.GetFullPath( file );

		if ( fullPath == null )
			return false;

		g_pSoundSystem.PlaySoundAtOSLevel( fullPath );
		return true;
	}

	/// <summary>
	/// Delete the cached package info. This will cause any future requests to get fresh information
	/// from the backend. This is useful if you just updated something and want to see the changes.
	/// </summary>
	public static void ClearPackageCache()
	{
		Package.ClearCache();
	}

	/// <summary>
	/// Create an unlimited web surface
	/// </summary>
	public static WebSurface CreateWebSurface()
	{
		return new WebSurface( false );
	}

	/// <summary>
	/// Get a serialized object for this object. Because you're in the editor, this is an
	/// unrestricted object, we aren't whitelisting or using TypeLibrary.
	/// </summary>
	public static SerializedObject GetSerializedObject( object obj )
	{
		return new ReflectionSerializedObject( obj );
	}

	/// <summary>
	/// Create a video writer
	/// </summary>
	public static VideoWriter CreateVideoWriter( string path, VideoWriter.Config config )
	{
		return new VideoWriter( path, config );
	}

	static bool disabledStreaming = false;

	/// <summary>
	/// Force textures to load fully when loading a model etc..
	/// </summary>
	public static IDisposable DisableTextureStreaming()
	{
		bool prev = disabledStreaming;
		disabledStreaming = true;
		g_pRenderDevice.SetForcePreloadStreamingData( true );

		return new DisposeAction( () =>
		{
			disabledStreaming = prev;
			g_pRenderDevice.SetForcePreloadStreamingData( prev );
		} );
	}

	/// <summary>
	/// Quit the whole engine
	/// </summary>
	/// <param name="toLauncher">Open the launcher on exit, if it's not already open.</param>
	public static void Quit( bool toLauncher = false )
	{
		Sandbox.Application.Exit();

		if ( toLauncher )
		{
			ProcessStartInfo info = new ProcessStartInfo( "sbox-launcher.exe" );
			info.WorkingDirectory = System.Environment.CurrentDirectory;

			Process.Start( info );
		}
	}

	/// <summary>
	/// Used for shadergraph
	/// </summary>
	public static bool IsVulkan => g_pRenderDevice.GetRenderDeviceAPI() == NativeEngine.RenderDeviceAPI_t.RENDER_DEVICE_API_VULKAN;

	/// <summary>
	/// True if we're currently recording a video (using the video command, or F6)
	/// </summary>
	public static bool IsRecordingVideo => ScreenRecorder.IsRecording();

	/// <summary>
	/// Display a modal dialog message. This is a blocking call.
	/// </summary>
	public static void DisplayDialog( string title, string message, string okay = "Okay", string icon = "⚠️", Widget parent = null )
	{
		var popup = new PopupDialogWidget( icon );

		popup.WindowTitle = title;
		popup.MessageLabel.Text = message;

		popup.ButtonLayout.AddStretchCell();
		popup.ButtonLayout.Add( new Button.Primary( okay ) { Clicked = () => popup.Destroy() } );

		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();

		// todo - find window for parent and center to window
	}

	/// <summary>
	/// Display a modal dialog message. This is a blocking call.
	/// </summary>
	public static void DisplayDialog( string title, string message, string noLabel, string yesLabel, Action action, string icon = "❓", Widget parent = null )
	{
		var popup = new PopupDialogWidget( icon );

		popup.WindowTitle = title;
		popup.MessageLabel.Text = message;

		popup.ButtonLayout.AddStretchCell();
		popup.ButtonLayout.Add( new Button( noLabel ) { Clicked = () => { popup.Destroy(); } } );
		popup.ButtonLayout.Add( new Button.Primary( yesLabel ) { Clicked = () => { popup.Destroy(); action(); } } );

		popup.SetModal( true, true );
		popup.Hide();
		popup.Show();
	}

	/// <summary>
	/// Show a popup control sheet for this. You should set parent to the control from this this sheet is created.
	/// If you do that properly, when that control is deleted, this popup will get deleted too. If you set it to null
	/// then the control sheet will stay open until it's closed.
	/// </summary>
	public static Widget OpenControlSheet( SerializedObject so, Widget parent, bool createWindow = true )
	{
		var originalType = so.Targets.FirstOrDefault()?.GetType() ?? null;
		if ( originalType is null ) return null;

		// Store a list of types to check (including original and base types)
		var typesToCheck = new List<Type>();
		var currentType = originalType;

		// Build list of types to check while maintaining reference to original type
		while ( currentType is not null )
		{
			typesToCheck.Add( currentType );
			currentType = currentType.BaseType;
		}

		foreach ( var typeToCheck in typesToCheck )
		{
			// Make IPopupEditor<T> with the original target type
			var genericType = typeof( IPopupEditor<> ).MakeGenericType( typeToCheck );

			bool IsSpecificEditor( TypeDescription widgetType )
			{
				var interfaces = widgetType.Interfaces
					.Where( x => x.IsGenericType )
					.ToList();

				//
				// This is somehow necessary because GetInterfaces() lists out System.Object, and TextureGenerator for TextureGeneratorPopup
				//

				var mostSpecificInterface = interfaces.FirstOrDefault( x =>
					x.GetGenericTypeDefinition() != typeof( IPopupEditor<> ) &&
					x.GetGenericArguments().Any( arg => arg != typeof( object ) && arg.IsAssignableTo( typeToCheck ) ) );

				var isSpecific = mostSpecificInterface != null;
				return isSpecific;
			}

			// Find any Widgets that implement it
			var allWidgets = EditorTypeLibrary.GetTypes<Widget>()
				.Where( x => x.Interfaces.Contains( genericType ) )
				.ToList();

			var list = allWidgets
				.OrderByDescending( x => IsSpecificEditor( x ) )
				.ThenBy( x => x.Name )
				.ToList();

			var found = list.FirstOrDefault();

			if ( found is not null )
			{
				try
				{
					var editor = found.Create<Widget>( [so, parent] );

					if ( createWindow )
					{
						var window = new PopupEditorWindow( parent );
						window.Layout.Add( editor );

						window.WindowTitle = $"Editing {so.TypeTitle}";
						window.SetWindowIcon( so.TypeIcon ?? "edit_note" );

						if ( so.ParentProperty is not null )
						{
							window.WindowTitle = $"Editing {so.ParentProperty.DisplayName} ({so.TypeTitle})";
						}

						window.ShowWindowAtCursor();
					}

					return editor;
				}
				catch ( System.Exception e )
				{
					Log.Error( e, $"Exception when creating {found.FullName}( SerializedObject target, Widget parent )" );
				}
			}
		}

		Log.Warning( $"OpenControlSheet - No editor found for {originalType.Name}" );
		return default;
	}

	/// <summary>
	/// Gets every search path seperated by ;
	/// </summary>
	public static string GetSearchPaths()
	{
		return EngineGlobal.GetGameSearchPath();
	}

	public static IEnumerable<string> FontFamilies => FontManager.FontFamilies;

	/// <summary>
	/// Access to the client's render settings
	/// </summary>
	public static RenderSettings RenderSettings => Sandbox.Engine.Settings.RenderSettings.Instance;

	/// <summary>
	/// Some assets are kv3, we want to convert them to json
	/// </summary>
	public static string KeyValues3ToJson( string kvString )
	{
		var kv = EngineGlue.LoadKeyValues3( kvString );
		if ( kv.IsNull ) return null;

		var json = EngineGlue.KeyValues3ToJson( kv );
		kv.DeleteThis();

		return json;
	}

	/// <summary>
	/// Some old ass assets are keyvalues (1). Convert them to Json so we can use them.
	/// </summary>
	public static string KeyValues1ToJson( string kvString )
	{
		return EngineGlue.KeyValuesToJson( kvString );
	}

	public static Pixmap GetFileThumbnail( string filePath, int width, int height )
	{
		QFileInfo fileInfo = QFileInfo.Create( filePath );
		return Pixmap.FromNative( fileInfo.GetIcon( width, height ) );
	}

	/// <summary>
	/// Restarts the editor with the same project.
	/// </summary>
	public static void RestartEditor()
	{
		EditorWindow.Close();

		ProcessStartInfo info = new ProcessStartInfo( "sbox-dev.exe", $"{Environment.CommandLine} -project \"{Project.Current.ConfigFilePath}\"" );
		info.UseShellExecute = true;
		info.CreateNoWindow = true;
		info.WorkingDirectory = System.Environment.CurrentDirectory;

		Process.Start( info );
	}

	/// <summary>
	/// Open a dialog prompt asking the user to restart the editor.
	/// </summary>
	/// <param name="message"></param>
	/// <param name="title"></param>
	public static void RestartEditorPrompt( string message, string title = "Restart Editor" )
	{
		Dialog.AskConfirm( RestartEditor, message, title, "Restart" );
	}

	/// <summary>
	/// Checks if a given folder is a code folder, e.g. [project root]/Code
	/// </summary>
	/// <param name="fullPath"></param>
	/// <returns></returns>
	public static bool IsCodeFolder( string fullPath )
	{
		if ( fullPath == null )
			throw new ArgumentNullException( nameof( fullPath ) );

		if ( fullPath.Length == 0 )
			return false;

		var normalizedPath = Path.GetFullPath( fullPath ).TrimEnd( Path.DirectorySeparatorChar );

		foreach ( var project in Project.All )
		{
			var projectCodePath = project.GetCodePath();
			if ( string.IsNullOrEmpty( projectCodePath ) )
				continue;

			var normalizedProjectPath = Path.GetFullPath( projectCodePath ).TrimEnd( Path.DirectorySeparatorChar );

			if ( normalizedPath.Equals( normalizedProjectPath, StringComparison.OrdinalIgnoreCase ) ||
				normalizedPath.StartsWith( normalizedProjectPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Checks if a given file is a code file
	/// </summary>
	/// <param name="fullPath"></param>
	/// <returns></returns>
	public static bool IsCodeFile( string fullPath )
	{
		return Path.GetExtension( fullPath ).Equals( ".cs", StringComparison.OrdinalIgnoreCase ) ||
			Path.GetExtension( fullPath ).Equals( ".scss", StringComparison.OrdinalIgnoreCase ) ||
			Path.GetExtension( fullPath ).Equals( ".razor", StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>
	/// Gets the source location for the given scene, used by action graph stack traces,
	/// and so the action graph editor knows which asset to save when editing a graph.
	/// </summary>
	public static ISourceLocation GetSourceLocation( this Scene scene )
	{
		return scene.OverrideSourceLocation ?? scene.Source?.SerializationOptions.SourceLocation;
	}

	/// <summary>
	/// Tries to find a project based on a given directory.
	/// </summary>
	public static Project FindProjectByDirectory( string fullPath )
	{
		if ( fullPath == null )
			throw new ArgumentNullException( nameof( fullPath ) );

		if ( fullPath.Length == 0 )
			return null;

		var normalizedPath = Path.GetFullPath( fullPath ).TrimEnd( Path.DirectorySeparatorChar );

		foreach ( var project in Project.All.OrderByDescending( x => x.GetRootPath().Length ) )
		{
			var projectCodePath = project.GetRootPath();
			if ( string.IsNullOrEmpty( projectCodePath ) )
				continue;

			var normalizedProjectPath = Path.GetFullPath( projectCodePath ).TrimEnd( Path.DirectorySeparatorChar );

			if ( normalizedPath.Equals( normalizedProjectPath, StringComparison.OrdinalIgnoreCase ) ||
				normalizedPath.StartsWith( normalizedProjectPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase ) )
			{
				return project;
			}
		}

		return null;
	}

	/// <summary>
	/// Compile a fucking shader. Takes a .shader file and compiles it.
	/// </summary>
	public static async Task<ShaderCompile.Results> CompileShader( string localPath, ShaderCompileOptions options, CancellationToken token = default )
	{
		if ( string.IsNullOrWhiteSpace( localPath ) )
			throw new ArgumentNullException( nameof( localPath ) );

		var path = FileSystem.Content.GetFullPath( localPath );

		var result = await ShaderCompile.Compile( path, localPath, options, token );

		if ( result.Success )
		{
			ConsoleSystem.Run( $"mat_reloadshaders {localPath}" );
		}

		return result;
	}

	// TODO: Just use one function and check if it's a full path already
	public static async Task<ShaderCompile.Results> CompileShader( BaseFileSystem fs, string localPath, ShaderCompileOptions options, CancellationToken token = default )
	{
		if ( string.IsNullOrWhiteSpace( localPath ) )
			throw new ArgumentNullException( nameof( localPath ) );

		var path = fs.GetFullPath( localPath );

		return await ShaderCompile.Compile( path, localPath, options, token );
	}

	[EditorEvent.Hotload]
	private static void ClearReflectionQueryCache()
	{
		ReflectionQueryCache.ClearTypeCache();
	}

	public static Asset GetAssetFromProject( Project project )
	{
		return project.ProjectSourceObject as Asset;
	}

	/// <summary>
	/// Create a TypeLibrary from a collection of assemblies
	/// </summary>
	/// <param name="assemblies"></param>
	/// <returns></returns>
	internal static TypeLibrary CreateTypeLibrary( CompilerOutput[] assemblies )
	{
		var library = new TypeLibrary();
		var packageLoader = new Sandbox.PackageLoader( "EditorTypeLibrary", typeof( GameInstanceDll ).Assembly );
		using var enroller = packageLoader.CreateEnroller( "EditorTypeLibrary" );

		enroller.OnAssemblyAdded = ( a ) =>
		{
			library.AddAssembly( a.Assembly, true );
		};

		foreach ( var assm in assemblies )
		{
			var ms = new MemoryStream( assm.AssemblyData );
			enroller.LoadAssemblyFromStream( assm.Compiler.AssemblyName, ms );
		}

		return library;
	}

	/// <summary>
	/// Finds a component in the scene and selects it in the editor
	/// </summary>
	/// <param name="component"></param>
	public static void FindInScene( Component component )
	{
		if ( !component.IsValid() )
			return;

		var go = component.GameObject;

		FindInScene( go );
	}

	/// <summary>
	/// Finds a GameObject in the scene and selects it in the editor
	/// </summary>
	/// <param name="go"></param>
	public static void FindInScene( GameObject go )
	{
		if ( !go.IsValid() )
			return;

		using ( SceneEditorSession.Active.UndoScope( $"Selected {go}" ).Push() )
		{
			SceneEditorSession.Active?.Selection.Set( go );
		}
	}
}


/// <summary>
/// This is created using EditorUtility.OpenControlSheet
/// </summary>
class PopupEditorWindow : Widget
{
	public PopupEditorWindow( Widget parent ) : base( parent )
	{
		Size = new Vector2( 500, 500 );
		DeleteOnClose = true;
		Layout = Layout.Column();
		WindowFlags = WindowFlags.Tool | WindowFlags.Customized | WindowFlags.WindowSystemMenuHint | WindowFlags.CloseButton;

		Width = 400;

		ShowWindowAtCursor();
	}

	public virtual void ShowWindowAtCursor()
	{
		Show();

		Position = Application.CursorPosition - new Vector2( Width * 0.5f, 3 );
		UpdateGeometry();
		ConstrainToScreen();

		Focus();
	}

	protected override void OnKeyPress( KeyEvent e )
	{
		if ( e.Key == KeyCode.Escape )
		{
			Close();
			e.Accepted = true;
			return;
		}

		base.OnKeyPress( e );
	}
}
