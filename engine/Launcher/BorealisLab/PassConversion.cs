using System.IO;
using Borealis.Source2;
using Sandbox.ShaderCompiler;

namespace Sandbox.BorealisLab;

/// <summary>
/// <c>-convert-passes a,b [-overwrite] [-standalone] [-compile-only]</c>: writes the pass module (<see cref="PassConverter"/>)
/// beside each named shader (a path under <c>core/shaders</c> without its extension: <c>copytexture</c>,
/// <c>postprocess/pp_vignette</c>), keeping a module that's there unless <c>-overwrite</c>, then compiles each module's
/// stages with every combo at its first value and each combo at its last in turn. <c>-standalone</c> writes a standalone
/// module (its modes declared), or makes one that's there standalone; a module with no <c>.shader</c> left is still
/// compiled. <c>-compile-only</c> writes nothing.
/// Prints a line per shader; exits 1 when any doesn't compile. No device and nothing of the engine starts.
/// </summary>
internal static class PassConversion
{
	public static bool Requested => Args.String( "-convert-passes" ) is not null;

	public static int Run()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "pass.slangh" ) ) )
			?? throw new InvalidOperationException( "No core content with shaders/sbox/pass.slangh" );
		var shaders = Path.Combine( core, "shaders" );
		var game = Path.GetDirectoryName( core );
		var compiler = new SlangCompiler( Borealis.Backend.EngineFolders.NativeIn( game ), [shaders] );

		var failed = 0;
		foreach ( var name in Args.String( "-convert-passes" ).Split( ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries ) )
		{
			var source = Path.Combine( shaders, name + ".shader" );
			var target = Path.ChangeExtension( source, ".slang" );
			if ( !File.Exists( source ) && !Args.Has( "-compile-only" ) && !(Args.Has( "-standalone" ) && File.Exists( target )) )
			{
				Console.WriteLine( $"{name}: no {source}" );
				failed++;
				continue;
			}

			if ( !Args.Has( "-compile-only" ) && (Args.Has( "-overwrite" ) || !File.Exists( target )) )
			{
				var text = File.ReadAllText( source );
				if ( PassConverter.Refuse( text ) is { } why )
				{
					Console.WriteLine( $"{name}: not converted, {why}" );
					continue;
				}

				var stem = Path.GetFileNameWithoutExtension( source );
				File.WriteAllText( target, (Args.Has( "-standalone" ) ? PassConverter.ConvertStandalone( text, stem ) : PassConverter.Convert( text, stem )).Replace( "\n", "\r\n" ) );
			}
			else if ( !Args.Has( "-compile-only" ) && Args.Has( "-standalone" ) && File.Exists( target ) )
			{
				// A module that's there already: its modes become declarations, nothing else changes
				var text = File.ReadAllText( target );
				var standalone = PassConverter.Standalone( text, Path.GetFileNameWithoutExtension( target ) );
				if ( standalone != text ) File.WriteAllText( target, standalone );
			}

			if ( !File.Exists( target ) )
			{
				Console.WriteLine( $"{name}: no module" );
				failed++;
				continue;
			}

			var errors = Compile( compiler, PassModule.Read( target, [shaders] ), out var compiled );
			Console.WriteLine( errors is null ? $"{name}: {compiled} stage compiles" : $"{name}: FAILED\n{errors}" );
			if ( errors is not null ) failed++;
		}

		return failed == 0 ? 0 : 1;
	}

	/// <summary>Each stage with its combos at their first values, then each at its last; the first error, or null.</summary>
	internal static string Compile( SlangCompiler compiler, PassModule module, out int compiled )
	{
		compiled = 0;
		foreach ( var stage in new[] { StageKind.Vertex, StageKind.Pixel, StageKind.Compute } )
		{
			if ( !module.Has( stage ) ) continue;
			var combos = module.Combos( stage );
			var sets = new List<Dictionary<string, int>> { combos.ToDictionary( c => c.Name, c => c.Min ) };
			foreach ( var combo in combos.Where( c => c.Max > c.Min ) )
			{
				var set = combos.ToDictionary( c => c.Name, c => c.Min );
				set[combo.Name] = combo.Max;
				sets.Add( set );
			}

			foreach ( var values in sets )
			{
				if ( !module.Has( stage, values ) ) continue;
				try
				{
					compiler.CompilePass( module, stage, values );
					compiled++;
				}
				catch ( Sandbox.ShaderCompiler.ShaderCompileException e )
				{
					return $"  {stage} {string.Join( ' ', values.Select( v => $"{v.Key}={v.Value}" ) )}:\n{e.Errors}";
				}
			}
		}

		return null;
	}
}
