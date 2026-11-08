using System.Text.Json.Serialization;
using System.Threading;

namespace Sandbox.Resources;

[Title( "Gradient - Radial" )]
[Icon( "vignette" )]
[ClassName( "gradientradial" )]
[Expose]
public class RadialGradient : TextureGenerator
{
	public Vector2Int Size { get; set; } = 128;

	[Range( 0, 2 )]
	public bool IsHdr { get; set; } = false;

	[Range( 0, 2 )]
	public float Scale { get; set; } = 1;

	[Range( 0, 1 )]
	public Vector2 Center { get; set; } = 0.5f;

	[KeyProperty]
	public Gradient Gradient { get; set; } = new Gradient( new Gradient.ColorFrame( 0.0f, Color.Cyan ), new Gradient.ColorFrame( 0.2f, Color.Red ), new Gradient.ColorFrame( 1.0f, Color.Yellow ) );

	[Header( "Normal Map" ), Title( "Height to Normal" )]
	public bool ConvertHeightToNormals { get; set; }

	[ShowIf( "ConvertHeightToNormals", true )]
	public float NormalScale { get; set; } = 1;

	[Hide, JsonIgnore]
	public override bool CacheToDisk => true;

	[System.Diagnostics.CodeAnalysis.SuppressMessage( "Reliability", "CA2000", Justification = "The caller owns the bitmap returned through ValueTask; failures and the intermediate heightmap are disposed here." )]
	protected override ValueTask<Bitmap> CreateBitmap( Options options, CancellationToken ct )
	{
		var w = Size.x.Clamp( 1, 1024 * 4 );
		var h = Size.y.Clamp( 1, 1024 * 4 );

		var bitmap = new Bitmap( w, h, IsHdr && !ConvertHeightToNormals );
		try
		{
			bitmap.Clear( Color.Transparent );
			bitmap.SetRadialGradient( bitmap.Rect.Size * Center, Scale * bitmap.Width, Gradient );
			bitmap.DrawRect( bitmap.Rect );
		}
		catch
		{
			bitmap.Dispose();
			throw;
		}

		if ( !ConvertHeightToNormals ) return ValueTask.FromResult( bitmap );

		using ( bitmap ) return ValueTask.FromResult( bitmap.HeightmapToNormalMap( NormalScale ) );
	}
}
