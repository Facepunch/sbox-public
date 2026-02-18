namespace Editor;

public partial class ProjectRow
{
	[Flags]
	enum LaunchFlags
	{
		/// <summary>
		/// Launch with no special flags.
		/// </summary>
		None = 1 << 0,

		/// <summary>
		/// Forcibly launch in VR mode.
		/// </summary>
		VR = 1 << 1,

		/// <summary>
		/// Enable Vulkan validation layers
		/// </summary>
		VulkanValidation = 1 << 2,

		/// <summary>
		/// Enable VR debug layers (also enables VR)
		/// </summary>
		VRDebug = 1 << 3
	}
}

