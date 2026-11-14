namespace Borealis;

/// <summary>
/// Pipeline stages a barrier waits on or blocks.
/// </summary>
[Flags]
public enum GpuStage
{
	/// <summary>Reading indirect arguments.</summary>
	DrawIndirect = 1 << 0,

	/// <summary>Reading vertex and index buffers.</summary>
	VertexInput = 1 << 1,

	/// <summary>Vertex and other pre-rasterisation shaders.</summary>
	PreRasterizationShaders = 1 << 2,

	/// <summary>Pixel shaders.</summary>
	FragmentShader = 1 << 3,

	/// <summary>Depth and stencil tests before shading.</summary>
	EarlyFragmentTests = 1 << 4,

	/// <summary>Depth and stencil tests after shading.</summary>
	LateFragmentTests = 1 << 5,

	/// <summary>Writing colour attachments.</summary>
	ColorAttachmentOutput = 1 << 6,

	/// <summary>Compute shaders.</summary>
	ComputeShader = 1 << 7,

	/// <summary>Copies, clears and blits.</summary>
	Transfer = 1 << 8,

	/// <summary>Every graphics stage.</summary>
	AllGraphics = 1 << 9,

	/// <summary>Before anything.</summary>
	TopOfPipe = 1 << 11,

	/// <summary>After everything.</summary>
	BottomOfPipe = 1 << 12,
}

/// <summary>
/// Memory accesses a barrier makes visible or waits for.
/// </summary>
[Flags]
public enum GpuAccess
{
	/// <summary>Reading indirect arguments.</summary>
	IndirectCommandRead = 1 << 0,

	/// <summary>Reading indices.</summary>
	IndexRead = 1 << 1,

	/// <summary>Reading vertex attributes.</summary>
	VertexAttributeRead = 1 << 2,

	/// <summary>Reading constant buffers.</summary>
	UniformRead = 1 << 3,

	/// <summary>Reading input attachments.</summary>
	InputAttachmentRead = 1 << 4,

	/// <summary>Shader reads.</summary>
	ShaderRead = 1 << 5,

	/// <summary>Shader writes.</summary>
	ShaderWrite = 1 << 6,

	/// <summary>Reading colour attachments.</summary>
	ColorAttachmentRead = 1 << 7,

	/// <summary>Writing colour attachments.</summary>
	ColorAttachmentWrite = 1 << 8,

	/// <summary>Reading depth and stencil.</summary>
	DepthStencilAttachmentRead = 1 << 9,

	/// <summary>Writing depth and stencil.</summary>
	DepthStencilAttachmentWrite = 1 << 10,

	/// <summary>Copy sources.</summary>
	TransferRead = 1 << 11,

	/// <summary>Copy and clear destinations.</summary>
	TransferWrite = 1 << 12,
}

/// <summary>
/// How a texture is laid out for its next use (<see cref="Gpu.GpuContext.TextureBarrier"/>).
/// </summary>
public enum TextureLayout
{
	/// <summary>Any access, compute writes included.</summary>
	General,

	/// <summary>Sampled by shaders.</summary>
	ShaderRead,

	/// <summary>Drawn to as a colour target.</summary>
	ColorAttachment,

	/// <summary>Tested and written as depth.</summary>
	DepthAttachment,
}

/// <summary>
/// A semaphore one context signals when its work is done and another waits on before starting (<see cref="GpuContext.SignalAtEnd"/>).
/// </summary>
public readonly record struct GpuSemaphore( IntPtr Handle );

/// <summary>
/// A window's swap chain, which a frame presents into. Default for none.
/// </summary>
public readonly record struct GpuSwapChain( IntPtr Handle )
{
	/// <summary>
	/// Whether this names a swap chain.
	/// </summary>
	public bool IsValid => Handle != IntPtr.Zero;
}
