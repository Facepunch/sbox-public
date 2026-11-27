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

	public override void Render()
	{
		if ( GradingMethod == GradingType.None && ColorSpace == ColorSpaceEnum.None )
			return;

		var blendFactor = GetWeighted( x => x.BlendFactor );
		var colorTempK = GetWeighted( x => x.ColorTempK );

		if ( blendFactor <= 0f )
			return;

		Attributes.Set( "BlendFactor", blendFactor );
		Attributes.Set( "ColorTempK", colorTempK );

		// The method and colour space pick the uber pass's grading (a specialization constant): method + 4 * space
		Attributes.Set( "UberGrading", (int)GradingMethod + 4 * (int)ColorSpace );

		if ( GradingMethod == GradingType.LUT )
		{
			Attributes.Set( "LookupTexture", LookupTexture );
		}

		if ( ColorSpace == ColorSpaceEnum.RGB )
		{
			ProcessCurve( RedCurve, CurveTokens.R, Attributes );
			ProcessCurve( GreenCurve, CurveTokens.G, Attributes );
			ProcessCurve( BlueCurve, CurveTokens.B, Attributes );
		}
		if ( ColorSpace == ColorSpaceEnum.HSV )
		{
			ProcessCurve( HueCurve, CurveTokens.H, Attributes );
			ProcessCurve( SaturationCurve, CurveTokens.S, Attributes );
			ProcessCurve( ValueCurve, CurveTokens.V, Attributes );
		}

		AddUberEffect( UberPostEffect.ColorGrading, Stage.AfterPostProcess, 4000, "ColorGrading" );
	}

	sealed class CurveTokens( string channel )
	{
		public static readonly CurveTokens R = new( "R" ), G = new( "G" ), B = new( "B" ), H = new( "H" ), S = new( "S" ), V = new( "V" );

		public readonly StringToken Frames = new( "CurveFrames" + channel );
		public readonly StringToken Divisors = new( "CurveDivisors" + channel );
		public readonly StringToken[] Frame = [new( "CurveFrame" + channel + 0 ), new( "CurveFrame" + channel + 1 ), new( "CurveFrame" + channel + 2 ), new( "CurveFrame" + channel + 3 )];
	}


	/// <summary>
	/// Represent our curves into shaders
	/// </summary>
	void ProcessCurve( Curve curve, CurveTokens tokens, RenderAttributes attributes )
	{
		int TotalFrames = curve.Length;

		// Cap the maximum points for this to 4
		if ( TotalFrames > 4 )
		{
			TotalFrames = 4;
		}

		Span<float> xArray = stackalloc float[4];
		attributes.Set( tokens.Frames, TotalFrames );
		for ( int i = 0; i < TotalFrames; ++i )
		{
			var frame = curve.Frames[i];
			xArray[i] = frame.Time;
			Vector4 localFrame = new Vector4( frame.Time, frame.Value, frame.In, frame.Out );
			attributes.Set( tokens.Frame[i], localFrame );
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
		attributes.Set( tokens.Divisors, divisorVector );
	}
}
