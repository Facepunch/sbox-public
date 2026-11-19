using Borealis.Backend;
using Borealis.Source2;
using Sandbox.ShaderCompiler;
using System.IO;

namespace BorealisGpuTests;

/// <summary>
/// A package shader's embedded source written against an older engine's includes compiles against today's
/// (<see cref="EmbeddedCompatibility"/>, docs/managed/pass-modules.md, "Shaders an older engine compiled"): its copies of
/// the engine's cbuffers and samplers, the names the engine has dropped, HLSL's classes and vector truncation, and a mode
/// whose pixel stage vfx disabled. Compiles only; no device.
/// </summary>
[TestClass]
public class EmbeddedCompatibilityTests
{
	static (SlangCompiler Compiler, string Shaders) Compiler()
	{
		var core = ContentFiles.FindGame().Roots.FirstOrDefault( r => File.Exists( Path.Combine( r, "shaders", "sbox", "embedded_end.slangh" ) ) );
		if ( core is null ) Assert.Inconclusive( "No core shaders" );
		var native = EngineFolders.NativeIn( Path.GetDirectoryName( core ) );
		if ( !File.Exists( Path.Combine( native, EngineFolders.Library( "slang" ) ) ) ) Assert.Inconclusive( "The engine's Slang isn't built" );
		var shaders = Path.Combine( core, "shaders" );
		return (new SlangCompiler( native, [shaders] ), shaders);
	}

	const string Vertex = """
		#include "system.fxc"
		#include "common.fxc"
		struct VS_INPUT { float3 vPositionOs : POSITION < Semantic( PosXyz ); >; float2 vUv : TEXCOORD0 < Semantic( LowPrecisionUv ); >; };
		struct PS_INPUT { float4 vPositionPs : SV_Position; float3 vPositionWs : TEXCOORD1; float2 vUv : TEXCOORD0; };
		PS_INPUT MainVs( VS_INPUT i )
		{
			PS_INPUT o;
			o.vPositionWs = i.vPositionOs;
			o.vPositionPs = Position3WsToPs( i.vPositionOs );
			o.vUv = i.vUv;
			return o;
		}
		""";

	static string Module( string pixel, string name ) => PassConverter.FromEmbedded(
		new Dictionary<string, string> { ["VFX_PROGRAM_VS"] = Vertex, ["VFX_PROGRAM_PS"] = pixel }, [("F_TEXTURE_FILTERING", 0, 4)], name );

	static void Compiles( string pixel, string name, Dictionary<string, int> values = null )
	{
		var (compiler, shaders) = Compiler();
		var module = PassModule.FromText( Path.Combine( Path.GetTempPath(), name + ".slang" ), Module( pixel, name ), [shaders] );
		values ??= [];
		foreach ( var stage in new[] { StageKind.Vertex, StageKind.Pixel } )
		{
			try
			{
				Assert.IsTrue( compiler.CompilePass( module, stage, values ).Stage.Spirv.Length > 0, $"{stage} has no code" );
			}
			catch ( ShaderCompileException e )
			{
				Assert.Fail( $"{stage}: {e.Errors}" );
			}
		}
	}

	/// <summary>
	/// A package's own lighting code with copies of the view's cbuffers as the older engine had them (each its own, the
	/// sun's with a bool the engine now reads from its flags) and of a sampler the engine declares: each is declared once,
	/// by the engine, and the package reads the engine's.
	/// </summary>
	[TestMethod]
	public void PackageCopiesOfEngineDeclarationsAreTheEngines()
	{
		const string pixel = """
			#include "system.fxc"
			#include "common.fxc"
			#ifndef CONST
			#define CONST
			cbuffer PerViewConstantBuffer_t {
				float4x4 g_matWorldToProjection;
				float3 g_vCameraPositionWs;
			};
			#endif
			cbuffer DirectionalLightCB {
				float4 g_DirectionalLightColor; // w fogstrength
				float4 g_DirectionalLightDirection;
				bool g_DirectionalLightEnabled;
			};
			cbuffer ClusteredLightingConstants {
				float4 ClusterCounts;
				float4 ClusterZParams;
				#define ClusterNear ClusterZParams.z
			};
			SamplerState g_sBilinearClamp < Filter(BILINEAR); Address(CLAMP); >;
			#include "common/classes/ClusterCulling.hlsl"
			Texture2D g_tColor < Attribute( "Color" ); >;
			struct PS_INPUT { float4 vPositionPs : SV_Position; float3 vPositionWs : TEXCOORD1; float2 vUv : TEXCOORD0; };
			float4 MainPs( PS_INPUT i ) : SV_Target0
			{
				float3 c = g_tColor.Sample( g_sBilinearClamp, i.vUv ).rgb * ClusterNear;
				if ( g_DirectionalLightEnabled ) c *= g_DirectionalLightColor.rgb;
				return float4( c + g_vCameraPositionWs, 1 );
			}
			""";
		Compiles( pixel, "embedded_engine_copies" );
	}

	/// <summary>
	/// What an older engine's includes had and today's don't: system.fxc's Translucent(), material.hlsl's TextureFiltering,
	/// the cube shadow's Poisson discs, the light lookups by world position and by tile, HLSL's classes
	/// and a wider vector truncated into a narrower one.
	/// </summary>
	[TestMethod]
	public void NamesTheEngineDroppedStillCompile()
	{
		const string pixel = """
			#include "system.fxc"
			#include "common.fxc"
			Translucent();
			#include "common/classes/Light.hlsl"
			Texture2D g_tColor < Attribute( "Color" ); >;
			class Tint
			{
				float3 Color;
				float3 Apply( float3 c ) { return c * Color; }
			};
			struct PS_INPUT { float4 vPositionPs : SV_Position; float3 vPositionWs : TEXCOORD1; float2 vUv : TEXCOORD0; };
			float4 MainPs( PS_INPUT i ) : SV_Target0
			{
				float4 sampled = g_tColor.Sample( TextureFiltering, i.vUv + PCFDiscSamples5[1] * 0.001 );
				float3 c = 0;
				c += sampled;
				for ( uint n = 0; n < Light::Count( i.vPositionWs ); n++ ) c += Light::From( i.vPositionWs, n, i.vUv ).Color;
				for ( uint t = 0; t < Light::Count( i.vPositionPs.xy ); t++ ) c += Light::From( i.vPositionPs.xy, i.vPositionWs, t ).Color;
				Tint tint;
				tint.Color = 1;
				return float4( tint.Apply( c ), 1 );
			}
			""";
		Compiles( pixel, "embedded_dropped_names" );
	}

	/// <summary>
	/// vfx's <c>#define MainPs Disabled</c> for a mode with no pixel stage (a depth pass without alpha test): the stage isn't
	/// there with those values, and is with the others.
	/// </summary>
	[TestMethod]
	public void DisabledPixelStageIsLeftOut()
	{
		const string pixel = """
			#include "system.fxc"
			StaticCombo( S_MODE_DEPTH, 0..1, Sys( ALL ) );
			struct PS_INPUT { float4 vPositionPs : SV_Position; float3 vPositionWs : TEXCOORD1; float2 vUv : TEXCOORD0; };
			#if ( S_MODE_DEPTH )
				#define MainPs Disabled
			#endif
			float4 MainPs( PS_INPUT i ) : SV_Target0 { return 1; }
			""";
		var module = PassModule.FromText( "embedded_disabled.slang", Module( pixel, "embedded_disabled" ) );
		Assert.IsTrue( module.Has( StageKind.Pixel, new Dictionary<string, int> { ["S_MODE_DEPTH"] = 0 } ) );
		Assert.IsFalse( module.Has( StageKind.Pixel, new Dictionary<string, int> { ["S_MODE_DEPTH"] = 1 } ) );
		Compiles( pixel, "embedded_disabled", new Dictionary<string, int> { ["S_MODE_DEPTH"] = 0 } );
	}

	/// <summary>The samplers the converter leaves to the engine are common_samplers.fxc's.</summary>
	[TestMethod]
	public void EngineSamplersAreCommonSamplers()
	{
		var (_, shaders) = Compiler();
		var declared = System.Text.RegularExpressions.Regex.Matches( File.ReadAllText( Path.Combine( shaders, "common_samplers.fxc" ) ), @"^\s*SamplerState\s+(\w+)", System.Text.RegularExpressions.RegexOptions.Multiline )
			.Select( m => m.Groups[1].Value ).ToArray();
		CollectionAssert.AreEquivalent( declared, EmbeddedCompatibility.EngineSamplers );
	}
}
