using System.Text.Json.Serialization;
using System.Threading;

namespace Sandbox.Resources;


[Title( "Gradient - Linear" )]
[Icon( "gradient" )]
[ClassName( "gradientlinear" )]
public class LinearGradient : TextureGenerator
{
	public Vector2Int Size { get; set; } = 128;

	[Range( 0, 2 )]
	public bool IsHdr { get; set; } = false;

	[Range( 0, 360 )]
	public float Angle { get; set; } = 0;

	[Range( 0, 2 )]
	public float Scale { get; set; } = 1;

	[Range( 0, 1 )]
	public Vector2 Center { get; set; } = 0.5f;

	[KeyProperty]
	public Gradient Gradient { get; set; } = new Gradient( new Gradient.ColorFrame( 0.0f, Color.White ), new Gradient.ColorFrame( 1, Color.Black ) );

	[Header( "Normal Map" ), Title( "Height to Normal" )]
	public bool ConvertHeightToNormals { get; set; }

	[ShowIf( "ConvertHeightToNormals", true )]
	public float NormalScale { get; set; } = 1;

	[Hide, JsonIgnore]
	public override bool CacheToDisk => true;

	protected override ValueTask<Texture> CreateTexture( Options options, CancellationToken ct )
	{
		using var bitmap = new Bitmap( Size.x, Size.y, IsHdr && !ConvertHeightToNormals );
		bitmap.Clear( Color.Transparent );

		var hw = bitmap.Rect.Size.Length * 0.5f * Scale.Clamp( 0.0001f, 1000f );
		var dir = Vector2.FromDegrees( Angle ) * hw;
		var center = Center * bitmap.Rect.Size;

		bitmap.SetLinearGradient( center - dir, center + dir, Gradient );
		bitmap.DrawRect( bitmap.Rect );

		if ( ConvertHeightToNormals )
		{
			using var normalMap = bitmap.HeightmapToNormalMap( NormalScale );
			ValueTask.FromResult( normalMap.ToTexture() );
		}

		return ValueTask.FromResult( bitmap.ToTexture() );
	}
}

