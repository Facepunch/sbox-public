using Borealis.Source2.Shaders;
using Sandbox.Engine.Shaders;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace EngineTests;

/// <summary>
/// Every <c>SpecializedConstant()</c> is a specialization constant, however many a stage declares: ids 64, 65, ... after the
/// combos', with <c>SpecializedConstantsBaked</c> in the reserved ids above them (<c>VFX_SPECIALIZATION_RESERVED_FIRST_ID</c>).
/// The engine's includes declare four of their own (DDGI, light modulators, shadow filtering, tools views), so a package
/// shader with one more (meteorlab.veloxtech's <c>snow_cover</c>, <c>VtWinterOn</c>) has to compile and draw with five.
/// </summary>
[TestClass]
public class ShaderSpecializedConstantsTest
{
	const string Source = """
		HEADER
		{
			Description = "Six SpecializedConstant() attributes";
		}

		MODES
		{
			Default();
		}

		FEATURES
		{
		}

		COMMON
		{
			#include "system.fxc"
		}

		VS
		{
			#include "common.fxc"

			struct VS_INPUT
			{
				float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
			};

			struct VS_OUTPUT
			{
				float4 vPositionPs : SV_Position;
			};

			VS_OUTPUT MainVs( VS_INPUT i )
			{
				VS_OUTPUT o;
				o.vPositionPs = float4( i.vPositionOs.xy, 0.5, 1.0 );
				return o;
			}
		}

		PS
		{
			#include "common.fxc"

			RenderState( CullMode, NONE );
			RenderState( DepthEnable, false );

			struct PS_INPUT
			{
				float4 vPositionPs : SV_Position;
			};

			int SpecA < Attribute( "SpecA" ); Default( 0 ); SpecializedConstant(); >;
			int SpecB < Attribute( "SpecB" ); Default( 0 ); SpecializedConstant(); >;
			uint SpecC < Attribute( "SpecC" ); Default( 0 ); SpecializedConstant(); >;
			bool SpecD < Attribute( "SpecD" ); Default( 0 ); SpecializedConstant(); >;
			int SpecE < Attribute( "SpecE" ); Default( 0 ); SpecializedConstant(); >;
			uint SpecF < Attribute( "SpecF" ); Default( 0 ); SpecializedConstant(); >;

			float4 MainPs( PS_INPUT i ) : SV_Target0
			{
				float4 c = float4( 0, 0, 0, 1 );
								c.r = 0.25 * SpecA + ( SpecB != 0 ? 0.5 : 0.0 );
				c.g = 0.25 * SpecC + ( SpecD ? 0.5 : 0.0 );
				c.b = 0.25 * SpecE + ( SpecF != 0 ? 0.5 : 0.0 );
				c.rgb += 0.125; // Drawn at all
				return c;
			}
		}
		""";

	static readonly string[] Names = ["SpecA", "SpecB", "SpecC", "SpecD", "SpecE", "SpecF"];

	/// <summary>Compile <see cref="Source"/> as <c>shaders/specialized_constants_test.shader</c> in a folder of its own; returns the folder.</summary>
	static string Compile()
	{
		var folder = EngineFileSystem.Root.GetFullPath( "/.source2/tests/specialized_constants" );
		Directory.CreateDirectory( Path.Combine( folder, "shaders" ) );
		var path = Path.Combine( folder, "shaders", "specialized_constants_test.shader" );
		File.WriteAllText( path, Source );

		var result = AsyncPump.RunSynchronously( () => ShaderCompile.Compile( path, "shaders/specialized_constants_test.shader", new ShaderCompileOptions { ForceRecompile = true, ConsoleOutput = false }, default ) );
		var output = string.Join( "\n", result.Programs.SelectMany( p => p.Output ?? [] ) );
		Assert.IsTrue( result.Success, output );
		Assert.IsTrue( result.Programs.All( p => p.Success ), output );
		Assert.IsTrue( File.Exists( path + "_c" ), path + "_c" );
		return folder;
	}

	/// <summary>Each of the six gets a specialization constant, 64 to 69 in order, and the stage's SPIR-V has them and the baked switch.</summary>
	[TestMethod]
	public void SixCompileAsSpecializationConstants()
	{
		var folder = Compile();
		try
		{
			var shader = ShaderFile.Load( Path.Combine( folder, "shaders", "specialized_constants_test.shader_c" ) );
			Assert.AreEqual( ShaderFile.MaxVcsVersion, shader.VcsVersion );
			var pixel = shader[Borealis.Source2.Shaders.ShaderProgramType.Pixel];
			var dynamic = pixel.LoadCombo( 0 ).DynamicCombos[0];
			var ids = dynamic.Variables
				.Where( b => pixel.Variables[b.Variable].Register == RegisterType.Specialization )
				.ToDictionary( b => pixel.Variables[b.Variable].Attribute, b => b.Offset );
			for ( int i = 0; i < Names.Length; i++ )
				Assert.AreEqual( 64 + i, ids.GetValueOrDefault( Names[i], -1 ), $"{Names[i]}'s specialization id ({string.Join( ", ", ids )})" );

			var spirv = dynamic.Shader.Spirv ?? dynamic.Shader.Shared?.Spirv;
			Assert.IsNotNull( spirv, "the pixel stage's SPIR-V" );
			var specIds = SpirvReflection.Read( spirv ).SpecializationIds;
			for ( int i = 0; i < Names.Length; i++ )
				CollectionAssert.Contains( specIds.ToList(), (uint)(64 + i), $"SpecId {64 + i} ({string.Join( ", ", specIds )})" );
			CollectionAssert.Contains( specIds.ToList(), ShaderStage.SpecializedConstantsBakedId, "SpecializedConstantsBaked" );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// A quad over the frame drawn with each one set alone (and the first two at a second value, and all at once): each
	/// lights the channel its value says, so each draw specializes every constant to its own value.
	/// </summary>
	[TestMethod]
	public void SixDrawWithEachValue()
	{
		TestGraphics.Require();
		var folder = Compile();
		NativeEngine.FullFileSystem.AddProjectPath( "specialized-constants-test", Project.NativeSearchPath( folder ) );
		NativeEngine.g_pResourceSystem.InvalidateDatabase();

		// As the engine gives a shader compiled while running to Borealis.Gpu, whose roots don't hold this folder
		RenderBackend.ShaderCompiledEverywhere( "shaders/specialized_constants_test.shader", File.ReadAllBytes( Path.Combine( folder, "shaders", "specialized_constants_test.shader_c" ) ) );
		var material = Material.Create( "specialized_constants_test", "shaders/specialized_constants_test.shader" );
		try
		{
			Assert.AreEqual( "shaders/specialized_constants_test.shader", material.Shader.native.GetFilename(), "fell back to another shader" );

			// The colour each attribute adds at value 1 (Source's MainPs)
			Vector3[] unit = [new( 0.25f, 0, 0 ), new( 0.5f, 0, 0 ), new( 0, 0.25f, 0 ), new( 0, 0.5f, 0 ), new( 0, 0, 0.25f ), new( 0, 0, 0.5f )];
			var cases = new (string Name, int[] Values)[]
			{
				("none", [0, 0, 0, 0, 0, 0]),
				("A", [1, 0, 0, 0, 0, 0]),
				("A2", [2, 0, 0, 0, 0, 0]),
				("B", [0, 1, 0, 0, 0, 0]),
				("C", [0, 0, 1, 0, 0, 0]),
				("C2", [0, 0, 2, 0, 0, 0]),
				("D", [0, 0, 0, 1, 0, 0]),
				("E", [0, 0, 0, 0, 1, 0]),
				("F", [0, 0, 0, 0, 0, 1]),
				("all", [1, 1, 1, 1, 1, 1]),
			};

			var wrong = new List<string>();
			foreach ( var (name, values) in cases )
			{
				var expected = new Vector3( 0.125f );
				for ( int i = 0; i < Names.Length; i++ ) expected += unit[i] * (Names[i] == "SpecB" || Names[i] == "SpecD" || Names[i] == "SpecF" ? Math.Min( values[i], 1 ) : values[i]);

				var drawn = Draw( material, values, name );
				if ( (drawn - expected).Length >= 0.03f ) wrong.Add( $"{name}: drew {drawn}, expected {expected}" );
			}

			Assert.AreEqual( 0, wrong.Count, string.Join( "; ", wrong ) );
		}
		finally
		{
			material.Destroy();
			Directory.Delete( folder, true );
		}
	}

	/// <summary>The frame's mean colour, linear (the bitmap holds sRGB), with the quad drawn over all of it with these values.</summary>
	static Vector3 Draw( Material material, int[] values, string name )
	{
		const int Size = 32;
		var attributes = new RenderAttributes();
		for ( int i = 0; i < Names.Length; i++ ) attributes.Set( Names[i], values[i] );

		static Vertex Corner( float x, float y ) => new( new Vector3( x, y, 0 ) );
		Vertex[] quad = [Corner( -1, -1 ), Corner( 1, -1 ), Corner( 1, 1 ), Corner( -1, -1 ), Corner( 1, 1 ), Corner( -1, 1 )];

		var world = new SceneWorld();
		try
		{
			var custom = new SceneCustomObject( world ) { Transform = Transform.Zero, Bounds = BBox.FromPositionAndSize( Vector3.Zero, 100000 ) };
			custom.RenderOverride = _ => Graphics.Draw( quad.AsSpan(), quad.Length, material, attributes );
			using var camera = new SceneCamera( "specialized constants" ) { World = world, Position = new Vector3( -100, 0, 0 ), Rotation = Rotation.Identity, BackgroundColor = Color.Black };
			camera.EnablePostProcessing = false;
			using var bitmap = new Bitmap( Size, Size );
			for ( int frame = 0; frame < 8; frame++ )
			{
				System.Threading.Thread.Sleep( 20 );
				camera.RenderToBitmap( bitmap );
			}

			if ( Environment.GetEnvironmentVariable( "SBOX_TEST_PICTURES" ) is { Length: > 0 } pictures )
			{
				Directory.CreateDirectory( pictures );
				File.WriteAllBytes( Path.Combine( pictures, $"specialized-constants-{name}.png" ), bitmap.ToPng() );
			}

			var sum = Vector3.Zero;
			foreach ( var p in bitmap.GetPixels() ) sum += new Vector3( Linear( p.r ), Linear( p.g ), Linear( p.b ) );
			return sum / (Size * Size);
		}
		finally
		{
			world.Delete();
		}
	}

	static float Linear( float srgb ) => srgb <= 0.04045f ? srgb / 12.92f : MathF.Pow( (srgb + 0.055f) / 1.055f, 2.4f );
}
