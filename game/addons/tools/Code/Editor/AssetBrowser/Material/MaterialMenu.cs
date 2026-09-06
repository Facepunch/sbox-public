using Sandbox.Resources;

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
			var file = Inspectors.TextureInspector.TextureFile.CreateDefault( paths );
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
				var file = Inspectors.TextureInspector.TextureFile.CreateDefault( [path], noCompress );
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
		var asset = entries.First().Asset;
		var assetName = asset.Name;

		Log.Info( assetName );

		// Derive the shared base name by stripping the trailing _suffix (e.g. ak47_roughness -> ak47)
		var lastUnderscore = assetName.LastIndexOf( '_' );
		var baseName = lastUnderscore > 0 ? assetName.Substring( 0, lastUnderscore ) : assetName;

		var fd = new FileDialog( null );
		fd.Title = "Create Material from Image Files..";
		fd.Directory = System.IO.Path.GetDirectoryName( asset.AbsolutePath );
		fd.DefaultSuffix = ".vmat";
		fd.SelectFile( $"{baseName}.vmat" );
		fd.SetFindFile();
		fd.SetModeSave();
		fd.SetNameFilter( "Material File (*.vmat)" );

		if ( !fd.Execute() )
			return;

		// Find all the image files in the same folder as the first asset we selected
		var assetPath = System.IO.Path.GetDirectoryName( asset.AbsolutePath ).NormalizeFilename( false );
		var assetPeers = AssetSystem.All.Where( x => x.AssetType == AssetType.ImageFile ).Where( x => x.AbsolutePath.StartsWith( assetPath ) ).ToArray();
		var assetPeersWithSameBaseName = assetPeers.Where( x => x.Name == baseName || x.Name.StartsWith( baseName + "_" ) ).ToArray();
		if ( assetPeersWithSameBaseName.Length > 0 )
		{
			assetPeers = assetPeersWithSameBaseName;
		}

		//
		// Try to work out what textures should go where using hacks and magic
		//
		string[] texColorExtensions = ["color", "diff", "albedo", "basecolor"];
		string[] texNormalExtensions = ["nrm", "normal", "amb"];
		string[] texAoExtensions = ["ao", "occ", "amb", "ambientocclusion"];
		string[] texRoughExtensions = ["rough", "roughness"];
		string[] texMetallicExtensions = ["metallic", "metal", "metalness"];
		string[] texSelfIllumExtensions = ["selfillum"];
		string[] texMaskExtensions = ["mask"];

		// 1. Color Texture
		string texColor = assetPeers
			.FirstOrDefault( peer => texColorExtensions.Any( keyword => peer.Name.Contains( $"_{keyword}" ) ) )
			?.RelativePath;

		texColor ??= asset.RelativePath; // Failing that, lets use whatever we have selected

		// 2. Normal Texture 
		string texNormal = assetPeers
			.FirstOrDefault( peer => texNormalExtensions.Any( keyword => peer.Name.Contains( $"_{keyword}" ) ) )
			?.RelativePath ?? "materials/default/default_normal.tga";

		// 3. AO Texture 
		string texAo = assetPeers
			.FirstOrDefault( peer => texAoExtensions.Any( keyword => peer.Name.Contains( $"_{keyword}" ) ) )
			?.RelativePath ?? "materials/default/default_ao.tga";

		// 4. Roughness Texture 
		string texRough = assetPeers
			.FirstOrDefault( peer => texRoughExtensions.Any( keyword => peer.Name.Contains( $"_{keyword}" ) ) )
			?.RelativePath ?? "materials/default/default_rough.tga";

		// 5. Metallic Texture 
		string texMetallic = assetPeers
			.FirstOrDefault( peer => texMetallicExtensions.Any( keyword => peer.Name.Contains( $"_{keyword}" ) ) )
			?.RelativePath;

		if ( texMetallic != null )
		{
			texMetallic = $"\n	F_METALNESS_TEXTURE 1\n	F_SPECULAR 1\n	TextureMetalness \"{texMetallic}\"";
		}

		// 6. Self Illum Texture 
		string texSelfIllum = assetPeers
			.FirstOrDefault( peer => texSelfIllumExtensions.Any( keyword => peer.Name.Contains( $"_{keyword}" ) ) )
			?.RelativePath;

		if ( texSelfIllum != null )
		{
			texSelfIllum = $"\n	F_SELF_ILLUM 1\n	TextureSelfIllumMask \"{texSelfIllum}\"";
		}

		// 7. Tint Mask Texture 
		string tintMask = assetPeers
			.FirstOrDefault( peer => texMaskExtensions.Any( keyword => peer.Name.Contains( $"_{keyword}" ) ) )
			?.RelativePath;

		if ( tintMask != null )
		{
			tintMask = $"\n	F_TINT_MASK 1\n	TextureTintMask \"{tintMask}\"";
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
			var textures = entries.Select( x => TextureFromImageFile( x ) ).Where( x => x is not null ).ToArray();
			var sprite = Sprite.FromTextures( textures );
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
				var texture = TextureFromImageFile( p );
				if ( texture is null ) continue;

				var sprite = Sprite.FromTexture( texture );
				var json = sprite.Serialize().ToJsonString();
				var outPath = System.IO.Path.Combine( System.IO.Path.GetDirectoryName( fd.SelectedFile ), System.IO.Path.GetFileNameWithoutExtension( p.Asset.AbsolutePath ) + ".sprite" );
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

	private static Texture TextureFromImageFile( AssetEntry entry )
	{
		var generator = new ImageFileGenerator
		{
			FilePath = entry.Asset.RelativePath
		};

		return generator.Create( ResourceGenerator.Options.Default );
	}
}
