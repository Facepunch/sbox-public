using Sandbox.Audio;
using Sandbox.Engine;
using Sandbox.Internal;
using Sentry;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Sandbox;

/// <summary>
/// Handles the loading of package assemblies into a loadcontext.
/// </summary>
internal sealed partial class PackageLoader : IDisposable
{
	bool FastHotloadEnabled => HotloadManager.hotload_fast;

	private List<LoadedAssembly> Loaded { get; set; } = new();
	private LoadContext LoadContext { get; set; }
	private ILHotload ILHotload { get; set; }
	private HotloadManager HotloadManager { get; set; }
	private List<LoadedAssembly> IncomingThisHotload { get; } = new();

	/// <summary>
	/// In tools mode we don't register events unless they're from a tools project
	/// </summary>
	public bool ToolsMode { get; set; }

	/// <summary>
	/// Disables access control on all loaded local packages, but not remote packages.
	/// </summary>
	private bool DisableAccessControl { get; init; }

	Logger log { get; } = new Logger( "PackageLoader" );
	HashSet<PackageManager.ActivePackage> loadedPackages = new();
	HashSet<(PackageManager.ActivePackage ap, string filename)> changedPackageDlls = new();

	/// <summary>
	/// Called on Game Loop Init
	/// </summary>
	public PackageLoader( string name, Assembly parentAssembly, bool disableAccessControl = false )
	{
		log = new Logger( $"PackageLoader/{name}" );
		LoadContext = new LoadContext( parentAssembly );
		// ILHotload only makes sense for the editor
		if ( Application.IsEditor || Application.IsUnitTest )
		{
			ILHotload = new ILHotload( name );
		}
		HotloadManager = new HotloadManager( name );
		HotloadManager.OnSuccess += OnFullHotloadSuccess;
		HotloadManager.AssemblyResolver = PackageManager.AccessControl;
		DisableAccessControl = disableAccessControl;

		loadedPackages.Clear();
		changedPackageDlls.Clear();
	}

	public void Dispose()
	{
		foreach ( var dllWatcher in dllWatchers )
		{
			dllWatcher.Dispose();
		}
		dllWatchers.Clear();

		OnAfterHotload = null;

		Loaded.Clear();
		Loaded = null;

		LoadContext.Unload();
		LoadContext = null;

		ILHotload?.Dispose();
		ILHotload = null;

		HotloadManager.Dispose();
		HotloadManager = null;
	}

	/// <summary>
	/// Make sure we should be able to load this dll
	/// </summary>
	bool TestAccessControl( System.IO.Stream dll, out TrustedBinaryStream trustedDll )
	{
		var eventRecord = new Api.Events.EventRecord( $"TestAccessControl" );
		eventRecord.SetValue( "AssemblySize", dll.Length );
		eventRecord.StartTimer( "Time" );

		AccessControlResult result = new AccessControlResult();
		try
		{
			result = PackageManager.AccessControl.VerifyAssembly( dll, out trustedDll );
			if ( result.Success )
			{
				eventRecord.FinishTimer( "Time" );
				eventRecord.Submit();

				return true;
			}
		}
		catch ( System.Exception e )
		{
			log.Warning( e, $"Assembly Exception: {e.Message}" );
		}

		foreach ( var error in result.Errors )
		{
			log.Warning( $"Whitelist Error: {error}" );
		}

		foreach ( var error in result.WhitelistErrors )
		{
			var metric = new Api.Events.EventRecord( $"accesscontrol.whitelist" );
			metric.SetValue( "type", error.Name );
			metric.SetValue( "locations", error.Locations );
			metric.Submit();
		}

		trustedDll = null;
		return false;
	}



	private void LoadPendingChanges()
	{
		log.Trace( "Loading Pending Changes" );

		var changedPackageDlls = this.changedPackageDlls
										.Where( x => x.ap is not null )
										.ToArray();
		this.changedPackageDlls.Clear();

		//
		// This can happen when recieving assemblies from a server
		//
		if ( !changedPackageDlls.Any() )
			return;

		var changedPackages = changedPackageDlls
									.Select( x => x.ap )
									.Distinct()
									.ToArray();

		//
		// We need to force-reload any package assemblies that depend on a changed package.
		//

		var hotloadedPackages = new HashSet<PackageManager.ActivePackage>();

		foreach ( var e in Package.SortByReferences( changedPackageDlls, x => x.ap.Package ) )
		{
			// don't load editor dlls here unless we're in tools mode
			if ( e.filename.EndsWith( ".editor.dll", StringComparison.OrdinalIgnoreCase ) && !ToolsMode )
				continue;

			var result = LoadAssemblyFromPackage( e.ap, e.filename );

			if ( result is not null && !result.FastHotload )
			{
				hotloadedPackages.Add( e.ap );
			}
		}

		var baseHotloaded = hotloadedPackages.Any( x => x.Package.IsNamed( "local.base" ) );
		var toolBaseHotloaded = hotloadedPackages.Any( x => x.Package.IsNamed( "local.toolbase" ) );

		bool ReferencesHotloadedPackage( Package package )
		{
			switch ( package.TypeName )
			{
				case "tool":
					if ( toolBaseHotloaded ) return true;
					break;

				case "game":
				case "addon":
					if ( baseHotloaded ) return true;
					break;

				default:
					return false;
			}

			return package.EnumeratePackageReferences()
				.Any( x => hotloadedPackages
					.Any( y => y.Package.IsNamed( x ) ) );
		}

		var dependentPackages = loadedPackages
			.Where( x => !changedPackages.Contains( x ) )
			.Where( x => ReferencesHotloadedPackage( x.Package ) )
			.ToArray();

		foreach ( var package in Package.SortByReferences( dependentPackages, x => x.Package ) )
		{
			LoadAllAssembliesFromPackage( package );
		}
	}

	private bool LoadAssemblyFromStream( string assmName, Stream stream, out LoadedAssembly assembly )
	{
		assembly = null;

		if ( !TestAccessControl( stream, out var trustedDll ) )
		{
			log.Warning( $"Couldn't load {assmName} - access control error" );
			trustedDll?.Dispose();
			return false;
		}

		assembly = AddAssembly( null, assmName, trustedDll, null );
		changedPackageDlls.Add( (null, assmName) );

		trustedDll?.Dispose();
		return assembly is not null;
	}

	private LoadedAssembly LoadAssemblyFromPackage( PackageManager.ActivePackage ap, string filename, byte[] bytes = null )
	{
		log.Trace( $"Loading \"{filename}\" from {ap.Package.Title}" );

		// is this a compiler created from the Editor folder in a game addon?
		bool isEditorAssembly = filename.EndsWith( ".editor.dll", StringComparison.OrdinalIgnoreCase ) && !ap.Package.IsRemote;
		bool isToolAssembly = ap.Package.TypeName == "tool" || isEditorAssembly;
		var assmName = System.IO.Path.GetFileNameWithoutExtension( filename );

		// if this is an editor dll, it shouldn't have loaded anywhere but the tools!
		Assert.False( isToolAssembly && !ToolsMode );
		Assert.True( ap.AssemblyFileSystem.FileExists( filename ), "File doesn't exist? Maybe a case sensitivity issue??" );

		bytes ??= ap.AssemblyFileSystem.ReadAllBytes( filename ).ToArray();
		var dll_stream = new System.IO.MemoryStream( bytes );

		TrustedBinaryStream trustedDll = null;

		//
		// Careful now. Remote packages should ALWAYS be access controlled.
		// Local packages, doesn't matter so much.
		//
		bool needsAccessControl = true;
		if ( ap.Package is LocalPackage localPackage )
		{
			if ( DisableAccessControl ) needsAccessControl = false;
			if ( ToolsMode && isToolAssembly ) needsAccessControl = false;
			if ( localPackage.Project.Config.IsStandaloneOnly ) needsAccessControl = false;
		}

		//
		// If this is a locally compiled package, and it's enabled - then skip
		// access control. This is used for tool packages which are ALWAYS local.
		//
		if ( !needsAccessControl )
		{
			trustedDll = PackageManager.AccessControl.TrustUnsafe( bytes );
		}

		//
		// otherwise, we need to do access control
		//
		else
		{
			if ( !TestAccessControl( dll_stream, out trustedDll ) )
			{
				trustedDll?.Dispose();
				log.Warning( $"Couldn't load {assmName} - access control error" );
				return null;
			}
			trustedDll?.Dispose();
		}

		//
		// Report errors here to sentry with extra information
		//
		using var scope = SentrySdk.PushScope();

		if ( ap.Package is LocalPackage local && !Application.IsStandalone )
		{
			var compiler = isToolAssembly ? local.EditorCompiler : local.Compiler;

			SentrySdk.ConfigureScope( scope =>
			{
				scope.Contexts["Code Changes"] = compiler?.ChangeSummary;
			} );
		}

		//
		// Read the codearchive - so we can send this to the client
		//
		var codearchivefilename = System.IO.Path.ChangeExtension( filename, ".cll" );
		byte[] codeArchive = null;
		if ( ap.AssemblyFileSystem.FileExists( codearchivefilename ) )
		{
			codeArchive = ap.AssemblyFileSystem.ReadAllBytes( codearchivefilename ).ToArray();
		}

		var result = AddAssembly( ap.Package, assmName, trustedDll, codeArchive );

		if ( result.FastHotload && ap.Package is LocalPackage lp )
		{
			//
			// For local packages, tell the package's compiler that a fast hotload has occurred for this compiled version.
			// We do this so the compiler will roll back its MetadataReference to the last version that wasn't fast hotloaded.
			// Other compilers that reference this package's compiler should reference non-fast-hotloaded versions only.
			//

			Assert.NotNull( result.Version );

			if ( result.IsEditorAssembly )
			{
				lp.EditorCompiler?.NotifyFastHotload( result.Version );
			}
			else
			{
				lp.Compiler?.NotifyFastHotload( result.Version );
			}
		}

		return result;
	}

	private List<FileWatch> dllWatchers = new();

	private bool LoadPackage( string ident, bool exactName = false )
	{
		log.Trace( $"LoadPackage( {ident} )" );
		var sw = Stopwatch.StartNew();

		var ap = PackageManager.Find( ident, true, exactName );
		if ( ap == null )
		{
			var packages = string.Join( " - \n", PackageManager.ActivePackages.Select( x => x.Package.FullIdent ) );

			throw new System.Exception( $"LoadPackage: Couldn't find active package '{ident}' ({exactName}:exactName)\n{packages}" );
		}

		if ( loadedPackages.Contains( ap ) )
			return true;

		loadedPackages.Add( ap );

		// if this is a local package, we can watch for dll changes
		if ( ap.Package is LocalPackage packageLocal )
		{
			var fw = ap.AssemblyFileSystem.Watch( "*.dll" );
			fw.OnChangedFile += dllName =>
			{
				changedPackageDlls.Add( (ap, dllName) );
			};
			dllWatchers.Add( fw );

			//
			// Hack Sadface: If this is a local game then include the base as a dependency
			//
			{
				bool needsToolsPackage = packageLocal.TypeName == "tool";

				if ( packageLocal.NeedsLocalBasePackage() )
				{
					LoadPackage( "local.base#local" );

					// if we're loading a local game, then load the tools package first, because
					// we are assuming that there is an editor folder, which will require it.
					needsToolsPackage = packageLocal.TypeName == "game" && ToolsMode;
				}

				if ( needsToolsPackage )
				{
					LoadPackage( "local.toolbase#local" );
				}
			}
		}

		//
		// Load any referenced packages first
		//
		foreach ( var child in ap.Package.EnumeratePackageReferences() )
		{
			LoadPackage( child );
		}

		var parent = ap.Package.GetMeta<string>( "ParentPackage", null );
		if ( !string.IsNullOrWhiteSpace( parent ) && Package.TryParseIdent( parent, out var _ ) )
		{
			LoadPackage( parent );
		}

		try
		{
			using var gr = new HeavyGarbageRegion();
			LoadAllAssembliesFromPackage( ap );
		}
		catch
		{
			// Failed to load assemblies, remove as a loaded package
			// If Hack Sadface gets resolved this could be removed and loadedPackages.Add be moved here instead
			loadedPackages.Remove( ap );
			throw;
		}

		log.Trace( $"LoadPackage {ident} took {sw.Elapsed.TotalSeconds:0.00}s" );
		return true;
	}

	private void LoadAllAssembliesFromPackage( PackageManager.ActivePackage package )
	{
		ArgumentNullException.ThrowIfNull( package );

		var ordered = new AssemblyOrderer();

		var assemblyList = package.AssemblyFileSystem.FindFile( "", "*.dll", true ).ToArray();

		foreach ( var assemblyName in assemblyList.OrderBy( x => x.Length ) ) // TODO - we'll have to deal with this at some point
		{
			// don't load editor dlls here unless we're in tools mode
			if ( assemblyName.EndsWith( ".editor.dll", StringComparison.OrdinalIgnoreCase ) && !ToolsMode )
				continue;

			var fileName = assemblyName;
			var bytes = package.AssemblyFileSystem.ReadAllBytes( fileName ).ToArray();
			ordered.Add( fileName, bytes );
		}

		foreach ( (var name, var bytes) in ordered.GetDependencyOrdered() )
		{
			var result = LoadAssemblyFromPackage( package, name, bytes );

			if ( result?.Assembly == null )
				throw new System.Exception( $"Error loading {name}" );
		}
	}

	LoadedAssembly AddAssembly( Package package, string assemblyName, TrustedBinaryStream dllStream, byte[] codeArchive )
	{
		LoadedAssembly loaded = new LoadedAssembly();
		loaded.Name = assemblyName;
		loaded.Package = package;
		loaded.CodeArchiveBytes = codeArchive;

		// read the compiled binary to a byte array
		{
			using var ms = new MemoryStream();
			dllStream.CopyTo( ms );
			loaded.CompiledAssemblyBytes = ms.ToArray();
		}

		if ( dllStream is not null )
		{
			loaded.Assembly = LoadContext.LoadWithEmbeds( loaded.CompiledAssemblyBytes, false );
			loaded.Version = loaded.Assembly?.GetName().Version;
			loaded.Name = loaded.Assembly?.GetName().Name;
		}

		return AddAssembly( loaded );
	}

	internal LoadedAssembly AddAssembly( LoadedAssembly incoming )
	{
		var outgoing = Loaded.FirstOrDefault( x => incoming.Name == x.Name );

		log.Trace( $"AddAssembly {incoming.Assembly} [{incoming.Name}] (outgoing:{outgoing?.Assembly})" );

		//foreach( var l in Loaded )
		//{
		//	log.Trace( $" - '{l.Name}'" );
		//}

		//
		// Skip if it's already loaded
		//
		if ( incoming.Assembly == outgoing?.Assembly )
		{
			log.Trace( $" - Unchanged Assembly - skipping" );
			return outgoing;
		}

		if ( TryFastHotload( incoming, outgoing ) )
		{
			log.Trace( $" - Fast Hotloaded" );
			LoadContext?.UnloadChild( incoming!.Assembly );
			EmitFastHotloadEvent( incoming );
			return outgoing;
		}

		log.Trace( $" - Full Hotloaded" );

		if ( outgoing is not null )
		{
			Loaded.Remove( outgoing );
			log.Trace( $"	Unloading {outgoing}" );
			LoadContext?.UnloadChild( outgoing.Assembly );
		}

		Loaded.Add( incoming );

		if ( incoming.Assembly is not null )
		{
			// We haven't added the incoming assembly to TypeLibrary yet, so we don't
			// let static constructors use it when calling RunAllStaticConstructors below.

			using var disableTypeLibrary = GlobalContext.Current.DisableTypelibraryScope( "Disabled during static constructors." );

			if ( !ToolsMode )
			{
				try
				{
					var ft = FastTimer.StartNew();
					ReflectionUtility.PreJIT( incoming.Assembly );
					Log.Trace( $"PreJit {incoming.Name} took {ft.Elapsed.TotalSeconds:0.00}" );
				}
				catch ( Exception ex )
				{
					Log.Warning( ex, $"{ex.GetType().Name} thrown while calling PreJIT on {incoming.Name} ({ex.Message})" );
				}
			}

			//
			// We can't let user code run during hotload, so run static constructors now.
			//

			try
			{
				ReflectionUtility.RunAllStaticConstructors( incoming.Assembly );
			}
			catch ( Exception ex )
			{
				Log.Warning( ex, $"{ex.GetType().Name} thrown while running static constructors for {incoming.Name}" );
				Log.Warning( ex );
			}
		}

		//
		// Replace( old, new ) will gracefully handle null for either old or new. We need to
		// call it in any case to tell hotload to check static fields in the new assembly during
		// hotloads, or stop checking fields in the old one.
		//

		HotloadManager.Replace( outgoing?.Assembly, incoming.Assembly );

		//
		// The switch event is only called when switching an assembly.
		// Not when adding. Or removing. This should be handled by the code that is
		// adding or removing.
		//
		if ( incoming.Assembly is not null && outgoing?.Assembly is not null )
		{
			incoming.FullHotload = true;
			TriggerUnregisterEvent( outgoing );
			IncomingThisHotload.Add( incoming );
		}

		return incoming;
	}

	/// <summary>
	/// If successful, will change outgoing in place and return true
	/// </summary>
	private bool TryFastHotload( LoadedAssembly incoming, LoadedAssembly outgoing )
	{
		if ( ILHotload is null ) return false;
		if ( incoming is null || outgoing is null ) return false;
		if ( !FastHotloadEnabled ) return false;
		if ( HasOldReferencedAssemblyUnloaded( incoming?.Assembly, outgoing?.Assembly ) ) return false;

		var sw = Stopwatch.StartNew();

		if ( ILHotload.Replace( outgoing?.Assembly, outgoing?.ModifiedAssembly ?? outgoing?.Assembly, incoming?.Assembly ) == false )
			return false;

		sw.Stop();

		log.Trace( $"Fast hotloaded {outgoing.Assembly} with methods from {incoming.Assembly} in {sw.ElapsedMilliseconds}ms" );

		// callbacks
		OnHotloadSuccess();

		//
		// garry: What do we do here? Which assembly changed? What do we need to store?
		//

		outgoing.FastHotload = true;
		outgoing.ModifiedAssembly = incoming?.Assembly;
		outgoing.CodeArchiveBytes = incoming.CodeArchiveBytes;
		outgoing.Version = incoming.Assembly.GetName().Version;
		return true;
	}

	/// <summary>
	/// Return true if, between <paramref name="incoming"/> and <paramref name="outgoing"/>,
	/// package references are added or removed, or a package reference changes version and wasn't fast-hotloaded itself.
	/// If either is the case, the incoming assembly can't be fast-hotloaded because the types it references have changed.
	/// </summary>
	private bool HasOldReferencedAssemblyUnloaded( Assembly incoming, Assembly outgoing )
	{
		foreach ( var asmRefName in incoming?.GetReferencedAssemblies() ?? Array.Empty<AssemblyName>() )
		{
			if ( !(asmRefName.Name?.StartsWith( "package.", StringComparison.OrdinalIgnoreCase ) ?? false) )
			{
				// Only care about package references
				continue;
			}

			var oldRefName = outgoing?.GetReferencedAssemblies()
				.FirstOrDefault( x => asmRefName.Name.Equals( x.Name, StringComparison.OrdinalIgnoreCase ) );

			if ( oldRefName == null )
			{
				// New reference
				return true;
			}

			if ( asmRefName.Version == null || oldRefName.Version == null )
			{
				log.Warning( "Unable to determine version of referenced package assembly" );
				return true;
			}

			if ( oldRefName.Version.CompareTo( asmRefName.Version ) == 0 )
			{
				// Referenced version hasn't changed
				continue;
			}

			var hotloadedAssembly = Loaded.FirstOrDefault( x => x.Name == asmRefName.Name && x.FastHotload );

			if ( hotloadedAssembly is not null && asmRefName.Version.Equals( hotloadedAssembly.Version ) )
			{
				// Referenced version was fast hotloaded, so old referenced version is still loaded,
				// so we can safely fast hotload
				continue;
			}

			log.Trace( $"Can't fast hotload, referenced assembly {asmRefName} has changed" );
			return true;
		}

		return false;
	}

	/// <summary>
	/// Trigger a switch event
	/// </summary>
	private void EmitFastHotloadEvent( LoadedAssembly incoming )
	{
		foreach ( var enroller in Enrollers.ToArray() )
		{
			try
			{
				enroller.OnHotloadEvent( incoming );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Error when running Enrollers fast hotload event for {enroller.Name}" );
			}
		}
	}

	/// <summary>
	/// Trigger a switch event
	/// </summary>
	private void TriggerRegisterEvent( LoadedAssembly incoming )
	{
		foreach ( var enroller in Enrollers.ToArray() )
		{
			try
			{
				enroller.OnRegisterEvent( incoming );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Error when running Enrollers register event for {enroller.Name}" );
			}
		}
	}

	/// <summary>
	/// Trigger a switch event
	/// </summary>
	private void TriggerUnregisterEvent( LoadedAssembly outgoing )
	{
		foreach ( var enroller in Enrollers.ToArray() )
		{
			try
			{
				enroller.OnUnregisterEvent( outgoing );
			}
			catch ( System.Exception e )
			{
				Log.Warning( e, $"Error when running Enrollers Unregister event for {enroller.Name}" );
			}
		}
	}

	/// <summary>
	/// Called by Enroller to get the assemblies associated with a package. If deep is true then
	/// this should return all assemblies needed by this package and its dependencies.
	/// </summary>
	public LoadedAssembly[] GetLoadedAssemblies( string packageIdent, bool deep, bool allowEditor )
	{
		var package = PackageManager.Find( packageIdent, true );
		if ( package is null ) return Array.Empty<LoadedAssembly>();

		var assemblyList = package.AssemblyFileSystem.FindFile( "", "*.dll", true ).ToArray();

		HashSet<LoadedAssembly> foundList = new();

		if ( deep )
		{
			if ( package.Package is LocalPackage pl && pl.NeedsLocalBasePackage() )
			{
				var children = GetLoadedAssemblies( "local.base#local", deep, allowEditor );

				foreach ( var c in children )
					foundList.Add( c );
			}

			foreach ( var child in package.Package.EnumeratePackageReferences() )
			{
				var children = GetLoadedAssemblies( child, deep, allowEditor );
				if ( children.Length == 0 ) continue;

				foreach ( var c in children )
					foundList.Add( c );
			}
		}

		foreach ( var assemblyName in assemblyList.OrderBy( x => x.Length ) )
		{
			// don't load editor dlls here unless we're in tools mode
			if ( assemblyName.EndsWith( ".editor.dll", StringComparison.OrdinalIgnoreCase ) && !allowEditor )
				continue;

			var withoutExtension = System.IO.Path.GetFileNameWithoutExtension( assemblyName );
			var found = Loaded.FirstOrDefault( x => x.Name == withoutExtension );
			if ( found is not null )
			{
				foundList.Add( found );
			}
		}

		return foundList.ToArray();
	}

	public void HotloadWatch( Assembly assembly )
	{
		if ( assembly is null ) return;
		HotloadManager.Watch( assembly );
	}

	public void HotloadIgnore( Assembly assembly )
	{
		if ( assembly is null ) return;
		HotloadManager.Ignore( assembly );
	}

	public void Tick()
	{
		if ( !changedPackageDlls.Any() && IncomingThisHotload.Count == 0 )
		{
			return;
		}

		LoadPendingChanges();

		try
		{
			// We want to lock the mixing thread,
			// so things don't get modified while it's using them
			lock ( MixingThread.LockObject )
			{
				HotloadManager.DoSwap();
			}
		}
		finally
		{
			IncomingThisHotload.Clear();
		}
	}

	void OnFullHotloadSuccess()
	{
		foreach ( var loadedAssembly in IncomingThisHotload )
		{
			TriggerRegisterEvent( loadedAssembly );
		}

		OnHotloadSuccess();
	}

	void OnHotloadSuccess()
	{
		OnAfterHotload?.Invoke();
	}

	public Action OnAfterHotload { get; set; }
}
