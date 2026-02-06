using Sandbox.UI;
using System.Text.Json.Serialization;
using System.Threading;

namespace Sandbox.Resources;

[Title( "Text" )]
[Icon( "format_size" )]
[ClassName( "text" )]
public class TextTextureGenerator : TextureGenerator
{
	public Vector2Int Size { get; set; } = 128;

	public Margin Margin { get; set; } = 0;

	public TextFlag TextFlags { get; set; } = TextFlag.Center | TextFlag.DontClip;

	[InlineEditor( Label = false )]
	public TextRendering.Scope TextScope { get; set; } = new( "🤕", Color.White, 64 );

	[Hide, JsonIgnore]
	public override bool CacheToDisk => true;

	protected override ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
	{
		var w = Size.x.Clamp( 1, 1024 * 4 );
		var h = Size.y.Clamp( 1, 1024 * 4 );

		var rect = new Rect( 0, 0, w, h );
		rect = rect.Shrink( Margin );

		using var bitmap = new Bitmap( w, h, false );
		bitmap.Clear( Color.Transparent );
		bitmap.DrawText( TextScope, rect, TextFlags );

		return ValueTask.FromResult( bitmap.ToTexture() );
	}

	public override string ToString()
	{
		return $"{TextScope.Text} ({TextScope.FontName}, {TextScope.FontSize})";
	}
}
