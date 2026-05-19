using NativeEngine;

namespace Sandbox;

public static partial class Graphics
{
	/// <summary>
	/// Draws a single model at the given Transform immediately.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="transform">Transform to draw the model at</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public static void DrawModel( Model model, Transform transform, RenderAttributes attributes = null )
	{
		AssertRenderBlock();

		attributes ??= Attributes;

		DrawModelInstanced( model, new[] { transform }, attributes );
	}

	/// <summary>
	/// Draws multiple instances of a model using GPU instancing, assuming standard implemented shaders.
	/// 
	/// Use `GetTransformMatrix( int instance )` in shaders to access the instance transform.
	/// 
	/// There is a limit of 1,048,576 transform slots per frame when using this method.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="transforms">Instance transform data to draw</param>
	/// <param name="lodLevel">LOD level to render (0 = highest detail)</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public static unsafe void DrawModelInstanced( Model model, Span<Transform> transforms, int lodLevel, RenderAttributes attributes = null )
	{
		AssertRenderBlock();

		if ( transforms.Length <= 0 )
			return;

		if ( !model.IsValid() )
			return;

		attributes ??= Attributes;

		var clampedLod = Math.Max( lodLevel, 0 );
		fixed ( Transform* pTransforms = transforms )
		{
			RenderTools.DrawModel( Context, SceneLayer, model.native, (IntPtr)pTransforms, transforms.Length, attributes.Get(), clampedLod );
		}
	}

	/// <summary>
	/// Draws multiple instances of a model using GPU instancing, assuming standard implemented shaders.
	/// 
	/// Use `GetTransformMatrix( int instance )` in shaders to access the instance transform.
	/// 
	/// There is a limit of 1,048,576 transform slots per frame when using this method.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="transforms">Instance transform data to draw</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public static unsafe void DrawModelInstanced( Model model, Span<Transform> transforms, RenderAttributes attributes = null )
	{
		AssertRenderBlock();

		if ( transforms.Length <= 0 )
			return;

		if ( !model.IsValid() )
			return;

		attributes ??= Attributes;

		fixed ( Transform* pTransforms = transforms )
		{
			RenderTools.DrawModel( Context, SceneLayer, model.native, (IntPtr)pTransforms, transforms.Length, attributes.Get() );
		}
	}

	/// <summary>
	/// Draws multiple instances of a model using GPU instancing with the number of instances being provided by indirect draw arguments.
	/// Use `SV_InstanceID` semantic in shaders to access the rendered instance.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="buffer">The GPU buffer containing the DrawIndirectArguments</param>
	/// <param name="bufferOffset">Optional offset in the GPU buffer</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public static void DrawModelInstancedIndirect( Model model, GpuBuffer buffer, int bufferOffset = 0, RenderAttributes attributes = null )
	{
		AssertRenderBlock();

		if ( buffer is null )
			return;

		attributes ??= Attributes;

		RenderTools.DrawModel( Context, SceneLayer, model.native, buffer.native, bufferOffset, attributes.Get() );
	}

	/// <summary>
	/// Draws multiple instances of a model using GPU instancing.
	/// This is similar to <see cref="DrawModelInstancedIndirect(Model, GpuBuffer, int, RenderAttributes)"/>,
	/// except the count is provided from the CPU rather than via a GPU buffer.
	/// 
	/// Use `SV_InstanceID` semantic in shaders to access the rendered instance.
	/// </summary>
	/// <param name="model">The model to draw</param>
	/// <param name="count">The number of instances to draw</param>
	/// <param name="attributes">Optional attributes to apply only for this draw call</param>
	public static unsafe void DrawModelInstanced( Model model, int count, RenderAttributes attributes = null )
	{
		AssertRenderBlock();

		if ( count <= 0 )
			return;

		if ( !model.IsValid() )
			return;

		attributes ??= Attributes;

		RenderTools.DrawModel( Context, SceneLayer, model.native, IntPtr.Zero, count, attributes.Get() );
	}
}
