namespace Sandbox.Rendering;

/// <summary>
/// A render target handle used with CommandLists
/// </summary>
public ref struct RenderTargetHandle
{
	public string Name { get; internal set; }

	/// <summary>
	/// Reference to the color texture of this target
	/// </summary>
	public readonly ColorTextureRef ColorTexture => new ColorTextureRef { Name = Name };

	/// <summary>
	/// Reference to the index of the color texture of this target
	/// </summary>
	public readonly ColorIndexRef ColorIndex => new ColorIndexRef { Name = Name };

	/// <summary>
	/// Reference to the size of the texture
	/// </summary>
	public readonly SizeHandle Size => new SizeHandle { Name = Name };

	public ref struct ColorTextureRef
	{
		public string Name { get; internal set; }
	}

	public ref struct ColorIndexRef
	{
		public string Name { get; internal set; }
	}

	public ref struct SizeHandle
	{
		public string Name { get; internal set; }

		public int Divisor { get; internal set; }
	}

}
