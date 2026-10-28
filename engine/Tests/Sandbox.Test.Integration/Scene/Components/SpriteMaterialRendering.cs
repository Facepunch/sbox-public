using NativeEngine;
using Sandbox.Rendering;
using Sandbox.Tasks;
using System;
using System.Runtime.InteropServices;

namespace SceneTests.Components;

/// <summary>
/// Draws sprite batches that use a custom material on the GPU and reads the result back, to show
/// that a material's parameters reach the sprites drawn with it.
/// </summary>
[TestClass]
public class SpriteMaterialRenderingTest
{
	/// <summary>
	/// The smallest sprite shader: the vertex stage of sprite_ps.shader (billboarding only), and a
	/// pixel stage that outputs one material parameter. <c>g_vTint</c> has no <c>Attribute()</c>, so it
	/// is a material parameter, set in the material editor or with <see cref="Material.Set(string, Color)"/>.
	/// </summary>
	const string TintSpriteShader = """
HEADER
{
	DevShader = true;
	Description = "Sprite material test";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#if ( D_OPAQUE == 0 )
		#define S_TRANSLUCENT 1
	#endif

	#include "common/shared.hlsl"

	struct SpriteData
	{
		float3 Position;
		float3 Rotation;
		float2 Scale;
		uint TintColor;
		uint OverlayColor;
		int TextureHandle;
		int RenderFlags;
		uint BillboardMode;
		uint FogStrengthCutout;
		uint Lighting;
		float DepthFeather;
		int SamplerIndex;
		int Splots;
		int Sequence;
		float SequenceTime;
		float RotationOffset;
		float4 MotionBlur;
		float3 Velocity;
		float4 BlendSheetUV;
		float2 Offset;
		uint CameraFade;
		int NormalTextureHandle;
		float RefractionStrength;
	};

	StructuredBuffer<SpriteData> Sprites < Attribute( "Sprites" ); >;
	StructuredBuffer<uint> SortLUT < Attribute( "SortLUT" ); >;
	int IsSorted < Attribute( "IsSorted" ); >;
	int CurrentBufferSize < Attribute( "SpriteCount" ); >;

	SpriteData GetSprite( uint index )
	{
		int spriteIndex = index;
		if ( IsSorted == 1 )
		{
			spriteIndex = SortLUT[CurrentBufferSize - 1 - index];
		}
		return Sprites[spriteIndex];
	}
}

struct PixelInput
{
	#include "common/pixelinput.hlsl"
	uint instanceID : TEXCOORD8;
};

struct VS_INPUT
{
	float3 pos : POSITION < Semantic( None ); >;
};

VS
{
	struct SpriteVertex
	{
		float4 position;
		float4 normal;
		float2 uv;
	};
	StructuredBuffer<SpriteVertex> Vertices < Attribute( "Vertices" ); >;

	PixelInput MainVs( uint vertexIndex : SV_VertexID, uint instanceID : SV_InstanceID )
	{
		SpriteVertex v = Vertices[vertexIndex];
		SpriteData sprite = GetSprite( instanceID );

		float3 scale = float3( sprite.Scale.x, sprite.Scale.y, 1.0 ) / 10;
		float3 cameraForward = g_vCameraDirWs;
		float3 cameraUp = -g_vCameraUpDirWs;
		float3 cameraRight = normalize( cross( cameraUp, cameraForward ) );
		cameraUp = normalize( cross( cameraForward, cameraRight ) );

		float4x4 transform;
		transform[0] = float4( cameraRight * scale.x, 0 );
		transform[1] = float4( cameraUp * scale.y, 0 );
		transform[2] = float4( -cameraForward * scale.z, 0 );
		transform[3] = float4( sprite.Position, 1 );

		PixelInput o;
		float3 vPositionWs = mul( transpose( transform ), float4( v.position.xyz, 1 ) ).xyz;
		o.vPositionWs = vPositionWs - g_vHighPrecisionLightingOffsetWs.xyz;
		o.vPositionPs = Position3WsToPs( vPositionWs );
		o.vNormalWs = -cameraForward;
		o.instanceID = instanceID;
		return o;
	}
}

PS
{
	#define CUSTOM_MATERIAL_INPUTS 1
	#include "common/pixel.hlsl"

	DynamicCombo( D_BLEND, 0..1, Sys( ALL ) );
	DynamicCombo( D_OPAQUE, 0..1, Sys( ALL ) );

	RenderState( BlendEnable, false );
	RenderState( DepthWriteEnable, false );
	RenderState( CullMode, NONE );

	float4 g_vTint < UiType( Color ); Default4( 1.0, 1.0, 1.0, 1.0 ); UiGroup( "Sprite,10/10" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		return float4( g_vTint.rgb, 1.0 );
	}
}
""";

	/// <summary>
	/// The stock pipeline draws nothing in unit tests (its lookup textures are null), so like
	/// CubemapSubmission this one clears and runs the camera's hooks in a single managed layer.
	/// </summary>
	sealed class HookPipeline : INativeRenderPipeline
	{
		[UnmanagedFunctionPointer( CallingConvention.StdCall )]
		delegate void OnRenderCallback( ManagedRenderSetup_t setup );

		static readonly DelegateFunctionPointer Callback = DelegateFunctionPointer.Get<OnRenderCallback>(
			setup =>
			{
				using ( new Graphics.Scope( in setup ) )
					Graphics.Context.Clear( Color.Transparent, true, true, false );
				Graphics.OnLayer( (int)Stage.AfterPostProcess, setup );
			} );

		public void AddLayersToView( CRenderPipelineSetup setup )
		{
			AddUIHook( "Sprite Material Test", Stage.AfterPostProcess, setup.m_pView, setup.m_viewport, setup.m_hFinalColor );
		}

		public ISceneLayer AddUIHook( string name, Stage stage, ISceneView view, RenderViewport viewport, SceneViewRenderTargetHandle color )
		{
			var layer = view.AddManagedProceduralLayer( name, viewport, Callback, default, true );
			layer.SetShaderMode( "Forward" );
			layer.SetOutput( color, -1, RenderColorSpace.RENDER_SRGB );
			layer.m_nLayerFlags |= LayerFlags.NeverRemove;
			return layer;
		}

		public void AddSkybox3DLayersToView( CRenderPipelineSetup setup ) { }
		public void OnSceneViewSubmitted( ISceneView view ) { }
		public void WarmUp() { }
		public void Shutdown() { }
	}

	static void AssertColor( Color expected, Color actual, string message )
	{
		Assert.IsTrue( MathF.Abs( expected.r - actual.r ) < 0.02f && MathF.Abs( expected.g - actual.g ) < 0.02f && MathF.Abs( expected.b - actual.b ) < 0.02f,
			$"{message}: expected {expected}, got {actual}" );
	}

	/// <summary>
	/// Two materials made from one shader, with different parameters, draw two batches that each
	/// show their own material's values, and a parameter changed at runtime shows up next frame.
	/// </summary>
	[TestMethod]
	public void MaterialParametersReachTheSpriteBatch()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GRAPHICS=1 to render sprites on the GPU." );

		var previousPipeline = RenderPipeline.Current;
		RenderPipeline.Current = new HookPipeline();

		var scene = new Scene();
		Material red = null, blue = null;

		try
		{
			using var sceneScope = scene.Push();

			var shader = SyncContext.RunBlocking( Shader.CompileAsync( TintSpriteShader ) );
			Assert.IsNotNull( shader );

			red = Material.Create( "sprite-material-test-red", shader );
			red.Set( "g_vTint", Color.Red );
			blue = Material.Create( "sprite-material-test-blue", shader );
			blue.Set( "g_vTint", Color.Blue );

			var left = scene.CreateObject().Components.Create<SpriteRenderer>();
			left.WorldPosition = new Vector3( 0, 25, 0 );
			left.Size = 40;
			left.MaterialOverride = red;

			var right = scene.CreateObject().Components.Create<SpriteRenderer>();
			right.WorldPosition = new Vector3( 0, -25, 0 );
			right.Size = 40;
			right.MaterialOverride = blue;

			using var target = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
			using var camera = new SceneCamera( "SpriteMaterialTest" )
			{
				World = scene.SceneWorld,
				FieldOfView = 60,
				ZNear = 1,
				ZFar = 1000,
				EnablePostProcessing = false
			};
			camera.Position = new Vector3( -100, 0, 0 );
			camera.Rotation = Rotation.Identity;

			// Draw the material batches exactly as the scene would, through their own RenderSceneObject
			camera.OnRenderStageHook += ( stage, _ ) =>
			{
				if ( stage != Stage.AfterPostProcess ) return;
				foreach ( var batch in scene.SceneWorld.SceneObjects.OfType<SpriteBatchSceneObject>() )
					batch.RenderSceneObject();
			};

			Bitmap Render()
			{
				// A few frames, so pipelines created in the background are ready
				for ( int i = 0; i < 10; ++i )
				{
					System.Threading.Thread.Sleep( 10 );
					EngineGlobal.SourceEnginePanelAppFrame();
					scene.GameTick( 0.0f );
					camera.RenderToTexture( target, null, default );
					g_pRenderDevice.Present( IntPtr.Zero );
					g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
					EngineLoop.DrainFrameEndDisposables();
					RenderTarget.EndOfFrame();
					EngineLoop.RunAsyncTasks();
				}

				return target.GetBitmap();
			}

			using ( var bitmap = Render() )
			{
				Assert.AreEqual( 2, scene.SceneWorld.SceneObjects.OfType<SpriteBatchSceneObject>().Count(), "One batch per material" );
				AssertColor( Color.Red, bitmap.GetPixel( 16, 32 ), "The red material's sprite" );
				AssertColor( Color.Blue, bitmap.GetPixel( 48, 32 ), "The blue material's sprite" );
			}

			red.Set( "g_vTint", Color.Green );

			using ( var bitmap = Render() )
			{
				AssertColor( Color.Green, bitmap.GetPixel( 16, 32 ), "A parameter changed at runtime" );
				AssertColor( Color.Blue, bitmap.GetPixel( 48, 32 ), "The other material is unaffected" );
			}
		}
		finally
		{
			scene.Destroy();
			red?.Destroy();
			blue?.Destroy();
			RenderPipeline.Current = previousPipeline;
		}
	}
}
