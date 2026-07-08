using System.Collections.Generic;
using Sandbox.Rendering;

namespace Sandbox.Clutter;

/// <summary>
/// Batched clutter, GPU frustum-culled per view and drawn indirect through each model's material.
/// </summary>
internal class ClutterBatchSceneObject : SceneCustomObject
{
	private static ComputeShader CullShader = new( "shaders/clutter_cull_cs.shader" );

	[ConVar( "clutter_cull_frustum_scale", ConVarFlags.Cheat )]
	internal static float CullFrustumScale { get; set; } = 1.0f;

	private const int MaxLods = 4; // dont think we need more than that

	internal struct LodParams
	{
		public Vector3 CameraPos;
		public float TanHalfFov;
		public float ViewportWidth;
	}

	internal static LodParams Lod { get; set; } = new() { TanHalfFov = 1.0f, ViewportWidth = 1920.0f };

	private readonly Model _model;
	private readonly int _lodCount;
	private readonly float _modelRadius;
	private readonly GpuBuffer<float> _lodDistances;

	private readonly CommandList _commandList = new( "ClutterBatch" );

	private GpuBuffer<GpuInstanceTransform>[] _visible;

	private GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>[] _args;

	private int _count;
	private int _capacity;

	private GpuBuffer<GpuInstanceTransform> _instances;

	private GpuBuffer<Vector4> _spheres;

	public ClutterBatchSceneObject( SceneWorld world, Model model ) : base( world )
	{
		_model = model;
		_modelRadius = model.Bounds.Size.Length * 0.5f;

		var switches = model.GetLodSwitchDistances() ?? [];
		_lodCount = Math.Clamp( switches.Length, 1, MaxLods );

		var distances = new float[_lodCount];
		for ( int i = 0; i < _lodCount; i++ )
			distances[i] = i < switches.Length ? switches[i] : 0f;

		_lodDistances = new GpuBuffer<float>( _lodCount, GpuBuffer.UsageFlags.Structured );
		_lodDistances.SetData( distances );

		_visible = new GpuBuffer<GpuInstanceTransform>[_lodCount];
		_args = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>[_lodCount];

		Flags.IsOpaque = true;
		Flags.IsTranslucent = false;
		Flags.CastShadows = true;
		Flags.WantsPrePass = true;
	}

	/// <summary>
	/// Uploads the instance set to the persistent GPU buffers. Only called when the set changes.
	/// </summary>
	public void SetInstances( List<Transform> transforms )
	{
		_count = transforms.Count;
		if ( _count == 0 )
			return;

		EnsureCapacity( _count );

		var modelBounds = _model.Bounds;
		var modelCenter = modelBounds.Center;
		var worldBounds = modelBounds.Transform( transforms[0] );

		var instances = new GpuInstanceTransform[_count];
		var spheres = new Vector4[_count];
		for ( int i = 0; i < _count; i++ )
		{
			var transform = transforms[i];
			var scale = transform.Scale;
			var center = transform.PointToWorld( modelCenter );
			var radius = _modelRadius * MathF.Max( scale.x, MathF.Max( scale.y, scale.z ) );

			instances[i] = GpuInstanceTransform.From( transform );
			spheres[i] = new Vector4( center.x, center.y, center.z, radius );

			worldBounds = worldBounds.AddBBox( modelBounds.Transform( transform ) );
		}

		_instances.SetData( instances );
		_spheres.SetData( spheres );

		Bounds = worldBounds;

		BuildCommandList();
	}

	private void EnsureCapacity( int count )
	{
		if ( _instances != null && count <= _capacity )
			return;

		DisposeBuffers();
		_capacity = count;

		_instances = new GpuBuffer<GpuInstanceTransform>( count, GpuBuffer.UsageFlags.Structured );
		_spheres = new GpuBuffer<Vector4>( count, GpuBuffer.UsageFlags.Structured );

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_visible[lod] = new GpuBuffer<GpuInstanceTransform>( count, GpuBuffer.UsageFlags.Structured | GpuBuffer.UsageFlags.Append );
			_args[lod] = new GpuBuffer<GpuBuffer.IndirectDrawIndexedArguments>( 1, GpuBuffer.UsageFlags.IndirectDrawArguments );
			_args[lod].SetData(
			[
				new GpuBuffer.IndirectDrawIndexedArguments { IndexCount = (uint)_model.GetIndexCountForLod( lod ) }
			] );
		}
	}

	/// <summary>
	/// Bakes the cull dispatch and indirect draws into <see cref="_commandList"/> for the current
	/// instance set. Per-view inputs are pushed through Graphics.Attributes in RenderSceneObject.
	/// </summary>
	private void BuildCommandList()
	{
		_commandList.Reset();

		if ( _instances == null || _count == 0 )
			return;

		_commandList.Attributes.Set( "AllInstances", _instances );
		_commandList.Attributes.Set( "AllInstanceSpheres", _spheres );
		_commandList.Attributes.Set( "InstanceCount", _count );
		_commandList.Attributes.Set( "ClutterModelRadius", _modelRadius );
		_commandList.Attributes.Set( "ClutterLodCount", _lodCount );
		_commandList.Attributes.Set( "ClutterLodSwitchDistances", _lodDistances );

		for ( int slot = 0; slot < MaxLods; slot++ )
			_commandList.Attributes.Set( $"VisibleLod{slot}", _visible[slot < _lodCount ? slot : 0] );

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_commandList.ResourceBarrierTransition( _visible[lod], ResourceState.UnorderedAccess );
			_commandList.SetCounterValue( _visible[lod], 0 );
		}

		_commandList.DispatchCompute( CullShader, _count, 1, 1 );

		// Appends must complete before reading each bucket's count.
		for ( int lod = 0; lod < _lodCount; lod++ )
			_commandList.UavBarrier( _visible[lod] );

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_commandList.ResourceBarrierTransition( _args[lod], ResourceState.CopyDestination );
			_commandList.CopyStructureCount( _visible[lod], _args[lod], 4 ); // -> args.InstanceCount
		}

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_commandList.ResourceBarrierTransition( _visible[lod], ResourceState.GenericRead );
			_commandList.ResourceBarrierTransition( _args[lod], ResourceState.IndirectArgument );
			_commandList.DrawModelInstancedIndirect( _model, _visible[lod], _args[lod], 0, lod );
		}
	}

	public override void RenderSceneObject()
	{
		if ( _instances == null || _count == 0 )
			return;

		// Per-view inputs, read by the cull dispatch during replay.
		Graphics.Attributes.Set( "ClutterFrustumScale", CullFrustumScale );
		Graphics.Attributes.Set( "ClutterLodCameraPos", Lod.CameraPos );
		Graphics.Attributes.Set( "ClutterLodTanHalfFov", Lod.TanHalfFov );
		Graphics.Attributes.Set( "ClutterLodViewportWidth", Lod.ViewportWidth );
		Graphics.Attributes.Set( "ClutterWorldToProjection", Graphics.SceneView.GetFrustum().GetReverseZViewProjTranspose() );

		_commandList.ExecuteOnRenderThread();
	}

	private void DisposeBuffers()
	{
		_instances?.Dispose();
		_instances = null;

		_spheres?.Dispose();
		_spheres = null;

		for ( int lod = 0; lod < _lodCount; lod++ )
		{
			_visible[lod]?.Dispose();
			_visible[lod] = null;
			_args[lod]?.Dispose();
			_args[lod] = null;
		}

		_capacity = 0;
	}

	internal override void OnNativeDestroy()
	{
		DisposeBuffers();
		_lodDistances?.Dispose();
		base.OnNativeDestroy();
	}
}
