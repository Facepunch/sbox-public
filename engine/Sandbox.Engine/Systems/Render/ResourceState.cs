namespace Sandbox.Rendering;

/// <summary>
/// Used to describe a GPU resources state for barrier transitions.
/// </summary>
/// <remarks>
/// There are intended to be a high level generic of resource states that can be translated
/// to any lower level graphics APIs. (VK/D3D12/Metal)
/// </remarks>
[Flags]
[Expose]
public enum ResourceState
{
	Common = 0x0,
	Present = 0x0,
	VertexOrIndexBuffer = 0x1,
	// Unused = 0x2,
	RenderTarget = 0x4,
	UnorderedAccess = 0x8,
	DepthWrite = 0x10,
	DepthRead = 0x20,
	NonPixelShaderResource = 0x40,
	PixelShaderResource = 0x80,
	StreamOut = 0x100,
	IndirectArgument = 0x200,
	Predication = 0x200,
	CopyDestination = 0x400,
	CopySource = 0x800,
	ResolveDestination = 0x1000,
	ResolveSource = 0x2000,

	GenericRead = VertexOrIndexBuffer | NonPixelShaderResource | PixelShaderResource | IndirectArgument | CopySource,
	AllShaderResource = NonPixelShaderResource | PixelShaderResource,

	[Obsolete( "Use VertexOrIndexBuffer - or for constant buffer use NonPixelShaderResource" )]
	VertexAndConstantBuffer = 0x1,

	[Obsolete( "Use VertexOrIndexBuffer" )]
	IndexBuffer = 0x1,
}
