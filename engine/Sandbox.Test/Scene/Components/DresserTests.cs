namespace GameObjects.Components;

[TestClass]
public class DresserTests
{
	private static Model CitizenModel => Model.Load( "models/citizen/citizen.vmdl" );

	[TestMethod]
	public void ChangingManualAttributes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();

		var smr = go.Components.Create<SkinnedModelRenderer>();
		smr.Model = CitizenModel;

		var dresser = go.Components.Create<Dresser>();
		dresser.BodyTarget = smr;

		// Age
		dresser.ManualAge = 0.42f;
		Assert.AreEqual( 0.42f, smr.Attributes.GetFloat( "skin_age" ), "Age should have been set by dresser" );

		// Tint
		dresser.ManualTint = 0.84f;
		Assert.AreEqual( 0.84f, smr.Attributes.GetFloat( "skin_tint" ), "Tint should have been set by dresser" );

		// Height
		dresser.ManualHeight = 0.14f;
		Assert.AreNotEqual( 0f /* default unset */, smr.GetFloat( "scale_height" ), "Height should have been set by dresser" );
	}

	[TestMethod]
	public void DeserializingManualAttributes()
	{
		var scene = new Scene();
		using var sceneScope = scene.Push();
		var go = scene.CreateObject();

		var smr = go.Components.Create<SkinnedModelRenderer>();
		smr.Model = CitizenModel;

		// Create a dresser component
		var dresser = go.Components.Create<Dresser>();
		dresser.BodyTarget = smr;

		dresser.ManualAge = 0.42f;
		dresser.ManualTint = 0.84f;
		dresser.ManualHeight = 0.14f;

		// One way to trigger a deserialize is to clone a GameObject. This is the one we'll test.
		// If the GameObject is disabled, the manual parameters should not be applied.
		var clone = go.Clone( new CloneConfig() { StartEnabled = false } );

		var smr2 = clone.GetComponent<SkinnedModelRenderer>( includeDisabled: true );

		Assert.AreNotEqual( 0.42f, smr2.Attributes.GetFloat( "skin_age" ), "Age should NOT have been set by dresser while loading" );
		Assert.AreNotEqual( 0.84f, smr2.Attributes.GetFloat( "skin_tint" ), "Tint should NOT have been set by dresser while loading" );
		Assert.AreEqual( 0f /* default unset */, smr2.GetFloat( "scale_height" ), "Height should NOT have been set by dresser while loading" );

		clone.Enabled = true;

		Assert.AreEqual( 0.42f, smr.Attributes.GetFloat( "skin_age" ), "Age should have been set by dresser" );
		Assert.AreEqual( 0.84f, smr.Attributes.GetFloat( "skin_tint" ), "Tint should have been set by dresser" );
		Assert.AreNotEqual( 0f /* default unset */, smr.GetFloat( "scale_height" ), "Height should have been set by dresser" );
	}
}
