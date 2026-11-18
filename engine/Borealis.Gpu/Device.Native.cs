namespace Borealis.Gpu;

/// <summary>
/// The Vulkan handles under a <see cref="Device"/>, for a library that works on it itself (a vendor upscaler): the instance,
/// adapter and device, <c>vkGetInstanceProcAddr</c> to load the rest from, and the graphics queue's family.
/// </summary>
public readonly record struct NativeDevice( nint Instance, nint PhysicalDevice, nint Device, nint GetInstanceProcAddr, uint GraphicsFamily );

/// <summary>
/// A texture's Vulkan image, its default view (every mip and layer; depth alone for a depth format) and its <c>VkFormat</c>.
/// </summary>
public readonly record struct NativeTexture( ulong Image, ulong View, int Format, int Width, int Height );

public sealed unsafe partial class Device
{
	/// <summary>
	/// The Vulkan handles under the device. What a library records with them goes in through
	/// <see cref="CommandList.BeginNative"/>; textures it reads or writes are in the layouts the caller moved them to.
	/// </summary>
	public NativeDevice Native => new( Instance.Instance, PhysicalDevice, Vk.Device, Vulkan.VkLoader.InstanceProcAddr, graphicsQueue.Family );

	/// <summary>The Vulkan loader's <c>vkGetInstanceProcAddr</c>, before any device is made (null without a loader).</summary>
	public static nint LoaderProcAddr => Vulkan.VkLoader.TryLoad() ? Vulkan.VkLoader.InstanceProcAddr : 0;

	/// <summary>A texture's Vulkan image, default view and format, for <see cref="Native"/>'s users.</summary>
	public NativeTexture NativeOf( TextureHandle texture )
	{
		ref var record = ref Get( texture );
		return new( record.Image, record.View, (int)record.Format, record.Info.Width, record.Info.Height );
	}
}
