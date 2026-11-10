using Sandbox.Rendering;

namespace Sandbox;

public readonly ref partial struct Painter
{
	/// <summary>
	/// Draws a sprite instance's current frame stretched to the destination rectangle.
	/// The animation origin is ignored. Tint defaults to white; drawing opacity, transform,
	/// clipping and blend mode apply. Does not advance playback. Missing frames draw nothing.
	/// </summary>
	public void Sprite( SpriteInstance sprite, Rect rect, Color? tint = null, FilterMode filter = FilterMode.Bilinear )
	{
		GetActiveContext();
		if ( !Enum.IsDefined( filter ) ) throw new ArgumentOutOfRangeException( nameof( filter ) );
		if ( sprite?.Texture is not { } texture ) return;
		Texture( texture, rect, tint ?? Color.White, filter );
	}
}
