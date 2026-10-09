namespace Sandbox;

public sealed partial class Dresser
{
	/// <summary>
	/// Shared sRGB iris palette for dressing and the avatar editor, starting at Citizen's brown.
	/// Browns occupy the first 15%, leaving room for hazel, green, teal, blue and grey.
	/// </summary>
	// Authored colours have 30% less HSV saturation, preserving their hue and brightness.
	public static Gradient EyeColorGradient { get; } = new(
		new Gradient.ColorFrame( 0, "#382B24" ),
		new Gradient.ColorFrame( 0.15f, "#514234" ),
		new Gradient.ColorFrame( 0.3f, "#8B7B56" ),
		new Gradient.ColorFrame( 0.475f, "#6D8065" ),
		new Gradient.ColorFrame( 0.65f, "#678582" ),
		new Gradient.ColorFrame( 0.825f, "#69879B" ),
		new Gradient.ColorFrame( 1, "#A2ACAF" ) );

	/// <summary>
	/// Position on the eye colour gradient, from zero to one.
	/// Local and owner avatars populate this automatically.
	/// </summary>
	[Property, Sync, Range( 0, 1 ), Group( "Eyes" )]
	[ShowIf( nameof( Source ), ClothingSource.Manual )]
	public float EyeColor
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.EyeColor;

			if ( field == value )
				return;

			field = value;
			UpdateEyeAttributes();
		}
	} = AvatarDefaults.EyeColor;

	/// <summary>
	/// Horizontal iris alignment from zero to one. The midpoint preserves Iris Center.
	/// This rotates the iris projection around the eyeball without changing the eye bones.
	/// </summary>
	[Property, Sync, Range( 0, 1 ), Group( "Eyes" )]
	[ShowIf( nameof( Source ), ClothingSource.Manual )]
	public float EyeAlign
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.EyeAlign;

			if ( field == value )
				return;

			field = value;
			UpdateEyeAttributes();
		}
	} = AvatarDefaults.EyeAlign;

	internal void UpdateEyeAttributes()
	{
		if ( _settingAppearance || !BodyTarget.IsValid() )
			return;

		var tint = EyeColorGradient.Evaluate( EyeColor );
		var offset = EyeAlign.Remap( 0, 1, -0.2f, 0.2f );

		SetEyeAttributes( BodyTarget.Attributes, tint, offset );
		foreach ( var renderer in BodyTarget.GetComponentsInChildren<SkinnedModelRenderer>( true ) )
		{
			if ( renderer != BodyTarget )
				SetEyeAttributes( renderer.Attributes, tint, offset );
		}
	}

	private static void SetEyeAttributes( RenderAttributes attributes, Color color, float align )
	{
		attributes.Set( "eye_color", new Vector4( color.r, color.g, color.b, 1 ) );
		attributes.Set( "eye_align", new Vector2( align, 0 ) );
	}
}
