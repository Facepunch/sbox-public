using NativeEngine;
using Sandbox.Rendering;
using System;

namespace SceneTests.Components;

/// <summary>
/// Checks uncovered HDR pixels in standalone and secondary camera views without involving water shading.
/// </summary>
[TestClass, DoNotParallelize]
public class CameraClearRenderingTests
{
	const int Width = 128;
	const int Height = 96;

	/// <summary>
	/// A camera looking at an empty world must retain its authored background through the native HDR pipeline.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void EmptyDirectHdrViewKeepsItsBackground( bool postProcess )
	{
		RequireGraphics();
		var scene = new Scene();
		using var scope = scene.Push();
		using var target = CreateTarget( "camera-empty-direct-clear" );
		try
		{
			var camera = CreateCamera( scene );
			RenderFrames( scene, camera, target, postProcess );
			AssertGreen( target, $"Direct HDR, post processing {postProcess}" );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// A visible renderer schedules an empty child view whose background is read directly after GPU completion.
	/// The witness is excluded from that view, so no geometry can conceal a missing or discarded clear.
	/// </summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public void EmptyChildHdrViewKeepsItsBackground( bool postProcess, bool refraction )
	{
		RequireGraphics();
		var scene = new Scene();
		using var scope = scene.Push();
		using var target = CreateTarget( "camera-empty-child-parent" );
		try
		{
			var camera = CreateCamera( scene );
			scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White;
			var witness = scene.CreateObject().Components.Create<ModelRenderer>();
			witness.Model = Model.Load( "models/dev/box.vmdl" );
			witness.MaterialOverride = Material.Load( "materials/dev/primary_red.vmat" );
			Assert.IsTrue( witness.Model.IsValid );
			Assert.IsTrue( witness.MaterialOverride.IsValid );
			witness.WorldScale = new Vector3( 512 / witness.Model.Bounds.Size.x,
				512 / witness.Model.Bounds.Size.y, 4 / witness.Model.Bounds.Size.z );
			witness.GameObject.Tags.Add( "clear-witness" );

			var commands = new CommandList( "Empty child HDR clear regression" );
			var child = commands.GetRenderTarget( "Empty child HDR clear", 1,
				ImageFormat.RGBA16161616F, ImageFormat.None );
			var setup = new ViewSetup
			{
				EnablePostprocessing = false,
				EnableUI = false,
				EnableDebugOverlays = false,
				ExcludeTags = new TagSet( ["clear-witness"] ),
				ViewHash = 73019
			};
			var view = refraction
				? commands.DrawRefraction( camera, new Plane( Vector3.Zero, Vector3.Up ), child,
					new RefractionSetup { RenderBehind = true, ViewSetup = setup } )
				: commands.DrawView( camera, child, setup );
			commands.Attributes.Set( "ClearRegressionChildValid", view.IsValid );
			string childName = child.Name;
			Texture childTexture = null;
			bool valid = false;
			int executions = 0;
			commands.AddAction( () =>
			{
				childTexture = commands.GetRenderTarget( childName ).ColorTarget;
				valid = Graphics.Attributes.GetBool( "ClearRegressionChildValid" );
				executions++;
			} );
			witness.ExecuteBefore = commands;

			RenderFrames( scene, camera, target, postProcess );
			Assert.IsTrue( executions > 0 && valid && childTexture is not null,
				"The visible witness did not schedule a valid child view." );
			AssertGreen( childTexture, $"Child {(refraction ? "DrawRefraction" : "DrawView")}, parent post processing {postProcess}" );
		}
		finally
		{
			scene.Destroy();
		}
	}

	static CameraComponent CreateCamera( Scene scene )
	{
		var camera = scene.CreateObject().Components.Create<CameraComponent>();
		camera.CustomSize = new Vector2( Width, Height );
		camera.WorldPosition = new Vector3( -200, 0, -64 );
		camera.WorldRotation = Rotation.LookAt( -camera.WorldPosition, Vector3.Up );
		camera.FieldOfView = 90;
		camera.ZNear = 1;
		camera.ZFar = 4096;
		camera.BackgroundColor = Color.Green;
		camera.AutoExposure.Enabled = false;
		camera.SceneCamera.Attributes.Set( "msaa", 0 );
		return camera;
	}

	static Texture CreateTarget( string name ) => Texture.CreateRenderTarget().WithSize( Width, Height )
		.WithFormat( ImageFormat.RGBA16161616F ).Create( name );

	static void RenderFrames( Scene scene, CameraComponent camera, Texture target, bool postProcess )
	{
		for ( int frame = 0; frame < 20; frame++ )
		{
			EngineGlobal.SourceEnginePanelAppFrame();
			scene.GameTick( 1.0 / 60 );
			scene.PreRender();
			Assert.IsTrue( camera.RenderToTexture( target, new ViewSetup
			{
				EnablePostprocessing = postProcess,
				EnableUI = false,
				EnableDebugOverlays = false
			} ) );
			g_pRenderDevice.Present( IntPtr.Zero );
			g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
			EngineLoop.DrainFrameEndDisposables();
			RenderTarget.EndOfFrame();
			EngineLoop.RunAsyncTasks();
		}
	}

	static void AssertGreen( Texture texture, string description )
	{
		var data = new ushort[Width * Height * 4];
		texture.GetPixels<ushort>( (0, 0, Width, Height), 0, 0, data, ImageFormat.RGBA16161616F );
		var average = Vector3.Zero;
		float maxError = 0;
		for ( int i = 0; i < data.Length; i += 4 )
		{
			var rgb = new Vector3( (float)BitConverter.UInt16BitsToHalf( data[i] ),
				(float)BitConverter.UInt16BitsToHalf( data[i + 1] ), (float)BitConverter.UInt16BitsToHalf( data[i + 2] ) );
			Assert.IsTrue( rgb.IsFinite, $"{description}: nonfinite background." );
			average += rgb;
			maxError = MathF.Max( maxError, rgb.Distance( new Vector3( 0, 1, 0 ) ) );
		}
		average /= Width * Height;
		Assert.IsTrue( maxError < 0.02f, $"{description}: expected an entirely green uncovered view, average={average}, maximum error={maxError}." );
	}

	static void RequireGraphics()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );
	}
}
