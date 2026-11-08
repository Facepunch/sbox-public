using Basalt;
using Basalt.Gpu;
using System.Collections.Generic;

namespace BasaltTests.Headless;

/// <summary>
/// What a headless context was asked to record.
/// </summary>
internal enum GpuOp
{
	Bind,
	SetViewport,
	Clear,
	SetConstants,
	Upload,
	BindInstances,
	BindGeometry,
	SetRenderState,
	BindVertexCacheIds,
	Draw,
	DrawCulled,
	DrawFragments,
	DrawFragmentsCulled,
	DrawIndexed,
	DrawFullscreen,
	DrawSky,
	Dispatch,
	DispatchSkinning,
	GenerateMorphs,
	TextureBarrier,
	BufferBarrier,
	CopyTexture,
	CopyFrameBuffer,
	CopyToOutput,
	Resolve,
	ResolveScratch,
	ReadBuffer,
}

/// <summary>
/// One recorded command, and the GPU scopes it was recorded in - a frame's layers each record in a scope of their name.
/// Fields a command doesn't use are left default.
/// </summary>
internal sealed record GpuCommand( GpuOp Op, string Scope )
{
	/// <summary>The material drawn or dispatched.</summary>
	public GpuMaterial Material { get; init; }

	/// <summary>The shader mode drawn with, or the constant buffer set.</summary>
	public StringToken Mode { get; init; }

	/// <summary>The mesh drawn.</summary>
	public RenderMesh Mesh { get; init; }

	/// <summary>Instances or draws drawn, or slices copied.</summary>
	public int Count { get; init; }

	/// <summary>Threads dispatched.</summary>
	public (int X, int Y, int Z) Threads { get; init; }

	/// <summary>Colour bound or cleared, a barrier's texture, or a copy's or resolve's destination.</summary>
	public GpuTexture Texture { get; init; }

	/// <summary>Depth bound or cleared.</summary>
	public GpuTexture Depth { get; init; }

	/// <summary>A copy's or resolve's source.</summary>
	public GpuTexture Source { get; init; }

	/// <summary>A barrier's, upload's or read's buffer. Null for the vertex cache.</summary>
	public GpuBuffer Buffer { get; init; }

	/// <summary>A dispatch's or fullscreen draw's own parameters.</summary>
	public ShaderParameters Parameters { get; init; }

	public GpuStage SrcStage { get; init; }
	public GpuAccess SrcAccess { get; init; }
	public GpuStage DstStage { get; init; }
	public GpuAccess DstAccess { get; init; }
	public TextureLayout Layout { get; init; }

	/// <summary>
	/// Whether it was recorded inside a scope named <paramref name="name"/>.
	/// </summary>
	public bool In( string name )
	{
		if ( Scope is null ) return false;
		foreach ( var scope in Scope.Split( " / " ) )
			if ( scope == name ) return true;
		return false;
	}

	/// <summary>Whether it draws geometry.</summary>
	public bool IsDraw => Op is GpuOp.Draw or GpuOp.DrawCulled or GpuOp.DrawFragments or GpuOp.DrawFragmentsCulled or GpuOp.DrawIndexed;

	/// <summary>Whether it's a barrier.</summary>
	public bool IsBarrier => Op is GpuOp.TextureBarrier or GpuOp.BufferBarrier;

	public override string ToString()
	{
		var what = Op switch
		{
			GpuOp.Draw or GpuOp.DrawCulled or GpuOp.DrawFragments or GpuOp.DrawFragmentsCulled => $"{Op} {Material} x{Count}",
			GpuOp.Dispatch => $"{Op} {Material} {Threads}",
			GpuOp.TextureBarrier => $"{Op} {Texture} {SrcStage}/{SrcAccess} -> {DstStage}/{DstAccess} {Layout}",
			GpuOp.BufferBarrier => $"{Op} {Buffer?.Resource?.ToString() ?? "vertex cache"} {SrcStage}/{SrcAccess} -> {DstStage}/{DstAccess}",
			GpuOp.Bind => $"{Op} {Texture} {Depth}",
			_ => Op.ToString(),
		};
		return Scope is null ? what : $"[{Scope}] {what}";
	}
}

/// <summary>
/// One context's recording, as submitted: on which queue, after which semaphore, signalling which.
/// </summary>
internal sealed record GpuSubmission( HeadlessContext Context, bool AsyncCompute, GpuSemaphore Wait, GpuSemaphore Signal, IReadOnlyList<GpuCommand> Commands )
{
	public override string ToString() => $"{(AsyncCompute ? "compute" : "graphics")} wait {Wait.Handle} signal {Signal.Handle}: {Commands.Count} commands";
}
