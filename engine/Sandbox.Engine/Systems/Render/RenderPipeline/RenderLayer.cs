using NativeEngine;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Sandbox.Rendering;

/// <summary>
/// ISceneLayer which is basically a render pass
/// </summary>
/// <remarks>Maybe we just call this RenderPass like every other engine.</remarks>
internal abstract class RenderLayer
{
	public string Name { get; set; }
	public LayerFlags Flags { get; set; }
	public SceneLayerType LayerType { get; set; }

	public ClearFlags ClearFlags { get; set; }
	public Color ClearColor { get; set; }

	/// <summary>
	/// Renders all matching scene objects with this shader mode if applicable
	/// </summary>
	public StringToken ShaderMode { get; set; }

	/// <summary>
	/// Scene objects must have these flags to be included in the layer
	/// </summary>
	public SceneObjectFlags ObjectFlagsRequired { get; set; }

	/// <summary>
	/// Scene objects with these flags will be excluded from the layer
	/// </summary>
	public SceneObjectFlags ObjectFlagsExcluded { get; set; }

	public SceneViewRenderTargetHandle ColorAttachment { get; set; } = -1;
	public SceneViewRenderTargetHandle DepthAttachment { get; set; } = -1;

	public RenderAttributes Attributes { get; set; } = new();

	/// <summary>
	/// Deferred render target attributes
	/// </summary>
	public Dictionary<StringToken, SceneViewRenderTargetHandle> RenderTargetAttributes { get; set; } = new();

	/// <summary>
	/// Add to view
	/// </summary>
	/// <remarks>Passing viewport here might not be what we want</remarks>
	public ISceneLayer AddToView( ISceneView view, RenderViewport viewport )
	{
		ISceneLayer nativeLayer;

		if ( this is ProceduralRenderLayer proceduralRenderLayer )
		{
			if ( (nint)proceduralRenderLayer.ProceduralCallback == IntPtr.Zero )
				proceduralRenderLayer.ProceduralCallback = DelegateFunctionPointer.Get<ProceduralRenderLayer.OnRenderCallback>( proceduralRenderLayer.Internal_OnRender );

			nativeLayer = view.AddManagedProceduralLayer( Name, viewport, proceduralRenderLayer.ProceduralCallback, IntPtr.Zero, true );
		}
		else
		{
			nativeLayer = view.AddRenderLayer( Name, viewport, ShaderMode, IntPtr.Zero );
		}

		nativeLayer.LayerEnum = LayerType;
		nativeLayer.m_nLayerFlags = Flags;

		nativeLayer.SetOutput( ColorAttachment, DepthAttachment );

		nativeLayer.AddObjectFlagsRequiredMask( ObjectFlagsRequired );
		nativeLayer.AddObjectFlagsExcludedMask( ObjectFlagsExcluded );

		nativeLayer.SetClearColor( new Vector4( ClearColor ), 0 );

		nativeLayer.m_nClearFlags = (int)ClearFlags;

		Attributes.Get().MergeToPtr( nativeLayer.GetRenderAttributesPtr() );

		foreach ( var attr in RenderTargetAttributes )
		{
			nativeLayer.SetAttr( attr.Key, attr.Value, SceneLayerMSAAMode_t.On, 1 /* SL_RENDER_TARGET_INPUT */ );
		}

		return nativeLayer;
	}
}

/// <summary>
/// A render layer with a callback
/// </summary>
internal abstract class ProceduralRenderLayer : RenderLayer
{
	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	internal delegate void OnRenderCallback( ManagedRenderSetup_t setup );

	// If we're a procedural layer, keep the delegate callback so GC doesn't eat us
	internal DelegateFunctionPointer ProceduralCallback;

	internal void Internal_OnRender( ManagedRenderSetup_t setup )
	{
		using var _ = new Graphics.Scope( in setup );
		OnRender();
	}

	internal virtual void OnRender()
	{

	}
}
