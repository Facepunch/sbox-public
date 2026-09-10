using NativeEngine;
using System.IO;

namespace Editor;

partial class StandaloneExporter
{

	record CodeResource( string RelativePath, string AbsolutePath );

	/// <summary>
	/// Fetch all code-defined core resources
	/// </summary>
	private static List<CodeResource> GetCodeResources( string engineDir )
	{
		var codeResources = CUtlVectorString.Create( 8, 8 );
		g_pResourceSystem.GetAllCodeManifests( codeResources );

		//
		// Resource manifest members have a path that is relative to the mounted filesystem
		// e.g. materials/core/lpvgridmaterial.vmat
		// Which means that the asset might be in core/, or it might be in assets/base/, or
		// it might be somewhere else.
		// We need to figure out where it is in order to copy it over successfully, (though
		// it should get copied anyway most of the time - if it's a shader or material in
		// the core folder)
		//

		List<CodeResource> list = new();
		for ( int i = 0; i < codeResources.Count(); ++i )
		{
			var relativePath = codeResources.Element( i );

			bool AddIfExists( string addonRootPath )
			{
				var absolutePath = $"{engineDir}/{addonRootPath}/{relativePath}";

				if ( File.Exists( absolutePath ) )
				{
					list.Add( new CodeResource( relativePath, absolutePath ) );
					return true;
				}

				var compiledPath = absolutePath + "_c";

				if ( File.Exists( compiledPath ) )
				{
					list.Add( new CodeResource( relativePath, compiledPath ) );
					return true;
				}

				return false;
			}

			// Check core
			if ( AddIfExists( "core" ) )
				continue;

			Logger.Warning( $"Code resource '{relativePath}' was NOT found in core?" );
		}

		codeResources.DeleteThis();
		return list;
	}
}
