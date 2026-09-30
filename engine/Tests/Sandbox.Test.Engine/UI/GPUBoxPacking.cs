using System;
using System.Runtime.InteropServices;
using Sandbox.UI;

namespace UITests;

[TestClass]
public class GPUBoxPackingTest
{
	[TestMethod]
	public void LayoutMatchesShader()
	{
		Assert.AreEqual( 272, Marshal.SizeOf<UICssBoxBatched.BoxInstance>() );
		Assert.AreEqual( 228, Marshal.OffsetOf<UICssBoxBatched.BoxInstance>( nameof( UICssBoxBatched.BoxInstance.Flags ) ).ToInt32() );
		Assert.AreEqual( 252, Marshal.OffsetOf<UICssBoxBatched.BoxInstance>( nameof( UICssBoxBatched.BoxInstance.BackgroundClipRect ) ).ToInt32() );
		Assert.AreEqual( 268, Marshal.OffsetOf<UICssBoxBatched.BoxInstance>( nameof( UICssBoxBatched.BoxInstance.ShapeIndex ) ).ToInt32() );
	}

	[TestMethod]
	[DataRow( 45f, false )]
	[DataRow( -45f, false )]
	[DataRow( 45f, true )]
	[DataRow( -45f, true )]
	public void PackedPerspectiveClipRoundTripsProjectedPoints( float angle, bool nested )
	{
		var perspective = Matrix.Identity;
		perspective.M34 = -1f / 300f;
		var transform = Matrix.CreateRotationY( angle ) * perspective * Matrix.CreateTranslation( new Vector3( 240, 160, 0 ) );
		var inverse = transform.Inverted;
		if ( nested )
		{
			var local = Matrix.CreateScale( new Vector3( 1.5f, 0.75f, 1 ) )
				* Matrix.CreateRotationZ( 25 ) * Matrix.CreateTranslation( new Vector3( 30, -20, 0 ) );
			transform = local * transform;
			inverse *= local.Inverted;
		}

		var rect = new Rect( -80, -60, 160, 120 );
		var radii = new BorderRadii { TopLeft = new Vector2( 8, 12 ), BottomRight = new Vector2( 6, 4 ) };
		var scissor = Painter.Scissoring.Single( rect, radii, inverse );
		var packed = UICssBoxBatched.ScissorInstance.From( scissor );
		Assert.AreEqual( 1, packed.Count );
		Assert.AreEqual( rect.ToVector4(), packed.Clips[0].Rect );
		Assert.AreEqual( radii.Horizontal, packed.Clips[0].RadiiH );
		Assert.AreEqual( radii.Vertical, packed.Clips[0].RadiiV );
		Assert.AreEqual( inverse, scissor.Clips[0].Matrix, "Packing must retain the full CPU inverse for composition." );

		foreach ( var point in new[] { new Vector4( -60, -40, 0, 1 ), new Vector4( 0, 0, 0, 1 ), new Vector4( 70, 50, 0, 1 ) } )
		{
			var projected = transform.Transform( point );
			// The shader receives projected XY, not the original projected depth.
			var screen = new Vector4( projected.x / projected.w, projected.y / projected.w, 0, 1 );
			var restored = packed.Clips[0].TransformMat.Transform( screen );
			Assert.AreEqual( point.x, restored.x / restored.w, 0.001f );
			Assert.AreEqual( point.y, restored.y / restored.w, 0.001f );
		}
	}

	[TestMethod]
	public void PackedAffineClipKeepsItsInverse()
	{
		var accumulated = Matrix.CreateTranslation( new Vector3( 100, 200, 0 ) );
		for ( int i = 0; i < 12; i++ )
		{
			accumulated = Matrix.CreateRotationZ( 5 ) * accumulated;
			var inverse = accumulated.Inverted;
			var scissor = Painter.Scissoring.Single( new Rect( -30, -20, 60, 40 ), BorderRadii.Zero, inverse );
			var packed = UICssBoxBatched.ScissorInstance.From( scissor );
			Assert.AreEqual( inverse, packed.Clips[0].TransformMat, "Preserve the unused depth coefficient even when inversion rounds it away from one." );
		}

		foreach ( var inverse in new[]
		{
			Matrix.Identity,
			Matrix.CreateTranslation( new Vector3( 120, -40, 0 ) ),
			Matrix.CreateScale( new Vector3( -2, 0.75f, 1 ) ) * Matrix.CreateRotationZ( 30 )
				* Matrix.CreateSkewX( 15 ) * Matrix.CreateTranslation( new Vector3( 240, 160, 0 ) )
		} )
		{
			var scissor = Painter.Scissoring.Single( new Rect( 0, 0, 100, 80 ), BorderRadii.Zero, inverse );
			var packed = UICssBoxBatched.ScissorInstance.From( scissor );
			Assert.AreEqual( inverse, packed.Clips[0].TransformMat );
		}
	}

	[TestMethod]
	public void PackedDegenerateClipIsEmpty()
	{
		var inverse = Matrix.Identity;
		inverse.M33 = 0;
		var scissor = Painter.Scissoring.Single( new Rect( 10, 20, 100, 80 ), new BorderRadii { TopLeft = new Vector2( 8 ) }, inverse );
		var packed = UICssBoxBatched.ScissorInstance.From( scissor );
		Assert.AreEqual( 1, packed.Count, "Keep an empty clip rather than disabling clipping." );
		Assert.AreEqual( default( Vector4 ), packed.Clips[0].Rect );
		Assert.AreEqual( default( Vector4 ), packed.Clips[0].RadiiH );
		Assert.AreEqual( default( Vector4 ), packed.Clips[0].RadiiV );
		Assert.AreEqual( Matrix.Identity, packed.Clips[0].TransformMat );
	}

	[TestMethod]
	public void PackedSettingsMatchTheShaderBitLayout()
	{
		// Fixed wire value: mode 3, image mode 2, style 9, fill 1, repeat 4, clip 3.
		var box = new UICssBoxBatched.BoxInstance { Flags = 0xFFFFF99Bu };
		Assert.AreEqual( 3, box.Mode );
		Assert.AreEqual( 2, box.BorderImageMode );
		Assert.AreEqual( (int)BorderStyle.Outset, box.BorderStyle );
		Assert.AreEqual( 1, box.BorderImageFill );
		Assert.AreEqual( (int)BackgroundRepeat.Clamp, box.BackgroundRepeat );
		Assert.AreEqual( (int)BackgroundClip.Text, box.BackgroundClip );

		box.Mode = box.BorderImageMode = box.BorderStyle = box.BorderImageFill = box.BackgroundRepeat = box.BackgroundClip = 0;
		Assert.AreEqual( 0xFFFFC000u, box.Flags, "Clearing settings must preserve the other flags." );

		box.Mode = 3;
		box.BorderImageMode = 2;
		box.BorderStyle = (int)BorderStyle.Outset;
		box.BorderImageFill = 1;
		box.BackgroundRepeat = (int)BackgroundRepeat.Clamp;
		box.BackgroundClip = (int)BackgroundClip.Text;
		Assert.AreEqual( 0xFFFFF99Bu, box.Flags );
	}

	[TestMethod]
	public void SettingValuesPreservesOtherSettings()
	{
		// Reuse one instance across every supported combination so both setting and clearing bits are exercised.
		var box = new UICssBoxBatched.BoxInstance { Flags = 0xA5A5C000u };
		for ( int mode = 0; mode < 4; mode++ )
			for ( int imageMode = 0; imageMode < 3; imageMode++ )
				foreach ( var style in Enum.GetValues<BorderStyle>() )
					for ( int fill = 0; fill < 2; fill++ )
						foreach ( var repeat in Enum.GetValues<BackgroundRepeat>() )
							foreach ( var clip in Enum.GetValues<BackgroundClip>() )
							{
								box.Mode = mode;
								box.BorderImageMode = imageMode;
								box.BorderStyle = (int)style;
								box.BorderImageFill = fill;
								box.BackgroundRepeat = (int)repeat;
								box.BackgroundClip = (int)clip;
								Assert.AreEqual( mode, box.Mode );
								Assert.AreEqual( imageMode, box.BorderImageMode );
								Assert.AreEqual( (int)style, box.BorderStyle );
								Assert.AreEqual( fill, box.BorderImageFill );
								Assert.AreEqual( (int)repeat, box.BackgroundRepeat );
								Assert.AreEqual( (int)clip, box.BackgroundClip );
								Assert.AreEqual( 0xA5A5C000u, box.Flags & 0xFFFFC000u );
							}
	}
}
