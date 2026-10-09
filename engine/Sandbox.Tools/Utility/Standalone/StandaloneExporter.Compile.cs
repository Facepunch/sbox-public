namespace Editor;

partial class StandaloneExporter
{
	async Task Compile()
	{
		var compilerSettings = Project.Config.GetCompileSettings();
		compilerSettings.Whitelist = false;
		if ( !compilerSettings.GetPreprocessorSymbols().Contains( "STANDALONE" ) )
			compilerSettings.DefineConstants += ";STANDALONE";

		var generated = await EditorUtility.Projects.Compile( Project, compilerSettings, ( s ) => Logger.Info( $"[Compiler] {s}" ) );
		if ( generated == null )
		{
			throw new System.Exception( "Failed to compile project" );
		}

		Dictionary<string, object> extrafiles = new();

		foreach ( var assembly in generated )
		{
			extrafiles[$".bin/{assembly.Compiler.AssemblyName}.dll"] = assembly.AssemblyData;
			Logger.Info( $"Adding: {assembly.Compiler.AssemblyName}.dll" );

			PeekAssembly( assembly.Compiler.AssemblyName, assembly.AssemblyData );
		}

		_exportConfig.AssemblyFiles = extrafiles;
	}

	/// <summary>
	/// Look inside this assembly for anything useful we can fill the manifest with
	/// </summary>
	private void PeekAssembly( string title, byte[] contents )
	{
		var attr = AssemblyMetadata.GetCustomAttributes( contents );

		var assetAttributes = attr.Where( x => x.AttributeFullName == "Sandbox.Cloud/AssetAttribute" )
								.ToArray();

		foreach ( var a in assetAttributes )
		{
			var ident = $"{a.Arguments[0]}";

			if ( !Package.TryParseIdent( ident, out var parts ) )
			{
				Log.Warning( $"Couldn't parse ident {ident}" );
				continue;
			}

			// do we need to add .version here?
			_exportConfig.CodePackages.Add( $"{parts.org}.{parts.package}" );
		}
	}
}
