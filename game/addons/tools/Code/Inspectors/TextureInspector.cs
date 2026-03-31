using static Editor.Inspectors.AssetInspector;

namespace Editor.Inspectors;

[CanEdit( "asset:vtex" )]
public class TextureInspector : Widget, IAssetInspector
{
	public class TextureFile
	{
		public class TextureSequence
		{
			[Title( "Images" ), Group( "Input" ), ImageAssetPath, KeyProperty]
			public string Source { get; set; }

			[ToggleGroup( "Sequence" )]
			public bool IsLooping { get; set; }

			[ToggleGroup( "FlipBook" )]
			public bool FlipBook { get; set; }

			[ToggleGroup( "FlipBook" )]
			public int Columns { get; set; }

			[ToggleGroup( "FlipBook" )]
			public int Rows { get; set; }

			[ToggleGroup( "FlipBook" )]
			public int Frames { get; set; } = 64;
		}

		public enum GammaType
		{
			Linear,
			SRGB,
		}

		public enum ImageFormatType
		{
			DXT5,
			DXT3,
			DXT1,
			RGBA8888,
			BC7,
			BC6H,
			RGBA16161616,
			RGBA16161616F,
			RGBA32323232F,
			R32F,
		}

		public enum MipAlgorithm
		{
			None,
			Box,
			// Everything else is kind of bullshit
		}

		[Hide]
		public List<string> Images { get; set; }

		[Header( "Input" )]
		public List<TextureSequence> Sequences { get; set; } = [];

		[Title( "Color Space" )]
		public GammaType InputColorSpace { get; set; } = GammaType.Linear;

		[Header( "Output" )]
		[Title( "Image Format" )]
		public ImageFormatType OutputFormat { get; set; } = ImageFormatType.DXT5;

		[Title( "Color Space" )]
		public GammaType OutputColorSpace { get; set; } = GammaType.Linear;

		[Title( "Mip Algorithm" )]
		public MipAlgorithm OutputMipAlgorithm { get; set; } = MipAlgorithm.None;

		[Hide]
		public string OutputTypeString { get; set; } = "2D";

		public static TextureFile CreateDefault( IEnumerable<string> images, bool noCompress = false )
		{
			return new TextureFile
			{
				Sequences = [.. images.Select( x => new TextureSequence()
				{
					Source = x,
					IsLooping = true
				} )],

				OutputFormat = noCompress ? ImageFormatType.RGBA8888 : ImageFormatType.DXT5,
				OutputColorSpace = GammaType.Linear,
				OutputMipAlgorithm = MipAlgorithm.None,
				InputColorSpace = GammaType.Linear,
				OutputTypeString = "2D"
			};
		}
	}

	private Asset Asset;
	private TextureFile File;
	private string FileData;

	public TextureInspector( Widget parent ) : base( parent )
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 4;
	}

	public void SetAsset( Asset asset )
	{
		if ( asset is null )
			return;
		Asset = asset;
		var json = System.IO.File.ReadAllText( Asset.AbsolutePath );
		if ( string.IsNullOrWhiteSpace( json ) )
			return;

		try
		{
			File = Json.Deserialize<TextureFile>( json );
			Asset.HasUnsavedChanges = false;

			if ( File.Images is not null )
			{
				foreach ( var image in File.Images )
				{
					File.Sequences.Add( new TextureFile.TextureSequence
					{
						Source = image,
						IsLooping = true
					} );
				}
				File.Images = null;
			}
		}
		catch
		{
			File = new();
			Asset.HasUnsavedChanges = true;
		}

		FileData = json;

		var so = File.GetSerialized();
		Layout.Add( ControlSheet.Create( so ) );
		so.OnPropertyChanged += ( _ ) => OnDirty();
	}

	public void SetInspector( AssetInspector inspector )
	{
		inspector.BindSaveToUnsavedChanges();
		inspector.OnSave += Save;
		inspector.OnReset += Restore;
	}

	private void OnDirty()
	{
		if ( Asset is null )
			return;
		if ( File is null )
			return;
		var json = Json.Serialize( File );
		if ( string.IsNullOrEmpty( json ) )
			return;
		System.IO.File.WriteAllText( Asset.AbsolutePath, json );
		if ( json == FileData )
			return;
		Asset.HasUnsavedChanges = true;
	}

	private void Save()
	{
		if ( Asset is null )
			return;
		if ( File is null )
			return;
		var json = Json.Serialize( File );
		if ( string.IsNullOrEmpty( json ) )
			return;
		System.IO.File.WriteAllText( Asset.AbsolutePath, json );
		FileData = json;
		Asset.HasUnsavedChanges = false;
	}

	private void Restore()
	{
		if ( string.IsNullOrWhiteSpace( FileData ) )
			return;
		System.IO.File.WriteAllText( Asset.AbsolutePath, FileData );
		Asset.HasUnsavedChanges = false;
	}
}
