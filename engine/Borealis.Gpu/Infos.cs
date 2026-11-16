using System.Numerics;

namespace Borealis.Gpu;

/// <summary>
/// A buffer to create (<see cref="Device.CreateBuffer"/>).
/// </summary>
public struct BufferInfo
{
	/// <summary>Size in bytes.</summary>
	public long Size;

	/// <summary>What it's used as. Copies to and from it are always allowed.</summary>
	public BufferUsage Usage;

	/// <summary>Where its memory lives.</summary>
	public MemoryLocation Memory;

	/// <summary>A name for debuggers and validation messages.</summary>
	public string Name;
}

/// <summary>
/// A texture to create (<see cref="Device.CreateTexture"/>).
/// </summary>
public struct TextureInfo
{
	/// <summary>Size in texels.</summary>
	public int Width, Height;

	/// <summary>Slices of a 3D texture; 1 otherwise.</summary>
	public int Depth;

	/// <summary>Array layers; a cube has 6 per cube, which <see cref="Layers"/> doesn't count.</summary>
	public int Layers;

	/// <summary>Mip levels; 1 for none.</summary>
	public int Mips;

	/// <summary>Samples per texel; 1 without MSAA.</summary>
	public int Samples;

	/// <summary>Its shape, which is also the dimension of its default view.</summary>
	public TextureDimension Dimension;

	/// <summary>Its format.</summary>
	public Format Format;

	/// <summary>What it's used as.</summary>
	public TextureUsage Usage;

	/// <summary>A name for debuggers and validation messages.</summary>
	public string Name;

	/// <summary>
	/// A sampled 2D texture that's filled by copies.
	/// </summary>
	public static TextureInfo Texture2D( int width, int height, Format format, int mips = 1, string name = null ) => new()
	{
		Width = width,
		Height = height,
		Depth = 1,
		Layers = 1,
		Mips = mips,
		Samples = 1,
		Dimension = TextureDimension.Texture2D,
		Format = format,
		Usage = TextureUsage.Sampled | TextureUsage.CopyDestination | TextureUsage.CopySource,
		Name = name,
	};

	/// <summary>
	/// A 2D colour or depth target (by its format) that can also be sampled and copied.
	/// </summary>
	public static TextureInfo RenderTarget( int width, int height, Format format, int samples = 1, string name = null ) => new()
	{
		Width = width,
		Height = height,
		Depth = 1,
		Layers = 1,
		Mips = 1,
		Samples = samples,
		Dimension = TextureDimension.Texture2D,
		Format = format,
		Usage = (format.IsDepth() ? TextureUsage.DepthTarget : TextureUsage.ColorTarget) | TextureUsage.CopySource | TextureUsage.CopyDestination
			| (samples == 1 ? TextureUsage.Sampled : TextureUsage.None),
		Name = name,
	};
}

/// <summary>
/// Another view of a texture (<see cref="Device.CreateView"/>).
/// </summary>
public struct TextureViewInfo
{
	/// <summary>The view's shape.</summary>
	public TextureDimension Dimension;

	/// <summary>Its format, or <see cref="Format.Unknown"/> for the texture's (linear form).</summary>
	public Format Format;

	/// <summary>The first mip it sees.</summary>
	public int BaseMip;

	/// <summary>How many mips it sees; 0 for the rest.</summary>
	public int MipCount;

	/// <summary>The first layer (cube face, array slice) it sees.</summary>
	public int BaseLayer;

	/// <summary>How many layers it sees; 0 for the rest.</summary>
	public int LayerCount;
}

/// <summary>
/// A sampler to create (<see cref="Device.CreateSampler"/>).
/// </summary>
public struct SamplerInfo
{
	/// <summary>Filtering when minified, magnified and between mips.</summary>
	public Filter MinFilter, MagFilter, MipFilter;

	/// <summary>What sampling outside 0..1 reads, per axis.</summary>
	public AddressMode AddressU, AddressV, AddressW;

	/// <summary>Anisotropic filtering, 1 for none; clamped to what the device supports.</summary>
	public float MaxAnisotropy;

	/// <summary>A comparison sampler's comparison, or <see cref="CompareFunction.Never"/> for an ordinary sampler.</summary>
	public CompareFunction Compare;

	/// <summary>The mip range sampled, and a bias added to the computed mip.</summary>
	public float MinLod, MaxLod, MipBias;

	/// <summary>
	/// A trilinear sampler with the same address mode on every axis.
	/// </summary>
	public static SamplerInfo Linear( AddressMode address = AddressMode.Wrap, float anisotropy = 1 ) => new()
	{
		MinFilter = Filter.Linear,
		MagFilter = Filter.Linear,
		MipFilter = Filter.Linear,
		AddressU = address,
		AddressV = address,
		AddressW = address,
		MaxAnisotropy = anisotropy,
		MaxLod = 1000,
	};

	/// <summary>
	/// A point sampler with the same address mode on every axis.
	/// </summary>
	public static SamplerInfo Point( AddressMode address = AddressMode.Clamp ) => new()
	{
		AddressU = address,
		AddressV = address,
		AddressW = address,
		MaxAnisotropy = 1,
		MaxLod = 1000,
	};
}

/// <summary>
/// One binding in a descriptor set layout (<see cref="Device.CreateBindingLayout"/>).
/// </summary>
public record struct BindingSlot( int Binding, BindingType Type, ShaderStages Stages, int Count = 1 );

/// <summary>
/// A vertex buffer a pipeline reads.
/// </summary>
public record struct VertexBufferLayout( int Stride, VertexStepRate StepRate = VertexStepRate.Vertex );

/// <summary>
/// A vertex shader input: its location, the vertex buffer it comes from and where in each element.
/// </summary>
public record struct VertexAttribute( int Location, int Buffer, VertexFormat Format, int Offset );

/// <summary>
/// How a pipeline blends into one colour target.
/// </summary>
public record struct BlendState
{
	/// <summary>Whether it blends at all.</summary>
	public bool Enable;

	/// <summary>Colour blending.</summary>
	public BlendFactor SourceColor, DestinationColor;

	/// <summary>Colour blend operation.</summary>
	public BlendOp ColorOp;

	/// <summary>Alpha blending.</summary>
	public BlendFactor SourceAlpha, DestinationAlpha;

	/// <summary>Alpha blend operation.</summary>
	public BlendOp AlphaOp;

	/// <summary>Channels written, RGBA in bits 0-3; 0 means all, <see cref="NoChannels"/> none.</summary>
	public int WriteMask;

	/// <summary>A <see cref="WriteMask"/> that writes no channel.</summary>
	public const int NoChannels = 0x10;

	/// <summary>No blending.</summary>
	public static BlendState Opaque => default;

	/// <summary>Nothing written: a target the shader has no output for.</summary>
	public static BlendState NoWrites => new() { WriteMask = NoChannels };

	/// <summary>Premultiplied-alpha-style "over" blending of straight alpha.</summary>
	public static BlendState AlphaBlend => new()
	{
		Enable = true,
		SourceColor = BlendFactor.SourceAlpha,
		DestinationColor = BlendFactor.OneMinusSourceAlpha,
		SourceAlpha = BlendFactor.One,
		DestinationAlpha = BlendFactor.OneMinusSourceAlpha,
	};

	/// <summary>Adding.</summary>
	public static BlendState Additive => new()
	{
		Enable = true,
		SourceColor = BlendFactor.One,
		DestinationColor = BlendFactor.One,
		SourceAlpha = BlendFactor.One,
		DestinationAlpha = BlendFactor.One,
	};
}

/// <summary>
/// A specialization constant: the <c>constant_id</c> a shader declares and the 32-bit value it gets in a pipeline.
/// </summary>
public record struct SpecializationConstant( uint Id, uint Value );

/// <summary>
/// Stencil testing for one face.
/// </summary>
public record struct StencilFace( StencilOp Fail, StencilOp Pass, StencilOp DepthFail, CompareFunction Compare );

/// <summary>
/// A graphics pipeline to create (<see cref="Device.CreateGraphicsPipeline"/>). Viewport, scissor, depth bias and the
/// stencil reference are set while recording.
/// </summary>
public struct GraphicsPipelineInfo
{
	/// <summary>The vertex shader and its entry point (default <c>main</c>).</summary>
	public ShaderHandle VertexShader;

	/// <summary>The vertex shader's entry point; null for <c>main</c>.</summary>
	public string VertexEntry;

	/// <summary>The pixel shader, or none for depth-only drawing.</summary>
	public ShaderHandle FragmentShader;

	/// <summary>The pixel shader's entry point; null for <c>main</c>.</summary>
	public string FragmentEntry;

	/// <summary>Specialization constants for the vertex shader; null for none.</summary>
	public SpecializationConstant[] VertexConstants;

	/// <summary>Specialization constants for the pixel shader; null for none.</summary>
	public SpecializationConstant[] FragmentConstants;

	/// <summary>
	/// A mesh shader in place of the vertex shader and vertex input (<see cref="DeviceCapabilities.MeshShaders"/>): drawn with
	/// <see cref="CommandList.DrawMeshTasksIndirect"/>; <see cref="VertexShader"/>, <see cref="VertexBuffers"/>,
	/// <see cref="Attributes"/> and <see cref="Topology"/> are unused then.
	/// </summary>
	public ShaderHandle MeshShader;

	/// <summary>The mesh shader's entry point; null for <c>main</c>.</summary>
	public string MeshEntry;

	/// <summary>Specialization constants for the mesh shader; null for none.</summary>
	public SpecializationConstant[] MeshConstants;

	/// <summary>The task shader that launches the mesh shader's groups, or none (with a mesh shader only).</summary>
	public ShaderHandle TaskShader;

	/// <summary>The task shader's entry point; null for <c>main</c>.</summary>
	public string TaskEntry;

	/// <summary>Specialization constants for the task shader; null for none.</summary>
	public SpecializationConstant[] TaskConstants;

	/// <summary>Layouts of descriptor sets 0 to 3; missing or default ones are empty. Set 4 is always the bindless set.</summary>
	public BindingLayoutHandle[] Sets;

	/// <summary>Vertex buffers, by binding.</summary>
	public VertexBufferLayout[] VertexBuffers;

	/// <summary>Vertex shader inputs.</summary>
	public VertexAttribute[] Attributes;

	/// <summary>How vertices are assembled.</summary>
	public PrimitiveTopology Topology;

	/// <summary>Face culling.</summary>
	public CullMode CullMode;

	/// <summary>Which winding is the front.</summary>
	public FrontFace FrontFace;

	/// <summary>Draw lines instead of filled triangles.</summary>
	public bool Wireframe;

	/// <summary>Clamp depth instead of clipping it (shadow pancaking).</summary>
	public bool DepthClamp;

	/// <summary>Whether depth bias applies (its values are set while recording).</summary>
	public bool DepthBias;

	/// <summary>Whether depth is tested.</summary>
	public bool DepthTest;

	/// <summary>Whether depth is written.</summary>
	public bool DepthWrite;

	/// <summary>The depth test. Borealis renders reverse-Z, so usually <see cref="CompareFunction.GreaterEqual"/>.</summary>
	public CompareFunction DepthCompare;

	/// <summary>Whether stencil is tested.</summary>
	public bool StencilTest;

	/// <summary>Stencil ops for front and back faces.</summary>
	public StencilFace StencilFront, StencilBack;

	/// <summary>Stencil read and write masks.</summary>
	public byte StencilReadMask, StencilWriteMask;

	/// <summary>Blending per colour target; one entry applies to every target.</summary>
	public BlendState[] Blend;

	/// <summary>Write coverage from alpha.</summary>
	public bool AlphaToCoverage;

	/// <summary>The colour targets' formats.</summary>
	public Format[] ColorFormats;

	/// <summary>The depth target's format, or <see cref="Format.Unknown"/> for none.</summary>
	public Format DepthFormat;

	/// <summary>Samples per pixel of the targets.</summary>
	public int Samples;

	/// <summary>A name for debuggers and validation messages.</summary>
	public string Name;
}

/// <summary>
/// A compute pipeline to create (<see cref="Device.CreateComputePipeline"/>).
/// </summary>
public struct ComputePipelineInfo
{
	/// <summary>The compute shader.</summary>
	public ShaderHandle Shader;

	/// <summary>Its entry point; null for <c>main</c>.</summary>
	public string Entry;

	/// <summary>Specialization constants; null for none.</summary>
	public SpecializationConstant[] Constants;

	/// <summary>Layouts of descriptor sets 0 to 3; missing or default ones are empty. Set 4 is always the bindless set.</summary>
	public BindingLayoutHandle[] Sets;

	/// <summary>A name for debuggers and validation messages.</summary>
	public string Name;
}

/// <summary>
/// One resource bound into a transient descriptor set (<see cref="CommandList.SetBindings"/>).
/// </summary>
public struct Binding
{
	/// <summary>The binding number.</summary>
	public int Slot;

	/// <summary>The element of an arrayed binding.</summary>
	public int ArrayElement;

	/// <summary>What's bound.</summary>
	public BindingType Type;

	/// <summary>A buffer, for buffer bindings.</summary>
	public BufferHandle Buffer;

	/// <summary>Where in the buffer, and how much; 0 size for the rest.</summary>
	public long Offset, Size;

	/// <summary>A texture's default view (or its sRGB view, with <see cref="Srgb"/>), for texture bindings.</summary>
	public TextureHandle Texture;

	/// <summary>A view, instead of <see cref="Texture"/>.</summary>
	public TextureViewHandle View;

	/// <summary>Use the texture's sRGB view.</summary>
	public bool Srgb;

	/// <summary>A sampler, for sampler bindings.</summary>
	public SamplerHandle Sampler;

	/// <summary>
	/// The layout a texture is in while it's read through this binding; <see cref="ImageLayout.Undefined"/> for the usual
	/// one (<see cref="ImageLayout.ShaderRead"/> sampled, <see cref="ImageLayout.General"/> for storage).
	/// </summary>
	public ImageLayout Layout;

	/// <summary>A constant buffer.</summary>
	public static Binding Uniform( int slot, BufferHandle buffer, long offset = 0, long size = 0 ) => new() { Slot = slot, Type = BindingType.UniformBuffer, Buffer = buffer, Offset = offset, Size = size };

	/// <summary>A storage buffer.</summary>
	public static Binding Storage( int slot, BufferHandle buffer, long offset = 0, long size = 0 ) => new() { Slot = slot, Type = BindingType.StorageBuffer, Buffer = buffer, Offset = offset, Size = size };

	/// <summary>A sampled texture, through its default or sRGB view.</summary>
	public static Binding Sampled( int slot, TextureHandle texture, bool srgb = false ) => new() { Slot = slot, Type = BindingType.SampledTexture, Texture = texture, Srgb = srgb };

	/// <summary>A sampled view.</summary>
	public static Binding Sampled( int slot, TextureViewHandle view ) => new() { Slot = slot, Type = BindingType.SampledTexture, View = view };

	/// <summary>A storage texture, through its default view (mip 0 for a texture with mips: use a view for others).</summary>
	public static Binding StorageTexture( int slot, TextureHandle texture ) => new() { Slot = slot, Type = BindingType.StorageTexture, Texture = texture };

	/// <summary>A storage view.</summary>
	public static Binding StorageTexture( int slot, TextureViewHandle view ) => new() { Slot = slot, Type = BindingType.StorageTexture, View = view };

	/// <summary>A sampler.</summary>
	public static Binding SamplerState( int slot, SamplerHandle sampler ) => new() { Slot = slot, Type = BindingType.Sampler, Sampler = sampler };

	/// <summary>Whether it binds the same as <paramref name="other"/>, field for field.</summary>
	internal readonly bool Same( in Binding other ) => Slot == other.Slot && ArrayElement == other.ArrayElement && Type == other.Type
		&& Buffer == other.Buffer && Offset == other.Offset && Size == other.Size && Texture == other.Texture && View == other.View
		&& Srgb == other.Srgb && Sampler == other.Sampler && Layout == other.Layout;
}

/// <summary>
/// A colour target of a rendering scope (<see cref="CommandList.BeginRendering"/>). It must be in
/// <see cref="ImageLayout.ColorTarget"/>.
/// </summary>
public struct ColorTarget
{
	/// <summary>The texture drawn to.</summary>
	public TextureHandle Texture;

	/// <summary>The mip and layer (cube face, slice) drawn to.</summary>
	public int Mip, Layer;

	/// <summary>Write through the sRGB view, encoding linear output.</summary>
	public bool Srgb;

	/// <summary>What it holds when rendering starts.</summary>
	public LoadOp Load;

	/// <summary>Whether it keeps what's drawn.</summary>
	public StoreOp Store;

	/// <summary>The clear colour, for <see cref="LoadOp.Clear"/>.</summary>
	public Vector4 ClearColor;

	/// <summary>A single-sample texture to resolve into at the end, in <see cref="ImageLayout.ColorTarget"/>; none for no resolve.</summary>
	public TextureHandle ResolveTarget;

	/// <summary>
	/// Draw to <paramref name="texture"/>, clearing it to <paramref name="clear"/> first or loading it.
	/// </summary>
	public static ColorTarget Of( TextureHandle texture, Vector4? clear = null, bool srgb = false ) => new()
	{
		Texture = texture,
		Srgb = srgb,
		Load = clear.HasValue ? LoadOp.Clear : LoadOp.Load,
		Store = StoreOp.Store,
		ClearColor = clear ?? default,
	};
}

/// <summary>
/// The depth target of a rendering scope (<see cref="CommandList.BeginRendering"/>). It must be in
/// <see cref="ImageLayout.DepthTarget"/>, or <see cref="ImageLayout.DepthReadOnly"/> when <see cref="ReadOnly"/>.
/// </summary>
public struct DepthTarget
{
	/// <summary>The texture, or none for no depth.</summary>
	public TextureHandle Texture;

	/// <summary>The mip and layer drawn to.</summary>
	public int Mip, Layer;

	/// <summary>What it holds when rendering starts.</summary>
	public LoadOp Load;

	/// <summary>Whether it keeps what's drawn.</summary>
	public StoreOp Store;

	/// <summary>The clear depth and stencil, for <see cref="LoadOp.Clear"/>.</summary>
	public float ClearDepth;

	/// <summary>The clear stencil, for <see cref="LoadOp.Clear"/>.</summary>
	public byte ClearStencil;

	/// <summary>Only tested, not written: it can be sampled at the same time.</summary>
	public bool ReadOnly;

	/// <summary>
	/// Draw to <paramref name="texture"/>'s depth, clearing it to <paramref name="clear"/> first or loading it.
	/// </summary>
	public static DepthTarget Of( TextureHandle texture, float? clear = null ) => new()
	{
		Texture = texture,
		Load = clear.HasValue ? LoadOp.Clear : LoadOp.Load,
		Store = StoreOp.Store,
		ClearDepth = clear ?? 0,
	};
}

/// <summary>
/// Orders accesses to a texture and moves it between layouts (<see cref="CommandList.Barrier(in TextureBarrier)"/>).
/// </summary>
public struct TextureBarrier
{
	/// <summary>The texture.</summary>
	public TextureHandle Texture;

	/// <summary>The stages and accesses that came before, which later work waits for.</summary>
	public PipelineStage SourceStage;

	/// <summary>The accesses that came before.</summary>
	public MemoryAccess SourceAccess;

	/// <summary>The stages and accesses that come after, which wait.</summary>
	public PipelineStage DestinationStage;

	/// <summary>The accesses that come after.</summary>
	public MemoryAccess DestinationAccess;

	/// <summary>The layout it's in, or <see cref="ImageLayout.Undefined"/> to discard its contents.</summary>
	public ImageLayout OldLayout;

	/// <summary>The layout it moves to.</summary>
	public ImageLayout NewLayout;

	/// <summary>The mips affected; 0 count for the rest.</summary>
	public int BaseMip, MipCount;

	/// <summary>The layers affected; 0 count for the rest.</summary>
	public int BaseLayer, LayerCount;

	/// <summary>
	/// A whole-texture transition.
	/// </summary>
	public static TextureBarrier Transition( TextureHandle texture, ImageLayout from, ImageLayout to, PipelineStage sourceStage, MemoryAccess sourceAccess, PipelineStage destinationStage, MemoryAccess destinationAccess ) => new()
	{
		Texture = texture,
		OldLayout = from,
		NewLayout = to,
		SourceStage = sourceStage,
		SourceAccess = sourceAccess,
		DestinationStage = destinationStage,
		DestinationAccess = destinationAccess,
	};
}

/// <summary>
/// Orders accesses to (part of) a buffer (<see cref="CommandList.Barrier(in BufferBarrier)"/>).
/// </summary>
public struct BufferBarrier
{
	/// <summary>The buffer.</summary>
	public BufferHandle Buffer;

	/// <summary>The range affected; 0 size for the rest.</summary>
	public long Offset, Size;

	/// <summary>The stages and accesses that came before.</summary>
	public PipelineStage SourceStage;

	/// <summary>The accesses that came before.</summary>
	public MemoryAccess SourceAccess;

	/// <summary>The stages and accesses that come after.</summary>
	public PipelineStage DestinationStage;

	/// <summary>The accesses that come after.</summary>
	public MemoryAccess DestinationAccess;
}
