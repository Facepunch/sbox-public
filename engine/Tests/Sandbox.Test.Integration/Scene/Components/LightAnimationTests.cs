using System;

namespace SceneTests.Components;

public partial class LightComponentTest
{
	/// <summary>
	/// Classic patterns use stepped ten-Hertz timing, wrap in both directions, and preserve normal brightness.
	/// </summary>
	[TestMethod]
	public void ClassicLightAnimationTiming()
	{
		var light = new PointLight { AnimationEnabled = true, AnimationPattern = LightAnimationPattern.FastStrobe };
		Assert.AreEqual( Color.White, light.EvaluateAnimation( 0.05 ) );
		Assert.AreEqual( Color.Black, light.EvaluateAnimation( 0.15 ) );
		Assert.AreEqual( Color.White, light.EvaluateAnimation( 1.25 ) );
		Assert.AreEqual( Color.Black, light.EvaluateAnimation( -0.05 ) );

		light.AnimationSpeed = 2;
		Assert.AreEqual( Color.Black, light.EvaluateAnimation( 0.075 ) );
		light.AnimationSpeed = 0;
		light.AnimationTimeOffset = 0.15f;
		Assert.AreEqual( Color.Black, light.EvaluateAnimation( 1000 ) );

		light.AnimationPattern = LightAnimationPattern.SlowStrobe;
		light.AnimationTimeOffset = 0.85f;
		Assert.AreEqual( 25 / 12.0f, light.EvaluateAnimation( 0 ).r );

		foreach ( var style in Enum.GetValues<LightAnimationPattern>() )
		{
			if ( style == LightAnimationPattern.Custom ) continue;

			light.AnimationPattern = style;
			light.AnimationSpeed = 1;
			light.AnimationTimeOffset = 0;
			var pattern = Light.GetAnimationPattern( light.AnimationPattern );
			Assert.IsTrue( pattern.Length > 0 );
			Assert.AreEqual( light.EvaluateAnimation( 0.05 ), light.EvaluateAnimation( pattern.Length / 10.0 + 0.05 ) );
			Assert.IsTrue( float.IsFinite( light.EvaluateAnimation( double.MaxValue ).r ) );
		}
	}

	/// <summary>
	/// Animation updates all light types on scene ticks and turning it off restores the authored HDR color immediately.
	/// </summary>
	[TestMethod]
	[DataRow( typeof( PointLight ) )]
	[DataRow( typeof( SpotLight ) )]
	[DataRow( typeof( DirectionalLight ) )]
	public void LightAnimationUpdatesAndRestoresColor( Type lightType )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var go = scene.CreateObject();
		var light = (Light)go.Components.Create( lightType );
		var tint = new Color( 2, 0.5f, 0.25f, 0.4f );
		light.LightColor = tint;
		light.Intensity = 3;
		light.Temperature = 3000;
		var authored = (Vector3)light.SceneObject.LightColor;
		Assert.IsFalse( light.AnimationEnabled );

		light.AnimationPattern = LightAnimationPattern.FastStrobe;
		light.AnimationEnabled = true;
		scene.GameTick( 0.15 );
		Assert.AreEqual( Vector3.Zero, (Vector3)light.SceneObject.LightColor );
		Assert.AreEqual( tint, light.LightColor );
		Assert.AreEqual( 3, light.Intensity );
		Assert.AreEqual( 3000, light.Temperature );

		light.AnimationEnabled = false;
		Assert.AreEqual( authored, (Vector3)light.SceneObject.LightColor );
		light.AnimationEnabled = true;
		light.Enabled = false;
		light.AnimationEnabled = false;
		light.Enabled = true;
		Assert.AreEqual( authored, (Vector3)light.SceneObject.LightColor );

		go.Destroy();
		scene.ProcessDeletes();
	}

	/// <summary>
	/// Custom loops combine gradient color, alpha, curve brightness and duration, including non-default curve ranges.
	/// </summary>
	[TestMethod]
	public void CustomLightAnimationLoops()
	{
		var light = new PointLight
		{
			AnimationEnabled = true,
			AnimationPattern = LightAnimationPattern.Custom,
			AnimationDuration = 2,
			AnimationCurve = 2,
			AnimationGradient = new Gradient( new( 0, Color.Red.WithAlpha( 0.5f ) ), new( 1, Color.Blue.WithAlpha( 0.5f ) ) )
		};

		Assert.AreEqual( Color.Red, light.EvaluateAnimation( 0 ) );
		Assert.AreEqual( new Color( 0.5f, 0, 0.5f ), light.EvaluateAnimation( 1 ) );
		Assert.AreEqual( light.EvaluateAnimation( 1 ), light.EvaluateAnimation( 3 ) );
		Assert.AreEqual( light.EvaluateAnimation( 1 ), light.EvaluateAnimation( -1 ) );

		light.AnimationGradient = Color.White;
		light.AnimationCurve = Curve.Linear with { TimeRange = new Vector2( 10, 20 ), ValueRange = new Vector2( 0, 4 ) };
		Assert.AreEqual( new Color( 2, 2, 2 ), light.EvaluateAnimation( 1 ) );
		light.AnimationCurve = -2;
		Assert.AreEqual( Color.Black, light.EvaluateAnimation( 1 ) );
		light.AnimationCurve = default;
		light.AnimationGradient = default;
		Assert.AreEqual( Color.White, light.EvaluateAnimation( 1 ) );
	}

	/// <summary>
	/// Invalid timing and curve values cannot send non-finite animation multipliers to the renderer.
	/// </summary>
	[TestMethod]
	public void LightAnimationRejectsInvalidInputs()
	{
		var light = new SpotLight
		{
			AnimationEnabled = true,
			AnimationPattern = LightAnimationPattern.Custom,
			AnimationDuration = 0,
			AnimationSpeed = float.NaN,
			AnimationTimeOffset = float.PositiveInfinity,
			AnimationCurve = float.NaN
		};
		Assert.AreEqual( 0.01f, light.AnimationDuration );
		Assert.AreEqual( 1f, light.AnimationSpeed );
		Assert.AreEqual( 0f, light.AnimationTimeOffset );
		Assert.AreEqual( Color.Black, light.EvaluateAnimation( double.NaN ) );
		light.AnimationDuration = float.NaN;
		Assert.AreEqual( 2f, light.AnimationDuration );
		light.AnimationEnabled = false;
		Assert.AreEqual( Color.White, light.EvaluateAnimation( 1 ) );
	}

	/// <summary>
	/// Saving and loading retains custom animation settings without replacing the authored tint or intensity.
	/// </summary>
	[TestMethod]
	public void LightAnimationSettingsRoundTrip()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var go = scene.CreateObject();
		var light = go.Components.Create<SpotLight>();
		light.LightColor = new Color( 2, 1, 0.5f );
		light.Intensity = 4;
		light.AnimationPattern = LightAnimationPattern.Custom;
		light.AnimationDuration = 3;
		light.AnimationSpeed = 0.5f;
		light.AnimationTimeOffset = -0.25f;
		light.AnimationCurve = Curve.Linear;
		light.AnimationGradient = new Gradient( new( 0, Color.Red ), new( 1, Color.Blue ) );
		light.AnimationEnabled = true;
		var sample = light.EvaluateAnimation( 0.75 );

		var loaded = SerializeRoundTrip( scene, go ).Components.Get<SpotLight>();
		Assert.IsTrue( loaded.AnimationEnabled );
		Assert.AreEqual( LightAnimationPattern.Custom, loaded.AnimationPattern );
		Assert.AreEqual( 3f, loaded.AnimationDuration );
		Assert.AreEqual( 0.5f, loaded.AnimationSpeed );
		Assert.AreEqual( -0.25f, loaded.AnimationTimeOffset );
		Assert.AreEqual( sample, loaded.EvaluateAnimation( 0.75 ) );
		Assert.AreEqual( new Color( 2, 1, 0.5f ), loaded.LightColor );
		Assert.AreEqual( 4f, loaded.Intensity );

		loaded.GameObject.Destroy();
		scene.ProcessDeletes();
	}
}
