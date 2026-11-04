using NativeEngine;
using Sandbox.Rendering;
using System.Runtime.InteropServices;

namespace Sandbox.SceneRenderer.Native;

/// <summary>
/// A native scene layer that calls back into managed code when it renders, in <see cref="Graphics"/>'s scope for the view.
/// The ones <see cref="NativeRenderPipeline"/> adds hold the view's state until it renders, so each pipeline has its own.
/// </summary>
internal abstract class NativeProceduralLayer
{
	[UnmanagedFunctionPointer( CallingConvention.StdCall )]
	delegate void OnRenderCallback( ManagedRenderSetup_t setup );

	readonly DelegateFunctionPointer callback;

	readonly List<(StringToken Name, SceneViewRenderTargetHandle Target)> inputs = new();

	protected NativeProceduralLayer( string name )
	{
		Name = name;
		callback = DelegateFunctionPointer.Get<OnRenderCallback>( Render );
	}

	public string Name { get; }
	public LayerFlags Flags { get; set; } = LayerFlags.NeverRemove;
	public SceneLayerType LayerType { get; set; }
	public ClearFlags ClearFlags { get; set; }
	public SceneViewRenderTargetHandle ColorAttachment { get; set; } = NativeRenderPipeline.NoTarget;
	public SceneViewRenderTargetHandle DepthAttachment { get; set; } = NativeRenderPipeline.NoTarget;

	/// <summary>
	/// Bind a render target of the view as an input attribute, multisampled if it is.
	/// </summary>
	protected void SetInput( StringToken name, SceneViewRenderTargetHandle target )
	{
		for ( int i = 0; i < inputs.Count; i++ )
		{
			if ( inputs[i].Name != name ) continue;
			inputs[i] = (name, target);
			return;
		}

		inputs.Add( (name, target) );
	}

	public ISceneLayer AddToView( ISceneView view, RenderViewport viewport )
	{
		var layer = view.AddManagedProceduralLayer( Name, viewport, callback, default, true );
		layer.LayerEnum = LayerType;
		layer.m_nLayerFlags = Flags;
		layer.SetOutput( ColorAttachment, DepthAttachment );
		layer.m_nClearFlags = (int)ClearFlags;
		if ( ClearFlags != ClearFlags.None ) layer.SetClearColor( default, 0 );

		foreach ( var (name, target) in inputs )
		{
			layer.SetAttr( name, target, SceneLayerMSAAMode_t.On, NativeRenderPipeline.RenderTargetInput );
		}

		return layer;
	}

	protected virtual void Render( ManagedRenderSetup_t setup )
	{
		using var _ = new Graphics.Scope( in setup );
		OnRender();
	}

	protected abstract void OnRender();
}
