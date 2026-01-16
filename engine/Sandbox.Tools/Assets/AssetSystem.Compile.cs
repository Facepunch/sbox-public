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

		var assetType = AssetType.Find( extension );
		if ( assetType is null )
		{
			Log.Info( $"Unknown asset type for {extension} - skipping compile!" );
			return false;
		}

		var compilers = EditorTypeLibrary.GetTypes<ResourceCompiler>().Where( x => !x.IsInterface && !x.IsAbstract ).ToArray();
		var chosen = compilers.Where( x => x.GetAttributes<ResourceCompiler.ResourceIdentityAttribute>().Any( y => y.Name == extension ) ).FirstOrDefault();

		// do we have a specific compiler?
		if ( chosen is not null )
		{
			var compiler = chosen.Create<ResourceCompiler>();
			compiler.SetContext( context );
			return compiler.CompileInternal();
		}

		// this is a game resource
		if ( assetType.IsGameResource )
		{
			CompileGameResource( context );
			return true;
		}

		// Nothing!

		return false;
	}

	static void CompileGameResource( ResourceCompileContext context )
	{
		// Get the json contents
		var jsonString = System.IO.File.ReadAllText( context.AbsolutePath );

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

		// Write binary blob data to BLOB block if companion file exists
		var blobPath = context.AbsolutePath + "_d";
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
