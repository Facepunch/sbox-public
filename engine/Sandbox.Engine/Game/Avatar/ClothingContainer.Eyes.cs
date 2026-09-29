namespace Sandbox;

public partial class ClothingContainer
{
	/// <summary>
	/// Position on Dresser's eye colour gradient, from zero to one.
	/// </summary>
	public float EyeColor
	{
		get;
		set => field = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.EyeColor;
	} = AvatarDefaults.EyeColor;

	/// <summary>
	/// Normalized horizontal iris alignment. The midpoint preserves the material's centre.
	/// </summary>
	public float EyeAlign
	{
		get;
		set => field = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.EyeAlign;
	} = AvatarDefaults.EyeAlign;

	/// <summary>
	/// Updates eye render attributes on the body and clothing without rebuilding the outfit.
	/// Materials using the procedural eye shader consume these per-renderer values.
	/// </summary>
	[Obsolete( "Use Dresser.UpdateAppearance instead." )]
	public void ApplyEyes( SkinnedModelRenderer body )
	{
		if ( !body.IsValid() )
			return;

		var dresser = Dresser.GetOrCreate( body );
		dresser.EyeColor = EyeColor;
		dresser.EyeAlign = EyeAlign;
		dresser.UpdateEyeAttributes();
	}
}
