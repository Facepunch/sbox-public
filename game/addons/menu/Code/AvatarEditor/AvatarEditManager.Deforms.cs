using Sandbox;

public sealed partial class AvatarEditManager
{
	/// <summary>
	/// Normalized neck deformation, applied immediately to the avatar.
	/// </summary>
	public float NeckSize
	{
		get => Container.NeckSize;
		set
		{
			Container.NeckSize = value;
			ApplyAppearanceChanges();
		}
	}

	/// <summary>
	/// Normalized waist deformation, applied immediately to the avatar.
	/// </summary>
	public float WaistSize
	{
		get => Container.WaistSize;
		set
		{
			Container.WaistSize = value;
			ApplyAppearanceChanges();
		}
	}

	/// <summary>
	/// Normalized chest deformation, applied immediately to the avatar.
	/// </summary>
	public float ChestSize
	{
		get => Container.ChestSize;
		set
		{
			Container.ChestSize = value;
			ApplyAppearanceChanges();
		}
	}

	/// <summary>
	/// Normalized head deformation, applied immediately to the avatar.
	/// </summary>
	public float HeadShape
	{
		get => Container.HeadShape;
		set
		{
			Container.HeadShape = value;
			ApplyAppearanceChanges();
		}
	}

	/// <summary>
	/// Normalized nose deformation, applied immediately to the avatar.
	/// </summary>
	public float NoseSize
	{
		get => Container.NoseSize;
		set
		{
			Container.NoseSize = value;
			ApplyAppearanceChanges();
		}
	}

	/// <summary>
	/// Normalized chin deformation, applied immediately to the avatar.
	/// </summary>
	public float ChinSize
	{
		get => Container.ChinSize;
		set
		{
			Container.ChinSize = value;
			ApplyAppearanceChanges();
		}
	}

}
