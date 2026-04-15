using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.UI;

/// <summary>
/// Renders descriptors that can't go through the instanced batch path.
/// Backdrops need framebuffer grabs.
/// </summary>
internal static class UIRenderer
{
	internal static void Draw( Span<BackdropDrawDescriptor> descriptors, CommandList commandList, bool reuseGrab = false )
	{
		for ( int i = 0; i < descriptors.Length; i++ )
		{
			ref var desc = ref descriptors[i];

			var attributes = commandList.Attributes;

			attributes.Set( "HasInverseScissor", 0 );
			attributes.SetCombo( "D_LAYERED", desc.IsLayered ? 1 : 0 );

			attributes.Set( "BoxPosition", desc.PanelRect.Position );
			attributes.Set( "BoxSize", desc.PanelRect.Size );
			attributes.Set( "BorderRadius", desc.BorderRadius );

			attributes.Set( "Brightness", desc.Brightness );
			attributes.Set( "Contrast", desc.Contrast );
			attributes.Set( "Saturate", desc.Saturate );
			attributes.Set( "Sepia", desc.Sepia );
			attributes.Set( "Invert", desc.Invert );
			attributes.Set( "HueRotate", desc.HueRotate );
			attributes.Set( "BlurScale", desc.BlurScale );

			attributes.SetCombo( "D_BLENDMODE", desc.OverrideBlendMode );

			if ( !reuseGrab )
				attributes.GrabFrameTexture( "FrameBufferCopyTexture", Graphics.DownsampleMethod.GaussianBlur );

			commandList.DrawQuad( desc.PanelRect, Material.UI.BackdropFilter, Color.White.WithAlpha( desc.Opacity ) );
		}

		var reset = commandList.Attributes;
		reset.Set( "Brightness", 1.0f );
		reset.Set( "Contrast", 1.0f );
		reset.Set( "Saturate", 1.0f );
		reset.Set( "Sepia", 0.0f );
		reset.Set( "Invert", 0.0f );
		reset.Set( "HueRotate", 0.0f );
		reset.Set( "BlurScale", 0.0f );
	}

}
