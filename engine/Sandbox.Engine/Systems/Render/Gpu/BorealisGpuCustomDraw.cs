using Borealis;
using Borealis.Backend;
using System.Runtime.InteropServices;
using GpuContext = Borealis.Gpu.GpuContext;

namespace Sandbox;

/// <summary>
/// Custom objects (particles' sprite batches, text, the terrain clipmap) drawn on Borealis.Gpu inside Borealis's frame: each
/// draws through the engine's graphics context into the list Borealis is recording, at its place in the layer, as a camera's
/// stages do (<see cref="BorealisGpuFrame.RunInFrame"/>), with the view's
/// constants and its targets. Borealis records custom objects' runs on the main thread, in segments of their own
/// (<c>MeshRenderFeature.MainThreadRuns</c>), so the graphics context, which is the backend's only one, is never shared with
/// the workers recording the meshes around them.
/// </summary>
internal static class BorealisGpuCustomDraw
{
	static readonly StringToken ViewConstantsName = new( "PerViewConstantBuffer_t" );
	static BorealisGpuFrame frame;
	static BorealisGpuView view;

	/// <summary>Draw custom objects at all; off skips them, for measuring what they cost.</summary>
	internal static bool Enabled { get; set; } = true;

	/// <summary>
	/// Run <paramref name="draw"/> (a custom object's <c>RenderSceneObject</c>) into the list <paramref name="context"/> is
	/// recording, on what it has bound, as <paramref name="layerType"/> from <paramref name="camera"/>. False when there's
	/// nothing to draw into: no Borealis.Gpu backend, no target, or no view constants. Not in a view rendered while
	/// the engine draws in another frame (a water body's refraction, from its command list): the graphics context is that
	/// frame's.
	/// </summary>
	/// <remarks>
	/// With depth bound alone (a shadow map's slice, the depth prepass) it draws into that depth, its materials in
	/// <paramref name="shaderMode"/> (those without it don't draw, as native skips them) with the rasterizer depth bias
	/// <paramref name="depthBias"/> and <paramref name="slopeScaledDepthBias"/>, as native's shadow layers draw custom objects.
	/// </remarks>
	public static bool Draw( GpuContext context, SceneLayerType layerType, RenderView camera, Action draw, StringToken shaderMode = default, int depthBias = 0, float slopeScaledDepthBias = 0 )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !Enabled || Graphics.IsActive ) return false;
		if ( RenderBackend.Default is not BorealisGpuRenderBackend backend || context is not BackendContext ) return false;

		var target = context.Bound;
		var depthAlone = target.Color is null;
		if ( depthAlone && target.Depth is null ) return false;
		if ( (target.Attributes as BackendParameters)?.Get( ViewConstantsName ) is not byte[] constants ) return false;

		frame ??= new BorealisGpuFrame( backend );
		view ??= new BorealisGpuView( backend );
		view.LayerType = layerType;
		view.ShaderMode = shaderMode;
		var viewport = target.Viewport;
		var aspect = viewport.Height > 0 ? viewport.Width / viewport.Height : 1;
		view.SetCamera( camera.Position, camera.Rotation, camera.FieldOfView, camera.ZNear, camera.ZFar, aspect, camera.Orthographic, camera.OrthoSize );
		// The object's transform, as its run bound it (MeshRenderFeature binds instance ids before a custom object renders)
		var (transforms, ids, offset) = ((BackendContext)context).LentInstances;
		backend.LendInstances( transforms, ids, offset );
		backend.DrawMode = shaderMode.Value == 0 ? null : StringToken.GetValue( shaderMode.Value );
		backend.DepthBias = depthAlone ? (depthBias, slopeScaledDepthBias) : default;
		try
		{
			frame.RunInFrame( target, constants, view, first: false, draw, depthAlone );
		}
		finally
		{
			backend.LendInstances( default, default, 0 );
			backend.DrawMode = null;
			backend.DepthBias = default;
		}

		context.RestoreBound();
		return true;
	}

	/// <summary>
	/// A dynamic object's vertices (<see cref="SceneDynamicObject.ManagedVertices"/>) with its material and attributes, in the
	/// block being recorded, as native's <c>CDynamicSceneObjectDesc</c> draws them with its transform slot bound.
	/// </summary>
	internal static void DrawDynamic( SceneDynamicObject sceneObject )
	{
		var vertices = sceneObject.ManagedVertices;
		var material = sceneObject.ManagedMaterial;
		if ( vertices is not { Count: > 0 } || material is null ) return;

		var attributes = sceneObject.CreatedAttributes?.ShadowSet ?? Graphics.Attributes;
		Graphics.Draw( CollectionsMarshal.AsSpan( vertices ), vertices.Count, material, attributes, sceneObject.ManagedPrimitiveType );
	}

	/// <summary>A dynamic or custom object in an overlay layer, waiting for its camera's output (<see cref="DrawOverlays"/>).</summary>
	record struct Overlay( RenderView Camera, bool WithDepth, SceneObject Object, Borealis.Gpu.BufferHandle Transforms, Borealis.Gpu.BufferHandle Ids, long Offset, float MinZ, float MaxZ );

	static readonly List<Overlay> overlays = new();
	static RenderAttributes customAttributes;

	/// <summary>
	/// Keep a dynamic or custom object Borealis's overlay layer reached for <paramref name="camera"/>'s output, with the transform
	/// its run bound: native draws its overlay layers after post processing, which on Borealis.Gpu runs after Borealis's frame.
	/// False when there's nothing to draw into.
	/// </summary>
	public static bool DeferOverlay( GpuContext context, RenderView camera, bool withDepth, SceneObject sceneObject )
	{
		ThreadSafe.AssertIsMainThread();
		// A view rendered inside another frame has no output of its own to draw them on
		if ( !Enabled || Graphics.IsActive ) return false;
		if ( RenderBackend.Default is not BorealisGpuRenderBackend || context is not BackendContext backendContext ) return false;

		// With the depth range the frame drew into (the main view keeps the near end for the 3D skybox), so depth tests compare
		var (transforms, ids, offset) = backendContext.LentInstances;
		var bound = context.Bound;
		overlays.Add( new Overlay( camera, withDepth, sceneObject, transforms, ids, offset, bound.MinZ, bound.MaxZ ) );
		return true;
	}

	/// <summary>Drop what's kept for <paramref name="camera"/>, before its frame records.</summary>
	public static void ForgetOverlays( RenderView camera )
	{
		var kept = 0;
		for ( int i = 0; i < overlays.Count; i++ )
		{
			if ( overlays[i].Camera != camera ) overlays[kept++] = overlays[i];
		}

		overlays.RemoveRange( kept, overlays.Count - kept );
	}

	/// <summary>Whether anything is kept for <paramref name="camera"/>, and whether any of it is depth tested.</summary>
	public static bool HasOverlays( RenderView camera, out bool withDepth )
	{
		var any = false;
		withDepth = false;
		foreach ( var overlay in overlays )
		{
			if ( overlay.Camera != camera ) continue;
			any = true;
			withDepth |= overlay.WithDepth;
		}

		return any;
	}

	/// <summary>
	/// Draw the objects kept for <paramref name="camera"/> into the block being recorded, on <paramref name="view"/>'s
	/// target, in the order Borealis's overlay layers reached them: those depth tested against <paramref name="depth"/> (a
	/// single-sampled depth of the target's size, or none to draw them over everything), then the rest. They're forgotten.
	/// </summary>
	public static void DrawOverlays( RenderView camera, BorealisGpuView view, Texture depth )
	{
		ThreadSafe.AssertIsMainThread();
		if ( overlays.Count == 0 || RenderBackend.Default is not BorealisGpuRenderBackend backend ) return;

		var withDepth = false;
		try
		{
			foreach ( var overlay in overlays )
			{
				if ( overlay.Camera != camera || !overlay.Object.IsValid() ) continue;

				var useDepth = overlay.WithDepth && depth is not null;
				if ( useDepth != withDepth )
				{
					withDepth = useDepth;
					view.BindDepth( withDepth ? depth : null );
				}

				backend.LendInstances( overlay.Transforms, overlay.Ids, overlay.Offset );
				backend.DepthRange = overlay.MaxZ > overlay.MinZ ? (overlay.MinZ, overlay.MaxZ) : (0, 1);
				if ( overlay.Object is SceneDynamicObject dynamic ) DrawDynamic( dynamic );
				else if ( overlay.Object is SceneCustomObject custom )
				{
					// What it sets stays its own, over the block's, as a native layer's context would keep it
					customAttributes ??= RenderAttributes.CreateManaged();
					customAttributes.Clear();
					using var scope = new Graphics.AttributeScope( customAttributes );
					custom.RenderInternal();
				}
			}
		}
		finally
		{
			backend.LendInstances( default, default, 0 );
			backend.DepthRange = (0, 1);
			if ( withDepth ) view.BindDepth( null );
			ForgetOverlays( camera );
		}
	}
}
