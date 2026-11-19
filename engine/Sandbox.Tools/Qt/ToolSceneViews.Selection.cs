using Sandbox.Rendering;
using System;

namespace Editor;

internal static partial class ToolSceneViews
{
	/// <summary>
	/// Hammer's selection on a view, as <c>CMapView::PreRenderSetup</c> draws it: objects Hammer flags for its selection stencil
	/// layers drawn into a mask (<c>tools_selection_mask</c>: red where a selected object covers the view, green where it's in
	/// front of the scene, blue and alpha the same for prefabs and instances), then over the finished frame, in one pass, the
	/// outline <c>tools_selection_outline</c> draws from such a mask and the tints native's overlay renderable asks for where
	/// the selection is in front. The mask is drawn after the scene's translucent objects, where its depth is whole; the
	/// outline and tints after post processing, over the tonemapped frame, as native draws them. Not drawn yet: the depth
	/// outline (<c>SelectionOutlineDepth2</c>, off by default) and the active instance's grey-out.
	/// </summary>
	internal sealed class ToolSelection : IDisposable
	{
		/// <summary><c>SCENEOBJECTFLAG_HAMMER_PREFAB_STENCIL_LAYER</c>.</summary>
		const ulong PrefabFlag = 1UL << 22;

		/// <summary><c>SCENEOBJECTFLAG_HAMMER_SELECTION_STENCIL_LAYER</c>.</summary>
		const ulong SelectionFlag = 1UL << 23;

		static readonly uint SelectionOverlayLayer = new StringToken( "HammerSelectionOverlayLayer" ).Value;
		static readonly uint PrefabOverlayLayer = new StringToken( "HammerPrefabOverlayLayer" ).Value;
		static readonly uint EnabledOverlayLayer = new StringToken( "HammerEnabledOverlayLayer" ).Value;

		// The outline's colours, as PreRenderSetup sets them: sRGB colours made linear, the occluded ones at half
		static readonly Vector4 OutlineColorA = Linear( 255, 183, 45, 1.0f );
		static readonly Vector4 OccludedColorA = Linear( 228, 210, 176, 0.5f );
		static readonly Vector4 OutlineColorB = Linear( 255, 64, 45, 1.0f );
		static readonly Vector4 OccludedColorB = Linear( 228, 180, 176, 0.5f );

		static readonly StringToken CompositeCombo = "D_COMPOSITE";
		static readonly StringToken AnyName = "SelectionAny";
		static readonly StringToken VisibleName = "SelectionVisible";
		static readonly StringToken MaskName = "SelectionMask";
		static readonly StringToken SelectionTintName = "SelectionTint";
		static readonly StringToken PrefabTintName = "PrefabTint";
		static readonly StringToken OutlineName = "SelectionOutline";
		static readonly StringToken OutlineColorAName = "OutlineColorA";
		static readonly StringToken OccludedColorAName = "OccludedColorA";
		static readonly StringToken OutlineColorBName = "OutlineColorB";
		static readonly StringToken OccludedColorBName = "OccludedColorB";

		readonly Material maskMaterial = Material.FromShader( "shaders/tools_selection_mask.shader" );
		readonly RenderAttributes attributes = new();

		readonly List<SceneObject> selected = new();
		readonly List<SceneObject> prefabs = new();
		bool outline;
		Vector4 selectionTint, prefabTint;
		RenderTarget mask;

		/// <summary>Whether the frame draws anything of the selection.</summary>
		bool Drawn => (selected.Count > 0 || prefabs.Count > 0) && (outline || selectionTint.w > 0 || prefabTint.w > 0);

		/// <summary>
		/// A new frame: <paramref name="drawOutline"/> as the request asks (<see cref="Flags.SelectionOutline"/>: the option
		/// on, something selected, not wireframe).
		/// </summary>
		public void BeginFrame( bool drawOutline )
		{
			selected.Clear();
			prefabs.Clear();
			outline = drawOutline;
			selectionTint = default;
			prefabTint = default;
		}

		/// <summary>
		/// The objects of <paramref name="world"/> Hammer flags for its selection stencil layers, which draw every world of the
		/// view (<c>CreateSelectionStencilLayer</c>).
		/// </summary>
		public void Gather( SceneWorld world )
		{
			lock ( world.InternalSceneObjects )
			{
				foreach ( var sceneObject in world.InternalSceneObjects )
				{
					if ( sceneObject.native.IsNull || !sceneObject.RenderingEnabled ) continue;

					var flags = (ulong)sceneObject.native.GetFlags();
					if ( (flags & SelectionFlag) != 0 ) selected.Add( sceneObject );
					else if ( (flags & PrefabFlag) != 0 ) prefabs.Add( sceneObject );
				}
			}
		}

		/// <summary>
		/// Whether a screen-space object is one of Hammer's overlay rectangles (<c>COverlayRenderable</c>: the whole view in
		/// one colour, which native's overlay layers draw only where the stencil holds the selection), which the tints stand
		/// for: its colour is kept and it isn't drawn as it is.
		/// </summary>
		public bool TakeOverlay( SceneObject sceneObject, ReadOnlySpan<ToolVertex> vertices )
		{
			var layer = sceneObject.LayerMatchToken;
			if ( layer != SelectionOverlayLayer && layer != PrefabOverlayLayer && layer != EnabledOverlayLayer ) return false;

			// The active instance's grey-out (HammerEnabledOverlayLayer) isn't drawn yet
			if ( layer == EnabledOverlayLayer || vertices.IsEmpty ) return true;

			var tint = TintFor( vertices[0].Color );
			if ( layer == SelectionOverlayLayer ) selectionTint = tint;
			else prefabTint = tint;
			return true;
		}

		/// <summary>
		/// The camera's stages (<see cref="SceneCamera.OnRenderStageHook"/>): the mask after the translucent objects, the
		/// outline and tints after post processing.
		/// </summary>
		public void OnStage( Stage stage, SceneCamera camera )
		{
			if ( stage == Stage.AfterTransparent ) DrawMask();
			else if ( stage == Stage.AfterPostProcess ) DrawOverFrame();
		}

		void DrawMask()
		{
			ReleaseMask();
			if ( !Drawn ) return;

			var size = Graphics.Viewport.Size;
			var width = (int)size.x;
			var height = (int)size.y;
			if ( width < 1 || height < 1 ) return;

			mask = RenderTarget.GetTemporary( width, height, ImageFormat.RGBA8888, ImageFormat.None, MultisampleAmount.MultisampleNone, 1, "Hammer selection mask" );
			Graphics.RenderTarget = mask;
			try
			{
				Graphics.Clear( Color.Transparent, true, false, false );

				// Graphics.Render draws with the block's attributes
				var drawAttributes = Graphics.Attributes;
				drawAttributes.SetCombo( CompositeCombo, 0 );
				drawAttributes.Set( AnyName, new Vector4( 0, 0, 1, 0 ) );
				drawAttributes.Set( VisibleName, new Vector4( 0, 0, 0, 1 ) );
				foreach ( var sceneObject in prefabs ) Draw( sceneObject );

				drawAttributes.Set( AnyName, new Vector4( 1, 0, 0, 0 ) );
				drawAttributes.Set( VisibleName, new Vector4( 0, 1, 0, 0 ) );
				foreach ( var sceneObject in selected ) Draw( sceneObject );
			}
			finally
			{
				Graphics.RenderTarget = null;
			}
		}

		void Draw( SceneObject sceneObject )
		{
			// Where the mirror placed it this frame
			if ( !sceneObject.IsValid() || sceneObject.RenderObject is not { } drawn ) return;
			Graphics.Render( sceneObject, drawn.Transform, material: maskMaterial );
		}

		void DrawOverFrame()
		{
			if ( mask is null ) return;

			try
			{
				attributes.SetCombo( CompositeCombo, 1 );
				attributes.Set( MaskName, mask.ColorTarget );
				attributes.Set( OutlineName, outline ? 1.0f : 0.0f );
				attributes.Set( OutlineColorAName, OutlineColorA );
				attributes.Set( OccludedColorAName, OccludedColorA );
				attributes.Set( OutlineColorBName, OutlineColorB );
				attributes.Set( OccludedColorBName, OccludedColorB );
				attributes.Set( SelectionTintName, selectionTint );
				attributes.Set( PrefabTintName, prefabTint );
				Graphics.Blit( maskMaterial, attributes );
			}
			finally
			{
				ReleaseMask();
			}
		}

		void ReleaseMask()
		{
			mask?.Dispose();
			mask = null;
		}

		/// <summary>An overlay rectangle's tint as <c>tools_selection_overlay</c> draws it: its vertex colour, sRGB, made linear.</summary>
		internal static Vector4 TintFor( Color32 color ) => Linear( color.r, color.g, color.b, color.a / 255.0f );

		/// <summary>The outline's colour where the selection is in front (<c>OutlineColorA</c>).</summary>
		internal static Vector4 SelectedOutlineColor => OutlineColorA;

		static Vector4 Linear( byte r, byte g, byte b, float a ) => new( ToLinear( r ), ToLinear( g ), ToLinear( b ), a );

		static float ToLinear( byte value )
		{
			var c = value / 255.0f;
			return c <= 0.04045f ? c / 12.92f : MathF.Pow( (c + 0.055f) / 1.055f, 2.4f );
		}

		public void Dispose()
		{
			ReleaseMask();
			selected.Clear();
			prefabs.Clear();
		}
	}
}
