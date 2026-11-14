namespace Sandbox.Rendering;

/// <summary>
/// Borealis's occlusion debug view into the engine's <see cref="OcclusionDebugTarget"/>, which the overlay shows.
/// </summary>
internal sealed class Source2OcclusionDebug : OcclusionDebugOutput
{
	public OcclusionDebugTarget Target { get; } = new();

	public override void Resize( Rect viewport ) => Target.Resize( viewport );

	public override void Bind( ShaderParameters parameters, Matrix worldToProjection ) => OcclusionDebugTarget.Bind( parameters.Engine(), Target, worldToProjection );

	public override void Clear( GpuContext context )
	{
		context.Invalidate();
		Target.Clear( context.NativeContext() );
	}

	public override void Resolve( GpuContext context, GpuTexture chain, bool frozen )
	{
		context.Invalidate();
		Target.Resolve( context.NativeContext(), chain.Engine(), native: false, frozen );
		Target.ReadCounts( context.NativeContext() );
	}

	public override void Dispose() => Target.Dispose();
}
