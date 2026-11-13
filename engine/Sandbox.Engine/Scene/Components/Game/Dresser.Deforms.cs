namespace Sandbox;

public sealed partial class Dresser
{
	/// <summary>
	/// Obsolete Citizen neck size. Always returns the neutral value; setting it has no effect.
	/// </summary>
	[Obsolete( "Citizen body deformation is no longer supported. This property has no effect." ), Hide]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public float NeckSize
	{
		get => AvatarDefaults.NeckSize;
		set
		{
		}
	}

	/// <summary>
	/// Obsolete Citizen waist size. Always returns the neutral value; setting it has no effect.
	/// </summary>
	[Obsolete( "Citizen body deformation is no longer supported. This property has no effect." ), Hide]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public float WaistSize
	{
		get => AvatarDefaults.WaistSize;
		set
		{
		}
	}

	/// <summary>
	/// Obsolete Citizen chest size. Always returns the neutral value; setting it has no effect.
	/// </summary>
	[Obsolete( "Citizen body deformation is no longer supported. This property has no effect." ), Hide]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public float ChestSize
	{
		get => AvatarDefaults.ChestSize;
		set
		{
		}
	}

	/// <summary>
	/// Obsolete Citizen head shape. Always returns the neutral value; setting it has no effect.
	/// </summary>
	[Obsolete( "Citizen body deformation is no longer supported. This property has no effect." ), Hide]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public float HeadShape
	{
		get => AvatarDefaults.HeadShape;
		set
		{
		}
	}

	/// <summary>
	/// Obsolete Citizen nose size. Always returns the neutral value; setting it has no effect.
	/// </summary>
	[Obsolete( "Citizen body deformation is no longer supported. This property has no effect." ), Hide]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public float NoseSize
	{
		get => AvatarDefaults.NoseSize;
		set
		{
		}
	}

	/// <summary>
	/// Obsolete Citizen chin size. Always returns the neutral value; setting it has no effect.
	/// </summary>
	[Obsolete( "Citizen body deformation is no longer supported. This property has no effect." ), Hide]
	[System.ComponentModel.EditorBrowsable( System.ComponentModel.EditorBrowsableState.Never )]
	public float ChinSize
	{
		get => AvatarDefaults.ChinSize;
		set
		{
		}
	}
}
