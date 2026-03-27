using System;

namespace GameObjects.Components;

[TestClass]
public class DresserTests
{
	private static Model CitizenModel => Model.Load( "models/citizen/citizen.vmdl" );

	[TestMethod]
	[Ignore( "Set-Getting attributes on the SkinnedModelRenderer doesn't seem to do anything in unit tests?" )]
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
		Assert.AreEqual( 0.42f, smr.GetFloat( "skin_age" ), "Age should have been set by dresser" );

		// Tint
		dresser.ManualTint = 0.84f;
		Assert.AreEqual( 0.84f, smr.GetFloat( "skin_tint" ), "Tint should have been set by dresser" );

		// Height
		dresser.ManualHeight = 0.14f;
		Assert.AreNotEqual( 1.0f, smr.GetFloat( "scale_height" ), "Height should have been set by dresser" );
	}
}
