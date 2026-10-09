
namespace Sandbox;

/// <summary>
/// Forces an enum property to be shown as a group of buttons.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public class EnumButtonGroupAttribute : System.Attribute
{
	/// <summary>
	/// Shows only icons, with each option's name and description in its tooltip.
	/// Options without an icon still display their name.
	/// </summary>
	public bool IconOnly { get; set; }
}

/// <summary>
/// Forces an enum property to be shown as a dropdown list.
/// </summary>
[AttributeUsage( AttributeTargets.Property | AttributeTargets.Field )]
public class EnumDropdownAttribute : System.Attribute
{
}
