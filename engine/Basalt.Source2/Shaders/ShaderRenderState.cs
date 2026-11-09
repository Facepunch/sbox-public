using Basalt.Source2.Resources;

namespace Basalt.Source2.Shaders;

/// <summary>
/// A pixel shader dynamic combo's render state (<c>CVfxRenderStateInfo</c>). Each part is fixed by the shader, or null
/// when it's dynamic: then the defaults apply with render-state variables on top
/// (the shader variant does that).
/// </summary>
public sealed class ShaderRenderState
{
	/// <summary>Fill, cull, depth bias, depth clip; null when dynamic.</summary>
	public RasterizerDesc? Rasterizer { get; init; }

	/// <summary>Depth and stencil testing; null when dynamic.</summary>
	public DepthStencilDesc? DepthStencil { get; init; }

	/// <summary>Blending; null when dynamic.</summary>
	public BlendDesc Blend { get; init; }

	internal static ShaderRenderState Read( ByteReader reader )
	{
		var dynamicRasterizer = reader.Bool();
		var dynamicDepthStencil = reader.Bool();
		var dynamicBlend = reader.Bool();

		RasterizerDesc? rasterizer = null;
		if ( !dynamicRasterizer )
		{
			rasterizer = new RasterizerDesc
			{
				FillMode = reader.Byte(),
				CullMode = reader.Byte(),
				DepthClip = reader.Bool(),
				Multisample = reader.Bool(),
				DepthBias = reader.Int(),
				DepthBiasClamp = reader.Float(),
				SlopeScaledDepthBias = reader.Float(),
			};
		}

		DepthStencilDesc? depthStencil = null;
		if ( !dynamicDepthStencil )
		{
			var desc = new DepthStencilDesc
			{
				DepthTest = reader.Bool(),
				DepthWrite = reader.Bool(),
				DepthFunc = reader.Byte(),
			};

			var version = reader.Byte();
			reader.Byte();
			desc.StencilEnable = reader.Bool();
			desc.StencilReadMask = reader.Byte();
			desc.StencilWriteMask = reader.Byte();
			desc.FrontFail = reader.Byte();
			desc.FrontDepthFail = reader.Byte();
			desc.FrontPass = reader.Byte();
			desc.FrontFunc = reader.Byte();
			desc.BackFail = reader.Byte();
			desc.BackDepthFail = reader.Byte();
			desc.BackPass = reader.Byte();
			desc.BackFunc = reader.Byte();
			reader.Skip( 4 );

			// Shaders from before reverse-Z had their depth comparison flipped on load
			if ( version < 1 ) desc.DepthFunc = DepthStencilDesc.Reverse( desc.DepthFunc );
			depthStencil = desc;
		}

		BlendDesc blend = null;
		if ( !dynamicBlend )
		{
			blend = new BlendDesc { AlphaToCoverage = reader.Bool(), Independent = reader.Bool() };
			reader.Byte();
			for ( int i = 0; i < 8; i++ ) blend.Targets[i].Enable = reader.Bool();
			for ( int i = 0; i < 8; i++ ) blend.Targets[i].Source = reader.Byte();
			for ( int i = 0; i < 8; i++ ) blend.Targets[i].Destination = reader.Byte();
			for ( int i = 0; i < 8; i++ ) blend.Targets[i].Op = reader.Byte();
			for ( int i = 0; i < 8; i++ ) blend.Targets[i].SourceAlpha = reader.Byte();
			for ( int i = 0; i < 8; i++ ) blend.Targets[i].DestinationAlpha = reader.Byte();
			for ( int i = 0; i < 8; i++ ) blend.Targets[i].OpAlpha = reader.Byte();
			for ( int i = 0; i < 8; i++ ) blend.Targets[i].WriteMask = reader.Byte();
			reader.Skip( 8 );
		}

		return new ShaderRenderState { Rasterizer = rasterizer, DepthStencil = depthStencil, Blend = blend };
	}
}

/// <summary>
/// <c>RsRasterizerStateDesc_t</c>: fill (0 solid, 1 wireframe), cull (0 none, 1 back, 2 front), depth bias.
/// </summary>
public struct RasterizerDesc
{
#pragma warning disable CS1591
	public byte FillMode;
	public byte CullMode;
	public bool DepthClip;
	public bool Multisample;
	public int DepthBias;
	public float DepthBiasClamp;
	public float SlopeScaledDepthBias;
#pragma warning restore CS1591

	/// <summary><c>DefaultRasterizerStateDesc</c>: solid, back-face culled, depth clipped.</summary>
	public static RasterizerDesc Default => new() { CullMode = 1, DepthClip = true, Multisample = true };
}

/// <summary>
/// <c>RsDepthStencilStateDesc_t</c>. Comparisons are <c>RsComparison_t</c> (never, less, equal, less-equal, greater,
/// not-equal, greater-equal, always); stencil ops <c>RsStencilOp_t</c> (keep, zero, replace, incr-sat, decr-sat,
/// invert, incr, decr) - both the same order as Vulkan's.
/// </summary>
public struct DepthStencilDesc
{
#pragma warning disable CS1591
	public bool DepthTest;
	public bool DepthWrite;
	public byte DepthFunc;
	public bool StencilEnable;
	public byte StencilReadMask;
	public byte StencilWriteMask;
	public byte FrontFail, FrontDepthFail, FrontPass, FrontFunc;
	public byte BackFail, BackDepthFail, BackPass, BackFunc;
#pragma warning restore CS1591

	/// <summary><c>DefaultDepthStencilStateDesc</c>: test and write, greater-equal (reverse-Z), no stencil.</summary>
	public static DepthStencilDesc Default => new()
	{
		DepthTest = true,
		DepthWrite = true,
		DepthFunc = 6,
		StencilReadMask = 0xFF,
		StencilWriteMask = 0xFF,
		FrontFunc = 7,
		BackFunc = 7,
	};

	internal static byte Reverse( byte comparison ) => comparison switch
	{
		1 => 4,
		3 => 6,
		4 => 1,
		6 => 3,
		_ => comparison,
	};
}

/// <summary>
/// <c>RsBlendStateDesc_t</c>, per colour target. Blend modes are <c>RsBlendMode_t</c> (D3D11 order), ops
/// <c>RsBlendOp_t</c> (add, subtract, reverse subtract, min, max).
/// </summary>
public sealed class BlendDesc
{
	/// <summary>Alpha to coverage.</summary>
	public bool AlphaToCoverage;

	/// <summary>Whether targets blend differently.</summary>
	public bool Independent;

	/// <summary>The eight targets.</summary>
	public BlendTarget[] Targets = new BlendTarget[8];

	/// <summary><c>DefaultBlendStateDesc</c>: off, source-alpha over, writing everything.</summary>
	public static BlendDesc Default
	{
		get
		{
			var desc = new BlendDesc();
			for ( int i = 0; i < 8; i++ ) desc.Targets[i] = new BlendTarget { Source = 4, Destination = 5, SourceAlpha = 4, DestinationAlpha = 1, WriteMask = 15 };
			return desc;
		}
	}

	/// <summary>A copy.</summary>
	public BlendDesc Clone() => new() { AlphaToCoverage = AlphaToCoverage, Independent = Independent, Targets = (BlendTarget[])Targets.Clone() };
}

/// <summary>
/// One colour target's blending.
/// </summary>
public struct BlendTarget
{
#pragma warning disable CS1591
	public bool Enable;
	public byte Source, Destination, Op;
	public byte SourceAlpha, DestinationAlpha, OpAlpha;
	public byte WriteMask;
#pragma warning restore CS1591
}
