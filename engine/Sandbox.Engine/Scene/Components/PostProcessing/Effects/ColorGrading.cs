using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Applies color grading to the camera
/// </summary>
[Title( "Color Grading" )]
[Category( "Post Processing" )]
[Icon( "center_focus_strong" )]
public sealed class ColorGrading : BasePostProcess<ColorGrading>
{
	public enum GradingType
	{
		None,
		TemperatureControl,
		LUT
	};

	public enum ColorSpaceEnum
	{
		None,
		RGB,
		HSV
	}

	[Group( "Grading" )]
	[Property] public GradingType GradingMethod { get; set; } = GradingType.None;

	[Group( "Grading" )]
	[Range( 1000, 40000 )]
	[ShowIf( nameof( GradingMethod ), GradingType.TemperatureControl )]
	[Property] public float ColorTempK { get; set; } = 6500.0f;

	[Group( "Grading" )]
	[Range( 0, 1 )]
	[ShowIf( nameof( GradingMethod ), GradingType.TemperatureControl )]
	[Property] public float BlendFactor { get; set; } = 1.0f;

	[Group( "Grading" )]
	[ShowIf( nameof( GradingMethod ), GradingType.LUT )]
	[Property] public Texture LookupTexture { get; set; } = Texture.White;

	// Per channel

	[Group( "Per Channel" )]
	[Property]
	public ColorSpaceEnum ColorSpace { get; set; } = ColorSpaceEnum.None;

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.RGB )]
	public Curve RedCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.RGB )]
	public Curve GreenCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.RGB )]
	public Curve BlueCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.HSV )]
	public Curve HueCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.HSV )]
	public Curve SaturationCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	[Group( "Per Channel" )]
	[Property]
	[ShowIf( nameof( ColorSpace ), ColorSpaceEnum.HSV )]
	public Curve ValueCurve { get; set; } = new Curve( new Curve.Frame( 0.0f, 0.5f ), new Curve.Frame( 1.0f, 1.0f ) );

	private static readonly Material Shader = Material.FromShader( "ColorGrading.shader" );

	public override void Render()
	{
		var blendFactor = GetWeighted( x => x.BlendFactor );
		var colorTempK = GetWeighted( x => x.ColorTempK );

		if ( blendFactor <= 0f )
			return;

		Attributes.Set( "BlendFactor", blendFactor );
		Attributes.Set( "ColorTempK", colorTempK );

		Attributes.SetComboEnum( "D_CGRAD_PASS", GradingMethod );

		if ( GradingMethod == GradingType.LUT )
		{
			Attributes.Set( "LookupTexture", LookupTexture );
		}

		Attributes.SetComboEnum( "D_COLORSPACE", ColorSpace );

		if ( ColorSpace == ColorSpaceEnum.RGB )
		{
			ProcessCurve( RedCurve, "R", Attributes );
			ProcessCurve( GreenCurve, "G", Attributes );
			ProcessCurve( BlueCurve, "B", Attributes );
		}
		if ( ColorSpace == ColorSpaceEnum.HSV )
		{
			ProcessCurve( HueCurve, "H", Attributes );
			ProcessCurve( SaturationCurve, "S", Attributes );
			ProcessCurve( ValueCurve, "V", Attributes );
		}

		var blit = BlitMode.WithBackbuffer( Shader, Stage.AfterPostProcess, 4000, false );
		Blit( blit, "ColorGrading" );
	}

	/// <summary>
	/// Represent our curves into shaders
	/// </summary>
	void ProcessCurve( Curve curve, string ChannelLetter, RenderAttributes attributes )
	{
		int TotalFrames = curve.Length;

		// Cap the maximum points for this to 4
		if ( TotalFrames > 4 )
		{
			TotalFrames = 4;
		}

		float[] xArray = new float[TotalFrames];
		attributes.Set( "CurveFrames" + ChannelLetter, TotalFrames );
		for ( int i = 0; i < TotalFrames; ++i )
		{
			var frame = curve.Frames[i];
			xArray[i] = frame.Time;
			Vector4 localFrame = new Vector4( frame.Time, frame.Value, frame.In, frame.Out );
			attributes.Set( "CurveFrame" + ChannelLetter + i, localFrame );
		}

		// Store the reciprocals of the 'x' deltas  (time in Curve.cs)  between successive point pairs.  
		// These are then passed to the pixel shader to avoid divisions.
		Vector4 divisorVector = new Vector4( 0 );
		if ( TotalFrames >= 2 )
		{
			divisorVector.x = 1.0f / Math.Max( xArray[1] - xArray[0], 0.00001f );
			if ( TotalFrames >= 3 )
			{
				divisorVector.y = 1.0f / Math.Max( xArray[2] - xArray[1], 0.00001f );
				if ( TotalFrames >= 4 )
				{
					divisorVector.z = 1.0f / Math.Max( xArray[3] - xArray[2], 0.00001f );
				}
			}
		}
		attributes.Set( "CurveDivisors" + ChannelLetter, divisorVector );
	}
}
