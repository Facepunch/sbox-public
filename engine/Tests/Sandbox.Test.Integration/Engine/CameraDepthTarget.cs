using System;

namespace EngineTests;

/// <summary>
/// Validate caller-owned depth attachments for Borealis camera captures.
/// </summary>
[TestClass]
public class CameraDepthTargetTest
{
	/// <summary>
	/// A stencil-free attachment cannot preserve the viewmodel prepass's stencil mask.
	/// </summary>
	[TestMethod]
	[DataRow( ImageFormat.D16 )]
	[DataRow( ImageFormat.D32 )]
	public void RejectsStencilFreeDepth( ImageFormat format )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var color = Texture.CreateRenderTarget().WithSize( 32, 32 ).Create();
		using var depth = Texture.CreateRenderTarget().WithSize( 32, 32 ).WithFormat( format ).Create();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();

		try
		{
			Assert.ThrowsException<ArgumentException>( () => camera.RenderToTexture( color, depth, default ) );
		}
		finally
		{
			scene.Destroy();
		}
	}

	/// <summary>
	/// Reject incompatible attachments before submitting a view.
	/// </summary>
	[TestMethod]
	public void RejectsWrongDepthSizeAndFormat()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var color = Texture.CreateRenderTarget().WithSize( 64, 64 ).Create();
		using var smallDepth = Texture.CreateRenderTarget().WithSize( 32, 32 ).WithFormat( ImageFormat.D24S8 ).Create();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();

		try
		{
			Assert.ThrowsException<ArgumentException>( () => camera.RenderToTexture( color, smallDepth, default ) );
			Assert.ThrowsException<ArgumentException>( () => camera.RenderToTexture( color, color, default ) );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void DrawsIntoMatchingColorAndDepth()
	{
		TestGraphics.Require();
		var scene = new Scene();
		using var scope = scene.Push();
		using var color = Texture.CreateRenderTarget().WithSize( 32, 32 ).WithFormat( ImageFormat.RGBA8888 ).Create();
		using var depth = Texture.CreateRenderTarget().WithSize( 32, 32 ).WithFormat( ImageFormat.D24S8 ).Create();
		var camera = scene.CreateObject().Components.Create<CameraComponent>();

		try
		{
			Assert.IsTrue( camera.RenderToTexture( color, depth, default, out var view ) );
			Assert.IsTrue( view.IsValid );
			Assert.IsTrue( depth.DrawnByView, "The caller's depth texture received the captured view." );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
