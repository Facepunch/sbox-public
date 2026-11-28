using NativeEngine;
using Sandbox.Resources;
using System;

namespace Editor;

public static partial class AssetSystem
{
	/// <summary>
	/// A compile of a type managed code compiles. On the main thread, where the type library changes (a hotload), as are
	/// managed compiles of build jobs running on threads of their own (<see cref="ContentCompilerHost.CompileOnMainThread"/>).
	/// </summary>
	internal static bool TryManagedCompile( IResourceCompilerContext context ) => ContentCompilerHost.CompileOnMainThread( () => ManagedCompile( context ) );

	/// <summary>
	/// A compile asked for while the editor's content build holds the roots: whether the build let go of them.
	/// </summary>
	internal static bool YieldContentBuild() => EditorContentBuild.Yield();

	static async Task<bool> ManagedCompile( IResourceCompilerContext _context )
	{
		using var context = new ResourceCompileContextImp( _context );

		var filename = context.AbsolutePath;
		var extension = System.IO.Path.GetExtension( filename ).Trim( '.' );

		if ( AssetType.Find( extension )?.IsUncompiled == true )
			return false;

		// Do we have a specific compiler? These register the source extensions they
		// handle themselves ([ResourceIdentity("wav")] etc), so check before the
		// asset type bail below - source extensions like wav aren't asset type
		// identifiers, and skipping here silently dropped the blocks these
		// compilers write (a sound's visemes and subtitles).
		var chosen = FindCompilerForExtension( extension );

		if ( chosen is not null )
		{
			var compiler = chosen.Create<ResourceCompiler>();
			compiler.SetContext( context );
			return await compiler.CompileInternalAsync();
		}

		if ( !IsGameResource( extension ) )
			return false;

		await CompileGameResource( context );
		return true;
	}

	/// <summary>
	/// A game resource's extension: one a type of the editor's type library declares (<see cref="AssetTypeAttribute"/>),
	/// which is what its asset type comes from.
	/// </summary>
	internal static bool IsGameResource( string extension )
	{
		var set = _gameResourceExtensions ??= EditorTypeLibrary.GetAttributes<AssetTypeAttribute>()
			.Where( x => !x.IsUncompiled && x.TargetType.IsAssignableTo( typeof( GameResource ) ) )
			.Select( x => x.Extension ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		return set.Contains( extension );
	}

	static volatile HashSet<string> _gameResourceExtensions;

	/// <summary>
	/// For a content build: each source type managed code takes part in (a game resource, or a compiler registered for
	/// its extension), with a game resource's compiled output extension (a native type's comes from the engine's asset
	/// types).
	/// </summary>
	internal static IEnumerable<string> ManagedCompilerTypes()
	{
		var extensions = EditorTypeLibrary.GetAttributes<AssetTypeAttribute>().Select( x => x.Extension )
			.Concat( EditorTypeLibrary.GetTypes<ResourceCompiler>().SelectMany( x => x.GetAttributes<ResourceCompiler.ResourceIdentityAttribute>() ).Select( x => x.Name ) )
			.Distinct( StringComparer.OrdinalIgnoreCase );

		foreach ( var extension in extensions.Order( StringComparer.OrdinalIgnoreCase ) )
		{
			var gameResource = IsGameResource( extension );
			var compiler = FindCompilerForExtension( extension );
			if ( !gameResource && (compiler is null || compiler.HasAttribute<ResourceCompiler.NoCompiledReadsAttribute>()) ) continue;

			yield return $".{extension}\t{(gameResource ? $".{extension.ToLowerInvariant()}_c" : "")}";
		}
	}

	/// <summary>
	/// Same result as <see cref="ManagedCompilerTypes()"/> by scanning the engine, tools and compiler assemblies directly,
	/// avoiding full <see cref="Sandbox.Internal.TypeLibrary"/> and <see cref="GameData"/> construction when a worker only
	/// answers type/fingerprint queries on an up-to-date build.
	/// </summary>
	internal static IEnumerable<string> ManagedCompilerTypes( IEnumerable<System.Reflection.Assembly> compilerAssemblies )
	{
		var gameResources = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var compilerTypes = new List<Type>();

		(System.Reflection.Assembly Assembly, bool Dynamic)[] sources =
		[
			(typeof( Vector3 ).Assembly, false),
			(typeof( BaseFileSystem ).Assembly, false),
			(typeof( Sandbox.Internal.TypeLibrary ).Assembly, false),
			(typeof( ToolsDll ).Assembly, true),
			(typeof( EngineLoop ).Assembly, false),
			(typeof( Facepunch.ActionGraphs.ActionGraph ).Assembly, false),
			.. (compilerAssemblies ?? []).Select( a => (a, true) ),
		];

		foreach ( var (assembly, isDynamic) in sources )
		{
			foreach ( var type in assembly.GetTypes() )
			{
				if ( type.IsAssignableTo( typeof( Delegate ) ) ) continue;
				if ( !isDynamic && !IsExposedEngineType( type ) ) continue;

				if ( type.IsAssignableTo( typeof( GameResource ) ) )
				{
					foreach ( var attr in type.GetCustomAttributes( typeof( AssetTypeAttribute ), inherit: false ).OfType<AssetTypeAttribute>() )
					{
						if ( !attr.IsUncompiled && !string.IsNullOrEmpty( attr.Extension ) )
							gameResources.Add( attr.Extension );
					}
				}

				if ( !type.IsInterface && !type.IsAbstract && type.IsAssignableTo( typeof( ResourceCompiler ) ) )
				{
					compilerTypes.Add( type );
				}
			}
		}

		var compilers = new Dictionary<string, Type>( StringComparer.OrdinalIgnoreCase );
		foreach ( var type in OrderCompilers( compilerTypes, x => x ) )
		{
			foreach ( var identity in type.GetCustomAttributes( typeof( ResourceCompiler.ResourceIdentityAttribute ), inherit: false ).OfType<ResourceCompiler.ResourceIdentityAttribute>() )
			{
				compilers.TryAdd( identity.Name, type );
			}
		}

		var extensions = gameResources.Concat( compilers.Keys ).Distinct( StringComparer.OrdinalIgnoreCase );
		foreach ( var extension in extensions.Order( StringComparer.OrdinalIgnoreCase ) )
		{
			var gameResource = gameResources.Contains( extension );
			compilers.TryGetValue( extension, out var compiler );
			if ( !gameResource && (compiler is null || Attribute.IsDefined( compiler, typeof( ResourceCompiler.NoCompiledReadsAttribute ), inherit: true )) ) continue;

			yield return $".{extension}\t{(gameResource ? $".{extension.ToLowerInvariant()}_c" : "")}";
		}

		static bool IsExposedEngineType( Type t )
		{
			if ( Attribute.IsDefined( t, typeof( LibraryAttribute ) ) ) return true;
			if ( Attribute.IsDefined( t, typeof( ExposeAttribute ) ) ) return true;
			if ( Attribute.IsDefined( t, typeof( ActionGraphIncludeAttribute ) ) ) return true;
			if ( t.IsNestedPublic && !Attribute.IsDefined( t, typeof( System.Runtime.CompilerServices.CompilerGeneratedAttribute ) ) )
				return IsExposedEngineType( t.DeclaringType );
			return false;
		}
	}

	// Extension -> compiler type, so bulk compiles don't repeat the type library
	// scan and attribute reflection once per asset. Case-insensitive because the
	// asset system lowercases paths but direct CompileResource callers might not.
	// Volatile and read via a local snapshot: compiles can run off the main
	// thread while a hotload on the main thread clears the cache.
	static volatile Dictionary<string, TypeDescription> _compilersByExtension;

	[EditorEvent.Hotload]
	static void ClearCompilerCache()
	{
		_compilersByExtension = null;
		_gameResourceExtensions = null;
	}

	/// <summary>
	/// Deterministic compiler precedence shared by <see cref="FindCompilerForExtension"/> and
	/// <see cref="ManagedCompilerTypes(IEnumerable{System.Reflection.Assembly})"/>: project/addon
	/// compilers take precedence over built-in <c>Sandbox.Tools</c> compilers, with ties broken by
	/// assembly and full type name.
	/// </summary>
	static IEnumerable<T> OrderCompilers<T>( IEnumerable<T> items, Func<T, Type> getType ) =>
		items
			.OrderBy( x => getType( x ).Assembly == typeof( ToolsDll ).Assembly ? 1 : 0 )
			.ThenBy( x => getType( x ).Assembly.GetName().Name, StringComparer.Ordinal )
			.ThenBy( x => getType( x ).FullName, StringComparer.Ordinal );

	static TypeDescription FindCompilerForExtension( string extension )
	{
		var map = _compilersByExtension;

		if ( map is null )
		{
			map = new Dictionary<string, TypeDescription>( System.StringComparer.OrdinalIgnoreCase );

			var candidates = EditorTypeLibrary.GetTypes<ResourceCompiler>()
				.Where( type => !type.IsInterface && !type.IsAbstract );

			foreach ( var type in OrderCompilers( candidates, x => x.TargetType ) )
			{
				foreach ( var identity in type.GetAttributes<ResourceCompiler.ResourceIdentityAttribute>() )
				{
					map.TryAdd( identity.Name, type );
				}
			}

			_compilersByExtension = map;
		}

		return map.TryGetValue( extension, out var found ) ? found : null;
	}

	internal static async Task CompileGameResource( ResourceCompileContextImp context )
	{
		// Get the json contents: the file's, or what a parent compile gave a child it generated
		var jsonString = context.TryReadOverrideInput( out var input ) ? System.Text.Encoding.UTF8.GetString( input ) : System.IO.File.ReadAllText( context.AbsolutePath );

		//
		// Pre Feb-2023 we saved GameResources to keyvalues. Keep support for loading this
		// format for a while by loading those keyvalues and converting them to json.
		//
		if ( jsonString.StartsWith( '<' ) )
		{
			log.Trace( $"KeyValue format detected ({context.AbsolutePath}) - converting to json" );
			var kv = EngineGlue.LoadKeyValues3( jsonString );
			jsonString = EngineGlue.KeyValues3ToJson( kv.FindOrCreateMember( "data" ) );
			kv.DeleteThis();
		}

		jsonString = await context.ScanJsonAsync( jsonString );

		context.Data.Write( jsonString );

		// Write binary blob data to BLOB block if companion file exists, and compile again once one does
		var blobPath = context.AbsolutePath + "_d";
		context.AddCompileReference( blobPath, optional: true );
		if ( System.IO.File.Exists( blobPath ) )
		{
			var blobData = System.IO.File.ReadAllBytes( blobPath );
			unsafe
			{
				fixed ( byte* ptr = blobData )
				{
					context.WriteBlock( BlobDataSerializer.CompiledBlobName, (IntPtr)ptr, blobData.Length );
				}
			}
		}
	}

	/// <summary>
	/// Compile a resource from text.
	/// </summary>
	public static bool CompileResource( string path, string text )
	{
		if ( string.IsNullOrWhiteSpace( text ) )
			return false;

		return IResourceCompilerSystem.GenerateResourceFile( path, text );
	}

	/// <summary>
	/// Compile a resource from binary data.
	/// </summary>
	public static unsafe bool CompileResource( string path, ReadOnlySpan<byte> data )
	{
		if ( data.Length == 0 )
			return false;

		fixed ( byte* dataPtr = data )
		{
			return IResourceCompilerSystem.GenerateResourceFile( path, (IntPtr)dataPtr, data.Length );
		}
	}
}
