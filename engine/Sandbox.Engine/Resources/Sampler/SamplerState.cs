using NativeEngine;
using System.Collections.Concurrent;

namespace Sandbox.Rendering;

/// <summary>
/// Represents a sampler state used to control how textures are sampled in shaders.
/// Example usage: 
/// <code>
/// SamplerState mySampler &lt; Attribute("sampler"); &gt;;
/// </code>
/// 
/// C# binding:
/// <code>
/// var sampler = new SamplerState
/// {
///     Filter = FilterMode.Trilinear,
///     AddressModeU = TextureAddressMode.Wrap,
///     AddressModeV = TextureAddressMode.Wrap,
///     AddressModeW = TextureAddressMode.Clamp,
///     MaxAnisotropy = 4
/// };
///
/// Graphics.Attributes.Set("sampler", sampler);
/// </code>
/// </summary>
[Expose]
public record struct SamplerState
{
	/// <summary>
	/// The texture filtering mode used for sampling (e.g., point, bilinear, trilinear).
	/// </summary>
	[KeyProperty]
	public FilterMode Filter { get; set; } = FilterMode.Bilinear;

	/// <summary>
	/// The addressing mode used for the U (X) texture coordinate.
	/// </summary>
	public TextureAddressMode AddressModeU { get; set; } = TextureAddressMode.Wrap;

	/// <summary>
	/// The addressing mode used for the V texture coordinate.
	/// </summary>
	public TextureAddressMode AddressModeV { get; set; } = TextureAddressMode.Wrap;

	/// <summary>
	/// The addressing mode used for the W texture coordinate.
	/// </summary>
	public TextureAddressMode AddressModeW { get; set; } = TextureAddressMode.Wrap;

	/// <summary>
	/// The bias applied to the calculated mip level during texture sampling.
	/// Positive values make textures appear blurrier; negative values sharpen.
	/// </summary>
	public float MipLodBias { get; set; } = 0;

	/// <summary>
	/// The maximum anisotropy level used for anisotropic filtering.
	/// Higher values improve texture quality at oblique viewing angles.
	/// </summary>
	public int MaxAnisotropy { get; set; } = 8;

	/// <summary>
	/// Border color to use if <see cref="TextureAddressMode.Border"/> is specified for AddressU, AddressV, or AddressW.
	/// </summary>
	public Color BorderColor { get; set; } = Color.Transparent;

	public SamplerState() { }

	static internal ConcurrentDictionary<SamplerState, int> _cachedSamplerIndex = [];

	/// <summary>
	/// Gets or creates a bindless sampler index for this <see cref="SamplerState"/>.
	/// </summary>
	internal static int GetBindlessIndex( SamplerState samplerState )
	{
		if ( _cachedSamplerIndex.TryGetValue( samplerState, out int value ) )
		{
			return value; // We already know the bindless index
		}

		CSamplerStateDesc samplerStateDesc = new( samplerState );
		var handle = g_pRenderDevice.FindOrCreateSamplerState( samplerStateDesc );

		int samplerIndex = g_pRenderDevice.GetSamplerIndex( handle );
		_cachedSamplerIndex[samplerState] = samplerIndex;

		return samplerIndex;
	}
}
