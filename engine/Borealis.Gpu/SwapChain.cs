using Borealis.Gpu.Vulkan;

namespace Borealis.Gpu;

/// <summary>
/// A swap chain to create (<see cref="Device.CreateSwapChain"/>).
/// </summary>
public struct SwapChainInfo
{
	/// <summary>The window it presents to.</summary>
	public SurfaceSource Surface;

	/// <summary>The size to start at; the surface's own size wins where it has one.</summary>
	public int Width, Height;

	/// <summary>How presentation is paced.</summary>
	public PresentMode PresentMode;

	/// <summary>Images to ask for; 0 for 3.</summary>
	public int ImageCount;

	/// <summary>
	/// Prefer a linear (UNORM) format, for applications that encode sRGB themselves. Off by default: images are sRGB, so
	/// linear output is encoded on write.
	/// </summary>
	public bool Linear;
}

/// <summary>
/// A window's chain of images to present (<see cref="Device.CreateSwapChain"/>). Each frame: <see cref="Acquire"/> an image,
/// draw into it (moving it from <see cref="ImageLayout.Undefined"/> to <see cref="ImageLayout.ColorTarget"/>, and to
/// <see cref="ImageLayout.Present"/> at the end), <see cref="Device.Submit"/> naming this swap chain, then
/// <see cref="Present"/>. Belongs to the thread that drives frames.
/// </summary>
public sealed unsafe class SwapChain : IDisposable
{
	readonly Device device;
	readonly VkDeviceApi vk;
	SwapChainInfo info;
	readonly ulong surface;

	ulong swapchain;
	TextureHandle[] images = [];
	ulong[] presentSemaphores = [];
	readonly ulong[] acquireSemaphores;
	uint imageIndex;
	int acquireSlot = -1;
	bool outOfDate;
	bool disposed;

	/// <summary>The images' format.</summary>
	public Format Format { get; private set; }

	/// <summary>The images' size.</summary>
	public int Width { get; private set; }

	/// <summary>The images' size.</summary>
	public int Height { get; private set; }

	/// <summary>The pacing in use, after falling back from what was asked for.</summary>
	public PresentMode PresentMode { get; private set; }

	/// <summary>How many images it has.</summary>
	public int ImageCount => images.Length;

	/// <summary>The image <see cref="Acquire"/> returned, until it's presented.</summary>
	public TextureHandle Current { get; private set; }

	internal bool HasImage => Current.IsValid;

	/// <summary>
	/// Submissions naming this swap chain since its image was acquired (<see cref="Device.Submit"/>): the first waits for the
	/// acquire and signals the present; a present must follow exactly one.
	/// </summary>
	internal int SubmitsForImage;

	/// <summary>
	/// Images presented with no submission for them since they were acquired (a frame whose last block never submitted),
	/// and submissions naming the swap chain again for the same image. Each would leave the queue waiting on a semaphore
	/// nothing signals, which the GPU's watchdog takes as a hang and resets the device for: <see cref="Present"/> and
	/// <see cref="Device.Submit"/> now cover both, and count them here.
	/// </summary>
	public int PresentsWithoutSubmit { get; private set; }

	/// <inheritdoc cref="PresentsWithoutSubmit"/>
	public int RepeatedSubmits { get; internal set; }
	internal ulong AcquireSemaphore => acquireSemaphores[acquireSlot];
	internal ulong PresentSemaphore => presentSemaphores[imageIndex];

	internal SwapChain( Device device, in SwapChainInfo info )
	{
		if ( !device.Presentation ) throw new GpuException( "This device was made without presentation (DeviceOptions.Presentation)" );

		this.device = device;
		this.info = info;
		vk = device.Vk;
		surface = info.Surface.CreateSurface( device.Instance );

		try
		{
			var queue = device.QueueFor( QueueType.Graphics );
			uint supported;
			device.Instance.vkGetPhysicalDeviceSurfaceSupportKHR( device.PhysicalDevice, queue.Family, surface, &supported ).Check( "vkGetPhysicalDeviceSurfaceSupportKHR" );
			if ( supported == 0 ) throw new GpuException( "The graphics queue can't present to this window" );

			acquireSemaphores = new ulong[device.FramesInFlight];
			for ( int i = 0; i < acquireSemaphores.Length; i++ ) acquireSemaphores[i] = CreateSemaphore();

			Create( info.Width, info.Height );
		}
		catch
		{
			device.Instance.vkDestroySurfaceKHR( device.Instance.Instance, surface, null );
			throw;
		}

		device.Register( this );
	}

	ulong CreateSemaphore()
	{
		var create = new VkSemaphoreCreateInfo { sType = VkStructureType.SemaphoreCreateInfo };
		ulong semaphore;
		vk.vkCreateSemaphore( vk.Device, &create, null, &semaphore ).Check( "vkCreateSemaphore" );
		return semaphore;
	}

	void Create( int width, int height )
	{
		var instance = device.Instance;
		VkSurfaceCapabilitiesKHR caps;
		instance.vkGetPhysicalDeviceSurfaceCapabilitiesKHR( device.PhysicalDevice, surface, &caps ).Check( "vkGetPhysicalDeviceSurfaceCapabilitiesKHR" );

		// A surface with a size of its own (Win32, X11) dictates it; Wayland leaves it to us
		uint w, h;
		if ( caps.currentExtent.width != uint.MaxValue )
		{
			w = caps.currentExtent.width;
			h = caps.currentExtent.height;
		}
		else
		{
			w = (uint)Math.Clamp( width, (int)caps.minImageExtent.width, (int)caps.maxImageExtent.width );
			h = (uint)Math.Clamp( height, (int)caps.minImageExtent.height, (int)caps.maxImageExtent.height );
		}

		Width = (int)w;
		Height = (int)h;
		if ( w == 0 || h == 0 ) return; // Minimised: nothing to make until it's restored

		uint formatCount;
		instance.vkGetPhysicalDeviceSurfaceFormatsKHR( device.PhysicalDevice, surface, &formatCount, null ).Check( "vkGetPhysicalDeviceSurfaceFormatsKHR" );
		var formats = stackalloc VkSurfaceFormatKHR[(int)formatCount];
		instance.vkGetPhysicalDeviceSurfaceFormatsKHR( device.PhysicalDevice, surface, &formatCount, formats ).Check( "vkGetPhysicalDeviceSurfaceFormatsKHR" );

		var chosen = formats[0];
		ReadOnlySpan<VkFormat> preferred = info.Linear ? [VkFormat.B8G8R8A8Unorm, VkFormat.R8G8B8A8Unorm] : [VkFormat.B8G8R8A8Srgb, VkFormat.R8G8B8A8Srgb];
		var found = false;
		foreach ( var want in preferred )
		{
			for ( int i = 0; i < formatCount && !found; i++ )
			{
				if ( formats[i].format == want && formats[i].colorSpace == Vulkan.Vk.ColorSpaceSrgbNonlinear )
				{
					chosen = formats[i];
					found = true;
				}
			}
		}

		uint modeCount;
		instance.vkGetPhysicalDeviceSurfacePresentModesKHR( device.PhysicalDevice, surface, &modeCount, null ).Check( "vkGetPhysicalDeviceSurfacePresentModesKHR" );
		var modes = stackalloc VkPresentMode[(int)modeCount];
		instance.vkGetPhysicalDeviceSurfacePresentModesKHR( device.PhysicalDevice, surface, &modeCount, modes ).Check( "vkGetPhysicalDeviceSurfacePresentModesKHR" );

		var modeTotal = (int)modeCount;
		bool Has( VkPresentMode mode )
		{
			for ( int i = 0; i < modeTotal; i++ )
			{
				if ( modes[i] == mode ) return true;
			}

			return false;
		}

		var presentMode = VkPresentMode.Fifo;
		PresentMode = PresentMode.Fifo;
		if ( info.PresentMode == PresentMode.FifoLatestReady && device.FifoLatestReady && Has( VkPresentMode.FifoLatestReady ) ) (presentMode, PresentMode) = (VkPresentMode.FifoLatestReady, PresentMode.FifoLatestReady);
		else if ( info.PresentMode == PresentMode.FifoLatestReady && !Has( VkPresentMode.Mailbox ) && Has( VkPresentMode.Immediate ) ) (presentMode, PresentMode) = (VkPresentMode.Immediate, PresentMode.Immediate);
		else if ( info.PresentMode == PresentMode.Immediate && Has( VkPresentMode.Immediate ) ) (presentMode, PresentMode) = (VkPresentMode.Immediate, PresentMode.Immediate);
		else if ( info.PresentMode != PresentMode.Fifo && Has( VkPresentMode.Mailbox ) ) (presentMode, PresentMode) = (VkPresentMode.Mailbox, PresentMode.Mailbox);

		var count = (uint)(info.ImageCount > 0 ? info.ImageCount : 3);
		count = Math.Max( count, caps.minImageCount );
		if ( caps.maxImageCount != 0 ) count = Math.Min( count, caps.maxImageCount );

		var usage = Vulkan.Vk.ImageColorAttachment | (caps.supportedUsageFlags & (Vulkan.Vk.ImageTransferDst | Vulkan.Vk.ImageTransferSrc));
		var old = swapchain;
		var create = new VkSwapchainCreateInfoKHR
		{
			sType = VkStructureType.SwapchainCreateInfoKhr,
			surface = surface,
			minImageCount = count,
			imageFormat = chosen.format,
			imageColorSpace = chosen.colorSpace,
			imageExtent = new VkExtent2D { width = w, height = h },
			imageArrayLayers = 1,
			imageUsage = usage,
			preTransform = (caps.supportedTransforms & Vulkan.Vk.TransformIdentity) != 0 ? Vulkan.Vk.TransformIdentity : caps.currentTransform,
			compositeAlpha = (caps.supportedCompositeAlpha & Vulkan.Vk.CompositeAlphaOpaque) != 0 ? Vulkan.Vk.CompositeAlphaOpaque : caps.supportedCompositeAlpha & (uint)-(int)caps.supportedCompositeAlpha,
			presentMode = presentMode,
			clipped = 1,
			oldSwapchain = old,
		};

		ulong created;
		vk.vkCreateSwapchainKHR( vk.Device, &create, null, &created ).Check( "vkCreateSwapchainKHR" );
		swapchain = created;
		if ( old != 0 ) vk.vkDestroySwapchainKHR( vk.Device, old, null );

		// For device-loss reports: a remade chain starts its images again from the first
		device.RememberEvent( "swap chain made, width:", w );

		uint imageCount;
		vk.vkGetSwapchainImagesKHR( vk.Device, swapchain, &imageCount, null ).Check( "vkGetSwapchainImagesKHR" );
		var vkImages = stackalloc ulong[(int)imageCount];
		vk.vkGetSwapchainImagesKHR( vk.Device, swapchain, &imageCount, vkImages ).Check( "vkGetSwapchainImagesKHR" );

		Format = Formats.FromVk( chosen.format );
		var textureInfo = new TextureInfo
		{
			Width = (int)w,
			Height = (int)h,
			Depth = 1,
			Layers = 1,
			Mips = 1,
			Samples = 1,
			Format = Format,
			Usage = TextureUsage.ColorTarget | TextureUsage.CopyDestination,
			Name = "Swap chain",
		};

		images = new TextureHandle[imageCount];
		presentSemaphores = new ulong[imageCount];
		for ( int i = 0; i < imageCount; i++ )
		{
			images[i] = device.AddExternalTexture( vkImages[i], textureInfo, chosen.format );
			presentSemaphores[i] = CreateSemaphore();
			device.SetName( VkObjectType.Image, vkImages[i], $"Swap chain image {i}" );
		}

		outOfDate = false;
	}

	void Release()
	{
		foreach ( var image in images ) device.RemoveExternalTexture( image );
		foreach ( var semaphore in presentSemaphores ) vk.vkDestroySemaphore( vk.Device, semaphore, null );
		images = [];
		presentSemaphores = [];
		Current = default;
	}

	/// <summary>
	/// Make the images a new size: after the window resizes. Waits for the GPU.
	/// </summary>
	public void Resize( int width, int height )
	{
		device.WaitIdle();
		Release();
		Create( width, height );
	}

	/// <summary>
	/// Pace presentation differently from now on (vsync switched): makes the images again with the new mode. Waits for the
	/// GPU. Not while an image is acquired.
	/// </summary>
	public void SetPresentMode( PresentMode mode )
	{
		if ( Current.IsValid ) throw new InvalidOperationException( "An image is acquired" );
		if ( info.PresentMode == mode ) return;
		info.PresentMode = mode;
		Resize( Width, Height );
	}

	/// <summary>
	/// Take the next image to draw into. Returns false, with no image, while the window is minimised. Recreates the
	/// images first if the window changed size.
	/// </summary>
	public bool Acquire( out TextureHandle image )
	{
		image = default;
		if ( Current.IsValid ) throw new InvalidOperationException( "The last acquired image wasn't presented" );

		for ( int attempt = 0; attempt < 2; attempt++ )
		{
			if ( outOfDate || swapchain == 0 || images.Length == 0 ) Resize( Width, Height );
			if ( images.Length == 0 ) return false;

			device.ThrowIfLost( "vkAcquireNextImageKHR" );
			acquireSlot = device.FrameSlot;
			uint index;
			var result = AcquireNext( &index );

			if ( result == VkResult.ErrorOutOfDate )
			{
				outOfDate = true;
				continue;
			}

			if ( result == VkResult.Suboptimal ) outOfDate = true;
			else device.Check( result, "vkAcquireNextImageKHR" );

			imageIndex = index;
			Current = image = images[index];
			SubmitsForImage = 0;
			device.RememberEvent( "acquired image, slot*16+image:", acquireSlot * 16 + index );
			return true;
		}

		return false;
	}

	/// <summary>
	/// Queue the acquired image for presentation, once the submission that named this swap chain has finished.
	/// </summary>
	public void Present()
	{
		if ( !Current.IsValid ) throw new InvalidOperationException( "No image was acquired" );

		// Nothing was submitted for the image: the present would wait on a semaphore no submission signals, and the queue behind
		// it with it, until the watchdog resets the GPU. Submit for it here (an empty frame, the image moved to be presented)
		if ( SubmitsForImage == 0 && !device.IsLost )
		{
			PresentsWithoutSubmit++;
			device.RememberEvent( "present without a submission for the image:", (int)imageIndex );
			device.Warn( "Borealis.Gpu.PresentWithoutSubmit", "A swap chain image was presented with nothing submitted for it since it was acquired; an empty frame was submitted for it" + (PresentsWithoutSubmit == 1 ? " (first time, from:\n" + Environment.StackTrace + ")" : "") );
			var list = device.BeginCommandList( QueueType.Graphics, "Present without a frame" );
			list.Barrier( new TextureBarrier { Texture = Current, OldLayout = ImageLayout.Undefined, NewLayout = ImageLayout.Present, SourceStage = PipelineStage.AllCommands, SourceAccess = MemoryAccess.None, DestinationStage = PipelineStage.AllCommands, DestinationAccess = MemoryAccess.None, MipCount = 1, LayerCount = 1 } );
			device.Submit( list, default, this );
		}

		var semaphore = presentSemaphores[imageIndex];
		var chain = swapchain;
		var index = imageIndex;
		var present = new VkPresentInfoKHR
		{
			sType = VkStructureType.PresentInfoKhr,
			waitSemaphoreCount = 1,
			pWaitSemaphores = &semaphore,
			swapchainCount = 1,
			pSwapchains = &chain,
			pImageIndices = &index,
		};

		var queue = device.QueueFor( QueueType.Graphics );
		VkResult result;
		lock ( queue.SubmitLock ) result = vk.vkQueuePresentKHR( queue.Handle, &present );

		device.RememberEvent( "presented image, result*16+image:", (int)result * 16 + index );
		Current = default;
		if ( result is VkResult.ErrorOutOfDate or VkResult.Suboptimal ) outOfDate = true;
		else device.Check( result, "vkQueuePresentKHR" );
	}

	/// <summary>
	/// <c>vkAcquireNextImageKHR</c> in slices rather than with no timeout: an image that never comes back (the GPU stopped)
	/// takes the device as lost after <see cref="Device.HangSeconds"/> instead of holding the thread forever.
	/// </summary>
	VkResult AcquireNext( uint* index )
	{
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		while ( true )
		{
			var result = vk.vkAcquireNextImageKHR( vk.Device, swapchain, 500_000_000, acquireSemaphores[acquireSlot], 0, index );
			if ( result is not (VkResult.Timeout or VkResult.NotReady) ) return result;

			var waited = System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalSeconds;
			if ( waited >= device.HangSeconds )
			{
				device.MarkLost( $"No swap chain image came back in {waited:F0} s" );
				return VkResult.ErrorDeviceLost;
			}
		}
	}

	/// <summary>
	/// Wait for the GPU and destroy the swap chain and its surface.
	/// </summary>
	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;

		device.WaitIdle();
		Release();
		foreach ( var semaphore in acquireSemaphores ) vk.vkDestroySemaphore( vk.Device, semaphore, null );
		if ( swapchain != 0 ) vk.vkDestroySwapchainKHR( vk.Device, swapchain, null );
		device.Instance.vkDestroySurfaceKHR( device.Instance.Instance, surface, null );
		device.Unregister( this );
	}
}

public sealed partial class Device
{
	/// <summary>
	/// Create a swap chain for a window. The device must have been made with <see cref="DeviceOptions.Presentation"/>.
	/// </summary>
	public SwapChain CreateSwapChain( in SwapChainInfo info ) => new( this, info );
}
