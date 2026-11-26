using Borealis.Gpu.Vulkan;

namespace Borealis.Gpu;

/// <summary>
/// The window a swap chain presents to, as the window system's own handles. <see cref="Borealis.Gpu"/> has no windows of
/// its own: the application makes them (SDL in s&amp;box, plain Win32 in the sample) and hands them over.
/// </summary>
public readonly struct SurfaceSource
{
	internal enum Kind
	{
		None,
		Win32,
		Xlib,
		Wayland,
		Custom,
		Metal,
	}

	internal Kind Type { get; init; }
	internal nint Display { get; init; }
	internal nint Window { get; init; }
	internal Func<nint, ulong> Create { get; init; }

	/// <summary>A Win32 window: its module instance and <c>HWND</c>.</summary>
	public static SurfaceSource Win32( nint hinstance, nint hwnd ) => new() { Type = Kind.Win32, Display = hinstance, Window = hwnd };

	/// <summary>An X11 window, through Xlib: the <c>Display*</c> and the <c>Window</c> id.</summary>
	public static SurfaceSource Xlib( nint display, nint window ) => new() { Type = Kind.Xlib, Display = display, Window = window };

	/// <summary>A Wayland surface: the <c>wl_display*</c> and <c>wl_surface*</c>.</summary>
	public static SurfaceSource Wayland( nint display, nint surface ) => new() { Type = Kind.Wayland, Display = display, Window = surface };

	/// <summary>A macOS window, as the <c>CAMetalLayer*</c> its view presents through.</summary>
	public static SurfaceSource Metal( nint layer ) => new() { Type = Kind.Metal, Window = layer };

	/// <summary>
	/// A window system that makes its own <c>VkSurfaceKHR</c> (SDL's <c>SDL_Vulkan_CreateSurface</c>): called with the
	/// <c>VkInstance</c>, it returns the surface, which the swap chain then owns.
	/// </summary>
	public static SurfaceSource Custom( Func<nint, ulong> createSurface ) => new() { Type = Kind.Custom, Create = createSurface };

	/// <summary>The instance extensions each platform's surfaces need.</summary>
	internal static string[] InstanceExtensions => OperatingSystem.IsWindows() ? ["VK_KHR_surface", "VK_KHR_win32_surface"]
		: OperatingSystem.IsMacOS() ? ["VK_KHR_surface", "VK_EXT_metal_surface"]
		: ["VK_KHR_surface", "VK_KHR_xlib_surface", "VK_KHR_xcb_surface", "VK_KHR_wayland_surface"];

	internal unsafe ulong CreateSurface( VkInstanceApi vk )
	{
		ulong surface = 0;
		switch ( Type )
		{
			case Kind.Win32:
				{
					if ( vk.vkCreateWin32SurfaceKHR == null ) throw new GpuException( "This Vulkan instance can't make Win32 surfaces" );
					var info = new VkWin32SurfaceCreateInfoKHR { sType = VkStructureType.Win32SurfaceCreateInfoKhr, hinstance = Display, hwnd = Window };
					vk.vkCreateWin32SurfaceKHR( vk.Instance, &info, null, &surface ).Check( "vkCreateWin32SurfaceKHR" );
					break;
				}

			case Kind.Xlib:
				{
					if ( vk.vkCreateXlibSurfaceKHR == null ) throw new GpuException( "This Vulkan instance can't make Xlib surfaces" );
					var info = new VkXlibSurfaceCreateInfoKHR { sType = VkStructureType.XlibSurfaceCreateInfoKhr, dpy = Display, window = (nuint)Window };
					vk.vkCreateXlibSurfaceKHR( vk.Instance, &info, null, &surface ).Check( "vkCreateXlibSurfaceKHR" );
					break;
				}

			case Kind.Wayland:
				{
					if ( vk.vkCreateWaylandSurfaceKHR == null ) throw new GpuException( "This Vulkan instance can't make Wayland surfaces" );
					var info = new VkWaylandSurfaceCreateInfoKHR { sType = VkStructureType.WaylandSurfaceCreateInfoKhr, display = Display, surface = Window };
					vk.vkCreateWaylandSurfaceKHR( vk.Instance, &info, null, &surface ).Check( "vkCreateWaylandSurfaceKHR" );
					break;
				}

			case Kind.Metal:
				{
					if ( vk.vkCreateMetalSurfaceEXT == null ) throw new GpuException( "This Vulkan instance can't make Metal surfaces" );
					var info = new VkMetalSurfaceCreateInfoEXT { sType = VkStructureType.MetalSurfaceCreateInfoExt, pLayer = Window };
					vk.vkCreateMetalSurfaceEXT( vk.Instance, &info, null, &surface ).Check( "vkCreateMetalSurfaceEXT" );
					break;
				}

			case Kind.Custom:
				surface = Create( vk.Instance );
				if ( surface == 0 ) throw new GpuException( "The window system made no surface" );
				break;

			default:
				throw new GpuException( "No window to present to" );
		}

		return surface;
	}
}
