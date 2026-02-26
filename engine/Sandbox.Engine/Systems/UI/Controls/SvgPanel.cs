using Sandbox.Rendering;

namespace Sandbox.UI;

/// <summary>
/// A generic panel that draws an SVG scaled to size
/// </summary>
[Library( "svg" ), Expose]
public partial class SvgPanel : Panel
{

	/// <summary>
	/// Content path to the SVG file
	/// </summary>
	public string Src
	{
		get => _src;

		set
		{
			if ( _src == value )
				return;

			_src = value;
			ReloadTexture();
		}
	}
	internal string _src;

	/// <summary>
	/// Optional color to draw the SVG with
	/// </summary>
	public string Color
	{
		get => _color;

		set
		{
			if ( _color == value )
				return;

			_color = value;
			ReloadTexture();
		}
	}
	internal string _color;

	public override bool HasContent => texture != null;

	Texture texture;
	int sizeHash;

	public override void FinalLayout( Vector2 offset )
	{
		base.FinalLayout( offset );

		if ( !IsVisible ) return;

		var hash = HashCode.Combine( Box.Rect.Width, Box.Rect.Height, ComputedStyle.BackgroundSizeX, ComputedStyle.BackgroundSizeY );
		if ( hash == sizeHash ) return;

		sizeHash = hash;
		ReloadTexture();
	}

	private async void ReloadTexture()
	{
		if ( ComputedStyle is null )
			return;

		var rect = Box.Rect;
		var width = rect.Width;
		var height = rect.Height;

		if ( ComputedStyle.BackgroundSizeX.HasValue && ComputedStyle.BackgroundSizeX != Length.Undefined )
			width = ComputedStyle.BackgroundSizeX.Value.GetPixels( width );

		if ( ComputedStyle.BackgroundSizeY.HasValue && ComputedStyle.BackgroundSizeY != Length.Undefined )
			height = ComputedStyle.BackgroundSizeY.Value.GetPixels( height );

		var url = $"{Src}?w={(int)width}&h={(int)height}";

		if ( !string.IsNullOrEmpty( Color ) )
		{
			url += $"&color={Color}";
		}

		texture = await Texture.LoadAsync( url );
	}

	internal override void DrawContent( CommandList commandList, PanelRenderer renderer, ref RenderState state )
	{
		if ( texture == null )
			return;

		if ( renderer is PanelRenderer pr )
		{
			pr.BuildCommandList_BackgroundTexture( this, texture, state, Length.Cover );
		}
	}
}
