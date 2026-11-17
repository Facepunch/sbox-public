namespace Borealis.Gpu;

/// <summary>
/// A queue work is submitted to.
/// </summary>
public enum QueueType
{
	/// <summary>Graphics, compute, copies and presentation.</summary>
	Graphics,

	/// <summary>Compute and copies, beside graphics. Where the adapter has none, this is the graphics queue.</summary>
	Compute,
}

/// <summary>
/// Where a buffer's memory lives.
/// </summary>
public enum MemoryLocation
{
	/// <summary>Video memory: everything the GPU reads and writes. Not mappable.</summary>
	Device,

	/// <summary>Memory the CPU writes and the GPU reads: staging and per-frame data. Always mapped.</summary>
	Upload,

	/// <summary>Memory the GPU writes and the CPU reads back, cached on the CPU. Always mapped.</summary>
	Readback,
}

/// <summary>
/// What a buffer is used as.
/// </summary>
[Flags]
public enum BufferUsage
{
	/// <summary>Nothing beyond copies.</summary>
	None = 0,

	/// <summary>A vertex stream.</summary>
	Vertex = 1 << 0,

	/// <summary>An index buffer.</summary>
	Index = 1 << 1,

	/// <summary>A constant (uniform) buffer.</summary>
	Uniform = 1 << 2,

	/// <summary>A storage buffer: structured, byte-address or append buffers.</summary>
	Storage = 1 << 3,

	/// <summary>Indirect draw or dispatch arguments, or a draw count.</summary>
	Indirect = 1 << 4,

	/// <summary>The source of copies.</summary>
	CopySource = 1 << 5,

	/// <summary>The destination of copies, updates and fills.</summary>
	CopyDestination = 1 << 6,
}

/// <summary>
/// What a texture is used as.
/// </summary>
[Flags]
public enum TextureUsage
{
	/// <summary>Nothing beyond copies.</summary>
	None = 0,

	/// <summary>Sampled by shaders, through the bindless table or a binding.</summary>
	Sampled = 1 << 0,

	/// <summary>Read and written by compute shaders.</summary>
	Storage = 1 << 1,

	/// <summary>Drawn to as a colour target.</summary>
	ColorTarget = 1 << 2,

	/// <summary>Drawn to as a depth (and stencil) target.</summary>
	DepthTarget = 1 << 3,

	/// <summary>The source of copies, blits and resolves.</summary>
	CopySource = 1 << 4,

	/// <summary>The destination of copies, blits, resolves and clears.</summary>
	CopyDestination = 1 << 5,
}

/// <summary>
/// The shape of a texture or a view of one.
/// </summary>
public enum TextureDimension
{
	/// <summary>A 2D texture.</summary>
	Texture2D,

	/// <summary>A 2D texture array.</summary>
	Texture2DArray,

	/// <summary>Six square faces.</summary>
	TextureCube,

	/// <summary>An array of cubes.</summary>
	TextureCubeArray,

	/// <summary>A volume.</summary>
	Texture3D,
}

/// <summary>
/// Texture filtering.
/// </summary>
public enum Filter
{
	/// <summary>The nearest texel.</summary>
	Nearest,

	/// <summary>Blend the nearest texels.</summary>
	Linear,
}

/// <summary>
/// What sampling outside 0..1 reads.
/// </summary>
public enum AddressMode
{
	/// <summary>Repeat.</summary>
	Wrap,

	/// <summary>Repeat, mirrored.</summary>
	Mirror,

	/// <summary>The edge texel.</summary>
	Clamp,

	/// <summary>The border colour (transparent black).</summary>
	Border,
}

/// <summary>
/// A comparison, for depth and stencil tests and comparison samplers. Values match Vulkan's.
/// </summary>
public enum CompareFunction
{
	/// <summary>Never passes.</summary>
	Never,

	/// <summary>Passes when less.</summary>
	Less,

	/// <summary>Passes when equal.</summary>
	Equal,

	/// <summary>Passes when less or equal.</summary>
	LessEqual,

	/// <summary>Passes when greater.</summary>
	Greater,

	/// <summary>Passes when not equal.</summary>
	NotEqual,

	/// <summary>Passes when greater or equal.</summary>
	GreaterEqual,

	/// <summary>Always passes.</summary>
	Always,
}

/// <summary>
/// What a descriptor binding holds.
/// </summary>
public enum BindingType
{
	/// <summary>A constant buffer (<c>cbuffer</c>, <c>b#</c>).</summary>
	UniformBuffer,

	/// <summary>A storage buffer: structured, byte-address or read-write (<c>t#</c> or <c>u#</c>).</summary>
	StorageBuffer,

	/// <summary>A texture sampled with a separate sampler (<c>t#</c>).</summary>
	SampledTexture,

	/// <summary>A texture compute reads and writes (<c>u#</c>).</summary>
	StorageTexture,

	/// <summary>A sampler (<c>s#</c>).</summary>
	Sampler,

	/// <summary>
	/// A constant buffer whose offset is given when its set is bound (<see cref="CommandList.SetBindingSet"/>), so a
	/// persistent set can point at constants that move every frame.
	/// </summary>
	UniformBufferDynamic,
}

/// <summary>
/// Shader stages. Values match Vulkan's.
/// </summary>
[Flags]
public enum ShaderStages
{
	/// <summary>None.</summary>
	None = 0,

	/// <summary>Vertex shaders.</summary>
	Vertex = 0x1,

	/// <summary>Geometry shaders.</summary>
	Geometry = 0x8,

	/// <summary>Pixel shaders.</summary>
	Fragment = 0x10,

	/// <summary>Compute shaders.</summary>
	Compute = 0x20,

	/// <summary>Task shaders (<see cref="DeviceCapabilities.MeshShaders"/>).</summary>
	Task = 0x40,

	/// <summary>Mesh shaders (<see cref="DeviceCapabilities.MeshShaders"/>).</summary>
	Mesh = 0x80,

	/// <summary>Every graphics stage.</summary>
	AllGraphics = 0x1F,

	/// <summary>Every stage.</summary>
	All = AllGraphics | Compute,
}

/// <summary>
/// How vertices are assembled. Values match Vulkan's.
/// </summary>
public enum PrimitiveTopology
{
	/// <summary>Points.</summary>
	PointList = 0,

	/// <summary>Separate lines.</summary>
	LineList = 1,

	/// <summary>A connected line.</summary>
	LineStrip = 2,

	/// <summary>Separate triangles.</summary>
	TriangleList = 3,

	/// <summary>A strip of triangles.</summary>
	TriangleStrip = 4,
}

/// <summary>
/// Which faces are culled. Values match Vulkan's.
/// </summary>
public enum CullMode
{
	/// <summary>None.</summary>
	None = 0,

	/// <summary>Front faces.</summary>
	Front = 1,

	/// <summary>Back faces.</summary>
	Back = 2,
}

/// <summary>
/// Which winding faces the camera. Values match Vulkan's.
/// </summary>
public enum FrontFace
{
	/// <summary>Counter-clockwise.</summary>
	CounterClockwise = 0,

	/// <summary>Clockwise.</summary>
	Clockwise = 1,
}

/// <summary>
/// Blend factors. Values match Vulkan's.
/// </summary>
public enum BlendFactor
{
	/// <summary>0.</summary>
	Zero = 0,
	/// <summary>1.</summary>
	One = 1,
	/// <summary>Source colour.</summary>
	SourceColor = 2,
	/// <summary>1 - source colour.</summary>
	OneMinusSourceColor = 3,
	/// <summary>Destination colour.</summary>
	DestinationColor = 4,
	/// <summary>1 - destination colour.</summary>
	OneMinusDestinationColor = 5,
	/// <summary>Source alpha.</summary>
	SourceAlpha = 6,
	/// <summary>1 - source alpha.</summary>
	OneMinusSourceAlpha = 7,
	/// <summary>Destination alpha.</summary>
	DestinationAlpha = 8,
	/// <summary>1 - destination alpha.</summary>
	OneMinusDestinationAlpha = 9,
}

/// <summary>
/// Blend operations. Values match Vulkan's.
/// </summary>
public enum BlendOp
{
	/// <summary>Source + destination.</summary>
	Add = 0,
	/// <summary>Source - destination.</summary>
	Subtract = 1,
	/// <summary>Destination - source.</summary>
	ReverseSubtract = 2,
	/// <summary>The smaller.</summary>
	Min = 3,
	/// <summary>The larger.</summary>
	Max = 4,
}

/// <summary>
/// Stencil operations. Values match Vulkan's.
/// </summary>
public enum StencilOp
{
	/// <summary>Keep the value.</summary>
	Keep = 0,
	/// <summary>Write 0.</summary>
	Zero = 1,
	/// <summary>Write the reference.</summary>
	Replace = 2,
	/// <summary>Add one, clamped.</summary>
	IncrementClamp = 3,
	/// <summary>Subtract one, clamped.</summary>
	DecrementClamp = 4,
	/// <summary>Invert the bits.</summary>
	Invert = 5,
	/// <summary>Add one, wrapping.</summary>
	IncrementWrap = 6,
	/// <summary>Subtract one, wrapping.</summary>
	DecrementWrap = 7,
}

/// <summary>
/// The format of one vertex attribute.
/// </summary>
public enum VertexFormat
{
	/// <summary>One 32-bit float.</summary>
	Float,
	/// <summary>Two 32-bit floats.</summary>
	Float2,
	/// <summary>Three 32-bit floats.</summary>
	Float3,
	/// <summary>Four 32-bit floats.</summary>
	Float4,
	/// <summary>Two 16-bit floats.</summary>
	Half2,
	/// <summary>Four 16-bit floats.</summary>
	Half4,
	/// <summary>Four unsigned bytes read as 0..1.</summary>
	UByte4Norm,
	/// <summary>Four signed bytes read as -1..1.</summary>
	Byte4Norm,
	/// <summary>Four unsigned bytes read as integers.</summary>
	UByte4,
	/// <summary>One 32-bit unsigned integer.</summary>
	UInt,
	/// <summary>Two 32-bit unsigned integers.</summary>
	UInt2,
	/// <summary>Four 32-bit unsigned integers.</summary>
	UInt4,
	/// <summary>Three 10-bit and one 2-bit unsigned values in 32 bits, x lowest, read as 0..1.</summary>
	UInt1010102Norm,
	/// <summary>Two 16-bit unsigned values read as 0..1 (lightmap uvs).</summary>
	UShort2Norm,
	/// <summary>One 32-bit signed integer.</summary>
	Int,
	/// <summary>One unsigned byte read as an integer.</summary>
	UByte,
	/// <summary>One signed byte read as an integer.</summary>
	Byte,
}

/// <summary>
/// Whether a vertex buffer advances per vertex or per instance.
/// </summary>
public enum VertexStepRate
{
	/// <summary>Per vertex.</summary>
	Vertex,

	/// <summary>Per instance.</summary>
	Instance,
}

/// <summary>
/// The size of an index.
/// </summary>
public enum IndexFormat
{
	/// <summary>16-bit indices.</summary>
	UInt16,

	/// <summary>32-bit indices.</summary>
	UInt32,
}

/// <summary>
/// What a render target holds when rendering starts.
/// </summary>
public enum LoadOp
{
	/// <summary>What it held.</summary>
	Load,

	/// <summary>The clear value.</summary>
	Clear,

	/// <summary>Anything: it will be overwritten.</summary>
	DontCare,
}

/// <summary>
/// Whether a render target keeps what was drawn when rendering ends.
/// </summary>
public enum StoreOp
{
	/// <summary>Keep it.</summary>
	Store,

	/// <summary>Discard it (it was only needed during rendering, or was resolved).</summary>
	DontCare,
}

/// <summary>
/// How a rendering scope carries on across command lists (<see cref="CommandList.BeginRendering"/>).
/// </summary>
[Flags]
public enum RenderingFlags
{
	/// <summary>It begins and ends in its list.</summary>
	None = 0,

	/// <summary>It's left suspended when it ends, for the next list submitted to resume.</summary>
	Suspend = 1,

	/// <summary>It continues the scope the list submitted before left suspended, when it's the list's first command.</summary>
	Resume = 2,
}

/// <summary>
/// How a swap chain paces presentation.
/// </summary>
public enum PresentMode
{
	/// <summary>Wait for vertical blank (vsync). Always supported.</summary>
	Fifo,

	/// <summary>Replace the queued image without tearing; falls back to <see cref="Fifo"/>.</summary>
	Mailbox,

	/// <summary>Present at once, tearing; falls back to <see cref="Mailbox"/> then <see cref="Fifo"/>.</summary>
	Immediate,
}

/// <summary>
/// Pipeline stages a barrier waits on or blocks. Values match <c>VkPipelineStageFlagBits2</c>.
/// </summary>
[Flags]
public enum PipelineStage : ulong
{
	/// <summary>No stage.</summary>
	None = 0,
	/// <summary>Before anything.</summary>
	TopOfPipe = 0x1,
	/// <summary>Reading indirect arguments.</summary>
	DrawIndirect = 0x2,
	/// <summary>Reading vertex and index buffers.</summary>
	VertexInput = 0x4,
	/// <summary>Vertex shaders.</summary>
	VertexShader = 0x8,
	/// <summary>Pixel shaders.</summary>
	FragmentShader = 0x80,
	/// <summary>Depth and stencil tests before shading.</summary>
	EarlyFragmentTests = 0x100,
	/// <summary>Depth and stencil tests after shading.</summary>
	LateFragmentTests = 0x200,
	/// <summary>Writing colour targets, resolves included.</summary>
	ColorTargetOutput = 0x400,
	/// <summary>Compute shaders.</summary>
	ComputeShader = 0x800,
	/// <summary>Copies, blits, resolves and clears.</summary>
	Transfer = 0x1000,
	/// <summary>After everything.</summary>
	BottomOfPipe = 0x2000,
	/// <summary>CPU access to mapped memory.</summary>
	Host = 0x4000,
	/// <summary>Every graphics stage.</summary>
	AllGraphics = 0x8000,
	/// <summary>Everything.</summary>
	AllCommands = 0x10000,
	/// <summary>Vertex and every other pre-rasterisation shader.</summary>
	PreRasterizationShaders = 0x4000000000,
	/// <summary>Task shaders.</summary>
	TaskShader = 0x80000,
	/// <summary>Mesh shaders.</summary>
	MeshShader = 0x100000,
}

/// <summary>
/// Memory accesses a barrier makes available or visible. Values match <c>VkAccessFlagBits2</c>.
/// </summary>
[Flags]
public enum MemoryAccess : ulong
{
	/// <summary>No access.</summary>
	None = 0,
	/// <summary>Reading indirect arguments.</summary>
	IndirectRead = 0x1,
	/// <summary>Reading indices.</summary>
	IndexRead = 0x2,
	/// <summary>Reading vertex attributes.</summary>
	VertexRead = 0x4,
	/// <summary>Reading constant buffers.</summary>
	UniformRead = 0x8,
	/// <summary>Shader reads: sampling, storage and buffer loads.</summary>
	ShaderRead = 0x20,
	/// <summary>Shader writes.</summary>
	ShaderWrite = 0x40,
	/// <summary>Reading colour targets (blending).</summary>
	ColorTargetRead = 0x80,
	/// <summary>Writing colour targets.</summary>
	ColorTargetWrite = 0x100,
	/// <summary>Reading depth and stencil.</summary>
	DepthTargetRead = 0x200,
	/// <summary>Writing depth and stencil.</summary>
	DepthTargetWrite = 0x400,
	/// <summary>Copy sources.</summary>
	TransferRead = 0x800,
	/// <summary>Copy and clear destinations.</summary>
	TransferWrite = 0x1000,
	/// <summary>CPU reads.</summary>
	HostRead = 0x2000,
	/// <summary>CPU writes.</summary>
	HostWrite = 0x4000,
	/// <summary>Any read.</summary>
	MemoryRead = 0x8000,
	/// <summary>Any write.</summary>
	MemoryWrite = 0x10000,
}

/// <summary>
/// How a texture's memory is laid out for what it's used for next.
/// </summary>
public enum ImageLayout
{
	/// <summary>Contents don't matter: the old layout of a texture about to be overwritten.</summary>
	Undefined,

	/// <summary>Any access; what storage textures are read and written in.</summary>
	General,

	/// <summary>Drawn to as a colour target.</summary>
	ColorTarget,

	/// <summary>Tested and written as depth.</summary>
	DepthTarget,

	/// <summary>Tested as depth and sampled at once.</summary>
	DepthReadOnly,

	/// <summary>Sampled by shaders.</summary>
	ShaderRead,

	/// <summary>The source of copies.</summary>
	CopySource,

	/// <summary>The destination of copies and clears.</summary>
	CopyDestination,

	/// <summary>Handed to the presentation engine.</summary>
	Present,
}
