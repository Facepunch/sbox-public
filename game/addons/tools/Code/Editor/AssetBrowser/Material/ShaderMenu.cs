namespace Editor;

internal static class ShaderMenu
{
	[Event( "asset.contextmenu", Priority = 50 )]
	public static void OnShaderFileAssetContext( AssetContextMenu e )
	{
		// Are all the files we have selected image assets?
		if ( !e.SelectedList.All( x => x.AssetType == AssetType.Shader ) )
			return;

		if ( e.SelectedList.Count == 1 )
			e.Menu.AddOption( $"Create Material", "image", action: () => CreateMaterialUsingShader( e.SelectedList.FirstOrDefault() ) );

		// A VFX shader (the legacy format) can be written again as Slang beside it
		if ( e.SelectedList.Count == 1 && e.SelectedList[0].Asset?.GetSourceFile( true ) is { } source && ShaderUpgrade.IsLegacy( source ) )
			e.Menu.AddOption( "Upgrade to Slang", "upgrade", action: () => UpgradeToSlang( source ) );
	}

	/// <summary>
	/// Write the shader again as Slang beside it (a surface, or a standalone module), asking first when there's one there
	/// already. The .shader stays as it is.
	/// </summary>
	private static void UpgradeToSlang( string source )
	{
		var target = System.IO.Path.ChangeExtension( source, ".slang" );
		if ( System.IO.File.Exists( target ) )
		{
			Dialog.AskConfirm( () => _ = Upgrade( source, true ), $"{System.IO.Path.GetFileName( target )} is there already. Replace it with a new upgrade of {System.IO.Path.GetFileName( source )}?", "Upgrade to Slang", "Replace" );
			return;
		}

		_ = Upgrade( source, false );
	}

	private static async Task Upgrade( string source, bool overwrite )
	{
		var result = await ShaderUpgrade.UpgradeAsync( source, overwrite );
		var notes = result.Notes.Count > 0 ? "\n\nNot converted to a surface:\n" + string.Join( "\n", result.Notes.Select( x => $"- {x}" ) ) : "";
		var errors = result.Errors is null ? "" : $"\n\n{result.Errors}";

		switch ( result.Kind )
		{
			case ShaderUpgrade.UpgradeKind.Surface:
			case ShaderUpgrade.UpgradeKind.Module:
				Log.Info( result.Message );
				if ( result.Errors is not null ) Log.Warning( result.Errors );

				// A surface beside the .shader it came from: both drawn, each with its defaults
				var (before, after) = result.Kind == ShaderUpgrade.UpgradeKind.Surface ? await CompareMaterials( source, result.Path ) : (null, null);
				new ShaderUpgradeDialog( result.Message + notes + errors, result.Errors is null,
					before, System.IO.Path.GetFileName( source ), after, System.IO.Path.GetFileName( result.Path ) ).Show();

				var asset = AssetSystem.RegisterFile( result.Path );
				MainAssetBrowser.Instance?.Local.UpdateAssetList();
				if ( asset is not null ) MainAssetBrowser.Instance?.Local.FocusOnAsset( asset );
				break;

			default:
				Log.Warning( result.Message );
				EditorUtility.DisplayDialog( "Upgrade to Slang", result.Message );
				break;
		}
	}

	/// <summary>
	/// A material of the <c>.shader</c> and one of the surface made from it, each with its defaults; nulls when either can't
	/// be made. The surface is compiled from its text, as an anonymous shader: the material system finds the <c>.shader</c>
	/// first by the name the two share.
	/// </summary>
	private static async Task<(Material Before, Material After)> CompareMaterials( string shaderPath, string slangPath )
	{
		try
		{
			var name = AssetSystem.FindByPath( shaderPath )?.Path;
			if ( string.IsNullOrEmpty( name ) ) return (null, null);

			var surface = await Shader.CompileAsync( await System.IO.File.ReadAllTextAsync( slangPath ) );
			if ( surface is null ) return (null, null);

			var before = Material.Create( "shader_upgrade_before", name );
			var after = Material.Create( "shader_upgrade_after", surface );
			return before is null || after is null ? (null, null) : (before, after);
		}
		catch ( Exception e )
		{
			Log.Warning( $"Upgrade to Slang: can't draw the two side by side: {e.Message}" );
			return (null, null);
		}
	}

	private static void CreateMaterialUsingShader( AssetEntry entry )
	{
		var asset = entry.Asset;
		var assetName = asset.Name;

		var fd = new FileDialog( null );
		fd.Title = "Create Material from Shader..";
		fd.Directory = System.IO.Path.GetDirectoryName( asset.AbsolutePath );
		fd.DefaultSuffix = ".vmat";
		fd.SelectFile( $"{assetName}.vmat" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Material File (*.vmat)" );

		if ( !fd.Execute() )
			return;

		var shaderPath = asset.GetCompiledFile();

		var file = $@"
Layer0
{{
	shader ""{shaderPath}""

}}
";
		System.IO.File.WriteAllText( fd.SelectedFile, file );

		var resultAsset = AssetSystem.RegisterFile( fd.SelectedFile );

		// These 3 lines are gonna be quite common I think.
		MainAssetBrowser.Instance?.Local.UpdateAssetList();
		MainAssetBrowser.Instance?.Local.FocusOnAsset( resultAsset );
		EditorUtility.InspectorObject = resultAsset;
	}
}
