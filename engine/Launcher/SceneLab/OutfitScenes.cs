using System.Threading.Tasks;

namespace Sandbox.SceneLab;

/// <summary>
/// Workshop clothing on Citizen 2.0's avatar deforms. The deforms are <c>citizen_deforms.prefab</c>, which a citizen's
/// <see cref="Dresser"/> puts under it: model deformers on the neck, waist, chest, head, nose and chin, all with
/// <c>ApplyToBoneMergedChildren</c> on, so the body's clothing is deformed with it. Clothing made before them wasn't made
/// for that, and some of it breaks.
/// </summary>
internal static class OutfitScenes
{
	/// <summary>
	/// https://sbox.game/glitchworkshop/thehotdogcostume/ - broken by the deforms.
	/// </summary>
	public static readonly Costume HotdogCostume = new( "Hotdog Costume", "glitchworkshop.thehotdogcostume" );

	/// <summary>
	/// https://sbox.game/falkoworkshop/mrbananasuit/
	/// </summary>
	public static readonly Costume BananaSuit = new( "Banana Suit", "falkoworkshop.mrbananasuit" );

	/// <summary>
	/// https://sbox.game/glitchworkshop/katana/
	/// </summary>
	public static readonly Costume Katana = new( "Katana", "glitchworkshop.katana" );

	/// <summary>
	/// https://sbox.game/tarbaganchik/golfbackpack
	/// </summary>
	public static readonly Costume GolfBackpack = new( "Golf Backpack", "tarbaganchik.golfbackpack" );

	/// <summary>
	/// The citizen addon's own pearl necklace, around the neck the deforms thin.
	/// </summary>
	public static readonly Costume PearlNecklace = new( "Pearl Necklace", "models/citizen_clothes/necklace/pearl_necklace/pearl_necklace.clothing" );

	/// <summary>
	/// https://sbox.game/pkxuni/earring_onio - hanging from the ears, so framed close on the faces.
	/// </summary>
	public static readonly Costume FriedOnionEarrings = new( "Fried Onion Earrings", "pkxuni.earring_onio", closeUp: true );

	/// <summary>
	/// Every costume, in the All Outfits preset's order: the front rank left to right, then the back. The necklace, the
	/// smallest, is in front. After the costumes, which it's initialised from.
	/// </summary>
	static readonly Costume[] All = [HotdogCostume, PearlNecklace, BananaSuit, Katana, GolfBackpack];

	const string DeformsPrefab = "models/citizen/citizen_deforms.prefab";

	/// <summary>
	/// The deformed citizens' weights, past what the avatar's sliders reach (the neck's 0.5, the nose's 0.8): the neck sucked
	/// in and the nose inflated, so what they do to the costume is plain.
	/// </summary>
	const float ThinNeck = 1.0f;
	const float BigNose = 3.0f;

	/// <summary>
	/// A citizen wearing a costume: where it stands across the camera's view, whether it has the deforms, whether they reach its
	/// costume, whether it has a big head too (<see cref="BigHead"/>), and its label. A rigid one's costume moves with the
	/// deforms without being reshaped (<see cref="SkinnedModelRenderer.DeformationMode"/>).
	/// </summary>
	record struct Variant( string Name, float Y, bool Deforms, bool DeformCostume, bool BigHead, string Title, string Body, string Costume, bool Rigid = false );

	/// <summary>
	/// A costume's citizens, left to right as the camera sees them. The last is the proposed fix: the costume left alone while
	/// the body under it keeps its deforms.
	/// </summary>
	static readonly Variant[] Citizens =
	[
		new( "Reference", 58, false, false, false, "Reference", "No deforms", "Costume as made" ),
		new( "Deformed", 0, true, true, false, "Both Deformed", "Thin neck, big nose", "Costume deformed too" ),
		new( "Body Only", -58, true, false, false, "Body Only", "Thin neck, big nose", "Costume not deformed" ),
	];

	/// <summary>
	/// A close-up costume's citizens, left to right, all with the deforms: reaching the costume, not reaching it, not reaching
	/// it with a big head too - where a costume on the head, left where it was made to sit, ends up inside it - and moving it
	/// rigidly with the big head, which should carry it out with the ears.
	/// </summary>
	static readonly Variant[] CloseUpCitizens =
	[
		new( "Item Deformed", 42, true, true, false, "Deformed", "Character deforms", "Item deformed too" ),
		new( "Item Not Deformed", 14, true, false, false, "Opted Out", "Character deforms", "Item not deformed" ),
		new( "Big Head", -14, true, false, true, "Big Head", "Deforms, big head", "Item not deformed" ),
		new( "Rigid", -42, true, true, true, "Rigid", "Deforms, big head", "Item moved, not bent", Rigid: true ),
	];

	/// <summary>
	/// The big head: a sphere around the head inflating it, which the avatar's deforms haven't got (their head deform squashes
	/// and stretches), from the head deform's centre in the citizen's model space.
	/// </summary>
	static readonly Vector3 HeadCenter = new( 1.2f, 0, 63 );
	const float HeadRadius = 14;
	const float BigHead = 0.6f;

	/// <summary>
	/// The All Outfits preset's layout: its costumes' groups of three, this many to a rank across the camera's view, this far
	/// apart; and the ranks this far apart going back - far enough, from its camera's 33 degrees above, that the heads of one
	/// don't hide the feet of the one behind.
	/// </summary>
	const int GroupsPerRank = 3;
	const float GroupSpacing = 200;
	const float RankSpacing = 190;

	/// <summary>
	/// Every costume at once: three groups of three citizens across the front and the rest behind, each group named at its
	/// feet and each citizen tagged with what it is.
	/// </summary>
	public static Scene AllOutfits() => Build( All );

	/// <summary>
	/// Dress every group once its costume has loaded.
	/// </summary>
	public static void DressAll( Scene scene )
	{
		foreach ( var costume in All ) costume.DressWhenLoaded( scene );
	}

	/// <summary>
	/// Every group is dressed - captures wait for it.
	/// </summary>
	public static bool AllDressed( Scene scene ) => All.All( x => x.Dressed( scene ) );

	/// <summary>
	/// The stage and a group of undressed citizens per costume, centred on where SceneLab's camera orbits. One costume is
	/// framed close from the front, with what each citizen is at its feet; several from above and in front, in ranks.
	/// </summary>
	static Scene Build( Costume[] costumes )
	{
		var single = costumes.Length == 1;
		var (cameraPosition, lookAt) = single ? (new Vector3( -210, 0, 80 ), new Vector3( 0, 0, 40 )) : (new Vector3( -420, 0, 300 ), new Vector3( 0, 0, 30 ));
		var scene = GameScenes.Stage( out var camera, cameraPosition, lookAt );
		using var _ = scene.Push();

		// The stage's sun lights them from behind; from over the camera's shoulder instead, onto their fronts
		scene.Directory.FindByName( "Sun" ).First().WorldRotation = Rotation.LookAt( new Vector3( 0.7f, 0.35f, -0.6f ) );

		var ranks = (costumes.Length + GroupsPerRank - 1) / GroupsPerRank;
		for ( int i = 0; i < costumes.Length; i++ )
		{
			var costume = costumes[i];
			var (rank, slot) = Math.DivRem( i, GroupsPerRank );
			var inRank = Math.Min( GroupsPerRank, costumes.Length - rank * GroupsPerRank );

			// Ranks go back from the camera, and a rank's groups are centred across it, its first on the left
			var group = new Vector3( (rank - (ranks - 1) * 0.5f) * RankSpacing, ((inRank - 1) * 0.5f - slot) * GroupSpacing, 0 );

			foreach ( var citizen in Citizens )
			{
				var feet = group + new Vector3( 0, citizen.Y, 0 );
				GameScenes.Citizen( costume.CitizenName( citizen.Name ), feet );

				if ( single ) Label( $"{citizen.Name} Label", feet + new Vector3( -20, 0, 10 ), 1, citizen.Title, citizen.Body, citizen.Costume );
				else Label( $"{costume.CitizenName( citizen.Name )} Tag", feet + new Vector3( -18, 0, 4 ), 0.75f, citizen.Title );
			}

			if ( !single ) Label( $"{costume.Name} Label", group + new Vector3( -48, 0, 6 ), 1.5f, costume.Name );
		}

		foreach ( var costume in costumes ) costume.Reset();
		return scene;
	}

	/// <summary>
	/// Four citizens' faces up close (<see cref="CloseUpCitizens"/>), sunk through the floor so their faces are at the height SceneLab's
	/// camera orbits, as the Morphs preset's are - for what's worn on the head.
	/// </summary>
	static Scene BuildCloseUp( Costume costume )
	{
		var scene = GameScenes.Stage( out var camera, new Vector3( -125, 0, 33 ), new Vector3( 0, 0, 30 ) );
		using var _ = scene.Push();

		// The stage's sun lights them from behind; from over the camera's shoulder instead, onto their faces
		scene.Directory.FindByName( "Sun" ).First().WorldRotation = Rotation.LookAt( new Vector3( 0.7f, 0.35f, -0.6f ) );

		foreach ( var citizen in CloseUpCitizens )
		{
			var feet = new Vector3( 0, citizen.Y, -33 );
			var body = GameScenes.Citizen( costume.CitizenName( citizen.Name ), feet );
			Label( $"{citizen.Name} Label", feet + new Vector3( -4, 0, 80 ), 0.4f, citizen.Title, citizen.Body, citizen.Costume );

			if ( !citizen.BigHead ) continue;

			var head = new GameObject( body.GameObject, true, "Big Head" );
			head.LocalPosition = HeadCenter;
			var deformer = head.Components.Create<ModelDeformer>();
			deformer.SceneVolume = new Sandbox.Volumes.SceneVolume { Type = Sandbox.Volumes.SceneVolume.VolumeTypes.Sphere, Sphere = new Sphere( 0, HeadRadius ) };
			deformer.Operation = ModelDeformer.OperationType.Inflate;
			deformer.Inflation = BigHead;
			deformer.ApplyToBoneMergedChildren = true;
		}

		costume.Reset();
		return scene;
	}

	/// <summary>
	/// A preset's costume: a clothing package, or the citizen addon's clothing by its path, on three citizens in one pose, each
	/// with a world panel at its feet saying what it is (see <see cref="Citizens"/>). They're dressed once the costume has
	/// loaded (<see cref="DressWhenLoaded"/>).
	/// </summary>
	internal sealed class Costume( string name, string source, bool closeUp = false )
	{
		public string Name => name;

		// Loaded once for the whole run
		Task<Clothing> clothing;

		// The citizen addon's clothing is on disk; a package waits for the backend and downloads
		bool IsLocal => source.EndsWith( ".clothing", StringComparison.OrdinalIgnoreCase );

		// The scene last dressed, so a scene is dressed once and Dressed says when
		Scene dressedScene;

		public string CitizenName( string citizen ) => $"{name}: {citizen}";

		/// <summary>
		/// The stage and this costume's citizens, undressed.
		/// </summary>
		public Scene Build() => closeUp ? BuildCloseUp( this ) : OutfitScenes.Build( [this] );

		/// <summary>
		/// A new scene isn't dressed yet.
		/// </summary>
		public void Reset() => dressedScene = null;

		/// <summary>
		/// Load the costume - a package once the backend is up - and dress its citizens in it once it has.
		/// </summary>
		public void DressWhenLoaded( Scene scene )
		{
			if ( dressedScene == scene || (!IsLocal && !PanelAppSystem.ApiReady.IsCompleted) ) return;

			clothing ??= IsLocal ? Task.FromResult( GameScenes.LoadResource<Clothing>( source, "addons/citizen/Assets" ) ) : LoadClothing( source );
			if ( !clothing.IsCompleted ) return;

			// A panel app doesn't register game resources, so the Dresser wouldn't find the deforms prefab itself
			GameScenes.LoadResource<PrefabFile>( DeformsPrefab, "addons/citizen/Assets" );

			// Dressed in nothing if it didn't load, so a capture still finishes
			if ( clothing.IsFaulted ) Log.Warning( clothing.Exception, $"Scene Lab: couldn't load {source}" );

			var outfit = new ClothingContainer();
			if ( clothing.IsCompletedSuccessfully && clothing.Result is { } loaded ) outfit.Add( new ClothingContainer.ClothingEntry( loaded ) );

			foreach ( var citizen in closeUp ? CloseUpCitizens : Citizens )
			{
				var body = scene.Directory.FindByName( CitizenName( citizen.Name ) ).First().Components.Get<SkinnedModelRenderer>();
				BridgeScene.Dress( outfit, body );

				foreach ( var item in body.GameObject.Children.Select( x => x.Components.Get<SkinnedModelRenderer>() ).Where( x => x?.BoneMergeTarget == body ) )
					item.DeformationMode = !citizen.DeformCostume ? SkinnedModelRenderer.DeformationModeType.None
						: citizen.Rigid ? SkinnedModelRenderer.DeformationModeType.Rigid : SkinnedModelRenderer.DeformationModeType.Normal;

				var root = body.Components.Get<Dresser>().UpdateDeforms( outfit, citizen.Deforms );
				if ( citizen.Deforms && root is null ) Log.Warning( $"Scene Lab: no {DeformsPrefab}" );

				foreach ( var deformer in root?.GetComponentsInChildren<ModelDeformer>( true ) ?? [] )
				{
					deformer.ApplyToBoneMergedChildren = true;

					// Set past the sliders, after the Dresser has set them from its appearance
					if ( deformer.GameObject.Name == "deform_neck" ) deformer.Weight = ThinNeck;
					if ( deformer.GameObject.Name == "deform_nose" ) deformer.Weight = BigNose;
				}
			}

			dressedScene = scene;
		}

		/// <summary>
		/// Every citizen is dressed - captures wait for it.
		/// </summary>
		public bool Dressed( Scene scene ) => dressedScene == scene;
	}

	/// <summary>
	/// A label facing the camera: a world panel with a title, and lines under it if it has any, <paramref name="scale"/> times
	/// its size.
	/// </summary>
	static void Label( string name, Vector3 position, float scale, string title, params string[] lines )
	{
		var go = new GameObject( true, name );
		go.WorldPosition = position;
		go.WorldRotation = Rotation.FromYaw( 180 );
		go.WorldScale = scale;

		// 0.05 units a pixel: 50 by 20 units, or 60 by 10 for a title alone
		var panel = go.Components.Create<WorldPanel>();
		panel.PanelSize = lines.Length > 0 ? new Vector2( 1000, 400 ) : new Vector2( 1200, 200 );

		var root = panel.GetPanel();
		root.StyleSheet.Add( Sandbox.UI.StyleSheet.FromString( LabelStyles, "/scenelab/outfit.scss" ) );
		var card = root.Add.Panel( "tag" );
		card.Add.Label( title, "title" );
		foreach ( var line in lines ) card.Add.Label( line, "body" );
	}

	const string LabelStyles = """
		.tag { position: absolute; left: 0; top: 0; right: 0; bottom: 0; flex-direction: column; align-items: center;
			justify-content: center; font-family: Poppins; color: white; background-color: rgba( 10, 14, 24, 0.75 );
			border-radius: 24px; }
		.title { font-size: 64px; font-weight: 700; white-space: nowrap; }
		.body { font-size: 40px; color: rgba( 255, 255, 255, 0.8 ); white-space: nowrap; }
		""";

	/// <summary>
	/// A clothing package's primary asset: fetched, mounted, and loaded from its compiled file in the package if mounting
	/// didn't register it - a panel app doesn't register game resources as a game does (<see cref="GameScenes.LoadResource"/>).
	/// </summary>
	static async Task<Clothing> LoadClothing( string ident )
	{
		MapScenes.EnableDownloads();

		var package = await Package.FetchAsync( ident, false );
		if ( package?.PrimaryAsset is not { Length: > 0 } path )
		{
			Log.Warning( $"Scene Lab: no package {ident}, or it has no primary asset" );
			return null;
		}

		var fs = await package.MountAsync();
		if ( fs is null )
		{
			Log.Warning( $"Scene Lab: couldn't mount {ident}" );
			return null;
		}

		if ( ResourceLibrary.TryGet<Clothing>( path, out var clothing ) ) return clothing;

		var file = path + "_c";
		if ( fs.FileExists( file ) && GameResource.GetPromise( typeof( Clothing ), path ) is Clothing promise && promise.TryLoadFromData( fs.ReadAllBytes( file ) ) )
			return promise;

		Log.Warning( $"Scene Lab: no clothing {path} in {ident}" );
		return null;
	}
}
