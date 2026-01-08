namespace Editor;

internal static class MaterialMenu
{
	[Event( "asset.contextmenu", Priority = 50 )]
	public static void OnMaterialFileAssetContext( AssetContextMenu e )
	{
		// Are all the files we have selected image assets?
		if ( !e.SelectedList.All( x => x.AssetType == AssetType.ImageFile ) )
			return;

		e.Menu.AddOption( $"Create Material", "image", action: () => CreateMaterialUsingImageFiles( e.SelectedList ) );

		if ( e.SelectedList.Count == 1 )
		{
			e.Menu.AddOption( $"Create Texture", "texture", action: () => CreateTextureUsingImageFiles( e.SelectedList ) );
			e.Menu.AddOption( $"Create Sprite", "emoji_emotions", action: () => CreateSpriteUsingImageFiles( e.SelectedList ) );
		}
		else
		{
			var menuTex = e.Menu.AddMenu( $"Create Texture", "texture" );
			menuTex.AddOption( $"Create Texture", "texture", action: () => CreateTextureUsingImageFiles( e.SelectedList ) );
			menuTex.AddOption( $"Create Texture Sheet", "grid_on", action: () => CreateTextureUsingImageFiles( e.SelectedList, true ) );
			var menuSprite = e.Menu.AddMenu( $"Create Sprite", "emoji_emotions" );
			menuSprite.AddOption( $"Create Sprite", "emoji_emotions", action: () => CreateSpriteUsingImageFiles( e.SelectedList ) );
			menuSprite.AddOption( $"Create Animation", "directions_run", action: () => CreateSpriteUsingImageFiles( e.SelectedList, true ) );
		}
	}

	private static void CreateTextureUsingImageFiles( IEnumerable<AssetEntry> entries, bool isTextureSheet = false )
	{
		var asset = entries.First().Asset;
		var assetName = asset.Name;

		var fd = new FileDialog( null );
		fd.Title = "Create Texture from Image Files..";
		fd.Directory = System.IO.Path.GetDirectoryName( asset.AbsolutePath );
		fd.DefaultSuffix = ".vtex";
		fd.SelectFile( $"{assetName}.vtex" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Texture File (*.vtex)" );

		if ( !fd.Execute() )
			return;

		if ( isTextureSheet )
		{
			var paths = entries.Select( x => System.IO.Path.ChangeExtension( x.Asset.Path, System.IO.Path.GetExtension( x.Asset.AbsolutePath ) ) );
			var file = TextureEditor.TextureFile.CreateDefault( paths );
			var json = Json.Serialize( file );
			System.IO.File.WriteAllText( fd.SelectedFile, json );

			asset = AssetSystem.RegisterFile( fd.SelectedFile );
		}
		else
		{
			// Individually export textures
			foreach ( var p in entries )
			{
				var path = System.IO.Path.ChangeExtension( p.Asset.Path, System.IO.Path.GetExtension( p.Asset.AbsolutePath ) );
				bool noCompress = p.Asset.MetaData.Get<bool>( "nocompress" );
				var file = TextureEditor.TextureFile.CreateDefault( [path], noCompress );
				var json = Json.Serialize( file );
				var outPath = System.IO.Path.Combine( System.IO.Path.GetDirectoryName( fd.SelectedFile ), System.IO.Path.GetFileNameWithoutExtension( path ) + ".vtex" );
				System.IO.File.WriteAllText( outPath, json );
				asset = AssetSystem.RegisterFile( outPath );
			}
		}

		MainAssetBrowser.Instance?.Local.UpdateAssetList();
		MainAssetBrowser.Instance?.Local.FocusOnAsset( asset );
		EditorUtility.InspectorObject = asset;
	}

	private static void CreateMaterialUsingImageFiles( IEnumerable<AssetEntry> entries )
	{
		string[] types = new[] { "color", "ao", "normal", "metallic", "rough", "diff", "diffuse", "nrm", "spec", "selfillum", "mask" };

		var asset = entries.First().Asset;
		var assetName = asset.Name;

		Log.Info( assetName );

		foreach ( var t in types )
		{
			if ( assetName.EndsWith( $"_{t}" ) ) assetName = assetName.Substring( 0, assetName.Length - (t.Length + 1) );
		}

		var fd = new FileDialog( null );
		fd.Title = "Create Material from Image Files..";
		fd.Directory = System.IO.Path.GetDirectoryName( asset.AbsolutePath );
		fd.DefaultSuffix = ".vmat";
		fd.SelectFile( $"{assetName}.vmat" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Material File (*.vmat)" );

		if ( !fd.Execute() )
			return;

		// Find all the image files in the same folder as the first asset we selected
		var assetPath = System.IO.Path.GetDirectoryName( asset.AbsolutePath ).NormalizeFilename( false );
		var assetPeers = AssetSystem.All.Where( x => x.AssetType == AssetType.ImageFile ).Where( x => x.AbsolutePath.StartsWith( assetPath ) ).ToArray();
		var assetPeersWithSameBaseName = assetPeers.Where( x => x.Name == assetName || x.Name.StartsWith( assetName + "_" ) ).ToArray();
		if ( assetPeersWithSameBaseName.Length > 0 )
		{
			assetPeers = assetPeersWithSameBaseName;
		}

		//
		// Try to work out what textures should go where using hacks and magic
		//

		string texColor = assetPeers.Where( x => x.Name.Contains( "_color" ) || x.Name.Contains( "_diff" ) ).Select( x => x.RelativePath ).FirstOrDefault();
		texColor ??= asset.RelativePath; // Failing that, lets use whatever we have selected, since they most likely selected the color one right

		string texNormal = assetPeers.Where( x => x.Name.Contains( "_nrm" ) || x.Name.Contains( "_normal" ) || x.Name.Contains( "_amb" ) ).Select( x => x.RelativePath ).FirstOrDefault( "materials/default/default_normal.tga" );
		string texAo = assetPeers.Where( x => x.Name.Contains( "_ao" ) || x.Name.Contains( "_occ" ) || x.Name.Contains( "_amb" ) ).Select( x => x.RelativePath ).FirstOrDefault( "materials/default/default_ao.tga" );
		string texRough = assetPeers.Where( x => x.Name.Contains( "_rough" ) ).Select( x => x.RelativePath ).FirstOrDefault( "materials/default/default_rough.tga" );

		string texMetallic = assetPeers.Where( x => x.Name.Contains( "_metallic" ) ).Select( x => x.RelativePath ).FirstOrDefault();
		if ( texMetallic != null )
		{
			texMetallic = $"\n	F_METALNESS_TEXTURE 1\n	F_SPECULAR 1\n	TextureMetalness \"{texMetallic}\"";
		}

		string texSelfIllum = assetPeers.Where( x => x.Name.Contains( "_selfillum" ) ).Select( x => x.RelativePath ).FirstOrDefault();
		if ( texSelfIllum != null )
		{
			texSelfIllum = $"\n	F_SELF_ILLUM 1\n	TextureSelfIllumMask \"{texSelfIllum}\"";
		}

		string tintMask = assetPeers.Where( x => x.Name.Contains( "_mask" ) ).Select( x => x.RelativePath ).FirstOrDefault();
		if ( tintMask != null )
		{
			tintMask = $"\n	F_TINT_MASK 1\n	TextureTintMask \"{texSelfIllum}\"";
		}

		var file = $@"
Layer0
{{
	shader ""shaders/complex.shader_c""

	TextureColor ""{texColor}""
	TextureAmbientOcclusion ""{texAo}""
	TextureNormal ""{texNormal}""
	TextureRoughness ""{texRough}""{texMetallic}{texSelfIllum}{tintMask}

}}
";
		System.IO.File.WriteAllText( fd.SelectedFile, file );

		var resultAsset = AssetSystem.RegisterFile( fd.SelectedFile );

		// These 3 lines are gonna be quite common I think.
		MainAssetBrowser.Instance?.Local.UpdateAssetList();
		MainAssetBrowser.Instance?.Local.FocusOnAsset( resultAsset );
		EditorUtility.InspectorObject = resultAsset;
	}

	private static async void CreateSpriteUsingImageFiles( IEnumerable<AssetEntry> entries, bool isAnimation = false )
	{
		var asset = entries.First().Asset;
		var assetName = asset.Name;

		var fd = new FileDialog( null );
		fd.Title = "Create Sprite from Image Files..";
		fd.Directory = System.IO.Path.GetDirectoryName( asset.AbsolutePath );
		fd.DefaultSuffix = ".sprite";
		fd.SelectFile( $"{assetName}.sprite" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Sprite File (*.sprite)" );

		if ( !fd.Execute() )
			return;

		if ( isAnimation )
		{
			// Export one sprite with each frame as an animation
			var paths = entries.Select( x => System.IO.Path.ChangeExtension( x.Asset.Path, System.IO.Path.GetExtension( x.Asset.AbsolutePath ) ) );
			var sprite = Sprite.FromTextures( paths.Select( x => Texture.Load( x ) ).ToArray() );
			var json = sprite.Serialize().ToJsonString();
			System.IO.File.WriteAllText( fd.SelectedFile, json );

			asset = AssetSystem.RegisterFile( fd.SelectedFile );
			while ( !asset.IsCompiledAndUpToDate )
			{
				await Task.Delay( 10 );
			}
		}
		else
		{
			// Export individual sprites
			foreach ( var p in entries )
			{
				var path = System.IO.Path.ChangeExtension( p.Asset.Path, System.IO.Path.GetExtension( p.Asset.AbsolutePath ) );
				var sprite = Sprite.FromTexture( Texture.Load( path ) );
				var json = sprite.Serialize().ToJsonString();
				var outPath = System.IO.Path.Combine( System.IO.Path.GetDirectoryName( fd.SelectedFile ), System.IO.Path.GetFileNameWithoutExtension( path ) + ".sprite" );
				System.IO.File.WriteAllText( outPath, json );

				asset = AssetSystem.RegisterFile( outPath );
				while ( !asset.IsCompiledAndUpToDate )
				{
					await Task.Delay( 10 );
				}
			}
		}

		MainAssetBrowser.Instance?.Local.UpdateAssetList();
		MainAssetBrowser.Instance?.Local.FocusOnAsset( asset );
		EditorUtility.InspectorObject = asset;
	}
}
