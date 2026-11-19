namespace Sandbox;

public partial class ClothingContainer
{
	/// <summary>
	/// Normalized neck deformation, from zero to one.
	/// </summary>
	public float NeckSize
	{
		get;
		set => field = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.NeckSize;
	} = AvatarDefaults.NeckSize;

	/// <summary>
	/// Normalized waist deformation, from zero to one.
	/// </summary>
	public float WaistSize
	{
		get;
		set => field = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.WaistSize;
	} = AvatarDefaults.WaistSize;

	/// <summary>
	/// Normalized chest deformation, from zero to one.
	/// </summary>
	public float ChestSize
	{
		get;
		set => field = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.ChestSize;
	} = AvatarDefaults.ChestSize;

	/// <summary>
	/// Normalized head deformation, from zero to one.
	/// </summary>
	public float HeadShape
	{
		get;
		set => field = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.HeadShape;
	} = AvatarDefaults.HeadShape;

	/// <summary>
	/// Normalized nose deformation, from zero to one.
	/// </summary>
	public float NoseSize
	{
		get;
		set => field = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.NoseSize;
	} = AvatarDefaults.NoseSize;

	/// <summary>
	/// Normalized chin deformation, from zero to one.
	/// </summary>
	public float ChinSize
	{
		get;
		set => field = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.ChinSize;
	} = AvatarDefaults.ChinSize;

	/// <summary>
	/// Obsolete Citizen body deformation. Has no effect and always returns null.
	/// </summary>
	[Obsolete( "Citizen body deformation is no longer supported. This method has no effect." )]
	public GameObject ApplyDeforms( SkinnedModelRenderer body, bool enabled = true ) => null;
}
