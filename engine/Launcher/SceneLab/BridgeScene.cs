namespace Sandbox.SceneLab;

/// <summary>
/// A GameObject scene built from engine components and core assets alone, for checking the managed renderer's
/// GameObject bridge (<c>r_managed_scene</c>) against native: <c>-gamescene builtin</c>. It has what the bridge
/// mirrors - model renderers with tints and overrides, a skinned citizen posed from a sequence with a body group
/// hidden and its skin overridden, a human in a material group, a sun with sky colour for ambient, a sky, and
/// shadowed point and spot lights - and nothing it doesn't, so a difference is a bridge bug rather than a missing
/// feature.
/// </summary>
internal static class BridgeScene
{
	public static Scene Create()
	{
		var scene = new Scene();
		using var _ = scene.Push();

		var camera = new GameObject( true, "Camera" );
		camera.WorldPosition = new Vector3( -260, -200, 170 );
		camera.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 30 ) - camera.WorldPosition );
		var cameraComponent = camera.Components.Create<CameraComponent>();
		cameraComponent.FieldOfView = 70;
		cameraComponent.FovAxis = CameraComponent.Axis.Horizontal;
		cameraComponent.ZNear = 5;
		cameraComponent.ZFar = 5000;
		cameraComponent.BackgroundColor = new Color( 0.08f, 0.09f, 0.11f );
		cameraComponent.IsMainCamera = true;
		cameraComponent.RenderExcludeTags.Add( "hidden" );

		// The sun, with contact shadows off: they're screen space, and the Contact Shadows preset has them
		var sun = new GameObject( true, "Sun" );
		sun.WorldRotation = Rotation.LookAt( new Vector3( -0.4f, 0.7f, -0.55f ) );
		var sunLight = sun.Components.Create<DirectionalLight>();
		sunLight.LightColor = new Color( 1.0f, 0.95f, 0.85f ) * 1.5f;
		sunLight.SkyColor = new Color( 0.12f, 0.14f, 0.18f );
		sunLight.Shadows = true;
		sunLight.ContactShadows = false;

		// The default sky, and its light through the whole-world probe SkyBox2D makes
		new GameObject( true, "Sky" ).Components.Create<SkyBox2D>();

		var floor = new GameObject( true, "Floor" );
		floor.WorldPosition = new Vector3( 0, 0, -1 );
		floor.WorldScale = new Vector3( 8, 8, 1 );
		floor.Components.Create<ModelRenderer>().Model = Model.Load( "models/dev/plane.vmdl" );

		// A model with LODs held at its last (ModelRenderer.LodOverride), near enough to pick its first by size: an
		// octahedron, not a sphere
		var fixedLod = new GameObject( true, "Fixed LOD" );
		fixedLod.WorldPosition = new Vector3( -90, 20, 20 );
		var fixedLodRenderer = fixedLod.Components.Create<ModelRenderer>();
		fixedLodRenderer.Model = LodTestModel.Create();
		fixedLodRenderer.Tint = new Color( 0.9f, 0.8f, 0.5f );
		fixedLodRenderer.LodOverride = 2;

		// A ring of boxes and spheres, tinted, one in glossy metal
		var metal = Material.Load( "materials/dev/dev_metal_rough10.vmat" );
		for ( int i = 0; i < 8; i++ )
		{
			var angle = i * MathF.Tau / 8;
			var go = new GameObject( true, $"Prop {i}" );
			go.IsStatic = i == 4; // part of the world, for tags, and a static shadow caster
			go.WorldPosition = new Vector3( MathF.Cos( angle ), MathF.Sin( angle ), 0 ) * 140 + Vector3.Up * 10;
			go.WorldRotation = Rotation.FromYaw( i * 20 );

			var renderer = go.Components.Create<ModelRenderer>();
			renderer.Model = Model.Load( i % 2 == 0 ? "models/dev/box.vmdl" : "models/dev/sphere.vmdl" );
			renderer.Tint = new ColorHsv( i * 45, 0.35f, 1 ).ToColor();
			if ( i == 3 ) renderer.MaterialOverride = metal;

			// A per index override, of the model's first material
			if ( i == 5 ) renderer.Materials.SetOverride( 0, Material.Load( "materials/dev/primary_red.vmat" ) );

			// Left out by the camera's exclude tags
			if ( i == 6 ) go.Tags.Add( "hidden" );
		}

		// A citizen mid-jump, frozen, with its feet hidden (a body group) and its skin swapped by attribute, the way
		// avatars are dressed
		var citizen = new GameObject( true, "Citizen" );
		citizen.WorldRotation = Rotation.FromYaw( 200 );
		var skinned = citizen.Components.Create<SkinnedModelRenderer>();
		skinned.Model = Model.Load( "models/citizen/citizen.vmdl" );
		skinned.UseAnimGraph = false;
		skinned.Sequence.Name = "AvatarMenu_Entry_Jump";
		skinned.Sequence.TimeNormalized = 0.4f;
		skinned.PlaybackRate = 0;
		skinned.SetBodyGroup( "Feet", 1 );
		skinned.SetMaterialOverride( Material.Load( "models/citizen/skin/citizen_skin_grey.vmat" ), "skin" );

		// A human in a material group, in its bind pose
		var human = new GameObject( true, "Human" );
		human.WorldPosition = new Vector3( -50, 70, 0 );
		human.WorldRotation = Rotation.FromYaw( 230 );
		var humanRenderer = human.Components.Create<SkinnedModelRenderer>();
		humanRenderer.Model = Model.Load( "models/citizen_human/citizen_human_male.vmdl" );
		humanRenderer.UseAnimGraph = false;
		humanRenderer.PlaybackRate = 0;
		humanRenderer.MaterialGroup = "skin_light";

		// Per object attributes, which the skin shader reads - how avatars get their skin tint and age. The human's skin
		// barely shows them; the citizen's, set in Change, is what proves they reach the shader
		humanRenderer.Attributes.Set( "skin_tint", 0.1f );
		humanRenderer.Attributes.Set( "skin_age", 1.0f );

		// A dressed avatar, as ClothingContainer.Apply dresses every player: clothing bone merged onto the body, the
		// body groups the clothes hide, the skin swapped by attribute and tinted by per object attributes
		var dressed = new GameObject( true, "Dressed" );
		dressed.WorldPosition = new Vector3( 75, 25, 0 );
		dressed.WorldRotation = Rotation.FromYaw( 190 );
		var body = dressed.Components.Create<SkinnedModelRenderer>();
		body.Model = Model.Load( "models/citizen/citizen.vmdl" );
		body.UseAnimGraph = false;
		body.Sequence.Name = "AvatarMenu_Entry_Jump";
		body.Sequence.TimeNormalized = 0.6f;
		body.PlaybackRate = 0;
		Dress( Outfit( Outfits ), body );

		// A shadowed point light and a shadowed spot
		var point = new GameObject( true, "Point Light" );
		point.WorldPosition = new Vector3( 90, -60, 90 );
		var pointLight = point.Components.Create<PointLight>();
		pointLight.LightColor = new Color( 0.4f, 0.7f, 1.0f ) * 3;
		pointLight.Radius = 350;
		pointLight.Shadows = true;

		var spot = new GameObject( true, "Spot Light" );
		spot.WorldPosition = new Vector3( -80, 90, 220 );
		spot.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 0 ) - spot.WorldPosition );
		var spotLight = spot.Components.Create<SpotLight>();
		spotLight.LightColor = new Color( 1.0f, 0.6f, 0.3f ) * 5;
		spotLight.Radius = 500;
		spotLight.ConeInner = 15;
		spotLight.ConeOuter = 30;
		spotLight.Shadows = true;

		return scene;
	}

	internal static readonly string[] Outfits =
	[
		"models/citizen_clothes/jacket/Hoodie/hoodie.clothing",
		"models/citizen_clothes/trousers/Jeans/jeans.clothing",
		"models/citizen_clothes/shoes/Trainers/trainers.clothing",
		"models/citizen_clothes/hat/Baseball_Cap/baseball_cap.clothing",
	];

	/// <summary>
	/// Dress a body in an outfit, as <c>ClothingContainer.Apply</c> did: its appearance, then its clothing (<see cref="Dresser"/>).
	/// </summary>
	internal static void Dress( ClothingContainer outfit, SkinnedModelRenderer body )
	{
		var dresser = Dresser.GetOrCreate( body );
		outfit.Normalize();
		dresser.UpdateAppearance( outfit );
		dresser.Apply( outfit );
	}

	/// <summary>
	/// An outfit of the citizen addon's clothing definitions.
	/// </summary>
	internal static ClothingContainer Outfit( IEnumerable<string> paths )
	{
		var outfit = new ClothingContainer();
		foreach ( var path in paths )
		{
			if ( GameScenes.LoadResource<Clothing>( path, "addons/citizen/Assets" ) is { } clothing ) outfit.Add( new ClothingContainer.ClothingEntry( clothing ) );
		}

		return outfit;
	}

	/// <summary>
	/// Change the scene through its components, the way a game would, so a second compare proves the mirror
	/// follows changes rather than only its first sync: moves, a tint, a material override, a disabled object
	/// (removed) and a new one (added), a light's colour and radius, a moved spot, the sun turned, the
	/// citizen at another point in its animation, and body groups, a material group and overrides changed.
	/// </summary>
	public static void Change( Scene scene )
	{
		using var _ = scene.Push();

		GameObject Find( string name ) => scene.Directory.FindByName( name ).First();

		Find( "Prop 0" ).WorldPosition += new Vector3( 0, 40, 30 );
		Find( "Prop 1" ).Components.Get<ModelRenderer>().Tint = new Color( 1, 0.2f, 0.2f );
		Find( "Prop 2" ).Enabled = false;
		Find( "Prop 4" ).Components.Get<ModelRenderer>().MaterialOverride = Material.Load( "materials/dev/dev_metal_rough10.vmat" );
		Find( "Fixed LOD" ).Components.Get<ModelRenderer>().LodOverride = null;

		var added = new GameObject( true, "Added" );
		added.WorldPosition = new Vector3( 40, -120, 20 );
		added.WorldScale = 0.5f;
		added.Components.Create<ModelRenderer>().Model = Model.Load( "models/dev/box.vmdl" );

		var point = Find( "Point Light" ).Components.Get<PointLight>();
		point.LightColor = new Color( 1.0f, 0.3f, 0.6f ) * 4;
		point.Radius = 250;

		var spot = Find( "Spot Light" );
		spot.WorldPosition = new Vector3( 60, 100, 200 );
		spot.WorldRotation = Rotation.LookAt( new Vector3( 0, 0, 0 ) - spot.WorldPosition );

		Find( "Sun" ).WorldRotation = Rotation.LookAt( new Vector3( 0.5f, 0.5f, -0.6f ) );

		// Per object state: the citizen's head hidden and feet back, its skin override gone and its skin tinted by
		// attributes made now; the human in another
		// material group; the per index override swapped
		var citizen = Find( "Citizen" ).Components.Get<SkinnedModelRenderer>();
		citizen.Sequence.TimeNormalized = 0.75f;
		citizen.SetBodyGroup( "Head", 1 );
		citizen.SetBodyGroup( "Feet", 0 );
		citizen.SetMaterialOverride( null, "skin" );
		citizen.Attributes.Set( "skin_tint", 0.95f );
		citizen.Attributes.Set( "skin_age", 0.0f );

		Find( "Human" ).Components.Get<SkinnedModelRenderer>().MaterialGroup = "skin_dark";

		// Redressed without the cap: its clothing objects go, the rest are made again, and the head body group comes back
		Dress( Outfit( Outfits.SkipLast( 1 ) ), Find( "Dressed" ).Components.Get<SkinnedModelRenderer>() );
		Find( "Prop 5" ).Components.Get<ModelRenderer>().Materials.SetOverride( 0, Material.Load( "materials/dev/primary_blue.vmat" ) );

		// Tags: the hidden prop shown, another hidden, and the static prop left out as part of the world
		Find( "Prop 6" ).Tags.Remove( "hidden" );
		Find( "Prop 7" ).Tags.Add( "hidden" );
		scene.Camera.RenderExcludeTags.Add( "world" );
	}
}
