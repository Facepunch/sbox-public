using Sandbox;

public sealed partial class AvatarEditManager
{
	/// <summary>
	/// Position on Dresser's shared eye colour palette.
	/// </summary>
	public float EyeColor
	{
		get => Container.EyeColor;
		set
		{
			Container.EyeColor = value;
			ApplyAppearanceChanges();
		}
	}

	/// <summary>
	/// Horizontal position of the iris in the eye's UV space.
	/// </summary>
	public float EyeAlign
	{
		get => Container.EyeAlign;
		set
		{
			Container.EyeAlign = value;
			ApplyAppearanceChanges();
		}
	}

}
