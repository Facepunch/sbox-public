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
		/// Enable Vulkan validation layers
		/// </summary>
		VulkanValidation = 1 << 2
	}
}

