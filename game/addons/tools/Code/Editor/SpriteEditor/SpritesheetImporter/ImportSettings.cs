using System.Text.Json.Serialization;

namespace Editor.SpriteEditor;

public class SpritesheetImportSettings
{
	[JsonIgnore, Hide]
	public Action OnFrameCountChanged;

	[Header( "Frame Count" )]
	[Property, Range( 1, 128, true, false ), Step( 1 )]
	public int HorizontalFrames
	{
		get => _horizontalFrames;
		set { if ( _horizontalFrames == value ) return; _horizontalFrames = value; OnFrameCountChanged?.Invoke(); }
	}
	[Hide] private int _horizontalFrames = 8;

	[Property, Range( 1, 128, true, false ), Step( 1 )]
	public int VerticalFrames
	{
		get => _verticalFrames;
		set { if ( _verticalFrames == value ) return; _verticalFrames = value; OnFrameCountChanged?.Invoke(); }
	}
	[Hide] private int _verticalFrames = 8;

	[Header( "Padding" )]
	[Property, Title( "Left" )]
	public int PaddingLeft { get; set; } = 0;

	[Property, Title( "Top" )]
	public int PaddingTop { get; set; } = 0;

	[Property, Title( "Right" )]
	public int PaddingRight { get; set; } = 0;

	[Property, Title( "Bottom" )]
	public int PaddingBottom { get; set; } = 0;

	[Header( "Separation" )]
	[Property, Range( 0, 99999, true, false ), Step( 1 )]
	public int HorizontalSeparation { get; set; } = 0;

	[Property, Range( 0, 99999, true, false ), Step( 1 )]
	public int VerticalSeparation { get; set; } = 0;

	/// <summary>
	/// Calculates the list of each frame's pixel rects given the size of the source image
	/// </summary>
	public List<Rect> GetFrames( int textureWidth, int textureHeight )
	{
		// Available space after stripping padding from all four sides
		int availW = textureWidth - PaddingLeft - PaddingRight;
		int availH = textureHeight - PaddingTop - PaddingBottom;
		int fw = Math.Max( 1, (availW - (HorizontalFrames - 1) * HorizontalSeparation) / HorizontalFrames );
		int fh = Math.Max( 1, (availH - (VerticalFrames - 1) * VerticalSeparation) / VerticalFrames );

		var frames = new List<Rect>( HorizontalFrames * VerticalFrames );

		for ( int row = 0; row < VerticalFrames; row++ )
		{
			for ( int col = 0; col < HorizontalFrames; col++ )
			{
				var x = PaddingLeft + col * (fw + HorizontalSeparation);
				var y = PaddingTop + row * (fh + VerticalSeparation);
				frames.Add( new Rect( x, y, fw, fh ) );
			}
		}

		return frames;
	}
}
