using Sandbox;

namespace Editor;

/// <summary>
/// Editor access to a project's compilers. These are internal on <see cref="Project"/> so game
/// code never sees them - editor code reaches them through these extensions.
/// </summary>
public static class ProjectExtensions
{
	extension( Project project )
	{
		/// <summary>
		/// The compiler for this project's game code, null when it has none.
		/// </summary>
		public Compiler Compiler => project.Compiler;

		/// <summary>
		/// The compiler for this project's editor code, null when it has none.
		/// </summary>
		public Compiler EditorCompiler => project.EditorCompiler;
	}

	extension( Project )
	{
		/// <summary>
		/// The compile group every local project's code builds in. Its compilers carry the live
		/// build state - what's building, whether the last build succeeded, and its diagnostics.
		/// </summary>
		public static CompileGroup CompileGroup => Project.CompileGroup;
	}
}
