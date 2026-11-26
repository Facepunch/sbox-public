using Borealis;
using Borealis.Backend;
using System.Runtime.InteropServices;
using GpuContext = Borealis.Gpu.GpuContext;
using OverlayStencil = Borealis.Gpu.OverlayStencil;

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

	/// <summary>Let go of the frame custom objects draw in and its stage textures, at shutdown (before the engine's leak check).</summary>
	internal static void Shutdown()
	{
		frame?.Dispose();
		frame = null;
		view?.Dispose();
		view = null;
		overlays.Clear();
		customAttributes = null;
		Rendering.GameDrawExtract.Shutdown();
	}

	/// <summary>
	/// Run <paramref name="draw"/> (a custom object's <c>RenderSceneObject</c>) into the list <paramref name="context"/> is
	/// recording, on what it has bound, as <paramref name="layerType"/> from <paramref name="camera"/>. False when there's
	/// nothing to draw into: no Borealis.Gpu backend, no target, or no view constants. Not in a view rendered while
	/// the engine draws in another frame (a water body's refraction, from its command list): the graphics context is that
	/// frame's. <see cref="Graphics.SetupLighting"/> in the draw lights from <paramref name="lightingWorld"/>'s probe volumes
	/// that a view showing <paramref name="subWorlds"/> shows (the object's own world's, without one).
	/// </summary>
	/// <remarks>
	/// With depth bound alone (a shadow map's slice, the depth prepass) it draws into that depth, its materials in
	/// <paramref name="shaderMode"/> (those without it don't draw, as native skips them) with the rasterizer depth bias
	/// <paramref name="depthBias"/> and <paramref name="slopeScaledDepthBias"/>, as native's shadow layers draw custom objects.
	/// In colour and depth it draws with <paramref name="cameraAttributes"/> (the camera's, <c>SceneCamera.Attributes</c>) under
	/// the frame's, as native's custom objects read their view's: lit sprites read the camera's particle lighting there.
	/// </remarks>
	public static bool Draw( GpuContext context, SceneLayerType layerType, RenderView camera, Action draw, StringToken shaderMode = default, int depthBias = 0, float slopeScaledDepthBias = 0, RenderAttributes cameraAttributes = null, RenderWorld lightingWorld = null, ulong subWorlds = 0 )
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
		view.LightingWorld = lightingWorld;
		view.LightingSubWorlds = subWorlds;
		var viewport = target.Viewport;
		var aspect = viewport.Height > 0 ? viewport.Width / viewport.Height : 1;
		view.SetCamera( camera.Position, camera.Rotation, camera.FieldOfView, camera.ZNear, camera.ZFar, aspect, camera.Orthographic, camera.OrthoSize );
		// The object's transform, as its run bound it (MeshRenderFeature binds instance ids before a custom object renders)
		var (transforms, ids, offset) = ((BackendContext)context).LentInstances;
		backend.LendInstances( transforms, ids, offset );
		// Into depth alone (shadow maps, the prepass) its instanced draws are culled: there the cull's break in the pass costs
		// less than the vertices it saves, in a multisampled colour pass more
		backend.CullView = depthAlone ? camera : null;
		backend.DrawMode = shaderMode.Value == 0 ? null : StringToken.GetValue( shaderMode.Value );
		backend.DepthBias = depthAlone ? (depthBias, slopeScaledDepthBias) : default;
		// A debug view's tools pass (wireframe, overdraw, shader IDs) over the custom object's draws too
		var drawer = backend.Context.Drawer;
		drawer.ToolsPass = depthAlone ? ToolsPass.None : context.ToolsPass;
		drawer.OverlayStencil = depthAlone ? context.OverlayStencil : OverlayStencil.None;
		// A game's custom object replays what the extract recorded for this camera (Rendering.GameDrawExtract)
		using var drawing = Rendering.GameDrawExtract.DrawingFor( cameraAttributes, overlay: false );
		try
		{
			frame.RunInFrame( target, constants, view, first: false, draw, depthAlone, cameraAttributes );
		}
		finally
		{
			drawer.ToolsPass = ToolsPass.None;
			drawer.OverlayStencil = OverlayStencil.None;
			view.LightingWorld = null;
			backend.LendInstances( default, default, 0 );
			backend.CullView = null;
			backend.DrawMode = null;
			backend.DepthBias = default;
		}

		// The mesh layer restores its depth target at the end of the custom run. Colour callbacks also use Draw
		// without a custom run, so still restore for them here.
		if ( !depthAlone ) context.RestoreBound();
		return true;
	}

	// The run DrawRun is drawing, which the one delegate handed to the frame reads, so a run allocates no closure
	static readonly List<CustomDraw> run = new();
	static GpuContext runContext;
	static Action drawRun;
	static RenderAttributes runAttributes;

	/// <summary>
	/// <see cref="Draw"/> for a run of mirrored custom objects that follow one another in a layer (<see cref="CustomObject.RenderRun"/>):
	/// one lent block for them all. Each draws with its own transform slot, its target and viewport bound again, and what it
	/// sets on the graphics attributes kept its own, over the block's (the view constants, the camera's attributes), as
	/// a block of its own would leave it.
	/// </summary>
	public static void DrawRun( GpuContext context, ReadOnlySpan<CustomDraw> draws, SceneLayerType layerType, RenderView camera, StringToken shaderMode = default, int depthBias = 0, float slopeScaledDepthBias = 0, RenderAttributes cameraAttributes = null, RenderWorld lightingWorld = null, ulong subWorlds = 0 )
	{
		run.Clear();
		foreach ( var draw in draws ) run.Add( draw );
		runContext = context;
		try
		{
			// The first object's slot: the frame takes its instances when the block starts, as for an object alone
			context.BindInstances( 1, draws[0].FirstInstance );
			Draw( context, layerType, camera, drawRun ??= DrawEachInRun, shaderMode, depthBias, slopeScaledDepthBias, cameraAttributes, lightingWorld, subWorlds );
		}
		finally
		{
			run.Clear();
			runContext = null;
		}
	}

	static void DrawEachInRun()
	{
		var backend = (BorealisGpuRenderBackend)RenderBackend.Default;
		var context = (BackendContext)runContext;
		runAttributes ??= RenderAttributes.CreateManaged();
		for ( int i = 0; i < run.Count; i++ )
		{
			var (custom, firstInstance) = run[i];
			if ( custom is not Sandbox.Rendering.BridgeCustomObject { Source: { } source } || !source.IsValid() ) continue;
			// Draw already lent the first object's instance slot when it began the block.
			if ( i > 0 )
			{
				context.BindInstances( 1, firstInstance );
				var (transforms, ids, offset) = context.LentInstances;
				backend.LendInstances( transforms, ids, offset );
			}

			runAttributes.Clear();
			using var scope = new Graphics.AttributeScope( runAttributes );
			view.Bind();
			source.RenderInternal();
		}
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

		var attributes = sceneObject.CreatedAttributes?.ManagedSet ?? Graphics.Attributes;
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
		using var drawing = Rendering.GameDrawExtract.DrawingFor( camera.HostView as RenderAttributes, overlay: true );
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
