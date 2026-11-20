using Sandbox.ShaderCompiler;
using System;
using System.IO;

namespace Editor;

public static partial class AssetSystem
{
	static readonly Dictionary<string, (DateTime Written, PassModule Module)> describedModules = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The native material editor's view of a shader only a module describes (IAssetSystem::DescribeModuleShader): the
	/// module at <paramref name="modulePath"/> for these feature values (<c>"F_A=1 F_B=2"</c>), as
	/// <see cref="MaterialEditorDescription"/> writes it. Empty when it can't be read.
	/// </summary>
	internal static string DescribeModuleShader( string modulePath, string featureValues )
	{
		try
		{
			if ( !File.Exists( modulePath ) ) return "";
			return MaterialEditorDescription.Describe( DescribedModule( modulePath ), MaterialEditorDescription.ParseFeatureValues( featureValues ) );
		}
		catch ( Exception e ) when ( e is IOException or InvalidOperationException or ArgumentException or FormatException )
		{
			Log.Warning( $"Couldn't describe {modulePath} for the material editor: {e.Message}" );
			return "";
		}
	}

	/// <summary>A module read once while its file is unchanged, its includes found beside it and in the engine's shaders.</summary>
	static PassModule DescribedModule( string path )
	{
		var written = File.GetLastWriteTimeUtc( path );
		lock ( describedModules )
		{
			if ( describedModules.TryGetValue( path, out var known ) && known.Written == written ) return known.Module;
			var folders = new List<string> { Path.GetDirectoryName( path ) };
			if ( EngineFileSystem.Root?.GetFullPath( "/core/shaders" ) is { } core && Directory.Exists( core ) ) folders.Add( core );
			var module = PassModule.Read( path, [.. folders] );
			describedModules[path] = (written, module);
			return module;
		}
	}
}
