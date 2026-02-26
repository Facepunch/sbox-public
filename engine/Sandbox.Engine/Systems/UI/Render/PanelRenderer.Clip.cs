using Sandbox.Engine;
using Sandbox.Rendering;

namespace Sandbox.UI;

internal partial class PanelRenderer
{
	/// <summary>
	/// Software scissor, panels outside of this should not be rendered
	/// </summary>
	internal Rect Scissor;

	/// <summary>
	/// Scissor passed to gpu shader to be transformed
	/// </summary>
	internal GPUScissor ScissorGPU;
	internal struct GPUScissor
	{
		public Rect Rect;
		public Vector4 CornerRadius;
		public Matrix Matrix;
	}

	/// <summary>
	/// Scope that updates the renderer's scissor state for child panels to inherit.
	/// Does NOT modify any command lists - those are set up separately in BuildCommandList.
	/// </summary>
	internal class ClipScope : IDisposable
	{
		Rect Previous;
		GPUScissor PreviousGPU;

		public ClipScope( Rect scissorRect, Vector4 cornerRadius, Matrix globalMatrix )
		{
			var renderer = GlobalContext.Current.UISystem.Renderer;

			Previous = renderer.Scissor;
			PreviousGPU = renderer.ScissorGPU;

			renderer.ScissorGPU.Rect = new Rect()
			{
				Left = Math.Max( scissorRect.Left, PreviousGPU.Rect.Left ),
				Top = Math.Max( scissorRect.Top, PreviousGPU.Rect.Top ),
				Right = Math.Min( scissorRect.Right, PreviousGPU.Rect.Right ),
				Bottom = Math.Min( scissorRect.Bottom, PreviousGPU.Rect.Bottom ),
			};

			renderer.ScissorGPU.CornerRadius = cornerRadius;
			renderer.ScissorGPU.Matrix = globalMatrix;

			var tl = globalMatrix.Transform( scissorRect.TopLeft );
			var tr = globalMatrix.Transform( scissorRect.TopRight );
			var bl = globalMatrix.Transform( scissorRect.BottomLeft );
			var br = globalMatrix.Transform( scissorRect.BottomRight );

			var min = Vector2.Min( Vector2.Min( tl, tr ), Vector2.Min( bl, br ) );
			var max = Vector2.Max( Vector2.Max( tl, tr ), Vector2.Max( bl, br ) );

			scissorRect = new Rect( min, max - min );

			renderer.Scissor = new Rect()
			{
				Left = Math.Max( scissorRect.Left, Previous.Left ),
				Top = Math.Max( scissorRect.Top, Previous.Top ),
				Right = Math.Min( scissorRect.Right, Previous.Right ),
				Bottom = Math.Min( scissorRect.Bottom, Previous.Bottom ),
			};
		}

		public void Dispose()
		{
			var renderer = GlobalContext.Current.UISystem.Renderer;
			renderer.Scissor = Previous;
			renderer.ScissorGPU = PreviousGPU;
		}
	}

	/// <summary>
	/// Create a clip scope for a panel's children. This updates the renderer's scissor state
	/// so child panels will inherit the correct scissor when their command lists are built.
	/// </summary>
	public ClipScope Clip( Panel panel )
	{
		if ( (panel.ComputedStyle?.Overflow ?? OverflowMode.Visible) == OverflowMode.Visible )
			return null;

		var size = (panel.Box.Rect.Width + panel.Box.Rect.Height) * 0.5f;
		var borderRadius = new Vector4( panel.ComputedStyle.BorderTopLeftRadius?.GetPixels( size ) ?? 0, panel.ComputedStyle.BorderTopRightRadius?.GetPixels( size ) ?? 0, panel.ComputedStyle.BorderBottomLeftRadius?.GetPixels( size ) ?? 0, panel.ComputedStyle.BorderBottomRightRadius?.GetPixels( size ) ?? 0 );

		return new ClipScope( panel.Box.ClipRect, borderRadius, panel.GlobalMatrix ?? Matrix.Identity );
	}

	static void SetScissorAttributes( CommandList commandList, GPUScissor scissor )
	{
		if ( scissor.Rect.Width == 0 && scissor.Rect.Height == 0 )
		{
			commandList.Attributes.Set( "HasScissor", 0 );
			return;
		}

		commandList.Attributes.Set( "ScissorRect", scissor.Rect.ToVector4() );
		commandList.Attributes.Set( "ScissorCornerRadius", scissor.CornerRadius );
		commandList.Attributes.Set( "ScissorTransformMat", scissor.Matrix );
		commandList.Attributes.Set( "HasScissor", 1 );
	}

	void InitScissor( Rect rect, CommandList commandList )
	{
		Scissor = rect;
		ScissorGPU = new() { Rect = rect, Matrix = Matrix.Identity };

		SetScissorAttributes( commandList, ScissorGPU );
	}

	/// <summary>
	/// Quick check to see if a panel should be culled based on the current scissor
	/// </summary>
	bool ShouldEarlyCull( Panel panel )
	{
		//
		// This shit should be fast, so don't do complicated shit here
		// Keep it simple AABB, doesn't matter if we miss some overflow because the shader will clear up anything else
		//

		//
		// Can't clip contents panels
		//
		if ( panel.ComputedStyle.Display == DisplayMode.Contents )
			return false;

		var rect = panel.Box.Rect;

		//
		// Grow our rect by any shadows we might have
		//
		if ( panel.ComputedStyle.BoxShadow is ShadowList shadows && shadows.Count > 0 )
		{
			for ( int i = 0; i < shadows.Count; i++ )
			{
				var shadow = shadows[i];
				if ( shadow.Inset ) continue;

				var shadowRect = panel.Box.Rect + new Vector2( shadow.OffsetX, shadow.OffsetY );
				rect.Add( shadowRect.Grow( shadow.Spread ) );
			}
		}

		//
		// AABB transform
		//
		if ( panel.GlobalMatrix.HasValue )
		{
			var mat = panel.GlobalMatrix.Value;
			var tl = mat.Transform( rect.TopLeft );
			var tr = mat.Transform( rect.TopRight );
			var bl = mat.Transform( rect.BottomLeft );
			var br = mat.Transform( rect.BottomRight );

			var min = Vector2.Min( Vector2.Min( tl, tr ), Vector2.Min( bl, br ) );
			var max = Vector2.Max( Vector2.Max( tl, tr ), Vector2.Max( bl, br ) );

			rect = new Rect( min, max - min );
		}

		return !Scissor.IsInside( rect );
	}
}
