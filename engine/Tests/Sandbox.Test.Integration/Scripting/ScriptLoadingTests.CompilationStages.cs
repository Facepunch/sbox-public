using System;
using System.Collections.Generic;
using System.Reflection;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Opt-in diagnostics use Breen's thread-local benchmark hook without exposing it through the game API.
	/// Nested stages overlap and must not be added together. Dispose restores any prior observer on failure too.
	/// </summary>
	private sealed class CompilationStages : IDisposable
	{
		private readonly FieldInfo observer = typeof( Breen.Script ).Assembly.GetType( "Breen.CompilationTiming", throwOnError: true )
			.GetField( "Observe", BindingFlags.Static | BindingFlags.NonPublic );
		private readonly object previous;
		private bool disposed;
		private string phase = "Load";

		/// <summary>
		/// Collected stage counts, elapsed milliseconds and allocations on the measured thread.
		/// </summary>
		public Dictionary<string, StageSample> Samples { get; } = new();

		/// <summary>
		/// Installs a collector for this thread and measurement only.
		/// </summary>
		public CompilationStages()
		{
			previous = observer.GetValue( null );
			observer.SetValue( null, (Action<string, double, long>)Record );
		}

		/// <summary>
		/// Separates post-publication component creation and callbacks from loading.
		/// </summary>
		public void BeginFrame() => phase = "FirstFrame";

		private void Record( string name, double milliseconds, long bytes )
		{
			string key = phase + ":" + name;
			var sample = Samples.GetValueOrDefault( key ) ?? new( 0, 0, 0 );
			Samples[key] = new( sample.Calls + 1, sample.Milliseconds + milliseconds, sample.Bytes + bytes );
		}

		/// <summary>
		/// Restores the prior hook, including when a measured operation throws.
		/// </summary>
		public void Dispose()
		{
			if ( disposed )
			{
				return;
			}
			observer.SetValue( null, previous );
			disposed = true;
		}
	}

	/// <summary>
	/// Aggregate observations for one compiler stage within one phase.
	/// </summary>
	/// <param name="Calls">Number of observed intervals.</param>
	/// <param name="Milliseconds">Total elapsed time, including nested stages.</param>
	/// <param name="Bytes">Total thread allocations, including nested stages.</param>
	private sealed record StageSample( int Calls, double Milliseconds, long Bytes );
}
