using Mono.Cecil;
using System;
using System.Collections.Generic;

namespace HotloadTests;

/// <summary>
/// Creates and runs <see cref="Sandbox.Hotload"/> instances the same way for every hotload test.
/// </summary>
internal static class HotloadRunner
{
	/// <summary>
	/// Hotload's assembly name formatter is global, so only set it once rather than from tests running in parallel.
	/// </summary>
	static HotloadRunner()
	{
		Sandbox.Hotload.AssemblyNameFormatter = name => name.Name;
	}

	/// <summary>
	/// A hotload with tracing and the standard upgraders, but nothing to watch or replace yet.
	/// </summary>
	public static Sandbox.Hotload Create( IAssemblyResolver assemblyResolver )
	{
		var hotload = new Sandbox.Hotload
		{
			TraceRoots = true,
			TracePaths = true,
			IncludeTypeTimings = true,
			IncludeProcessorTimings = true,
			AssemblyResolver = assemblyResolver
		};

		hotload.AddDefaultUpgraders();
		hotload.AddDefaultSkips();

		// We don't want to call AddDefaultWatchAssemblies() because we want to
		// be able to parallelize hotloads by default, by only watching per-test
		// assemblies.

		return hotload;
	}

	/// <summary>
	/// Updates references, writes the result to the test output, and fails the test if
	/// nothing happened, or if there were errors and <paramref name="allowErrors"/> is false.
	/// </summary>
	public static HotloadResult Run( Sandbox.Hotload hotload, bool allowErrors = false )
	{
		var result = hotload.UpdateReferences();

		Assert.IsFalse( result.NoAction );

		Console.WriteLine( $"NoAction = {result.NoAction}" );
		Console.WriteLine( $"InstancesProcessed = {result.InstancesProcessed}" );
		Console.WriteLine( $"ProcessingTime = {result.ProcessingTime:F2}ms" );
		Console.WriteLine( $"result.Entries = {result.Entries.Count}" );

		foreach ( var entry in result.Entries )
		{
			Console.WriteLine( $"  [{entry.Type}] {entry.ToString().Replace( "\n", "\n    " )}" );
		}

		PrintTimings( "Type Timings", result.TypeTimings );
		PrintTimings( "Processor Timings", result.ProcessorTimings );

		if ( !allowErrors )
		{
			Assert.IsFalse( result.HasErrors );
		}

		return result;
	}

	private static void PrintTimings( string title, IReadOnlyDictionary<string, InstanceTimingEntry> timings )
	{
		Console.WriteLine( $"{title}:" );

		foreach ( var (name, entry) in timings.OrderByDescending( x => x.Value.Milliseconds ) )
		{
			PrintTimingEntry( "  ", name, entry );
			Console.WriteLine( "    Roots:" );

			foreach ( var (rootName, rootEntry) in entry.Roots.OrderByDescending( x => x.Value.Milliseconds ) )
			{
				PrintTimingEntry( "      ", rootName, rootEntry );
			}
		}
	}

	private static void PrintTimingEntry( string indent, string name, TimingEntry entry )
	{
		Console.WriteLine( $"{indent}{name}:" );
		Console.WriteLine( $"{indent}  Instances = {entry.Instances}" );
		Console.WriteLine( $"{indent}  TimeSpan = {entry.Milliseconds:F2}ms" );
	}
}
