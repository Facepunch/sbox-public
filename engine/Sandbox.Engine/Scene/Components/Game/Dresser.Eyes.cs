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
	/// Obsolete Citizen iris alignment. Always returns the neutral value; setting it has no effect.
	/// </summary>
	[Obsolete( "Eye alignment is no longer supported. This property has no effect." ), Hide]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public float EyeAlign
	{
		get => AvatarDefaults.EyeAlign;
		set
		{
		}
	}

	internal void UpdateEyeAttributes()
	{
		if ( _settingAppearance || !BodyTarget.IsValid() || !SupportsHumanAppearance )
			return;

		var tint = EyeColorGradient.Evaluate( EyeColor );

		SetEyeAttributes( BodyTarget.Attributes, tint );
		_hasEyeAttributes = true;
		foreach ( var renderer in BodyTarget.GetComponentsInChildren<SkinnedModelRenderer>( true ) )
		{
			if ( renderer != BodyTarget )
				SetEyeAttributes( renderer.Attributes, tint );
		}
	}

	private static void SetEyeAttributes( RenderAttributes attributes, Color color )
	{
		attributes.Set( "eye_color", new Vector4( color.r, color.g, color.b, 1 ) );
		// Human eyes keep their authored alignment, with no Citizen iris offset.
		attributes.Set( "eye_align", Vector2.Zero );
	}
}
