using NativeEngine;
using System.Runtime.CompilerServices;

namespace Sandbox;

internal static partial class SandboxEngineExtensions
{
	/// <summary>Rounds down to a supported sample count, matching NumSamplesToRenderMultisampleType.</summary>
	internal static RenderMultisampleType ToEngineMultisampleType( this int samples ) => samples switch
	{
		< 2 => RenderMultisampleType.RENDER_MULTISAMPLE_NONE,
		< 4 => RenderMultisampleType.RENDER_MULTISAMPLE_2X,
		< 6 => RenderMultisampleType.RENDER_MULTISAMPLE_4X,
		< 8 => RenderMultisampleType.RENDER_MULTISAMPLE_6X,
		< 16 => RenderMultisampleType.RENDER_MULTISAMPLE_8X,
		_ => RenderMultisampleType.RENDER_MULTISAMPLE_16X
	};

	internal static RenderMultisampleType ToEngine( this MultisampleAmount self )
	{
		switch ( self )
		{
			case MultisampleAmount.MultisampleNone: return RenderMultisampleType.RENDER_MULTISAMPLE_NONE;
			case MultisampleAmount.Multisample2x: return RenderMultisampleType.RENDER_MULTISAMPLE_2X;
			case MultisampleAmount.Multisample4x: return RenderMultisampleType.RENDER_MULTISAMPLE_4X;
			case MultisampleAmount.Multisample6x: return RenderMultisampleType.RENDER_MULTISAMPLE_6X;
			case MultisampleAmount.Multisample8x: return RenderMultisampleType.RENDER_MULTISAMPLE_8X;
			case MultisampleAmount.Multisample16x: return RenderMultisampleType.RENDER_MULTISAMPLE_16X;
			default: return CSceneSystem.GetMainSwapChainMultisampleType(); // Fall back to what the main swapchain is using
		}
	}

	internal static MultisampleAmount FromEngine( this RenderMultisampleType self )
	{
		switch ( self )
		{
			case RenderMultisampleType.RENDER_MULTISAMPLE_NONE: return MultisampleAmount.MultisampleNone;
			case RenderMultisampleType.RENDER_MULTISAMPLE_2X: return MultisampleAmount.Multisample2x;
			case RenderMultisampleType.RENDER_MULTISAMPLE_4X: return MultisampleAmount.Multisample4x;
			case RenderMultisampleType.RENDER_MULTISAMPLE_6X: return MultisampleAmount.Multisample6x;
			case RenderMultisampleType.RENDER_MULTISAMPLE_8X: return MultisampleAmount.Multisample8x;
			case RenderMultisampleType.RENDER_MULTISAMPLE_16X: return MultisampleAmount.Multisample16x;
			default: throw new System.Exception( "Unknown multisample amount" );
		}
	}

	/// <summary>
	/// Convert to a FloatSpan, which allows easy SIMD/AVX2 instructions
	/// </summary>
	[MethodImpl( MethodImplOptions.AggressiveInlining )]
	public static FloatSpan AsFloatSpan( this Span<float> span ) => new FloatSpan( span );
}
