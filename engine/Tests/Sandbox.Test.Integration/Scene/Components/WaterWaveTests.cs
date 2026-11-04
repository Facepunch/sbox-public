namespace SceneTests.Components;

/// <summary>
/// Swimming immersion follows the waves and the volume's bottom.
/// </summary>
[TestClass]
public class WaterWaveTests
{
	/// <summary>
	/// The discovery trigger's full crest envelope does not overstate swimming immersion.
	/// Inclined water, reversed character direction and the volume's bottom use the same fraction.
	/// </summary>
	[TestMethod]
	public void ImmersionUsesWaveSurfaceAndVolumeBottom()
	{
		if ( !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires SBOX_TEST_GRAPHICS=1." );

		var scene = Scene.CreateEditorScene();
		using var scope = scene.Push();
		try
		{
			var water = scene.CreateObject().Components.Create<WaterBody>();
			water.Depth = 20;
			water.WorldTransform = new Transform( new Vector3( 30, 40, 50 ), Rotation.From( 20, 30, 10 ), new Vector3( -2, 3, -4 ) );
			var foot = water.FromSurface( new Vector3( 0, 0, -20 ) );
			var head = water.FromSurface( new Vector3( 20, 0, 20 ) );
			Assert.AreEqual( 0.5f, water.SubmergedFraction( foot, head ), 0.0001f );
			Assert.AreEqual( 0.5f, water.SubmergedFraction( head, foot ), 0.0001f );
			Assert.AreEqual( 0.5f, water.SubmergedFraction( water.FromSurface( new Vector3( 0, 0, -100 ) ), water.FromSurface( new Vector3( 0, 0, -60 ) ) ), 0.0001f );
			water.Waves = true;
			water.WaveHeight = 40;
			water.WaveSpeed = 0;
			var point = new Vector2( 30, -20 );
			float height = water.SampleHeight( point );
			foot = water.FromSurface( new Vector3( point.x, point.y, height - 10 ) );
			head = water.FromSurface( new Vector3( point.x, point.y, height + 10 ) );
			Assert.AreEqual( 0.5f, water.SubmergedFraction( foot, head ), 0.0001f );
			Assert.AreEqual( 0f, water.SubmergedFraction( head, head + water.WorldRotation.Up * 10 ), 0.0001f );
			scene.EditorTick( 1, 1f / 60 );
			var trigger = scene.GetAll<BoxCollider>().Single();
			Assert.AreEqual( 40f, trigger.Center.z + trigger.Scale.z * 0.5f, 0.0001f );
			Assert.AreEqual( -80f, trigger.Center.z - trigger.Scale.z * 0.5f, 0.0001f );
			Assert.AreEqual( water, WaterBody.FromCollider( trigger ) );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
