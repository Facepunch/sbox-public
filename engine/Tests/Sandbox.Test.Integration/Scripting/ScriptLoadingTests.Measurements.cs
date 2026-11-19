using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Opt-in measurements include real TypeLibrary registration, scene component migration and the first update.
	/// Watcher callbacks are dispatched directly to exclude filesystem debounce and background notification races.
	/// Set SBOX_SCRIPT_MEASUREMENTS to an output filename prefix; ordinary test runs skip this workload.
	/// SBOX_SCRIPT_STAGES=1 additionally records nested compiler stages; profiled timings are not baseline timings.
	/// </summary>
	[DataTestMethod, TestCategory( "Slow" )]
	[DataRow( 4, false )]
	[DataRow( 4, true )]
	[DataRow( 16, false )]
	[DataRow( 16, true )]
	public void MeasureScriptStartupAndHotload( int count, bool namedBatch )
	{
		var output = Environment.GetEnvironmentVariable( "SBOX_SCRIPT_MEASUREMENTS" );
		if ( string.IsNullOrWhiteSpace( output ) )
		{
			Assert.Inconclusive( "Set SBOX_SCRIPT_MEASUREMENTS to run engine performance measurements." );
		}

		var sources = new Dictionary<string, string>();
		var revisions = new int[count];
		var expected = new int[count * 2];
		var samples = new List<object>();
		for ( int i = 0; i < count; i++ )
		{
			sources[$"measure/file{i:D2}.csx"] = MeasurementSource( i, 0 );
		}

		scene = new Scene();
		using var scope = scene.Push();
		Measure( "Startup", count, 0 );
		foreach ( int changed in new[] { 1, count } )
		{
			for ( int round = 1; round <= 3; round++ )
			{
				for ( int i = 0; i < changed; i++ )
				{
					sources[$"measure/file{i:D2}.csx"] = MeasurementSource( i, ++revisions[i] );
				}
				Measure( "Edit", changed, round );
			}
		}

		File.WriteAllText( $"{output}-{count}-{namedBatch}.json", JsonSerializer.Serialize( samples, new JsonSerializerOptions { WriteIndented = true } ) );

		void Measure( string operation, int changed, int round )
		{
			// Source generation and disk I/O are excluded from the measured engine publication interval.
			foreach ( var (path, source) in sources.Take( changed ) )
			{
				files.WriteAllText( path, source );
			}
			using var stages = Environment.GetEnvironmentVariable( "SBOX_SCRIPT_STAGES" ) == "1" ? new CompilationStages() : null;
			long allocated = GC.GetAllocatedBytesForCurrentThread();
			long started = Stopwatch.GetTimestamp();
			if ( namedBatch )
			{
				var result = Game.Scripting.LoadFiles( sources );
				Assert.IsTrue( result.Success, string.Join( "; ", result.Diagnostics ) );
			}
			else if ( operation == "Startup" )
			{
				ResourceLoader.LoadAllGameResource( files );
			}
			else
			{
				DispatchResourceChanges( Enumerable.Range( 0, changed ).Select( i => $"measure/file{i:D2}.csx" ).ToArray() );
			}
			double loadMs = Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
			Assert.IsNull( Game.Scripting.LastError, Game.Scripting.LastError?.ToString() );
			Assert.IsFalse( Game.Scripting.RequiresReload );

			stages?.BeginFrame();
			long firstFrame = Stopwatch.GetTimestamp();
			if ( operation == "Startup" )
			{
				reloadRoots = new object[count * 2];
				for ( int i = 0; i < reloadRoots.Length; i++ )
				{
					reloadRoots[i] = scene.CreateObject().Components.Create( Game.TypeLibrary.GetType( $"MeasuredScript{i / 2}_{i % 2}" ) );
				}
			}
			for ( int i = 0; i < reloadRoots.Length; i++ )
			{
				((Component)reloadRoots[i]).InternalUpdate();
			}
			double frameMs = Stopwatch.GetElapsedTime( firstFrame ).TotalMilliseconds;
			long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
			stages?.Dispose();
			for ( int i = 0; i < reloadRoots.Length; i++ )
			{
				expected[i] += 6 + revisions[i / 2];
				var type = Game.TypeLibrary.GetType( $"MeasuredScript{i / 2}_{i % 2}" );
				Assert.AreSame( type.TargetType, reloadRoots[i].GetType() );
				Assert.AreEqual( expected[i], type.GetProperty( "Value" ).GetValue( reloadRoots[i] ), $"{operation}, round {round}, changed {changed}, component {i}" );
			}
			samples.Add( new
			{
				Operation = operation,
				Files = count,
				Changed = changed,
				Round = round,
				NamedBatch = namedBatch,
				LoadMs = loadMs,
				FirstFrameMs = frameMs,
				PauseMs = loadMs + frameMs,
				Bytes = bytes,
				Stages = stages?.Samples
			} );
		}
	}

	/// <summary>
	/// Exercises the installed resource watchers with one deterministic notification, excluding asynchronous filesystem timing.
	/// Real filesystem delivery and owning-context dispatch are covered by the existing watcher integration tests.
	/// </summary>
	private void DispatchResourceChanges( params string[] paths )
	{
		var callback = typeof( FileWatch ).GetMethod( "TriggerCallback", BindingFlags.Instance | BindingFlags.NonPublic );
		var changes = paths.ToList();
		foreach ( var watcher in files.watchers.ToArray() )
		{
			callback.Invoke( watcher, [changes] );
		}
	}

	private static string MeasurementSource( int file, int revision )
	{
		var source = new StringBuilder();
		for ( int item = 0; item < 2; item++ )
		{
			source.AppendLine( $"class MeasuredScript{file}_{item} : Component {{ [Property] public int Value {{ get; set; }} " +
				$"protected override void OnUpdate() {{ Value += Step0() + Step1() + Step2() + {revision}; }}" );
			for ( int method = 0; method < 3; method++ )
			{
				source.AppendLine( $"int Step{method}() {{ return {method + 1}; }}" );
			}
			source.AppendLine( "}" );
		}
		return source.ToString();
	}
}
