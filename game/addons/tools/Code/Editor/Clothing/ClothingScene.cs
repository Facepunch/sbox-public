namespace Editor;

/// <summary>
/// Renders clothing icons on the Human model.
/// </summary>
public class ClothingScene
{
	/// <summary>
	/// The isolated scene used by the icon preview and export.
	/// </summary>
	public Scene Scene;

	/// <summary>
	/// Default camera pitch for icons without a specialized framing preset.
	/// </summary>
	public float Pitch = 15.0f;

	/// <summary>
	/// Default camera yaw for icons without a specialized framing preset.
	/// </summary>
	public float Yaw = 35.0f;

	/// <summary>
	/// The Human body wearing the previewed clothing.
	/// </summary>
	public SkinnedModelRenderer Body;

	/// <summary>
	/// Whether the selected item has a Human skin or a clothing mesh to export.
	/// </summary>
	public bool HasRenderableClothing => clothing?.HasHumanSkin == true || itemRenderers.Any( x => x.IsValid() && !x.GameObject.IsDestroyed );

	Clothing.IconSetup iconSetup;
	Clothing clothing;
	SkinnedModelRenderer[] itemRenderers = [];
	Dictionary<Model, Vector3[]> accessoryPoints = new();

	/// <summary>
	/// Creates the preview scene, camera and Human body.
	/// </summary>
	public ClothingScene()
	{
		Scene = Scene.CreateEditorScene();

		using ( Scene.Push() )
		{
			var camera = new GameObject( true, "camera" ).GetOrAddComponent<CameraComponent>();

			Body = new GameObject( true, "player" ).GetOrAddComponent<SkinnedModelRenderer>();
			Body.Model = Model.Load( "models/citizen_human/human.vmdl" );

			camera.BackgroundColor = new Color( 0.1f, 0.1f, 0.1f, 0.0f );
		}
	}

	/// <summary>
	/// Dresses the preview body and applies the clothing's icon setup.
	/// </summary>
	public void InstallClothing( Clothing clothing )
	{
		this.clothing = clothing;
		iconSetup = clothing.Icon;
		accessoryPoints.Clear();

		// Camera presets control framing, not skin appearance. Only actual skin items
		// show their skin; clothing and accessories use the same neutral mannequin.
		bool wantsGreySkin = !clothing.HasHumanSkin;

		using var x = Scene.Push();

		Body.Model = Model.Load( "models/citizen_human/human.vmdl" );
		Body.ClearMaterialOverrides();

		ClothingContainer container = new ClothingContainer { PrefersHuman = true };
		container.Add( clothing );

		var dresser = Dresser.GetOrCreate( Body );
		container.Normalize();
		dresser.UpdateAppearance( container );
		dresser.Apply( container );

		// Remove any extra clothes (underwear) that were added by the Dresser.
		foreach ( var child in Body.GameObject.Children.ToArray() )
		{
			if ( !child.Tags.Has( "clothing" ) ) continue;
			if ( child.Name == $"Clothing - {clothing.ResourceName}" ) continue;

			child.Destroy();
		}

		itemRenderers = Body.GameObject.Children
			.Where( child => !child.IsDestroyed && child.Tags.Has( "clothing" ) )
			.SelectMany( child => child.GetComponents<SkinnedModelRenderer>() ).ToArray();

		// Removing the Dresser's default underwear must also restore the limbs it hid.
		// Skin portraits still preserve their selected skin's bodygroup choices.
		foreach ( var (name, value) in container.GetBodyGroups( new[] { clothing }, Body.Model ) )
		{
			if ( value == 0 && !wantsGreySkin ) continue;

			Body.SetBodyGroup( name, value );
		}

		if ( wantsGreySkin )
		{
			var greySkin = Material.Load( "models/citizen/skin/citizen_skin_grey.vmat" );

			// The base mannequin also contains untagged materials, such as the mouth.
			// Override all of it, while preserving the clothing's own materials.
			Body.MaterialOverride = greySkin;
			foreach ( var model in itemRenderers )
			{
				model.SetMaterialOverride( greySkin, "skin" );
				model.SetMaterialOverride( greySkin, "face" );
				model.SetMaterialOverride( greySkin, "eyes" );
				model.SetMaterialOverride( greySkin, "eyeao" );
			}
		}
	}

	/// <summary>
	/// Updates the Human pose, facial expression, lighting and icon framing.
	/// </summary>
	public void Update()
	{
		UpdateLighting();

		Body.Set( "b_grounded", true );
		Body.Set( "aim_eyes", Vector3.Forward * 100.0f );
		Body.Set( "aim_head", Vector3.Forward * 100.0f );
		Body.Set( "aim_body", Vector3.Forward * 100.0f );
		Body.Set( "aim_body_weight", 1.0f );
		Body.Set( "static_pose", 1 );

		// Clothing sits on a neutral mannequin. Expressions belong to skin portraits.
		var expression = clothing?.HasHumanSkin == true ? 1.0f : 0.0f;
		Body.Morphs.Set( "mouthSmile_L", 0.5f * expression );
		Body.Morphs.Set( "mouthSmile_R", 0.5f * expression );
		Body.Morphs.Set( "browInnerUp", 0.9f * expression );
		Body.Morphs.Set( "browOuterUp_L", 0.3f * expression );
		Body.Morphs.Set( "browOuterUp_R", 0.3f * expression );
		Body.Morphs.Set( "jawOpen", 0.1f * expression );

		Scene.EditorTick( RealTime.Now, RealTime.Delta );

		UpdateCameraPosition();
	}

	void UpdateLighting()
	{
		using var _ = Scene.Push();

		// Neutral key and fill keep the clothing colors readable.
		{
			var go = Scene.Directory.FindByName( "sun" )?.FirstOrDefault() ?? new GameObject( true, "sun" );
			var light = go.GetOrAddComponent<DirectionalLight>();
			light.WorldRotation = Rotation.From( 45, -180, 0 );
			light.LightColor = Color.White * 1.5f;
			light.SkyColor = Color.Gray * 0.8f;
		}


		// Subtle cool rim separates dark items without a cyan highlight.
		{
			var go = Scene.Directory.FindByName( "pointlight1" )?.FirstOrDefault() ?? new GameObject( true, "pointlight1" );
			var light = go.GetOrAddComponent<PointLight>();
			light.WorldPosition = new Vector3( -100, 10, 70 ) * 1.1f;
			light.LightColor = new Color( 0.85f, 0.92f, 1.0f ) * 3;
			light.Radius = 500;
			light.Shadows = true;
		}

		// Subtle warm rim.
		{
			var go = Scene.Directory.FindByName( "pointlight3" )?.FirstOrDefault() ?? new GameObject( true, "pointlight3" );
			var light = go.GetOrAddComponent<PointLight>();
			light.WorldPosition = new Vector3( -20, 40, 20 ) * 5;
			light.LightColor = new Color( 1.0f, 0.95f, 0.9f ) * 2;
			light.Radius = 500;
			light.Shadows = true;
		}

		// Neutral front fill reveals fabric and surface details.
		{
			var go = Scene.Directory.FindByName( "pointlight2" )?.FirstOrDefault() ?? new GameObject( true, "pointlight2" );
			var light = go.GetOrAddComponent<PointLight>();
			light.WorldPosition = new Vector3( 50, 100, 170 );
			light.LightColor = Color.White * 5;
			light.Radius = 500;
			light.Shadows = true;
		}

		// envmap
		{
			var go = Scene.Directory.FindByName( "envmap" )?.FirstOrDefault() ?? new GameObject( true, "envmap" );
			var c = go.GetOrAddComponent<EnvmapProbe>();
			c.WorldPosition = new Vector3( 0, 0, 0 );
			c.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
			c.Texture = Texture.Load( "textures/cubemaps/default2.vtex" );
			c.TintColor = Color.White * 0.5f;
			c.Bounds = BBox.FromPositionAndSize( 0, 100000 );
		}

		var sharpen = Scene.Camera.GetOrAddComponent<Sharpen>();
		sharpen.Scale = 0.5f;
	}

	/// <summary>
	/// Fits the clothing in camera space, using tighter padding for full outfits.
	/// </summary>
	void UpdateCameraPosition()
	{
		if ( !Body.IsValid() )
			return;

		var mode = GetFramingMode();
		var fullOutfit = IsFullOutfit( mode );
		var angles = mode switch
		{
			Clothing.IconSetup.IconModes.Hand => new Angles( 15, 130, 0 ),
			Clothing.IconSetup.IconModes.Foot => new Angles( 15, 150, 0 ),
			Clothing.IconSetup.IconModes.Wrist => new Angles( 15, itemRenderers.Any( FrameLeftHand ) ? 220 : 130, 45 ),
			Clothing.IconSetup.IconModes.Ear => new Angles( 5, 150, 0 ),
			Clothing.IconSetup.IconModes.Chest or Clothing.IconSetup.IconModes.Head or Clothing.IconSetup.IconModes.Eyes
				or Clothing.IconSetup.IconModes.Mouth or Clothing.IconSetup.IconModes.HumanSkin => new Angles( 5, 160, 0 ),
			_ => fullOutfit || GetBodyRegion() is not null ? new Angles( 5, 160, 0 ) : new Angles( Pitch, 180 - Yaw, 0 )
		};

		if ( IsWorkshopModel( "finish skins/tactical backpack/models/tactical_backpack_black.vmdl" ) )
			angles = new Angles( 5, 25, 0 );

		if ( IsWorkshopModel( "pirate_hook/pirate_hook_m_human.vmdl" ) )
			angles = new Angles( 15, 230, 45 );

		var rotation = angles.ToRotation();
		var points = GetFramingPoints( mode ).ToArray();
		var bounds = BBox.FromPoints( points.Select( p => rotation.Inverse * p ) );
		var center = rotation * bounds.Center;

		// A long lens keeps full outfits readable from collar to boots without shrinking
		// the lower half through perspective. The mannequin's head is outside this fit.
		Scene.Camera.FieldOfView = fullOutfit ? 5 : 20;
		Scene.Camera.ZFar = 5000;
		Scene.Camera.ZNear = 0.1f;

		// Account for depth as well as width and height, so nearer geometry cannot clip.
		var padding = fullOutfit ? 0.96f : GetBodyRegion() is not null ? 0.94f : 0.88f;
		var slope = MathF.Tan( Scene.Camera.FieldOfView * 0.5f * MathF.PI / 180 ) * padding;
		var distance = 1.0f;
		foreach ( var corner in points )
		{
			var delta = corner - center;
			var extent = MathF.Max( MathF.Abs( Vector3.Dot( delta, rotation.Left ) ), MathF.Abs( Vector3.Dot( delta, rotation.Up ) ) );
			distance = MathF.Max( distance, extent / slope - Vector3.Dot( delta, rotation.Forward ) );
		}

		if ( iconSetup.UsePositionOffset )
			center += rotation * iconSetup.PositionOffset;

		Scene.Camera.WorldPosition = center - rotation.Forward * distance;
		Scene.Camera.WorldRotation = rotation;
	}

	/// <summary>
	/// Legacy Citizen skin presets now choose a Human view from the item category.
	/// </summary>
	Clothing.IconSetup.IconModes GetFramingMode()
	{
		if ( clothing?.HasHumanSkin == true )
			return Clothing.IconSetup.IconModes.HumanSkin;

		// Older workshop earrings used Mouth or CitizenSkin with large Citizen offsets.
		// Fit one earring directly instead of framing the space between the pair.
		if ( clothing?.Category is Clothing.ClothingCategory.EarringStud or Clothing.ClothingCategory.EarringDangle or Clothing.ClothingCategory.EarringSpecial )
			return Clothing.IconSetup.IconModes.Ear;

		// Head-only costumes sometimes shipped as Chest or with no category at all.
		if ( clothing?.HideBody == Clothing.BodyGroups.Head && itemRenderers.Length > 0
			&& itemRenderers.All( x => x.Model.Bounds.Mins.z > 50 ) )
			return Clothing.IconSetup.IconModes.Head;

		if ( IsWorkshopModel( "models/citizen_clothes/trousers/fanny_pack/fanny_pack_male_black.vmdl" ) )
			return Clothing.IconSetup.IconModes.Generic;

		if ( iconSetup.Mode is not (Clothing.IconSetup.IconModes.Generic or Clothing.IconSetup.IconModes.CitizenSkin) )
			return iconSetup.Mode;

		return clothing?.Category switch
		{
			Clothing.ClothingCategory.Gloves => Clothing.IconSetup.IconModes.Hand,
			Clothing.ClothingCategory.Footwear or Clothing.ClothingCategory.Socks or Clothing.ClothingCategory.Heels
				or Clothing.ClothingCategory.Sandals or Clothing.ClothingCategory.Shoes or Clothing.ClothingCategory.Trainers
				or Clothing.ClothingCategory.Boots or Clothing.ClothingCategory.Slippers => Clothing.IconSetup.IconModes.Foot,
			Clothing.ClothingCategory.Wristwear or Clothing.ClothingCategory.WristWatch or Clothing.ClothingCategory.WristBand
				or Clothing.ClothingCategory.WristJewel or Clothing.ClothingCategory.WristSpecial => Clothing.IconSetup.IconModes.Wrist,
			Clothing.ClothingCategory.Eyewear or Clothing.ClothingCategory.GlassesEye or Clothing.ClothingCategory.GlassesSun
				or Clothing.ClothingCategory.GlassesSpecial => Clothing.IconSetup.IconModes.Eyes,
			Clothing.ClothingCategory.Hat or Clothing.ClothingCategory.Headwear or Clothing.ClothingCategory.Hair
				or Clothing.ClothingCategory.HairShort or Clothing.ClothingCategory.HairMedium or Clothing.ClothingCategory.HairLong
				or Clothing.ClothingCategory.HairUpdo or Clothing.ClothingCategory.HairSpecial => Clothing.IconSetup.IconModes.Head,
			_ => Clothing.IconSetup.IconModes.Generic
		};
	}

	/// <summary>
	/// Identifies outfits and long coats that need their complete silhouette visible in small tiles.
	/// </summary>
	bool IsFullOutfit( Clothing.IconSetup.IconModes mode )
	{
		if ( mode is not (Clothing.IconSetup.IconModes.Generic or Clothing.IconSetup.IconModes.Chest) )
			return false;

		return clothing?.Category is Clothing.ClothingCategory.Fullbody or Clothing.ClothingCategory.Dress
				or Clothing.ClothingCategory.Suit or Clothing.ClothingCategory.Uniform or Clothing.ClothingCategory.Coat
			|| clothing?.Category == Clothing.ClothingCategory.Costume
				&& clothing.HideBody.HasFlag( Clothing.BodyGroups.Legs );
	}

	/// <summary>
	/// Composes garments around the part being sold, allowing unrelated mannequin parts outside the tile.
	/// Full outfits retain their complete silhouette instead.
	/// </summary>
	BBox? GetBodyRegion()
	{
		// These legacy workshop definitions have missing categories or use a torso preset
		// for an item whose identifying detail is elsewhere. Keep corrections in the preview,
		// without changing the downloaded author's resource.
		if ( IsWorkshopModel( "human_dino_costume_02.vmdl" ) )
			return BBox.FromPositionAndSize( new Vector3( 4, 0, 62 ), new Vector3( 18, 20, 29 ) );

		if ( IsWorkshopModel( "punkshorts/male_punkshorts.vmdl" ) )
			return BBox.FromPositionAndSize( new Vector3( 2, 0, 33 ), new Vector3( 0, 22, 19 ) );

		if ( IsWorkshopModel( "models/citizen_clothes/trousers/fanny_pack/fanny_pack_male_black.vmdl" ) )
			return BBox.FromPositionAndSize( new Vector3( 2, 0, 37 ), new Vector3( 0, 17.5f, 12 ) );

		if ( IsWorkshopModel( "finish skins/tactical backpack/models/tactical_backpack_black.vmdl" ) )
			return null;

		var mode = GetFramingMode();
		if ( clothing is null || IsFullOutfit( mode ) || mode is not (Clothing.IconSetup.IconModes.Generic or Clothing.IconSetup.IconModes.Chest) )
			return null;

		// Human proportions in model space. The torso compositions deliberately omit the
		// head and sleeve ends, matching the detail coverage of the original inventory icons.
		return clothing.Category switch
		{
			Clothing.ClothingCategory.Bra => BBox.FromPositionAndSize( new Vector3( 2, 0, 51 ), new Vector3( 0, 22, 18 ) ),
			Clothing.ClothingCategory.Underwear or Clothing.ClothingCategory.Underpants => BBox.FromPositionAndSize( new Vector3( 2, 0, 34 ), new Vector3( 0, 22, 16 ) ),
			Clothing.ClothingCategory.Shorts => BBox.FromPositionAndSize( new Vector3( 2, 0, 31 ), new Vector3( 0, 24, 23 ) ),
			Clothing.ClothingCategory.Skirt => BBox.FromPositionAndSize( new Vector3( 2, 0, 29 ), new Vector3( 0, 26, 26 ) ),
			Clothing.ClothingCategory.Bottoms or Clothing.ClothingCategory.Jeans or Clothing.ClothingCategory.Trousers => BBox.FromPositionAndSize( new Vector3( 2, 0, 21 ), new Vector3( 0, 26, 42 ) ),
			Clothing.ClothingCategory.Tops or Clothing.ClothingCategory.TShirt or Clothing.ClothingCategory.Sweatshirt
				or Clothing.ClothingCategory.Hoodie or Clothing.ClothingCategory.Shirt or Clothing.ClothingCategory.Vest
				or Clothing.ClothingCategory.Knitwear or Clothing.ClothingCategory.Jacket or Clothing.ClothingCategory.Cardigan
				or Clothing.ClothingCategory.Coat or Clothing.ClothingCategory.Gilet or Clothing.ClothingCategory.Costume
				=> BBox.FromPositionAndSize( new Vector3( 2, 0, 46 ), new Vector3( 0, 30, 30 ) ),
			_ => null
		};
	}

	/// <summary>
	/// Uses the item rather than the mannequin to determine how much of the frame it occupies.
	/// </summary>
	IEnumerable<Vector3> GetFramingPoints( Clothing.IconSetup.IconModes mode )
	{
		if ( GetBodyRegion() is BBox region )
			return region.Corners.Select( p => Body.WorldTransform.PointToWorld( p ) );

		var renderers = itemRenderers.Where( x => x.IsValid() && !x.GameObject.IsDestroyed ).ToArray();
		if ( renderers.Length > 0 )
		{
			var points = renderers.SelectMany( renderer => GetItemPoints( renderer, mode ) ).ToArray();
			if ( mode == Clothing.IconSetup.IconModes.Ear )
			{
				// Tiny studs need some ear context; large pendants can fill the tile.
				var bounds = BBox.FromPoints( points );
				var minimumSize = clothing.Category == Clothing.ClothingCategory.EarringStud ? 2.5f : 1.5f;
				return BBox.FromPositionAndSize( bounds.Center, Vector3.Max( bounds.Size, new Vector3( minimumSize ) ) ).Corners;
			}

			return points;
		}

		// Skin definitions have no separate clothing mesh. Frame the Human head and neck.
		if ( mode == Clothing.IconSetup.IconModes.HumanSkin && Body.GetAttachment( "eyes" ) is Transform eyes )
			return BBox.FromPositionAndSize( eyes.Position + Vector3.Down, new Vector3( 8, 8, 12 ) ).Corners;

		return Body.Bounds.Corners;
	}

	/// <summary>
	/// Uses mesh geometry for accessories, following the posed head, hand or foot.
	/// Paired items show one side rather than the empty space between limbs.
	/// </summary>
	IEnumerable<Vector3> GetItemPoints( SkinnedModelRenderer renderer, Clothing.IconSetup.IconModes mode )
	{
		var fullOutfit = IsFullOutfit( mode );
		if ( !fullOutfit && mode is not (Clothing.IconSetup.IconModes.Hand or Clothing.IconSetup.IconModes.Foot or Clothing.IconSetup.IconModes.Wrist
			or Clothing.IconSetup.IconModes.Head or Clothing.IconSetup.IconModes.Eyes or Clothing.IconSetup.IconModes.Mouth or Clothing.IconSetup.IconModes.Ear) )
			return renderer.Bounds.Corners;

		if ( !accessoryPoints.TryGetValue( renderer.Model, out var points ) )
		{
			points = renderer.Model.GetVertices()?
				.Where( v => mode switch
				{
					Clothing.IconSetup.IconModes.Hand or Clothing.IconSetup.IconModes.Wrist =>
						!HasPairedHands( renderer ) || (FrameLeftHand( renderer ) ? v.Position.y > 0 : v.Position.y < 0),
					Clothing.IconSetup.IconModes.Ear => v.Position.y < 0,
					Clothing.IconSetup.IconModes.Foot => v.Position.y > 0,
					_ => true
				} )
				.Select( v => v.Position ).ToArray();
			accessoryPoints[renderer.Model] = points;
		}

		if ( points is not { Length: > 0 } )
			return renderer.Bounds.Corners;

		// The icon uses the Human's static pose. Fit the garment's mesh rather than
		// conservative animation bounds, which add empty space above and below it.
		if ( fullOutfit )
			return points.Select( p => renderer.WorldTransform.PointToWorld( p ) );

		var boneName = mode switch
		{
			Clothing.IconSetup.IconModes.Hand or Clothing.IconSetup.IconModes.Wrist => FrameLeftHand( renderer ) ? "hand_L" : "hand_R",
			Clothing.IconSetup.IconModes.Foot => "foot_L",
			Clothing.IconSetup.IconModes.Head or Clothing.IconSetup.IconModes.Eyes or Clothing.IconSetup.IconModes.Mouth or Clothing.IconSetup.IconModes.Ear => "head",
			_ => renderer.Model.Bounds.Center.y > 0 ? "hand_L" : "hand_R"
		};
		if ( renderer.Model.Bones.HasBone( boneName ) && renderer.TryGetBoneTransform( boneName, out var posed ) )
		{
			var bind = renderer.Model.GetBoneTransform( boneName );
			return points.Select( p => posed.PointToWorld( bind.PointToLocal( p ) ) );
		}

		return points.Select( p => renderer.WorldTransform.PointToWorld( p ) );
	}

	/// <summary>
	/// Chooses the decorated hand for asymmetric accessories, otherwise the right side of a pair.
	/// </summary>
	bool FrameLeftHand( SkinnedModelRenderer renderer ) =>
		IsWorkshopModel( "pirate_hook/pirate_hook_m_human.vmdl" ) || renderer.Model.Bounds.Mins.y > 0
			|| renderer.Model.Bones.HasBone( "hand_L" ) && !renderer.Model.Bones.HasBone( "hand_R" );

	/// <summary>
	/// Single wrist attachments may use coordinates relative to the hand, so only split actual pairs.
	/// </summary>
	static bool HasPairedHands( SkinnedModelRenderer renderer ) =>
		renderer.Model.Bones.HasBone( "hand_L" ) && renderer.Model.Bones.HasBone( "hand_R" );

	/// <summary>
	/// Matches legacy workshop content that needs a composition its published metadata cannot describe.
	/// </summary>
	bool IsWorkshopModel( string path ) => string.Equals( clothing?.HumanAltModel, path, StringComparison.OrdinalIgnoreCase );
}
