namespace SceneTests.Components;

/// <summary>
/// Checks that a Dresser owns one body's appearance and releases its generated state.
/// </summary>
[TestClass]
public class DresserTests
{
	private static SkinnedModelRenderer CreateBody( Scene scene )
	{
		var body = scene.CreateObject().AddComponent<SkinnedModelRenderer>( false );
		body.Model = Model.Load( "models/citizen_human/human.vmdl" );
		Assert.IsFalse( body.Model.IsError, "The Human model must load for dressing tests" );
		body.Enabled = true;
		return body;
	}

	private static GameObject LegacyDeforms( SkinnedModelRenderer body ) =>
		body.GameObject.Children.SingleOrDefault( x => !x.IsDestroyed && x.Name == "citizen_deforms" );

	/// <summary>
	/// Explicit outfit requests remain supported on remote bodies through both public APIs.
	/// </summary>
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( false, true )]
	[DataRow( true, false )]
	[DataRow( true, true )]
	public async Task ExplicitOutfitsApplyToProxies( bool useLegacyApi, bool asynchronous )
	{
		var scene = new Scene();
		using var scope = scene.Push();
		using var connections = new ClientAndHost( Game.TypeLibrary );
		connections.BecomeClient();

		var body = CreateBody( scene );
		body.GameObject.NetworkSpawn( connections.Host );
		Assert.IsTrue( body.IsProxy );

		var outfit = new ClothingContainer { Age = 0.8f };
		outfit.Add( new Clothing { HumanAltModel = "models/citizen_human/human.vmdl" } );

		if ( useLegacyApi )
		{
#pragma warning disable CS0618 // Exercise the compatibility entry points.
			if ( asynchronous )
			{
				await outfit.ApplyAsync( body, default );
			}
			else
			{
				outfit.Apply( body );
			}
#pragma warning restore CS0618
		}
		else
		{
			var dresser = Dresser.GetOrCreate( body );
			dresser.UpdateAppearance( outfit );

			if ( asynchronous )
			{
				await dresser.ApplyAsync( outfit );
			}
			else
			{
				dresser.Apply( outfit );
			}
		}

		Assert.AreEqual( 1, body.GameObject.Children.Count( x => !x.IsDestroyed && x.Tags.Has( "clothing" ) ) );
		Assert.AreEqual( 0.8f, body.Attributes.GetFloat( "skin_age" ) );
	}

	/// <summary>
	/// Resetting an independently dressed body must not initialize a Dresser or overwrite appearance.
	/// </summary>
	[TestMethod]
	public void LegacyResetDoesNotCreateDresser()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateBody( scene );
		body.Attributes.Set( "skin_age", 0.9f );
		body.Attributes.Set( "skin_tint", 0.7f );
		var clothing = scene.CreateObject();
		clothing.Parent = body.GameObject;
		clothing.Tags.Add( "clothing" );

#pragma warning disable CS0618 // Exercise the compatibility entry point.
		new ClothingContainer().Reset( body );
#pragma warning restore CS0618
		scene.ProcessDeletes();

		Assert.IsTrue( clothing.IsDestroyed );
		Assert.IsNull( Dresser.Find( body ) );
		Assert.IsFalse( body.GameObject.Children.Any( x => x.Name == "citizen_deforms" ) );
		Assert.AreEqual( 0.9f, body.Attributes.GetFloat( "skin_age" ) );
		Assert.AreEqual( 0.7f, body.Attributes.GetFloat( "skin_tint" ) );
	}

	/// <summary>
	/// An inactive Human body's existing Dresser and appearance survive a clothing reset.
	/// </summary>
	[TestMethod]
	public void LegacyResetPreservesExistingAppearance()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateBody( scene );
		var dresser = Dresser.GetOrCreate( body );
		var outfit = new ClothingContainer { Age = 0.9f, NeckSize = 0.8f, EyeColor = 0.7f };
		outfit.Add( new Clothing { HumanAltModel = "models/citizen_human/human.vmdl" } );
		dresser.UpdateAppearance( outfit );
		dresser.Apply( outfit );
		Assert.IsNull( LegacyDeforms( body ), "Human bodies must not create Citizen deformation prefabs" );
		body.GameObject.Enabled = false;

#pragma warning disable CS0618 // Exercise the compatibility entry point.
		outfit.Reset( body );
#pragma warning restore CS0618
		scene.ProcessDeletes();

		Assert.AreSame( dresser, Dresser.Find( body ) );
		Assert.IsNull( LegacyDeforms( body ), "Human appearance updates must not create Citizen deformation prefabs" );
		Assert.IsFalse( body.GameObject.Children.Any( x => x.Tags.Has( "clothing" ) ) );
		Assert.AreEqual( 0.9f, body.Attributes.GetFloat( "skin_age" ) );
		Assert.AreEqual( 0.8f, dresser.NeckSize );
		Assert.AreEqual( 0.7f, dresser.EyeColor );
	}

	/// <summary>
	/// The hidden preview body must reuse its component rather than accumulating Dressers.
	/// </summary>
	[TestMethod]
	public void ReusesDresserOnInactiveBody()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateBody( scene );
		body.GameObject.Enabled = false;

		var dresser = Dresser.GetOrCreate( body );
		Assert.AreSame( dresser, Dresser.GetOrCreate( body ) );
		Assert.AreEqual( 1, body.GetComponents<Dresser>( true ).Count() );
	}

	/// <summary>
	/// Human appearance edits retain clothing renderers and update their values without Citizen deforms.
	/// </summary>
	[TestMethod]
	public void AppearanceEditsPreserveOutfitObjects()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateBody( scene );
		var dresser = Dresser.GetOrCreate( body );
		var outfit = new ClothingContainer();
		var item = new Clothing
		{
			HumanAltModel = "models/citizen_human/human.vmdl",
			AllowTintSelect = true,
			TintSelection = new Gradient( new Gradient.ColorFrame( 0, Color.Red ), new Gradient.ColorFrame( 1, Color.Blue ) )
		};
		var entry = outfit.Add( item );
		dresser.UpdateAppearance( outfit );
		dresser.Apply( outfit );
		var clothing = body.GameObject.Children.Single( x => !x.IsDestroyed && x.Tags.Has( "clothing" ) );
		Assert.IsNull( LegacyDeforms( body ), "Human bodies must not create Citizen deformation prefabs" );

		outfit.Age = 0.8f;
		outfit.Height = 0.9f;
		outfit.NeckSize = 0.7f;
		outfit.EyeColor = 0.65f;
		entry.Tint = 1;
		dresser.UpdateAppearance( outfit );

		Assert.IsNull( LegacyDeforms( body ), "Human appearance updates must not create Citizen deformation prefabs" );
		Assert.AreSame( clothing, body.GameObject.Children.Single( x => !x.IsDestroyed && x.Tags.Has( "clothing" ) ) );
		Assert.AreEqual( 0.8f, body.Attributes.GetFloat( "skin_age" ) );
		Assert.AreEqual( Color.Blue, clothing.GetComponent<SkinnedModelRenderer>().Tint );
		Assert.AreEqual( 0.7f, dresser.NeckSize );
		Assert.AreEqual( 0.65f, dresser.EyeColor );
	}

	/// <summary>
	/// Outfit application uses current appearance even if the supplied outfit has older values.
	/// </summary>
	[TestMethod]
	public async Task OutfitDoesNotImportStaleAppearance()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateBody( scene );
		var dresser = Dresser.GetOrCreate( body );
		dresser.UpdateAppearance( new ClothingContainer { EyeColor = 0.8f, NeckSize = 0.9f, Age = 0.7f } );

		await dresser.ApplyAsync( new ClothingContainer { EyeColor = 0.1f, NeckSize = 0.2f, Age = 0.3f } );

		Assert.AreEqual( 0.8f, dresser.EyeColor );
		Assert.AreEqual( 0.9f, dresser.NeckSize );
		Assert.AreEqual( 0.7f, body.Attributes.GetFloat( "skin_age" ) );
		Assert.IsFalse( dresser.IsDressing );
	}

	/// <summary>
	/// Retargeting transfers the model callback to the new Human body while retaining live values.
	/// </summary>
	[TestMethod]
	public void RetargetingReleasesPreviousBody()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var first = CreateBody( scene );
		var second = CreateBody( scene );
		var dresser = Dresser.GetOrCreate( first );
		dresser.NeckSize = 0.8f;
		Assert.IsTrue( first.ModelChanged?.GetInvocationList().Any( x => x.Target == dresser ) ?? false );

		dresser.BodyTarget = second;
		scene.ProcessDeletes();

		Assert.IsNull( LegacyDeforms( first ) );
		Assert.IsFalse( first.ModelChanged?.GetInvocationList().Any( x => x.Target == dresser ) ?? false );
		Assert.IsTrue( second.ModelChanged?.GetInvocationList().Any( x => x.Target == dresser ) ?? false );
		Assert.IsNull( LegacyDeforms( second ), "Retargeting to Human must not create Citizen deformation prefabs" );
		Assert.AreEqual( 0.8f, dresser.NeckSize );
	}

	/// <summary>
	/// Destroying the Dresser releases its model callback while the Human body remains alive.
	/// </summary>
	[TestMethod]
	public void DestructionReleasesBodyCallback()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateBody( scene );
		var dresser = Dresser.GetOrCreate( body );
		Assert.IsTrue( body.ModelChanged?.GetInvocationList().Any( x => x.Target == dresser ) ?? false );
		Assert.IsNull( LegacyDeforms( body ), "Human bodies must not create Citizen deformation prefabs" );

		dresser.Destroy();
		scene.ProcessDeletes();

		Assert.IsTrue( body.IsValid() );
		Assert.IsNull( LegacyDeforms( body ) );
		Assert.IsFalse( body.ModelChanged?.GetInvocationList().Any( x => x.Target == dresser ) ?? false );
	}

	/// <summary>
	/// An authored deformation child on a Human body must survive destruction of the Dresser.
	/// </summary>
	[TestMethod]
	public void AuthoredDeformsAreNotDestroyed()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateBody( scene );
		var authored = scene.CreateObject();
		authored.Name = "citizen_deforms";
		authored.Parent = body.GameObject;
		var dresser = Dresser.GetOrCreate( body );

		dresser.Destroy();
		scene.ProcessDeletes();

		Assert.IsFalse( authored.IsDestroyed );
	}

	/// <summary>
	/// Legacy container entry points delegate to the same Dresser and remain callable.
	/// </summary>
	[TestMethod]
	public async Task LegacyContainerMethodsReuseTheDresser()
	{
		var scene = new Scene();
		using var scope = scene.Push();
		var body = CreateBody( scene );
		var outfit = new ClothingContainer { EyeColor = 0.8f, NeckSize = 0.7f, Height = 2, Age = 2 };

#pragma warning disable CS0618 // Exercise the compatibility entry points.
		outfit.Apply( body );
		var dresser = Dresser.GetOrCreate( body );
		Assert.IsNull( LegacyDeforms( body ), "Human bodies must not create Citizen deformation prefabs" );
		await outfit.ApplyAsync( body, default );
		Assert.IsNull( outfit.ApplyDeforms( body ), "The legacy deformation API must leave Human bodies unchanged" );
		outfit.ApplyEyes( body );
		var cancelled = outfit.ApplyAsync( body, new System.Threading.CancellationToken( true ) );
		Assert.IsTrue( cancelled.IsCanceled, "Cancellation should return a cancelled task, not throw synchronously." );
#pragma warning restore CS0618

		Assert.AreSame( dresser, Dresser.GetOrCreate( body ) );
		Assert.AreEqual( 1, body.GetComponents<Dresser>( true ).Count() );
		Assert.AreEqual( 0.8f, dresser.EyeColor );
		Assert.AreEqual( 0.7f, dresser.NeckSize );
		Assert.AreEqual( 1f, dresser.ManualHeight );
		Assert.AreEqual( 1f, dresser.ManualAge );
	}
}
