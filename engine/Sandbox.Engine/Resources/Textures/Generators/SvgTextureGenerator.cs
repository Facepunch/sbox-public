using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Threading;

namespace Sandbox.Resources;

[Title( "Svg Source" )]
[Icon( "format_shapes" )]
[ClassName( "svgsource" )]
[Expose]
public class SvgSourceGenerator : TextureGenerator
{
	public Vector2Int Size { get; set; } = 256;

	[TextArea]
	public string Source { get; set; } = """
		
		<?xml version="1.0" encoding="utf-8"?>

		<!-- Uploaded to: SVG Repo, www.svgrepo.com, Generator: SVG Repo Mixer Tools -->
		<svg width="800px" height="800px" viewBox="0 0 64 64" xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" aria-hidden="true" role="img" class="iconify iconify--emojione" preserveAspectRatio="xMidYMid meet">

		<path d="M15.8 52.1C9 47.7 6.3 30.6 8.5 22.9c1.6-5.8 7.8-14.3 13.4-16.5c4.7-1.9 15.5-1.9 20.1 0c5.6 2.2 11.8 10.7 13.4 16.5c2.2 7.8.5 24.8-6.2 29.2c-14.2 9.2-19.2 9.2-33.4 0" fill="#f5d1ac">

		</path>

		<path d="M5.1 24.7c3.6 7.9 4.5 8.2 7.9-1.2c1.8-5 .5-8 2.7-11.2c1.2-1.8 3.9-4.8 3.9-4.8S-1.7 9.7 5.1 24.7" fill="#423223">

		</path>

		<path d="M14.2 7.2c-5.4 3.5-16.9 2.1-10.1 17c3.6 7.9 4.5 8.2 7.9-1.2c1.8-5 .5-8 2.7-11.2c1.2-1.8 4.9-4.3 4.9-4.3s-1.7-2.7-5.4-.3" fill="#947151">

		</path>

		<path d="M58.9 24.6c-3.6 7.9-4.5 8.2-7.9-1.2c-1.8-5-.5-8-2.7-11.2c-1.2-1.8-3.9-4.8-3.9-4.8s21.3 2.3 14.5 17.2" fill="#423223">

		</path>

		<path d="M49.8 7.2c5.4 3.5 16.9 2.1 10.1 17c-3.6 7.9-4.5 8.2-7.9-1.2c-1.8-5-.5-8-2.7-11.2c-1.2-1.8-4.9-4.3-4.9-4.3s1.7-2.7 5.4-.3" fill="#947151">

		</path>

		<circle cx="17.7" cy="30.7" fill="#ffffff" r="6">

		</circle>

		<circle cx="16.2" cy="30.7" fill="#3e4347" r="4.5">

		</circle>

		<circle cx="46.3" cy="30.7" fill="#ffffff" r="6">

		</circle>

		<circle cx="47.8" cy="30.7" fill="#3e4347" r="4.5">

		</circle>

		<path d="M21.7 48.8l4.6 4.9c2.8 2.9 8.5 2.9 11.3 0l4.7-4.9l-4.8-5h-11l-4.8 5" fill="#7d644b">

		</path>

		<path d="M32 39.6s-4.9 7-4.3 10.3c.8 4.8 7.7 4.8 8.6 0c.6-3.3-4.3-10.3-4.3-10.3" fill="#f15a61">

		</path>

		<path d="M32 51.7l1.1-6.7h-2.2l1.1 6.7" fill="#ba454b">

		</path>

		<path fill="#423223" d="M27 41.5h10v4.6H27z">

		</path>

		<path d="M47.8 42.6l-7.1-7.5c-4.3-4.5-13.1-4.5-17.4 0l-7.1 7.5c-2 2.1-2 5.6 0 7.7c2 2.1 5.3 2.1 7.3 0l7.1-7.5c.7-.7 2-.7 2.7 0l7.1 7.5c2 2.1 5.3 2.1 7.3 0c2.2-2.1 2.2-5.6.1-7.7" fill="#947151">

		</path>

		<g fill="#3e4347">

		<path d="M26.1 35.7c0-2.6 2.6-3.1 5.9-3.1c3.3 0 5.9.5 5.9 3.1c0 2.1-4.7 3.9-5.9 3.9c-1.2 0-5.9-1.9-5.9-3.9"></path>
		<path d="M23.31 39.012l.989-.992l.991.989l-.989.991z"></path>
		<path d="M20.947 41.811l.989-.991l.99.989l-.988.99z"></path>
		<path d="M24.125 42.763l.989-.991l.991.988l-.988.992z"></path>
		<path d="M38.703 38.988l.992-.988l.988.991l-.991.989z"></path>
		<path d="M41.128 41.762l.992-.989l.988.991l-.991.989z"></path>
		<path d="M37.947 42.811l.991-.988l.989.99l-.991.99z"></path>
		</g>
		</svg>
		""";

	[ToggleGroup( "Colorize" )]
	public bool Colorize { get; set; }

	[Group( "Colorize" )]
	public Color TargetColor { get; set; } = Color.White;


	[Group( "Adjustments" )][Range( 0, 2 )] public float Scale { get; set; } = 1.0f;
	[Group( "Adjustments" )] public Vector2 Offset { get; set; } = 0.0f;
	[Group( "Adjustments" )][Range( 0, 360 )] public float Rotate { get; set; } = 0.0f;
	[Group( "Adjustments" )] public bool FlipVertical { get; set; } = false;
	[Group( "Adjustments" )] public bool FlipHorizontal { get; set; } = false;

	[Hide, JsonIgnore]
	public override bool CacheToDisk => true;

	protected override ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
	{
		var w = Size.x.Clamp( 1, 1024 * 4 );
		var h = Size.y.Clamp( 1, 1024 * 4 );

		using var bitmap = Bitmap.CreateFromSvgString( Source, w, h, Scale, Offset, Rotate );
		if ( bitmap is null ) return default;

		if ( Colorize )
		{
			bitmap.Colorize( TargetColor );
		}

		// adjustments
		if ( FlipHorizontal && FlipVertical )
		{
			using var flippedHorizontal = bitmap.FlipHorizontal();
			using var flippedVertical = flippedHorizontal.FlipVertical();
			return ValueTask.FromResult( flippedVertical.ToTexture() );
		}

		if ( FlipHorizontal )
		{
			using var flippedHorizontal = bitmap.FlipHorizontal();
			return ValueTask.FromResult( flippedHorizontal.ToTexture() );
		}

		if ( FlipVertical )
		{
			using var flippedVertical = bitmap.FlipVertical();
			return ValueTask.FromResult( flippedVertical.ToTexture() );
		}

		return ValueTask.FromResult( bitmap.ToTexture() );
	}
}
