using SkiaSharp;
using System.IO;
using System.Runtime.InteropServices;

namespace Editor.TerrainEditor;

class ImportHeightmapPopup : Widget
{
	class ImportSettings
	{
		public enum BitDepthEnum { Bit8, Bit16 }
		public enum ByteOrderEnum { Windows, Mac }

		public string FileName { get; set; }
		public int Resolution { get; set; }
		public BitDepthEnum BitDepth { get; set; }
		public ByteOrderEnum ByteOrder { get; set; }
	}

	ImportSettings Settings { get; set; }
	ScrollArea scrollArea;
	Terrain terrain;

	public ImportHeightmapPopup( Widget parent, Terrain terrain, string filename ) : base( parent )
	{
		WindowFlags = WindowFlags.Dialog | WindowFlags.Customized | WindowFlags.WindowTitle | WindowFlags.CloseButton | WindowFlags.WindowSystemMenuHint;
		DeleteOnClose = true;
		WindowTitle = $"Import Heightmap";
		SetWindowIcon( "file_download" );

		this.terrain = terrain;

		scrollArea = new ScrollArea( this );
		scrollArea.Canvas = new Widget( this );

		Layout = Layout.Column();
		Layout.Spacing = 8;
		Layout.Margin = 16;

		var warning = new WarningBox( "Heightmaps must use a single channel and be 8 or 16 bit.\nIf resolution is not power of two (e.g 512x512) the image will be resampled." );
		Layout.Add( warning );

		Layout.Add( scrollArea );

		Settings = new()
		{
			FileName = filename
		};

		PickDefaults( filename );

		var so = EditorUtility.GetSerializedObject( Settings );

		var bottomToolbar = new BottomToolbar();
		bottomToolbar.Done.Pressed = Import;
		Layout.Add( bottomToolbar );

		Visible = false;
		Width = 400;
		MinimumWidth = 350;
		Height = 1720;
		MaximumHeight = 720;

		scrollArea.Canvas.VerticalSizeMode = SizeMode.CanGrow;
		scrollArea.Canvas.Layout = Layout.Column();
		scrollArea.Canvas.Layout.AddSpacingCell( 16 );
		scrollArea.Canvas.Layout.Add( ControlSheet.Create( so ) );
		scrollArea.Canvas.Layout.AddSpacingCell( 32 );
		scrollArea.Canvas.Layout.AddStretchCell();

		AdjustSize();
		Position = Application.CursorPosition - new Vector2( Width * 0.5f, 3 );

		ConstrainToScreen();

		Show();
		Focus();
	}

	/// <summary>
	/// Pick default resolution / depth from the file size
	/// </summary>
	public void PickDefaults( string path )
	{
		var file = File.Open( path, FileMode.Open, FileAccess.Read );
		var fileSize = file.Length;
		file.Close();

		// We can take a good guess if it's 16 or 8 bit depending on squareness
		int resolution = MathX.CeilToInt( (float)Math.Sqrt( fileSize / 2 ) );
		if ( resolution * resolution * 2 == fileSize )
		{
			Settings.Resolution = resolution;
			Settings.BitDepth = ImportSettings.BitDepthEnum.Bit16;
			return;
		}

		resolution = MathX.CeilToInt( (float)Math.Sqrt( fileSize ) );
		if ( resolution * resolution == fileSize )
		{
			Settings.Resolution = resolution;
			Settings.BitDepth = ImportSettings.BitDepthEnum.Bit8;
			return;
		}
	}

	void Import()
	{
		// Round the final resolution to the nearest power of two
		var realResolution = RoundDownToPowerOfTwo( Settings.Resolution );

		var file = File.ReadAllBytes( Settings.FileName );

		ushort[] heightmap = new ushort[Settings.Resolution * Settings.Resolution];

		for ( int y = 0; y < Settings.Resolution; y++ )
		{
			for ( int x = 0; x < Settings.Resolution; x++ )
			{
				int index = x + y * Settings.Resolution;

				if ( Settings.ByteOrder == ImportSettings.ByteOrderEnum.Mac )
				{
					byte temp;
					temp = file[index * 2];
					file[index * 2] = file[index * 2 + 1];
					file[index * 2 + 1] = temp;
				}

				ushort height = Settings.BitDepth == ImportSettings.BitDepthEnum.Bit16 ? BitConverter.ToUInt16( file, index * 2 ) : file[index];
				heightmap[index] = height;
			}
		}

		if ( realResolution != Settings.Resolution )
		{
			heightmap = ResampleHeightmap( heightmap, Settings.Resolution, realResolution ).ToArray();
		}

		terrain.Storage.SetResolution( realResolution );
		terrain.Storage.HeightMap = heightmap;

		// Recreate GPU textures, mesh, and collider with the new heightmap data
		terrain.Create();

		Close();
	}

	static Span<ushort> ResampleHeightmap( Span<ushort> original, int originalSize, int newSize )
	{
		// Create SKBitmap with the original data copied in
		var bitmap = new SKBitmap( originalSize, originalSize, SKColorType.Alpha16, SKAlphaType.Opaque );
		using ( var pixmap = bitmap.PeekPixels() )
		{
			var dataBytes = MemoryMarshal.AsBytes( original );
			Marshal.Copy( dataBytes.ToArray(), 0, pixmap.GetPixels(), dataBytes.Length );
		}

		// Create new resized bitmap
		var newBitmap = bitmap.Resize( new SKSizeI( newSize, newSize ), SKSamplingOptions.Default );

		// Output pixels
		using ( var pixmap = newBitmap.PeekPixels() )
		{
			return pixmap.GetPixelSpan<ushort>();
		}
	}

	static int RoundDownToPowerOfTwo( int value )
	{
		value = value | (value >> 1);
		value = value | (value >> 2);
		value = value | (value >> 4);
		value = value | (value >> 8);
		value = value | (value >> 16);
		return value - (value >> 1);
	}
}


file class BottomToolbar : Widget
{
	public Button Done { get; }

	public BottomToolbar()
	{
		Done = new Button.Primary( "Import", "file_download", this );

		Layout = Layout.Row();
		Layout.Margin = 16;
		Layout.AddStretchCell();
		Layout.Add( Done );
	}

	protected override void OnPaint()
	{
		Paint.Pen = Theme.ControlBackground.WithAlpha( 0.5f );
		Paint.PenSize = 2;

		Paint.DrawLine( LocalRect.TopLeft, LocalRect.TopRight );
	}

}
