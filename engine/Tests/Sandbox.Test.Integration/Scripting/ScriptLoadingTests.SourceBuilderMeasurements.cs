using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace ScriptingTests;

public partial class ScriptLoadingTests
{
	/// <summary>
	/// Measures the generated shader-source pattern observed in the workshop: many literal List.Add calls
	/// inside a named-file method. Includes unchanged large methods during helper edits and a source edit.
	/// Set SBOX_SCRIPT_MEASUREMENTS to an output prefix. This does not compile a GPU shader or measure rendering.
	/// </summary>
	[DataTestMethod, TestCategory( "Slow" )]
	[DataRow( 64 )]
	[DataRow( 555 )]
	public void MeasureSourceBuilderStartupAndHotload( int lines )
	{
		var output = Environment.GetEnvironmentVariable( "SBOX_SCRIPT_MEASUREMENTS" );
		if ( string.IsNullOrWhiteSpace( output ) )
		{
			Assert.Inconclusive( "Set SBOX_SCRIPT_MEASUREMENTS to run engine performance measurements." );
		}

		var text = Enumerable.Range( 0, lines ).Select( i => $"// Generated shader line {i}" ).ToArray();
		var source = new StringBuilder( "class MeasuredSource { public static string Build() { var lines = new List<string>();\n" );
		foreach ( var line in text )
		{
			source.AppendLine( $"lines.Add( \"{line}\" );" );
		}
		source.AppendLine( "return string.Join( \"\\n\", lines ); } }" );
		var sources = new Dictionary<string, string>
		{
			["measure/source.csx"] = source.ToString(),
			["measure/helper.csx"] = "class MeasuredHelper { public static int Step() { return 1; } }",
			["measure/component.csx"] = """
				class MeasuredSourceComponent : Component
				{
					[Property] public int Value { get; set; }
					[Property] public string Text { get; set; }
					protected override void OnUpdate() { Value += MeasuredHelper.Step(); Text = MeasuredSource.Build(); }
				}
				"""
		};
		var samples = new List<object>();
		var expectedText = string.Join( "\n", text );
		int expectedValue = 0;
		scene = new Scene();
		using var scope = scene.Push();
		Measure( "Startup", 0, 1 );
		for ( int round = 1; round <= 3; round++ )
		{
			sources["measure/helper.csx"] = $"class MeasuredHelper {{ public static int Step() {{ return {round + 1}; }} }}";
			Measure( "HelperEdit", round, round + 1 );
		}

		sources["measure/source.csx"] = sources["measure/source.csx"].Replace( "// Generated shader line 0", "// Changed shader line 0" );
		expectedText = expectedText.Replace( "// Generated shader line 0", "// Changed shader line 0" );
		Measure( "SourceEdit", 1, 4 );
		File.WriteAllText( $"{output}-source-builder-{lines}.json", JsonSerializer.Serialize( samples, new JsonSerializerOptions { WriteIndented = true } ) );

		void Measure( string operation, int round, int increment )
		{
			using var stages = Environment.GetEnvironmentVariable( "SBOX_SCRIPT_STAGES" ) == "1" ? new CompilationStages() : null;
			long allocated = GC.GetAllocatedBytesForCurrentThread();
			long started = Stopwatch.GetTimestamp();
			var loaded = Game.Scripting.LoadFiles( sources );
			double loadMs = Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
			Assert.IsTrue( loaded.Success, string.Join( "; ", loaded.Diagnostics ) );
			Assert.IsFalse( loaded.RequiresReload );
			stages?.BeginFrame();
			started = Stopwatch.GetTimestamp();
			if ( operation == "Startup" )
			{
				reloadRoots = [scene.CreateObject().Components.Create( Game.TypeLibrary.GetType( "MeasuredSourceComponent" ) )];
			}
			((Component)reloadRoots[0]).InternalUpdate();
			double frameMs = Stopwatch.GetElapsedTime( started ).TotalMilliseconds;
			long bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
			stages?.Dispose();
			var type = Game.TypeLibrary.GetType( "MeasuredSourceComponent" );
			expectedValue += increment;
			Assert.AreSame( type.TargetType, reloadRoots[0].GetType() );
			Assert.AreEqual( expectedValue, type.GetProperty( "Value" ).GetValue( reloadRoots[0] ) );
			Assert.AreEqual( expectedText, type.GetProperty( "Text" ).GetValue( reloadRoots[0] ) );
			Assert.IsNull( Game.Scripting.LastError, Game.Scripting.LastError?.ToString() );
			samples.Add( new
			{
				Operation = operation,
				Lines = lines,
				Round = round,
				LoadMs = loadMs,
				FirstFrameMs = frameMs,
				PauseMs = loadMs + frameMs,
				Bytes = bytes,
				Stages = stages?.Samples
			} );
		}
	}
}
