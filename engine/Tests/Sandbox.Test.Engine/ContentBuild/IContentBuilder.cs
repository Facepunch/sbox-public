using System;
using System.Collections.Generic;
using System.Threading;

namespace ContentBuild;

/// <summary>
/// A content builder under test. It builds a <see cref="ContentFixture"/> root in place: compiled
/// outputs land beside their sources, as they do in a project. Everything it writes, including its own
/// state, stays inside <see cref="ContentFixture.Parent"/>. Scenarios only see what it writes to disk
/// plus the <see cref="BuildResult"/>.
/// </summary>
public interface IContentBuilder
{
	string Name { get; }

	BuildResult Build( ContentFixture fixture, BuildOptions options, CancellationToken token );

	/// <summary>
	/// Forgets everything remembered about earlier builds of the fixture, so the next one starts clean.
	/// </summary>
	void Clean( ContentFixture fixture );
}

public sealed record BuildOptions
{
	/// <summary>
	/// Root-relative source paths to build, or null for everything under the root.
	/// </summary>
	public IReadOnlyList<string> Select { get; init; }

	/// <summary>
	/// Rebuild regardless of up-to-date state.
	/// </summary>
	public bool Force { get; init; }

	/// <summary>
	/// Global compile settings, as resourcecompiler arguments (e.g. -maxcompiledtextureres 4).
	/// </summary>
	public IReadOnlyList<string> CompilerArgs { get; init; } = [];

	/// <summary>
	/// Called with each asset as its compile finishes, from any thread.
	/// </summary>
	public Action<string> JobFinished { get; init; }
}

/// <param name="Success">Every selected asset compiled and nothing was cancelled.</param>
/// <param name="Failed">Root-relative source paths that failed to compile.</param>
/// <param name="Jobs">Root-relative sources given to a compiler this build, in dispatch order (not generated children).</param>
/// <param name="Log">Compiler output, for failure messages.</param>
/// <param name="Reruns">Compiles discarded and run again because what they read wasn't known in time; a source
/// appears in <paramref name="Jobs"/> twice only for this. Scenarios require none.</param>
public sealed record BuildResult( bool Success, IReadOnlyList<string> Failed, IReadOnlyList<string> Jobs, string Log, int Reruns = 0 );
