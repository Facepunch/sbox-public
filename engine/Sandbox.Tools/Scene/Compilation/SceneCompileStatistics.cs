using System;
using System.Collections.Generic;

namespace Editor;

/// <summary>
/// Measurements from a successful scene compile, excluding unchanged referenced assets.
/// </summary>
public sealed class SceneCompileStatistics
{
	/// <summary>
	/// When this compilation finished.
	/// </summary>
	public DateTimeOffset CompletedAt { get; internal set; }

	/// <summary>
	/// Total elapsed time, including editor yields and resource compilation.
	/// </summary>
	public TimeSpan Duration { get; internal set; }

	/// <summary>
	/// Vertices in generated models, including translucent and converted meshes.
	/// </summary>
	public long VertexCount { get; internal set; }

	/// <summary>
	/// Triangles in generated models, including translucent and converted meshes.
	/// </summary>
	public long TriangleCount { get; internal set; }

	/// <summary>
	/// Independently culled fragments in opaque aggregates.
	/// </summary>
	public long FragmentCount { get; internal set; }

	/// <summary>
	/// Elapsed time for each compile stage, in execution order.
	/// </summary>
	public IReadOnlyList<SceneCompileStage> Stages { get; internal set; } = [];
}

/// <summary>
/// Elapsed time for one stage of a scene compile, including editor yields.
/// </summary>
/// <param name="Name">The stage's display name.</param>
/// <param name="Duration">The elapsed time.</param>
public sealed record SceneCompileStage( string Name, TimeSpan Duration );
