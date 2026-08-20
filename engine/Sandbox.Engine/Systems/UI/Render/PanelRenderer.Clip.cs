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
	/// Accumulated clip rect from <see cref="OverflowMode.ClipWhole"/> ancestors.
	/// Any panel whose bounds extend outside this rect will be skipped entirely.
	/// Null when no clip-whole ancestor is active.
	/// </summary>
	internal Rect? ClipWholeRect;

	internal bool IsOutsideClipWholeRect( Rect r )
	{
		if ( !ClipWholeRect.HasValue ) return false;
		var clip = ClipWholeRect.Value;
		return r.Left < clip.Left || r.Top < clip.Top || r.Right > clip.Right || r.Bottom > clip.Bottom;
	}

	/// <summary>
	/// Accumulated clip rect from <see cref="OverflowMode.Scroll"/> and <see cref="OverflowMode.Hidden"/> ancestors.
	/// Any panel whose bounds lie completely outside this rect will be skipped entirely (early cull).
	/// Uses an overlap test so partially-visible panels still render.
	/// Null when no scroll/hidden ancestor is active.
	/// </summary>
	internal Rect? ScrollCullRect;

	internal bool IsOutsideScrollCullRect( Rect r )
	{
		if ( !ScrollCullRect.HasValue ) return false;
		var clip = ScrollCullRect.Value;
		return r.Right <= clip.Left || r.Bottom <= clip.Top || r.Left >= clip.Right || r.Top >= clip.Bottom;
	}

	/// <summary>
	/// The clip in effect while building, inherited by child panels
	/// </summary>
	internal GPUScissor ScissorGPU;

	/// <summary>
	/// A stack of rounded rects a pixel has to be inside all of. Each one lives in the layout space of the
	/// panel that clips, reached from screen space through its matrix, so nested rounded clips all keep their
	/// corners. Plain rects in the same space merge into one entry; past MaxClips the newest is merged into the
	/// top one, which loses its corners but never lets anything through.
	/// </summary>
	internal struct GPUScissor
	{
		public const int MaxClips = 4;

		public struct Clip
		{
			public Rect Rect;
			public BorderRadii Radii;
			public Matrix Matrix;
		}

		[System.Runtime.CompilerServices.InlineArray( MaxClips )]
		public struct ClipList
		{
			Clip _element;
		}

		public ClipList Clips;
		public int Count;

		/// <summary>
		/// Keep what's outside instead - box-shadows use this to stay out of their panel
		/// </summary>
		public bool Invert;

		public readonly bool IsEmpty => Count == 0;

		public static GPUScissor Single( in Rect rect, in BorderRadii radii, in Matrix matrix, bool invert = false )
		{
			var s = new GPUScissor { Invert = invert };
			s.Push( rect, radii, matrix );
			return s;
		}

		public void Push( in Rect rect, in BorderRadii radii, in Matrix matrix )
		{
			if ( Count > 0 )
			{
				ref var top = ref Clips[Count - 1];

				var mergeable = top.Radii.IsZero && radii.IsZero && top.Matrix == matrix;
				if ( mergeable || Count == MaxClips )
				{
					top.Rect = Intersect( top.Rect, rect );
					return;
				}
			}

			Clips[Count++] = new Clip { Rect = rect, Radii = radii, Matrix = matrix };
		}

		/// <summary>
		/// Panel layers draw in their own space, where layout space is pixel space
		/// </summary>
		public void ClearMatrices()
		{
			for ( int i = 0; i < Count; i++ )
				Clips[i].Matrix = Matrix.Identity;
		}

		public readonly int GetHash()
		{
			var hash = HashCode.Combine( Count, Invert );
			for ( int i = 0; i < Count; i++ )
			{
				ref readonly var c = ref Clips[i];
				hash = HashCode.Combine( hash, c.Rect, c.Radii.TopLeft, c.Radii.TopRight, c.Radii.BottomLeft, c.Radii.BottomRight, c.Matrix );
			}
			return hash;
		}

		static Rect Intersect( in Rect a, in Rect b )
		{
			return new Rect()
			{
				Left = Math.Max( a.Left, b.Left ),
				Top = Math.Max( a.Top, b.Top ),
				Right = Math.Min( a.Right, b.Right ),
				Bottom = Math.Min( a.Bottom, b.Bottom ),
			};
		}
	}

	/// <summary>
	/// Scope that updates the renderer's scissor state for child panels to inherit.
	/// Does NOT modify any command lists - those are set up separately in BuildCommandList.
	/// </summary>
	internal ref struct ClipScope
	{
		Rect Previous;
		GPUScissor PreviousGPU;
		bool _disposed = false; // Whether this scope actually set a new scissor or not. If the panel had overflow: visible then we won't set a new scissor, so we can skip restoring the old one.

		public ClipScope( Rect scissorRect, BorderRadii radii, Matrix globalMatrix )
		{
			_disposed = true;

			var renderer = GlobalContext.Current.UISystem.Renderer;

			Previous = renderer.Scissor;
			PreviousGPU = renderer.ScissorGPU;

			renderer.ScissorGPU.Push( scissorRect, radii, globalMatrix );

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
			if ( !_disposed ) return;
			_disposed = false;

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
		var overflow = panel.ComputedStyle?.Overflow ?? OverflowMode.Visible;
		if ( overflow == OverflowMode.Visible || overflow == OverflowMode.ClipWhole ) return default;

		// Overflow clips to the padding box, whose corners are the border radii less the border
		var rect = panel.Box.Rect;
		var size = (rect.Width + rect.Height) * 0.5f;
		var radii = BorderRadii.FromStyle( panel.ComputedStyle, rect ).Inner( GetBorderWidths( panel.ComputedStyle, size ) );

		return new ClipScope( panel.Box.ClipRect, radii, panel.GlobalMatrix ?? Matrix.Identity );
	}

	static readonly string[] ScissorRectAttribute = ["ScissorRect0", "ScissorRect1", "ScissorRect2", "ScissorRect3"];
	static readonly string[] ScissorRadiiHAttribute = ["ScissorRadiiH0", "ScissorRadiiH1", "ScissorRadiiH2", "ScissorRadiiH3"];
	static readonly string[] ScissorRadiiVAttribute = ["ScissorRadiiV0", "ScissorRadiiV1", "ScissorRadiiV2", "ScissorRadiiV3"];
	static readonly string[] ScissorMatAttribute = ["ScissorMat0", "ScissorMat1", "ScissorMat2", "ScissorMat3"];

	/// <summary>
	/// The clip stack for shaders that draw one quad at a time, see ui/scissor.hlsl. Invert isn't carried - only
	/// the batched shadow path uses it.
	/// </summary>
	internal static void SetScissorAttributes( CommandList commandList, in GPUScissor scissor )
	{
		commandList.Attributes.Set( "HasScissor", scissor.Count > 0 ? 1 : 0 );
		commandList.Attributes.Set( "ScissorCount", scissor.Count );

		for ( int i = 0; i < scissor.Count; i++ )
		{
			ref readonly var c = ref scissor.Clips[i];
			commandList.Attributes.Set( ScissorRectAttribute[i], c.Rect.ToVector4() );
			commandList.Attributes.Set( ScissorRadiiHAttribute[i], c.Radii.Horizontal );
			commandList.Attributes.Set( ScissorRadiiVAttribute[i], c.Radii.Vertical );
			commandList.Attributes.Set( ScissorMatAttribute[i], c.Matrix );
		}
	}

	void InitScissor( Rect rect )
	{
		Scissor = rect;
		ScissorGPU = GPUScissor.Single( rect, BorderRadii.Zero, Matrix.Identity );
	}

	void InitScissor( Rect rect, CommandList commandList )
	{
		InitScissor( rect );
		SetScissorAttributes( commandList, ScissorGPU );
	}

}
