using Sandbox;
using System.IO;

namespace Editor.TerrainEditor;

partial class TerrainComponentWidget : ComponentEditorWidget
{
	TerrainMaterialList MaterialList;

	bool FilterProperties( SerializedProperty o )
	{
		if ( o.PropertyType.IsAssignableTo( typeof( Delegate ) ) ) return false;
		if ( !o.HasAttribute<PropertyAttribute>() ) return false;

		// Stupid stuff, we shouldn't be forced to inherit this on Terrain @layla
		// But just hide it since it's useless and confusing right now
		if ( o.Name == nameof( Collider.Static ) ) return false;
		if ( o.Name == nameof( Collider.Surface ) ) return false;
		if ( o.Name == nameof( Collider.IsTrigger ) ) return false;

		return true;
	}

	Widget PaintPage()
	{
		var container = new Widget( null );

		container.Layout = Layout.Column();

		{
			var hmilayout = container.Layout.AddColumn();
			hmilayout.Spacing = 8;
			hmilayout.Margin = 16;

			var header = new Label( "Heightmap" );
			header.SetStyles( "font-weight: bold" );
			hmilayout.Add( header );

			var layout = hmilayout.AddRow();
			layout.Spacing = 8;
			layout.AddStretchCell();
			layout.Add( new Button( "Import Splatmap..." ) { Clicked = ImportSplatmap } );
			layout.Add( new Button( "Import..." ) { Clicked = ImportHeightmap } );
			layout.Add( new Button( "Export..." ) { Enabled = false } );
		}

		{
			var header = new Label( "Terrain Materials" );
			header.SetStyles( "font-weight: bold" );

			var terrain = SerializedObject.Targets.FirstOrDefault() as Terrain;

			MaterialList = new TerrainMaterialList( null, terrain );
			MaterialList.HorizontalSizeMode = SizeMode.CanGrow;
			var tlayout = container.Layout.AddColumn();
			tlayout.Spacing = 8;
			tlayout.Margin = 16;
			tlayout.Add( header );
			tlayout.Add( MaterialList );

			var hlayout = tlayout.AddRow();
			hlayout.Spacing = 8;
			hlayout.AddStretchCell();

			var newTerrainMat = new Button( "New Terrain Material..." );
			newTerrainMat.Clicked += NewTerrainMaterial;

			var cloudMats = new Button( "Browse...", "cloud" );
			cloudMats.Clicked += () =>
			{
				var picker = AssetPicker.Create( null, AssetType.FromExtension( "tmat" ) );
				picker.OnAssetPicked = x =>
				{
					var material = x.First().LoadResource<TerrainMaterial>();
					terrain.Storage.Materials.Add( material );
					terrain.UpdateMaterialsBuffer();
					MaterialList?.BuildItems();
				};
				picker.Show();
			};

			hlayout.Add( cloudMats );
			hlayout.Add( newTerrainMat );

			var cs = new ControlSheet();
			cs.AddObject( terrain.Storage.MaterialSettings.GetSerialized() );
			tlayout.Add( cs );
		}

		return container;
	}

	void NewTerrainMaterial()
	{
		var filepath = EditorUtility.SaveFileDialog( "Create Terrain Material", "tmat", $"{Project.Current.GetAssetsPath()}/" );
		if ( filepath is null ) return;

		var asset = AssetSystem.CreateResource( "tmat", filepath );

		if ( !asset.TryLoadResource<TerrainMaterial>( out var material ) )
			return;

		asset.Compile( true );
		MainAssetBrowser.Instance?.Local.UpdateAssetList();

		var terrain = SerializedObject.Targets.FirstOrDefault() as Terrain;
		terrain.Storage.Materials.Add( material );
		terrain.UpdateMaterialsBuffer();
		MaterialList?.BuildItems();

		asset.OpenInEditor();
	}

	Widget ActualSettingsPage()
	{
		var container = new Widget( null );

		var sheet = new ControlSheet();
		sheet.AddObject( SerializedObject, FilterProperties );

		container.Layout = Layout.Column();
		container.Layout.Add( sheet );

		return container;
	}

	Widget SettingsPage()
	{
		var container = new Widget( null );

		var tabs = new TabWidget( this );
		tabs.AddPage( "Edit Terrain", "add_circle", PaintPage() );
		tabs.AddPage( "Settings", "settings", ActualSettingsPage() );

		container.Layout = Layout.Column();
		container.Layout.Add( tabs );

		return container;
	}

	public static Color32 BalanceWeights( Color32 color )
	{
		var sum = color.r + color.g + color.b;
		var a = (byte)Math.Max( 0, 255 - sum );
		return new Color32( color.r, color.g, color.b, a );
	}

	void ImportSplatmap()
	{
		if ( SerializedObject.Targets.FirstOrDefault() is not Terrain terrain )
			return;


		var fd = new FileDialog( null ) { Title = "Import Splatmap(s) from image file(s)..." };
		fd.SetFindExistingFiles();
		fd.SetModeOpen();
		fd.SetNameFilter( "Image File (*.png *.tga *.jpg *.psd)" );

		if ( !fd.Execute() )
			return;

		var files = fd.SelectedFiles;
		if ( files.Count == 0 )
			return;

		var resolution = terrain.Storage.Resolution;
		var numPixels = resolution * resolution;

		// Accumulate all weights from all splatmaps
		var allWeights = new List<(int materialIndex, float weight)>[numPixels];
		for ( int i = 0; i < numPixels; i++ )
		{
			allWeights[i] = [];
		}

		int materialIndexOffset = 0;

		// Load all splatmaps and accumulate weights
		foreach ( var file in files )
		{
			using ( var bitmap = EditorUtility.LoadBitmap( file ) )
			{
				if ( bitmap.Width != resolution || bitmap.Height != resolution )
				{
					Log.Warning( $"Skipping {Path.GetFileName( file )}:  does not match terrain resolution " );
					continue;
				}

				var data = bitmap.EncodeTo( ImageFormat.RGBA8888 );
				for ( int i = 0; i < numPixels; i++ )
				{
					int byteOffset = i * 4;
					float r = data[byteOffset + 0] / 255f;
					float g = data[byteOffset + 1] / 255f;
					float b = data[byteOffset + 2] / 255f;
					float a = data[byteOffset + 3] / 255f;

					allWeights[i].Add( (materialIndexOffset + 0, r) );
					allWeights[i].Add( (materialIndexOffset + 1, g) );
					allWeights[i].Add( (materialIndexOffset + 2, b) );
					allWeights[i].Add( (materialIndexOffset + 3, a) );
				}
			}

			materialIndexOffset += 4;
		}

		// find top 2 contributors across all splatmaps, we dont care about the rest
		for ( int i = 0; i < numPixels; i++ )
		{
			var weights = allWeights[i];

			// Find top 2
			weights.Sort( ( a, b ) => b.weight.CompareTo( a.weight ) );

			// Get top 2 materials
			int firstIdx = weights[0].materialIndex;
			float firstWeight = weights[0].weight;

			int secondIdx = weights.Count > 1 ? weights[1].materialIndex : firstIdx;
			float secondWeight = weights.Count > 1 ? weights[1].weight : 0;

			// Calculate blend factor
			byte blendFactor = 0;
			float totalWeight = firstWeight + secondWeight;
			if ( totalWeight > 0.0001f )
			{
				float normalizedSecond = secondWeight / totalWeight;
				blendFactor = (byte)Math.Clamp( (int)(normalizedSecond * 255f), 0, 255 );
			}

			// Clamp material indices to valid range
			byte baseMat = (byte)Math.Clamp( firstIdx, 0, Math.Min( 31, terrain.Storage.Materials.Count - 1 ) );
			byte overlayMat = (byte)Math.Clamp( secondIdx, 0, Math.Min( 31, terrain.Storage.Materials.Count - 1 ) );

			var compactMaterial = new CompactTerrainMaterial( baseMat, overlayMat, blendFactor, false );
			terrain.Storage.ControlMap[i] = compactMaterial.Packed;
		}

		terrain.SyncGPUTexture();
	}

	public static (byte r, byte g, byte b, byte a) NormalizeRGBA( byte r, byte g, byte b, byte a )
	{
		// Calculate the total sum of the RGBA values
		int total = r + g + b + a;

		// Calculate the normalization factor
		double factor = 255.0 / total;

		// Normalize each component by multiplying with the factor and rounding to the nearest integer
		byte rNormalized = (byte)Math.Round( r * factor );
		byte gNormalized = (byte)Math.Round( g * factor );
		byte bNormalized = (byte)Math.Round( b * factor );
		byte aNormalized = (byte)Math.Round( a * factor );

		return (rNormalized, gNormalized, bNormalized, aNormalized);
	}

	void ImportHeightmap()
	{
		var terrain = SerializedObject.Targets.FirstOrDefault() as Terrain;
		if ( !terrain.IsValid() ) return;

		var fd = new FileDialog( null );
		fd.Title = "Import Heightmap from image file...";
		// fd.Directory = System.IO.Path.GetDirectoryName( assets.First().AbsolutePath );
		fd.SetFindFile();
		fd.SetModeOpen();
		fd.SetNameFilter( "Image File (*.raw *.r8 *.r16)" );

		if ( !fd.Execute() )
			return;

		new ImportHeightmapPopup( this, terrain, fd.SelectedFile );
	}
}
