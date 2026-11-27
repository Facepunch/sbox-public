using Borealis.Backend;
using Borealis.Source2;
using Borealis.Source2.Shaders;
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
	/// An older engine's pixel input had the face's side as <c>bIsFrontFace</c> (My Summer Cottage's shaders read it); today's
	/// has it as <c>face</c>, and only when back faces draw. Both compile, with back faces and without.
	/// </summary>
	[TestMethod]
	public void FrontFaceOfAnOlderPixelInputCompiles()
	{
		const string pixel = """
			#include "system.fxc"
			#include "common.fxc"
			struct PixelInput
			{
				#include "common/pixelinput.hlsl"
			};
			float4 MainPs( PixelInput i ) : SV_Target0
			{
				return i.bIsFrontFace ? float4( 1, 1, 1, 1 ) : float4( 0, 0, 0, 1 );
			}
			""";
		Compiles( pixel, "embedded_front_face" );
		Compiles( "#define S_RENDER_BACKFACES 1\n" + pixel, "embedded_front_face_backfaces" );
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
	/// A sampler an include declares in two <c>#if</c> branches (embedded_end.slangh's TextureFiltering) has the active
	/// branch's states, as vfx reads them from the preprocessed program, wherever the other branch is in the file.
	/// </summary>
	[TestMethod]
	public void IncludedSamplersTakeTheActiveBranchesStates()
	{
		const string header = """
			#ifdef SBOX_BRANCH_POINT
			SamplerState Chosen < Filter( POINT ); >;
			#else
			SamplerState Chosen < Filter( BILINEAR ); >;
			#endif
			#ifndef SBOX_BRANCH_UNDEFINED
			SamplerState Kept < Filter( TRILINEAR ); AddressU( CLAMP ); >;
			#else
			SamplerState Kept < Filter( POINT ); >;
			#endif
			""";
		const string pixel = """
			#include "system.fxc"
			#include "common.fxc"
			#define SBOX_BRANCH_POINT
			#include "embedded_branch_samplers.hlsl"
			Texture2D g_tColor < Attribute( "Color" ); >;
			struct PS_INPUT { float4 vPositionPs : SV_Position; float3 vPositionWs : TEXCOORD1; float2 vUv : TEXCOORD0; };
			float4 MainPs( PS_INPUT i ) : SV_Target0
			{
				return g_tColor.Sample( Chosen, i.vUv ) + g_tColor.Sample( Kept, i.vUv );
			}
			""";
		var (compiler, shaders) = Compiler();
		var folder = Path.Combine( Path.GetTempPath(), $"embedded_branches_{Guid.NewGuid():N}" );
		Directory.CreateDirectory( folder );
		try
		{
			File.WriteAllText( Path.Combine( folder, "embedded_branch_samplers.hlsl" ), header );
			var module = PassModule.FromText( Path.Combine( folder, "embedded_branches.slang" ), Module( pixel, "embedded_branches" ), [shaders] );
			var stage = compiler.CompilePass( module, StageKind.Pixel, new Dictionary<string, int>() ).Stage;
			Annotations Of( string name ) => stage.Bindings.Single( b => b.Kind == BindingKind.Sampler && b.Variable == name ).Annotations;
			Assert.AreEqual( "POINT", Of( "Chosen" ).Filter?.Trim(), "the branch the module's #define takes" );
			Assert.AreEqual( ("TRILINEAR", "CLAMP"), (Of( "Kept" ).Filter?.Trim(), Of( "Kept" ).AddressU?.Trim()), "the branch nothing defined takes" );
		}
		catch ( ShaderCompileException e )
		{
			Assert.Fail( e.Errors );
		}
		finally
		{
			Directory.Delete( folder, true );
		}
	}

	/// <summary>
	/// An older engine's screen-space trace gave the hit's uv as <c>HitClipSpace</c> and whether it hit as <c>ValidHit</c> (a
	/// water shader's reflections); today's gives <c>HitUv</c> and a confidence.
	/// </summary>
	[TestMethod]
	public void AnOlderScreenSpaceTraceCompiles()
	{
		const string pixel = """
			#include "system.fxc"
			#include "common.fxc"
			#include "math_general.fxc"
			#include "common/classes/ScreenSpaceTrace.hlsl"
			struct PS_INPUT { float4 vPositionPs : SV_Position; float3 vPositionWs : TEXCOORD1; float2 vUv : TEXCOORD0; };
			float4 MainPs( PS_INPUT i ) : SV_Target0
			{
				TraceResult trace = ScreenSpace::Trace( i.vPositionWs, normalize( i.vPositionWs ), 64 );
				float2 uv = saturate( trace.HitClipSpace.xy * g_vFrameBufferCopyInvSizeAndUvScale.zw );
				return float4( uv, 0, trace.ValidHit ? 1 : 0 );
			}
			""";
		Compiles( pixel, "embedded_older_trace" );
	}

	/// <summary>
	/// An older engine's <c>GetExtraPerInstanceShaderData</c> took the vertex input; today's takes the instance's transform
	/// index (a barrier shader's tint).
	/// </summary>
	[TestMethod]
	public void ExtraDataFromTheVertexInputCompiles()
	{
		const string vertex = """
			#include "system.fxc"
			#include "common.fxc"
			#include "common/shared.hlsl"
			struct VertexInput
			{
				#include "common/vertexinput.hlsl"
			};
			struct PixelInput
			{
				#include "common/pixelinput.hlsl"
				float4 vTintColor : TEXCOORD9;
			};
			#include "common/vertex.hlsl"
			PixelInput MainVs( VertexInput v )
			{
				PixelInput i = ProcessVertex( v );
				ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( v );
				i.vTintColor = extraShaderData.vTint;
				return FinalizeVertex( i );
			}
			""";
		var (compiler, shaders) = Compiler();
		var text = PassConverter.FromEmbedded( new Dictionary<string, string> { ["VFX_PROGRAM_VS"] = vertex, ["VFX_PROGRAM_PS"] = "#include \"system.fxc\"\nfloat4 MainPs() : SV_Target0 { return 1; }\n" }, [("F_TEXTURE_FILTERING", 0, 4)], "embedded_extra_data" );
		var module = PassModule.FromText( Path.Combine( Path.GetTempPath(), "embedded_extra_data.slang" ), text, [shaders] );
		try
		{
			Assert.IsTrue( compiler.CompilePass( module, StageKind.Vertex, new Dictionary<string, int>() ).Stage.Spirv.Length > 0 );
		}
		catch ( ShaderCompileException e )
		{
			Assert.Fail( e.Errors );
		}
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

	/// <summary>
	/// For vfx's recompile (ShaderUpgrade) the shims' text goes in whole, since vfx doesn't find <c>sbox/</c>'s includes,
	/// with their Slang (extensions, operators) between the pragmas that take it past vfx's own HLSL parser to Slang
	/// alone; and the package's copy of an engine cbuffer is the engine's there too.
	/// </summary>
	[TestMethod]
	public void RecompileTextHasTheShimsInline()
	{
		var (_, shaders) = Compiler();
		var text = EmbeddedCompatibility.ForRecompile( "#include \"system.fxc\"\ncbuffer DirectionalLightCB { float4 g_DirectionalLightColor; };\nTranslucent();\nfloat4 MainPs() : SV_Target0 { return g_DirectionalLightColor; }\n", shaders );
		Assert.IsFalse( text.Contains( "#include \"sbox/", StringComparison.Ordinal ), text );
		Assert.IsTrue( text.Contains( "#define Translucent()", StringComparison.Ordinal ) );
		Assert.IsTrue( text.Contains( "#include \"common/ViewEnvironment.hlsl\"", StringComparison.Ordinal ) );
		Assert.IsFalse( text.Contains( "cbuffer DirectionalLightCB", StringComparison.Ordinal ) );
		var begin = text.IndexOf( "#pragma sbox_slang_begin", StringComparison.Ordinal );
		var end = text.IndexOf( "#pragma sbox_slang_end", StringComparison.Ordinal );
		Assert.IsTrue( begin >= 0 && begin < text.IndexOf( "extension Light", StringComparison.Ordinal ), text );
		Assert.IsTrue( text.IndexOf( "float3 operator+( float3 a, float4 b )", StringComparison.Ordinal ) is var op && op > begin && op < end, text );
		Assert.IsTrue( end > text.IndexOf( "extension TraceResult", StringComparison.Ordinal ), text );
	}

	/// <summary>
	/// A package's own copy of an older system header declares common.fxc's descriptor sets (a post-process library's
	/// <c>Class/System.slang</c>); vfx's parser takes each once, so the package's are the engine's include.
	/// </summary>
	[TestMethod]
	public void PackageCopiesOfTheEnginesDescriptorSetsAreTheEngines()
	{
		const string pixel = """
			#include "system.fxc"
			#ifndef SYSTEM
			#define SYSTEM
			ExternalDescriptorSet g_globalLateBoundBindlessSet Slot 1;
			#endif
			#include "common.fxc"
			Texture2D g_tColor < Attribute( "Color" ); >;
			struct PS_INPUT { float4 vPositionPs : SV_Position; float3 vPositionWs : TEXCOORD1; float2 vUv : TEXCOORD0; };
			float4 MainPs( PS_INPUT i ) : SV_Target0 { return g_tColor.Sample( g_sBilinearClamp, i.vUv ); }
			""";
		var text = EmbeddedCompatibility.Apply( pixel );
		Assert.IsFalse( text.Contains( "ExternalDescriptorSet", StringComparison.Ordinal ), text );
		Assert.IsTrue( text.Contains( "#include \"common.fxc\" // g_globalLateBoundBindlessSet, declared by the engine", StringComparison.Ordinal ), text );
		Compiles( pixel, "embedded_descriptor_sets" );
	}

	/// <summary>
	/// Includes no package ships (a library's <c>tonemap_exempt.hlsl</c>, Mouse Fishing's materials; retro_shader's own
	/// <c>void_infection.hlsl</c>) are their stand-ins, so the shaders compile, here and, inlined, in vfx's recompile.
	/// </summary>
	[TestMethod]
	public void IncludesNoPackageShipsAreStandIns()
	{
		const string pixel = """
			#include "system.fxc"
			#include "common.fxc"
				#include "environmentsystems/shaders/tonemap_exempt.hlsl"
			#include "materials/shaders/void_infection.hlsl"
			Texture2D g_tColor < Attribute( "Color" ); >;
			float g_flTonemapExempt < Default1( 0 ); >;
			struct PS_INPUT { float4 vPositionPs : SV_Position; float3 vPositionWs : TEXCOORD1; float2 vUv : TEXCOORD0; };
			float4 MainPs( PS_INPUT i ) : SV_Target0
			{
				float voidFactor, fogFactor;
				GetVoidFactors( i.vPositionWs, g_flTime, voidFactor, fogFactor );
				float4 c = g_tColor.Sample( g_sBilinearClamp, i.vUv ) * voidFactor;
				return MarkTonemapExempt( float4( lerp( c.rgb, 0, fogFactor ), c.a ), g_flTonemapExempt );
			}
			""";
		Compiles( pixel, "embedded_stand_ins" );

		var (_, shaders) = Compiler();
		var text = EmbeddedCompatibility.ForRecompile( pixel, shaders );
		Assert.IsFalse( text.Contains( "tonemap_exempt.hlsl\"", StringComparison.Ordinal ) || text.Contains( "void_infection.hlsl\"", StringComparison.Ordinal ), text );
		Assert.IsFalse( text.Contains( "#include \"sbox/", StringComparison.Ordinal ), text );
		Assert.IsTrue( text.Contains( "float4 MarkTonemapExempt(", StringComparison.Ordinal ) && text.Contains( "void GetVoidFactors(", StringComparison.Ordinal ), text );
	}

	/// <summary>
	/// HLSL's truncation through the intrinsics and into a scalar (a water shader's <c>min( float4, float2 )</c>, a float
	/// function returning <c>float4( ... )</c>), while the engine's own calls (<c>max( float3, float )</c>,
	/// <c>min( float4, float )</c>) keep the built-ins: none is ambiguous, each result its size.
	/// </summary>
	[TestMethod]
	public void TruncationThroughIntrinsicsAndIntoScalarsCompiles()
	{
		const string pixel = """
			#include "system.fxc"
			#include "common.fxc"
			Texture2D g_tColor < Attribute( "Color" ); >;
			struct PS_INPUT { float4 vPositionPs : SV_Position; float3 vPositionWs : TEXCOORD1; float2 vUv : TEXCOORD0; };
			float Grey( float3 c )
			{
				float f = dot( c, float3( 0.3, 0.6, 0.1 ) );
				return float4( f.xxx, 1.0 );
			}
			float4 MainPs( PS_INPUT i ) : SV_Target0
			{
				float4 c = g_tColor.Sample( g_sBilinearClamp, i.vUv );
				float2 edge = min( c, g_vViewportSize - i.vUv ) + max( i.vUv, c );
				float3 lit = min( c, i.vPositionWs ) + max( i.vPositionWs, c );
				float3 floor3 = max( c.rgb - 0.5f, 0.0f );
				float4 ceil4 = min( c, 1.0f );
				return float4( lit * Grey( floor3 ) + float3( edge, 0 ), 1 ) * ceil4;
			}
			""";
		Compiles( pixel, "embedded_intrinsic_truncation" );
	}

	/// <summary>An older engine's <c>TerrainStruct.Resolution</c>, its units per texel today (Rust Legacy's grass).</summary>
	[TestMethod]
	public void AnOlderTerrainResolutionCompiles()
	{
		const string pixel = """
			#include "system.fxc"
			#include "common.fxc"
			#include "common/shared.hlsl"
			struct PixelInput
			{
				#include "common/pixelinput.hlsl"
			};
			#include "terrain/TerrainCommon.hlsl"
			float4 MainPs( PixelInput i ) : SV_Target0
			{
				float2 uv = i.vPositionWithOffsetWs.xy / ( 512.0 * Terrain::Get().Resolution );
				return float4( uv, 0, 1 );
			}
			""";
		Compiles( pixel, "embedded_older_terrain" );
	}

	/// <summary>
	/// An older engine's bindless lookup with an sRGB flag (MTL's sauna shaders, Heartland's grass), its DDGI volume lookup
	/// (Galaxy Wars RP's characters) and the base library's <c>InvProjectPosition</c> (an SSGI pass): each compiles as HLSL
	/// took it.
	/// </summary>
	[TestMethod]
	public void OlderBindlessDdgiAndBaseLibraryNamesCompile()
	{
		const string pixel = """
			#include "system.fxc"
			#include "common.fxc"
			#include "common/classes/Bindless.hlsl"
			#include "common/DDGI/DDGI.hlsl"
			int g_nMaskIdx < Attribute( "MaskIdx" ); >;
			struct PS_INPUT { float4 vPositionPs : SV_Position; float3 vPositionWs : TEXCOORD1; float2 vUv : TEXCOORD0; };
			float4 MainPs( PS_INPUT i ) : SV_Target0
			{
				Texture2D mask = Bindless::GetTexture2D( NonUniformResourceIndex( g_nMaskIdx ), false );
				float3 gi = DDGI::Evaluate( DDGI::GetVolume( i.vPositionWs ), i.vPositionWs, float3( 0, 0, 1 ), float3( 1, 0, 0 ) );
				float3 back = InvProjectPosition( float3( i.vUv, 0.5 ), g_matProjectionToWorld );
				return float4( gi + back, 1 ) * mask.Sample( g_sBilinearClamp, i.vUv );
			}
			""";
		Compiles( pixel, "embedded_older_bindless_ddgi" );
	}

	/// <summary>The samplers the converter leaves to the engine are sbox/samplers.slangh's (common_samplers.fxc's).</summary>
	[TestMethod]
	public void EngineSamplersAreCommonSamplers()
	{
		var (_, shaders) = Compiler();
		var declared = System.Text.RegularExpressions.Regex.Matches( File.ReadAllText( Path.Combine( shaders, SlangHeaders.Replaced["common_samplers.fxc"] ) ), @"^\s*SamplerState\s+(\w+)", System.Text.RegularExpressions.RegexOptions.Multiline )
			.Select( m => m.Groups[1].Value ).ToArray();
		CollectionAssert.AreEquivalent( declared, EmbeddedCompatibility.EngineSamplers );
	}

	/// <summary>
	/// A lit module's pixel stage (a material .shader's MainPs shading through ShadingModelStandard) gets a forward entry that
	/// writes the normal and roughness Shade kept at target 1, switched by <c>kForwardNormalsOutput</c>, as surfaces' forward
	/// entries do, so Borealis's screen-space reflections trace from it; others keep their own entry.
	/// </summary>
	[TestMethod]
	public void LitModulesWriteTheReflectionsNormals()
	{
		const string lit = "[shader( \"pixel\" )] float4 MainPs( PixelInput i, bool front : SV_IsFrontFace ) : SV_Target0\n{\n\treturn ShadingModelStandard::Shade( i, m );\n}\n";
		var wrapped = SlangCompiler.ForwardNormalsEntry( lit );
		StringAssert.Contains( wrapped, "float4 MainPsLit( PixelInput i, bool front : SV_IsFrontFace ) : SV_Target0" );
		StringAssert.Contains( wrapped, "ForwardOutput MainPs( PixelInput i, bool front : SV_IsFrontFace ) { return ForwardOut( MainPsLit( i, front ), s_ForwardNormalsKept ); }" );

		// Unlit, writing target 1 itself, or not returning a colour: as written
		const string unlit = "[shader( \"pixel\" )] float4 MainPs( PixelInput i ) : SV_Target0 { return 1; }\n";
		Assert.AreEqual( unlit, SlangCompiler.ForwardNormalsEntry( unlit ) );
		var ids = lit + "struct O { uint2 Id : SV_Target1; };\n";
		Assert.AreEqual( ids, SlangCompiler.ForwardNormalsEntry( ids ) );
		var output = lit.Replace( "float4 MainPs", "PS_OUTPUT MainPs" );
		Assert.AreEqual( output, SlangCompiler.ForwardNormalsEntry( output ) );

		// Compiled: the bark .shader's pixel stage has the switch and a float output at location 1; with no target there the
		// pipeline leaves it out (BackendDevice.FitOutputs)
		var (compiler, shaders) = Compiler();
		var bark = Path.Combine( AppContext.BaseDirectory, "Data", "bark.shader" );
		var module = PassModule.FromText( Path.Combine( Path.GetTempPath(), "bark_normals.slang" ), PassConverter.Convert( File.ReadAllText( bark ), "bark_normals" ), [shaders] );
		var pixel = compiler.CompilePass( module, StageKind.Pixel, new Dictionary<string, int>() ).Stage;
		var reflection = SpirvReflection.Read( pixel.Spirv );
		CollectionAssert.Contains( reflection.SpecializationIds.ToArray(), BackendDevice.NormalsOutputId );
		Assert.IsTrue( reflection.Outputs.Any( o => o.Location == 1 && o.Kind == SpirvScalar.Float ), "a float output at target 1" );
		Assert.IsTrue( reflection.Outputs.Any( o => o.Location == 0 ), "the colour stays at target 0" );
	}
}
