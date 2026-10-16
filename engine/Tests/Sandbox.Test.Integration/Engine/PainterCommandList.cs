using Sandbox.Rendering;
using System;

#pragma warning disable CS0618 // These tests verify interoperability with the legacy HudPainter API.

namespace EngineTests;

/// <summary>
/// Painter recording that needs a render device: textures, bindless samplers and layer compositing
/// materials. The rest of the command list tests live in Sandbox.Test.Engine (UI/PainterCommandList.cs).
/// </summary>
[TestClass]
public class PainterCommandListTest
{
	[TestMethod]
	public void TextureSamplingDefaults()
	{
		var list = new CommandList();
		var rect = new Rect( 0, 0, 32, 32 );
		using ( var painter = Painter.Begin( list, rect ) )
		{
			painter.Texture( Texture.White, rect );
			painter.Fill = Fill.Image( Texture.White );
			painter.Rect( rect );
		}
		new HudPainter( list ).DrawTexture( Texture.White, rect );
		using var inspection = Painter.Begin( list, rect );
		var recorded = inspection.ActiveContext.Batcher.Instances;
		Assert.AreEqual( 3, recorded.Count );
		Assert.AreEqual( recorded[1].SamplerIndex, recorded[0].SamplerIndex );
		Assert.AreEqual( SamplerState.GetBindlessIndex( new SamplerState
		{
			AddressModeU = TextureAddressMode.Clamp,
			AddressModeV = TextureAddressMode.Clamp,
			Filter = FilterMode.Anisotropic
		} ), recorded[2].SamplerIndex );
	}

	[TestMethod]
	public void LegacyHudSupportsGeneralMatrices()
	{
		var list = new CommandList();
		var hud = new HudPainter( list );
		Matrix[] transforms =
		[
			Matrix.CreateScale( new Vector3( 2, 2, 2 ) ),
			Matrix.CreateRotationY( 30 ),
			Matrix.CreateTranslation( new Vector3( 10, 20, 30 ) )
		];
		foreach ( var transform in transforms )
		{
			list.Reset();
			hud.SetMatrix( transform );
			hud.DrawRect( new Rect( 0, 0, 20, 20 ), Color.Red );
			hud.DrawTexture( Texture.White, new Rect( 20, 0, 20, 20 ) );
			using var painter = Painter.Begin( list, new Rect( 0, 0, 100, 100 ) );
			var recorded = painter.ActiveContext.Batcher.Instances;
			Assert.AreEqual( 2, recorded.Count );
			Assert.AreEqual( transform, Transform( list, recorded[0] ) );
			Assert.AreEqual( transform, Transform( list, recorded[1] ) );
			Assert.AreEqual( Matrix.Identity, painter.Transform );
			bool rejected = false;
			try { painter.Transform = transform; }
			catch ( ArgumentException ) { rejected = true; }
			Assert.IsTrue( rejected, "Painter's public transform remains 2D." );
		}
	}

	[TestMethod]
	public void PanelAndLayerPaintersCannotClearTheDestination()
	{
		var commands = new CommandList();
		var bounds = new Rect( 0, 0, 64, 64 );
		using var painter = Painter.Begin( commands, bounds );
		using ( var destination = painter.WithDestination( bounds, 1, 1, BlendMode.Normal, Matrix.Identity ) )
		{
			bool panelRejected = false;
			try { destination.Painter.Clear( Color.Red ); }
			catch ( InvalidOperationException ) { panelRejected = true; }
			Assert.IsTrue( panelRejected );
		}

		using var layer = painter.BeginLayer( bounds );
		bool rejected = false;
		try { painter.Clear( Color.Red ); }
		catch ( InvalidOperationException ) { rejected = true; }
		Assert.IsTrue( rejected );
	}

	static Matrix Transform( CommandList list, UICssBoxBatched.BoxInstance instance )
	{
		var batcher = list.FindResource<Painter.Context>().Batcher;
		return batcher.Transforms[instance.TransformIndex].Mat;
	}

}
