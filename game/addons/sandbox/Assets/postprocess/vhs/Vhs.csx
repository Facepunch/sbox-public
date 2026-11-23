using Sandbox.Rendering;

/// <summary>
/// Plays the camera image through a simulated VHS deck and CRT display.
/// </summary>
[PostProcessMenu, Title( "#asset.vhs.title" ), Icon( "videocam" ), ClassName( "vhs" )]
[Group( "#effects.group.art_styles" )]
public sealed class Vhs : PostProcessPass
{
	/// <summary>
	/// Centers a 4:3 tape picture in the camera viewport.
	/// </summary>
	[Property, Title( "4:3 Playback" )]
	public bool FourThreePlayback { get; set; }

	/// <summary>
	/// Blend between the original image and tape playback.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float Strength { get; set; } = 1.0f;

	/// <summary>
	/// Horizontal line wobble and the rolling mistracking band.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float Tracking { get; set; } = 0.55f;

	/// <summary>
	/// How far color smears behind sharp brightness edges.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float ColorBleed { get; set; } = 0.75f;

	/// <summary>
	/// Amount of tape grain and short horizontal dropouts.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float TapeNoise { get; set; } = 0.4f;

	/// <summary>
	/// Darkness of the CRT scanlines.
	/// </summary>
	[Property, Range( 0, 1 )]
	public float Scanlines { get; set; } = 0.4f;

	Material _material = Material.FromShader( "postprocess/vhs/vhs.shader" );

	/// <summary>
	/// Applies VHS playback to the current camera image.
	/// </summary>
	public override void Render()
	{
		float strength = Weighted( x => ((Vhs)x).Strength );
		if ( strength <= 0 ) return;

		var attributes = ViewAttributes;
		attributes.Set( "Strength", strength );
		attributes.Set( "Tracking", Weighted( x => ((Vhs)x).Tracking, true ) );
		attributes.Set( "ColorBleed", Weighted( x => ((Vhs)x).ColorBleed, true ) );
		attributes.Set( "TapeNoise", Weighted( x => ((Vhs)x).TapeNoise, true ) );
		attributes.Set( "Scanlines", Weighted( x => ((Vhs)x).Scanlines, true ) );
		attributes.Set( "TapeTime", (float)(Time.NowDouble % 3600) );

		var viewport = Camera.CustomSize ?? Camera.ScreenRect.Size;
		float aspect = MathF.Max( 1, viewport.x ) / MathF.Max( 1, viewport.y );
		bool fourThree = Weighted( x => ((Vhs)x).FourThreePlayback ? 1f : 0f, true ) >= 0.5f;
		var scale = fourThree ? new Vector2( MathF.Min( 1, (4f / 3f) / aspect ), MathF.Min( 1, aspect * 0.75f ) ) : Vector2.One;
		attributes.Set( "PictureScale", scale );

		DrawPass( _material, Stage.AfterPostProcess, 230, "VHS" );
	}
}
