using Sandbox.Engine.Shaders;
using Sandbox.Tasks;
using System.IO;
using System.Linq;

namespace EngineTests;

/// <summary>
/// Source between <c>#pragma sbox_slang_begin</c> and <c>#pragma sbox_slang_end</c> skips vfx's HLSL parser, which can't read
/// Slang, and reaches Slang's compile: how a recompiled package shader's Slang shims (operators, extensions) compile in the
/// engine's install-time recompile (sbox/embedded_end.slangh).
/// </summary>
[TestClass]
public class ShaderSlangOnlySourceTest
{
	const string Source = """
		HEADER
		{
			Description = "Slang between the pragmas";
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
				o.vPositionPs = float4( i.vPositionOs.xyz, 1.0 );
				return o;
			}
		}

		PS
		{
			#include "common.fxc"

			struct PS_INPUT
			{
				float4 vPositionPs : SV_Position;
			};

			float4 g_vTint < Attribute( "SlangOnlyTint" ); Default4( 1.0, 0.0, 0.0, 1.0 ); >;

			float4 MainPs( PS_INPUT i ) : SV_Target0
			{
				float3 c = float3( 0, 0, 1 ) + g_vTint;
				return float4( c, 1 );
			}

			#pragma sbox_slang_begin
			float3 operator+( float3 a, float4 b ) { return a + b.xyz; }
			extension vector<float, 2>
			{
				__implicit_conversion( 900 ) __init( float4 v ) { this = v.xy; }
			}
			#pragma sbox_slang_end
		}
		""";

	[TestMethod]
	public void SlangBetweenThePragmasCompiles()
	{
		var folder = EngineFileSystem.Root.GetFullPath( "/.source2/shadercache/compiled/tests" );
		Directory.CreateDirectory( folder );
		var path = Path.Combine( folder, "slang_only_source.shader" );
		File.WriteAllText( path, Source );

		try
		{
			var result = AsyncPump.RunSynchronously( () => ShaderCompile.Compile( path, "tests/slang_only_source.shader", new ShaderCompileOptions { ForceRecompile = true, ConsoleOutput = false }, default ) );
			var output = string.Join( "\n", result.Programs.SelectMany( p => p.Output ?? [] ) );
			Assert.IsTrue( result.Success, output );
			Assert.IsTrue( result.Programs.All( p => p.Success ), output );
		}
		finally
		{
			File.Delete( path );
			File.Delete( path + "_c" );
		}
	}
}
