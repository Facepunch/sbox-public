using System.Collections.Generic;

namespace SceneTests.Components;

/// <summary>
/// Exercises inline clothing body replacements through the same renderer and outfit APIs used by games.
/// </summary>
[TestClass]
public class ClothingBodyTests
{
	/// <summary>
	/// External model changes become the restoration baseline, including while a player model is equipped.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ExternalModelChangesReplaceTheBaseline( bool equipPlayerModel )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = scene.CreateObject().AddComponent<SkinnedModelRenderer>();
		body.Model = Model.Load( "models/dev/box.vmdl" );
		var dresser = Dresser.GetOrCreate( body );
		var outfit = new ClothingContainer();
		if ( equipPlayerModel )
			outfit.Add( CreateHuman() );
		dresser.Apply( outfit );

		var newModel = Model.Load( "models/dev/sphere.vmdl" );
		body.Model = newModel;
		dresser.Apply( outfit );
		dresser.Clear();
		Assert.AreSame( newModel, body.Model );
	}

	/// <summary>
	/// Exposed skin in garments follows the body's material group, while original material-only resources still work.
	/// </summary>
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void GarmentSkinMatchesTheSelectedBodyMaterial( bool legacyOverride )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = scene.CreateObject().AddComponent<SkinnedModelRenderer>();
		var dresser = Dresser.GetOrCreate( body );
		var replacement = CreateHuman();
		replacement.BodyModel = "models/citizen_human/citizen_human_male.vmdl";
		replacement.BodyMaterialGroup = "body_03";
		var gloves = new Clothing
		{
			HumanAltModel = "models/citizen_clothes/gloves/fingerless_gloves/models/fingerless_gloves_m_human.vmdl",
			SlotsUnder = Clothing.Slots.LeftHand | Clothing.Slots.RightHand
		};
		Assert.IsFalse( Model.Load( gloves.HumanAltModel ).IsError );
		var outfit = new ClothingContainer();
		outfit.Add( replacement );
		outfit.Add( gloves );
#pragma warning disable CS0618 // Original Human material-only resources do not require HasHumanSkin.
		if ( legacyOverride )
			outfit.Add( new Clothing { HumanSkinMaterial = "models/citizen_human/bodies/male/body_mode_dark.vmat" } );
#pragma warning restore CS0618
		dresser.Apply( outfit );
		var garment = body.GetComponentsInChildren<SkinnedModelRenderer>()
			.Single( x => x != body && x.Model.Name == gloves.HumanAltModel );

		// Named overrides have no public getter; inspect the renderer state consumed by ApplyMaterialOverrides.
		var field = typeof( ModelRenderer ).GetField( "taggedMaterialOverrides", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
		var overrides = (Dictionary<string, Material>)field.GetValue( garment );
		var expected = legacyOverride ? "models/citizen_human/bodies/male/body_mode_dark.vmat" : "models/citizen_human/bodies/male/body_male_light.vmat";
		Assert.AreEqual( expected, overrides["skin"].Name );
		if ( !legacyOverride )
			Assert.IsNull( field.GetValue( body ), "Group-based bodies must not need skin overrides themselves" );
	}

	/// <summary>
	/// Restricted bodies require all occupied slots across both clothing layers, and reject unassigned items.
	/// </summary>
	[TestMethod]
	[DataRow( Clothing.Slots.HeadTop, (Clothing.Slots)0, true )]
	[DataRow( (Clothing.Slots)0, Clothing.Slots.HeadTop, true )]
	[DataRow( Clothing.Slots.HeadTop, Clothing.Slots.Chest, false )]
	[DataRow( Clothing.Slots.HeadTop | Clothing.Slots.Chest, (Clothing.Slots)0, false )]
	[DataRow( (Clothing.Slots)0, (Clothing.Slots)0, false )]
	public void RestrictedBodyRequiresEveryOccupiedSlot( Clothing.Slots under, Clothing.Slots over, bool expected )
	{
		var body = new Clothing
		{
			Type = Clothing.ClothingType.PlayerModel,
			AllowedSlots = Clothing.Slots.HeadTop
		};
		var garment = new Clothing { SlotsUnder = under, SlotsOver = over };
		Assert.AreEqual( expected, body.CanWear( garment ) );
		Assert.IsFalse( body.CanWear( null ) );
		Assert.IsFalse( body.CanWear( new Clothing { Type = Clothing.ClothingType.PlayerModel } ) );
		body.AllowedSlots = Clothing.Slots.All;
		Assert.IsTrue( body.CanWear( garment ) );
		body.AllowedSlots = Clothing.Slots.None;
		Assert.IsFalse( body.CanWear( garment ) );
	}

	/// <summary>
	/// Dressing a restricted model filters garments and automatic underwear, and reacts to allowance changes.
	/// </summary>
	[TestMethod]
	public void DressingRespectsAllowedSlots()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var renderer = scene.CreateObject().AddComponent<SkinnedModelRenderer>();
		var dresser = Dresser.GetOrCreate( renderer );
		var replacement = CreateHuman();
		replacement.BodyModel = "models/dev/box.vmdl";
		replacement.AllowedSlots = Clothing.Slots.HeadTop;
		var outfit = new ClothingContainer();
		outfit.Add( replacement );
		var hat = outfit.Add( new Clothing
		{
			HumanAltModel = "models/dev/sphere.vmdl",
			SlotsUnder = Clothing.Slots.HeadTop,
			AllowTintSelect = true,
			TintSelection = new Gradient( new Gradient.ColorFrame( 0, Color.Red ), new Gradient.ColorFrame( 1, Color.Blue ) )
		} );
		outfit.Add( new Clothing { HumanAltModel = "models/dev/box.vmdl", SlotsOver = Clothing.Slots.Chest } );
		outfit.Add( new Clothing { HumanAltModel = "models/dev/box.vmdl" } );
		dresser.Apply( outfit );
		scene.ProcessDeletes();
		var garments = renderer.GetComponentsInChildren<SkinnedModelRenderer>().Where( x => x != renderer ).ToArray();
		Assert.AreEqual( 1, garments.Length, "Only the head garment should be added, with no automatic underwear" );
		Assert.AreEqual( "models/dev/sphere.vmdl", garments[0].Model.Name );
		Assert.AreEqual( -1f, renderer.Attributes.GetFloat( "skin_tint", -1f ), "Accessories must not change custom body appearance" );

		hat.Tint = 1;
		dresser.UpdateAppearance( outfit );
		Assert.AreEqual( Color.Blue, garments[0].Tint, "Tint edits must reach existing garments on restricted custom bodies" );
		Assert.AreEqual( -1f, renderer.Attributes.GetFloat( "skin_tint", -1f ), "Tint edits must preserve custom body appearance" );

		replacement.AllowedSlots = Clothing.Slots.None;
		dresser.Apply( outfit );
		scene.ProcessDeletes();
		Assert.IsFalse( renderer.GameObject.Children.Any( x => x.Tags.Has( "clothing" ) ) );

		replacement.AllowedSlots = Clothing.Slots.All;
		dresser.Apply( outfit );
		scene.ProcessDeletes();
		Assert.IsTrue( renderer.GetComponentsInChildren<SkinnedModelRenderer>().Count( x => x != renderer ) >= 3 );
	}

	/// <summary>
	/// Allowed slot masks and legacy subtitle values survive serialization.
	/// </summary>
	[TestMethod]
	[DataRow( Clothing.Slots.All )]
	[DataRow( Clothing.Slots.None )]
	[DataRow( Clothing.Slots.HeadTop | Clothing.Slots.Glasses )]
	public void AllowedSlotsRoundTrip( Clothing.Slots slots )
	{
		var body = new Clothing { AllowedSlots = slots };
		var copy = new Clothing();
		copy.Deserialize( body.Serialize() );
		Assert.AreEqual( slots, copy.AllowedSlots );
		copy.LoadFromJson( "{\"Subtitle\":\"Legacy subtitle\"}" );
		Assert.AreEqual( Clothing.Slots.All, copy.AllowedSlots );
		Assert.AreEqual( "Legacy subtitle", copy.Serialize()["Subtitle"].GetValue<string>() );
	}

	private static Clothing CreateHuman()
	{
		var model = Model.Load( "models/citizen_human/human.vmdl" );
		Assert.IsFalse( model.IsError );

		return new Clothing
		{
			ReplacesBody = true,
			BodyModel = model.Name
		};
	}

	/// <summary>
	/// An unspecified bodygroup mask uses model defaults, while an explicit zero remains zero.
	/// </summary>
	[TestMethod]
	public void BodyReplacementUsesAuthoredBodyGroups()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = scene.CreateObject().AddComponent<SkinnedModelRenderer>();
		var dresser = Dresser.GetOrCreate( body );
		var replacement = CreateHuman();
		replacement.AllowedSlots = Clothing.Slots.None;
		var outfit = new ClothingContainer();
		outfit.Add( replacement );
		dresser.Apply( outfit );
		Assert.AreEqual( replacement.BodyModel, body.Model.Name );
		Assert.AreEqual( body.Model.Parts.DefaultMask, body.BodyGroups );

		replacement.BodyGroupMask = 0;
		dresser.Apply( outfit );
		Assert.AreEqual( 0ul, body.BodyGroups );
		dresser.Clear();
		Assert.IsNull( body.Model );
	}

	/// <summary>
	/// An equipped body selects garment variants, and removing it restores the base body's choices.
	/// </summary>
	[TestMethod]
	public void ReplacementResolvesBeforeClothingAndRestoresBase()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = scene.CreateObject().AddComponent<SkinnedModelRenderer>();
		body.Model = Model.Load( "models/dev/box.vmdl" );
		body.BodyGroups = 0;
		var original = body.Model;
		var dresser = Dresser.GetOrCreate( body );

		var replacement = CreateHuman();
		replacement.BodyTags.Add( "female" );
		var outfit = new ClothingContainer();
		outfit.Add( replacement );
		outfit.Add( new Clothing { HumanAltFemaleModel = "models/dev/sphere.vmdl" } );
		dresser.Apply( outfit );

		Assert.AreEqual( replacement.BodyModel, body.Model.Name );
		Assert.IsTrue( body.GetComponentsInChildren<SkinnedModelRenderer>()
			.Any( x => x != body && x.Model.Name == "models/dev/sphere.vmdl" ), "The selected body's tags must choose the female garment" );

		dresser.Clear();
		scene.ProcessDeletes();
		Assert.AreSame( original, body.Model );
		Assert.AreEqual( 0ul, body.BodyGroups );
		Assert.IsFalse( body.GameObject.Children.Any( x => x.Tags.Has( "clothing" ) ) );
		Assert.AreEqual( -1f, body.Attributes.GetFloat( "skin_tint", -1f ), "The previous body's appearance must not leak into a custom model" );
		Assert.AreEqual( new Vector4( -1 ), body.Attributes.GetVector4( "eye_color", new Vector4( -1 ) ) );
	}

	/// <summary>
	/// Clothing hides parts temporarily and removing it returns to the player body's chosen bodygroups.
	/// </summary>
	[TestMethod]
	public void RemovingClothingRestoresResourceBodyGroups()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = scene.CreateObject().AddComponent<SkinnedModelRenderer>();
		var dresser = Dresser.GetOrCreate( body );
		body.Model = Model.Load( CreateHuman().BodyModel );
		dresser.Apply( new ClothingContainer() );
		var baseline = body.BodyGroups;
		var outfit = new ClothingContainer();
		outfit.Add( new Clothing { HideBody = Clothing.BodyGroups.Head } );

		dresser.Apply( outfit );
		Assert.AreNotEqual( baseline, body.BodyGroups );
		dresser.Clear();
		Assert.AreEqual( baseline, body.BodyGroups );
	}

	/// <summary>
	/// Custom bodies do not receive built-in garments, underwear or avatar appearance attributes.
	/// </summary>
	[TestMethod]
	public void CustomBodyPreservesAuthoredAppearance()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = scene.CreateObject().AddComponent<SkinnedModelRenderer>();
		var dresser = Dresser.GetOrCreate( body );
		var replacement = CreateHuman();
		replacement.AllowedSlots = Clothing.Slots.None;
		body.Attributes.Set( "skin_age", 0.25f );
		body.Attributes.Set( "custom_attribute", 7f );
		var outfit = new ClothingContainer();
		outfit.Add( replacement );
		outfit.Add( new Clothing { HumanAltModel = "models/dev/sphere.vmdl" } );

		dresser.UpdateAppearance( new ClothingContainer { Age = 0.9f } );
		dresser.Apply( outfit );

		Assert.AreEqual( 0.25f, body.Attributes.GetFloat( "skin_age" ) );
		Assert.AreEqual( 7f, body.Attributes.GetFloat( "custom_attribute" ) );
		Assert.IsFalse( body.GameObject.Children.Any( x => x.Tags.Has( "clothing" ) ) );
	}

	/// <summary>
	/// Body items share one slot even when their clothing slot masks are empty.
	/// </summary>
	[TestMethod]
	public void EquippingAnotherBodyReplacesPreviousItem()
	{
		var first = new Clothing { ReplacesBody = true };
		var second = new Clothing { ReplacesBody = true };
		var outfit = new ClothingContainer();
		outfit.Add( first );
		outfit.Add( second );

		Assert.IsFalse( outfit.Has( first ) );
		Assert.IsTrue( outfit.Has( second ) );
	}

	/// <summary>
	/// Old body JSON and old C# properties map to the new inline body settings without changing public signatures.
	/// </summary>
	[TestMethod]
	public void LegacyBodyResourcesAndCodeRemainCompatible()
	{
		var clothing = new Clothing();
		clothing.LoadFromJson( """
			{
				"HasHumanSkin": true,
				"HumanSkinModel": "models/citizen_human/human.vmdl",
				"HumanSkinBodyGroups": 0,
				"HumanSkinMaterialGroup": "skin_dark",
				"HumanSkinTags": "female",
				"HumanSkinMaterial": "models/citizen/skin/citizen_skin_grey.vmat",
				"HumanEyesMaterial": "models/citizen/skin/citizen_skin_grey.vmat"
			}
			""" );
		Assert.IsTrue( clothing.ReplacesBody );
		Assert.AreEqual( Clothing.ClothingType.PlayerModel, clothing.Type );
		Assert.AreEqual( "models/citizen_human/human.vmdl", clothing.BodyModel );
		Assert.AreEqual( 0ul, clothing.BodyGroupMask );
		Assert.AreEqual( "skin_dark", clothing.BodyMaterialGroup );
		Assert.IsTrue( clothing.BodyTags.Has( "female" ) );
		Assert.AreEqual( Clothing.Slots.All, clothing.AllowedSlots );
#pragma warning disable CS0618 // Verify that legacy material overrides remain supported.
		Assert.AreEqual( "models/citizen/skin/citizen_skin_grey.vmat", clothing.HumanSkinMaterial );
		Assert.AreEqual( "models/citizen/skin/citizen_skin_grey.vmat", clothing.HumanEyesMaterial );
#pragma warning restore CS0618
		var saved = clothing.Serialize();
		Assert.IsTrue( saved["HasHumanSkin"].GetValue<bool>() );
		Assert.AreEqual( 0ul, saved["HumanSkinBodyGroups"].GetValue<ulong>() );
		Assert.AreEqual( "models/citizen/skin/citizen_skin_grey.vmat", saved["HumanSkinMaterial"].GetValue<string>() );
		Assert.AreEqual( "models/citizen/skin/citizen_skin_grey.vmat", saved["HumanEyesMaterial"].GetValue<string>() );

#pragma warning disable CS0618 // These members must remain callable by existing game code.
		Assert.IsTrue( clothing.HasHumanSkin );
		Assert.AreEqual( clothing.BodyModel, clothing.HumanSkinModel );
		Assert.AreEqual( 0ul, clothing.HumanSkinBodyGroups );
		Assert.AreEqual( clothing.BodyMaterialGroup, clothing.HumanSkinMaterialGroup );
		Assert.AreSame( clothing.BodyTags, clothing.HumanSkinTags );
		clothing.HasHumanSkin = false;
		clothing.HumanSkinModel = "models/dev/box.vmdl";
		clothing.HumanSkinBodyGroups = 1;
		clothing.HumanSkinMaterialGroup = "default";
		clothing.HumanSkinTags = new TagSet();
		clothing.HumanSkinMaterial = null;
		clothing.HumanEyesMaterial = null;
#pragma warning restore CS0618
		Assert.IsFalse( clothing.ReplacesBody );
		Assert.AreEqual( Clothing.ClothingType.Clothing, clothing.Type );
		Assert.AreEqual( "models/dev/box.vmdl", clothing.BodyModel );
		Assert.AreEqual( 1ul, clothing.BodyGroupMask );
		Assert.AreEqual( "default", clothing.BodyMaterialGroup );
		Assert.IsFalse( clothing.BodyTags.Has( "female" ) );
#pragma warning disable CS0618
		Assert.IsNull( clothing.HumanSkinMaterial );
		Assert.IsNull( clothing.HumanEyesMaterial );
#pragma warning restore CS0618
	}

	/// <summary>
	/// An explicit type wins over the old flag regardless of property order, and round-trips with a consistent legacy flag.
	/// </summary>
	[TestMethod]
	[DataRow( "{\"Type\":\"Clothing\",\"HasHumanSkin\":true}", Clothing.ClothingType.Clothing )]
	[DataRow( "{\"HasHumanSkin\":true,\"Type\":\"Clothing\"}", Clothing.ClothingType.Clothing )]
	[DataRow( "{\"Type\":\"PlayerModel\",\"HasHumanSkin\":false}", Clothing.ClothingType.PlayerModel )]
	[DataRow( "{\"HasHumanSkin\":false,\"Type\":\"PlayerModel\"}", Clothing.ClothingType.PlayerModel )]
	public void ItemTypeTakesPrecedenceOverLegacyFlag( string json, Clothing.ClothingType expected )
	{
		var clothing = new Clothing();
		clothing.LoadFromJson( json );
		Assert.AreEqual( expected, clothing.Type );
		Assert.AreEqual( expected == Clothing.ClothingType.PlayerModel, clothing.ReplacesBody );

		var saved = clothing.Serialize();
		Assert.AreEqual( clothing.ReplacesBody, saved["HasHumanSkin"].GetValue<bool>() );
		var copy = new Clothing();
		copy.Deserialize( saved );
		Assert.AreEqual( expected, copy.Type );

		// Reloading old garment data must not retain the type from a previously loaded body item.
		copy.LoadFromJson( "{\"Title\":\"Legacy garment\"}" );
		Assert.AreEqual( Clothing.ClothingType.Clothing, copy.Type );
	}

	/// <summary>
	/// Obsolete Citizen fields survive resource loading and saving alongside the existing Human model fields.
	/// </summary>
	[TestMethod]
	public void LegacyClothingFieldsRoundTrip()
	{
		var clothing = new Clothing();
		clothing.LoadFromJson( """
			{
				"Model": "models/legacy_citizen.vmdl",
				"HumanAltModel": "models/human_garment.vmdl",
				"HumanAltFemaleModel": "models/female_garment.vmdl",
				"SkinMaterial": "materials/legacy_skin.vmat",
				"EyesMaterial": "materials/legacy_eyes.vmat"
			}
			""" );

		var saved = clothing.Serialize();
		Assert.AreEqual( "models/legacy_citizen.vmdl", saved["Model"].GetValue<string>() );
		Assert.AreEqual( "models/human_garment.vmdl", saved["HumanAltModel"].GetValue<string>() );
		Assert.AreEqual( "models/female_garment.vmdl", saved["HumanAltFemaleModel"].GetValue<string>() );
		Assert.AreEqual( "materials/legacy_skin.vmat", saved["SkinMaterial"].GetValue<string>() );
		Assert.AreEqual( "materials/legacy_eyes.vmat", saved["EyesMaterial"].GetValue<string>() );

		var copy = new Clothing();
		copy.Deserialize( saved );
		Assert.AreEqual( "models/legacy_citizen.vmdl", copy.GetModel( [] ) );

		var tags = new TagSet();
		tags.Add( "human" );
		Assert.AreEqual( "models/human_garment.vmdl", copy.GetModel( [], tags ) );
		tags.Add( "female" );
		Assert.AreEqual( "models/female_garment.vmdl", copy.GetModel( [], tags ) );
		copy.HumanAltFemaleModel = null;
		Assert.AreEqual( "models/human_garment.vmdl", copy.GetModel( [], tags ) );
	}

	/// <summary>
	/// Resource round-trips keep model-default bodygroups unset and retain explicit material choices.
	/// </summary>
	[TestMethod]
	public void BodyRoundTripPreservesDefaultsAndMaterials()
	{
		var source = CreateHuman();
		source.BodyMaterialGroup = "skin_dark";
		source.BodyTags.Add( "female" );
#pragma warning disable CS0618 // Existing serialized overrides must still round-trip.
		source.HumanSkinMaterial = "materials/legacy_skin_not_installed.vmat";
#pragma warning restore CS0618
		var copy = new Clothing { BodyGroupMask = 0 };
		var saved = source.Serialize();
		Assert.IsFalse( saved.ContainsKey( "HumanSkinBodyGroups" ) );
		copy.Deserialize( saved );

		Assert.AreEqual( source.BodyModel, copy.BodyModel );
		Assert.IsNull( copy.BodyGroupMask );
		Assert.AreEqual( source.BodyMaterialGroup, copy.BodyMaterialGroup );
#pragma warning disable CS0618
		Assert.AreEqual( source.HumanSkinMaterial, copy.HumanSkinMaterial );
#pragma warning restore CS0618
		Assert.IsTrue( copy.BodyTags.Has( "female" ) );
		Assert.AreEqual( source.AllowedSlots, copy.AllowedSlots );
		copy.BodyGroupMask = 1;
		copy.CopyFrom( source );
		Assert.IsNull( copy.BodyGroupMask, "Copying model defaults must clear an existing explicit mask" );
	}

	/// <summary>
	/// Body tags participate in the same conditional garment selection as clothing tags.
	/// </summary>
	[TestMethod]
	public void BodyTagsSelectConditionalGarments()
	{
		var replacement = CreateHuman();
		replacement.BodyTags.Add( "robot" );
		var clothing = new Clothing
		{
			ConditionalModels = new() { ["robot"] = "models/dev/box.vmdl" },
			HumanAltModel = "models/dev/sphere.vmdl"
		};

		Assert.AreEqual( "models/dev/box.vmdl", clothing.GetModel( [], replacement.BodyTags ) );
	}

	/// <summary>
	/// A replacement with missing model data cannot displace a valid base, even with no explicit base resource.
	/// </summary>
	[TestMethod]
	public void MissingReplacementLeavesOriginalBodyIntact()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = scene.CreateObject().AddComponent<SkinnedModelRenderer>();
		body.Model = Model.Load( "models/dev/box.vmdl" );
		var original = body.Model;
		var dresser = Dresser.GetOrCreate( body );
		var outfit = new ClothingContainer();
		outfit.Add( new Clothing { ReplacesBody = true } );
		dresser.Apply( outfit );
		Assert.AreSame( original, body.Model );

		outfit.Add( CreateHuman() );
		dresser.Apply( outfit );
		Assert.AreNotSame( original, body.Model );
		dresser.Clear();
		Assert.AreSame( original, body.Model );
	}
}
