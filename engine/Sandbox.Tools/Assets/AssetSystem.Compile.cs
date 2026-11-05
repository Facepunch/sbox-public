using NativeEngine;
using Sandbox.Resources;
using System;

namespace Editor;

public static partial class AssetSystem
{
	internal unsafe static bool TryManagedCompile( IResourceCompilerContext _context )
	{
		using var context = new ResourceCompileContextImp( _context );

		var filename = context.AbsolutePath;
		var extension = System.IO.Path.GetExtension( filename ).Trim( '.' );

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
			return compiler.CompileInternal();
		}

		if ( !IsGameResource( extension ) )
			return false;

		CompileGameResource( context );
		return true;
	}

	/// <summary>
	/// A game resource's extension: one a type of the editor's type library declares (<see cref="AssetTypeAttribute"/>),
	/// which is what its asset type comes from.
	/// </summary>
	internal static bool IsGameResource( string extension )
	{
		var set = _gameResourceExtensions ??= EditorTypeLibrary.GetAttributes<AssetTypeAttribute>().Select( x => x.Extension ).ToHashSet( StringComparer.OrdinalIgnoreCase );
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
			if ( !gameResource && FindCompilerForExtension( extension ) is null ) continue;

			yield return $".{extension}\t{(gameResource ? $".{extension.ToLowerInvariant()}_c" : "")}";
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

	static TypeDescription FindCompilerForExtension( string extension )
	{
		var map = _compilersByExtension;

		if ( map is null )
		{
			map = new Dictionary<string, TypeDescription>( System.StringComparer.OrdinalIgnoreCase );

			foreach ( var type in EditorTypeLibrary.GetTypes<ResourceCompiler>() )
			{
				if ( type.IsInterface || type.IsAbstract )
					continue;

				foreach ( var identity in type.GetAttributes<ResourceCompiler.ResourceIdentityAttribute>() )
				{
					map.TryAdd( identity.Name, type );
				}
			}

			_compilersByExtension = map;
		}

		return map.TryGetValue( extension, out var found ) ? found : null;
	}

	internal static void CompileGameResource( ResourceCompileContextImp context )
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

		jsonString = context.ScanJson( jsonString );

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
