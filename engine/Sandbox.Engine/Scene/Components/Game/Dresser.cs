using System.Threading;

namespace Sandbox;

/// <summary>
/// Allows easily dressing a citizen or human in clothing
/// </summary>
[Alias( "Sandbox.ApplyLocalClothing" )]
[Expose]
[Title( "Dresser" )]
[Category( "Game" )]
[Icon( "checkroom" )]
public sealed partial class Dresser : Component, Component.ExecuteInEditor
{
	public enum ClothingSource
	{
		/// <summary>
		/// Manually select the clothing to wear
		/// </summary>
		Manual,

		/// <summary>
		/// Dress according to the local user's avatar
		/// </summary>
		LocalUser,

		/// <summary>
		/// Dress according to the avatar of the network owner of this GameObject
		/// </summary>
		OwnerConnection
	}

	/// <summary>
	/// Where to get the clothing from
	/// </summary>
	[Property]
	public ClothingSource Source { get; set; }

	/// <summary>
	/// When using <see cref="ClothingSource.OwnerConnection"/>, strip any clothing items that are not owned in their Steam Inventory.
	/// Disable only if your game handles ownership checks itself.
	/// </summary>
	[Property]
	[ShowIf( nameof( Source ), ClothingSource.OwnerConnection )]
	public bool RemoveUnownedItems { get; set; } = true;

	/// <summary>
	/// Who are we dressing? This should be the renderer of the body of a Citizen or Human
	/// </summary>
	[Property]
	public SkinnedModelRenderer BodyTarget
	{
		get;
		set
		{
			if ( field == value )
				return;

			CancelDressing();
			DetachBody();
			field = value;

			if ( field.IsValid() )
				field.ModelChanged += UpdateAppearance;

			UpdateAppearance();
		}
	}

	/// <summary>
	/// Should we change the height too?
	/// </summary>
	[Property]
	public bool ApplyHeightScale { get; set; } = true;

	[Header( "Manual Attributes" )]
	[ShowIf( "Source", ClothingSource.Manual )]
	[Property, Range( 0, 1 )]
	[Change( nameof( OnManualChange ) )]
	[Sync]
	public float ManualHeight { get; set; } = 0.5f;

	[ShowIf( "Source", ClothingSource.Manual )]
	[Property, Range( 0, 1 )]
	[Change( nameof( OnManualChange ) )]
	[Sync]
	public float ManualTint { get; set; } = 0.5f;

	[ShowIf( "Source", ClothingSource.Manual )]
	[Property, Range( 0, 1 )]
	[Change( nameof( OnManualChange ) )]
	[Sync]
	public float ManualAge { get; set; } = 0.5f;

	[Header( "Manual Items" )]
	[ShowIf( "Source", ClothingSource.Manual )]
	[Property]
	public List<ClothingContainer.ClothingEntry> Clothing { get; set; } = [];

	[ShowIf( "Source", ClothingSource.Manual )]
	[Property]
	public List<string> WorkshopItems { get; set; }

	protected override void OnAwake()
	{
		if ( IsProxy || _hasOutfitRequest )
			return;

		_ = Apply();
	}

	protected override void OnEnabled() => UpdateAppearance();

	protected override void OnDestroy()
	{
		CancelDressing();
		DetachBody();
	}

	private void DetachBody()
	{
		if ( BodyTarget is not null )
			BodyTarget.ModelChanged -= UpdateAppearance;

		ReleaseDeforms();
		_clothingRenderers.Clear();
	}

	/// <summary>
	/// Finds the Dresser for a body or adds a transient, manually controlled one.
	/// Reuse the returned component for outfit requests and live appearance edits.
	/// </summary>
	public static Dresser GetOrCreate( SkinnedModelRenderer body )
	{
		ArgumentNullException.ThrowIfNull( body );
		var dresser = Find( body );
		if ( dresser.IsValid() )
			return dresser;

		using var scope = body.Scene.Push();
		dresser = body.GameObject.AddComponent<Dresser>( false );
		dresser.Flags |= ComponentFlags.NotSaved | ComponentFlags.NotNetworked;
		dresser._hasOutfitRequest = true;
		dresser.BodyTarget = body;
		dresser.Enabled = true;
		return dresser;
	}

	/// <summary>
	/// Finds an existing Dresser for a body, including inactive components.
	/// </summary>
	internal static Dresser Find( SkinnedModelRenderer body ) =>
		body.Scene.GetComponentsInChildren<Dresser>( true ).FirstOrDefault( x => x.IsValid() && x.BodyTarget == body );

	async Task<Clothing> InstallWorkshopClothing( string ident, CancellationToken ct )
	{
		if ( string.IsNullOrEmpty( ident ) ) return default;

		var package = await Package.FetchAsync( ident, false );
		if ( package is null ) return default;
		if ( package.TypeName != "clothing" ) return default;
		if ( ct.IsCancellationRequested ) return default;

		var primaryAsset = package.PrimaryAsset;
		if ( string.IsNullOrWhiteSpace( primaryAsset ) ) return default;

		var fs = await package.MountAsync();
		if ( fs is null ) return default;
		if ( ct.IsCancellationRequested ) return default;

		// try to load it
		return ResourceLibrary.Get<Clothing>( primaryAsset );
	}

	CancellationTokenSource _cts;

	/// <summary>
	/// If we're dressing in an async way - stop it.
	/// </summary>
	public void CancelDressing()
	{
		var request = _cts;
		_cts = null;
		request?.Cancel();
		request?.Dispose();
	}

	async ValueTask<ClothingContainer> GetClothing( CancellationToken token )
	{
		if ( Source == ClothingSource.OwnerConnection )
		{
			if ( Network.Owner != null )
			{
				return ClothingContainer.CreateFromConnection( Network.Owner, RemoveUnownedItems );
			}

			return new ClothingContainer();
		}

		if ( Source == ClothingSource.LocalUser )
		{
			return ClothingContainer.CreateFromLocalUser();
		}

		if ( Source == ClothingSource.Manual )
		{
			var clothing = new ClothingContainer();
			clothing.AddRange( Clothing );
			SetClothingTints( Clothing );

			if ( WorkshopItems != null && WorkshopItems.Count > 0 )
			{
				var tasks = WorkshopItems.Select( x => InstallWorkshopClothing( x, token ) );

				foreach ( var task in tasks )
				{
					var c = await task;

					if ( c is null )
						continue;

					clothing.Add( c );
				}
			}

			clothing.Normalize();
			return clothing;
		}

		return null;
	}

	/// <summary>
	/// True if we're dressing, in an async way
	/// </summary>
	public bool IsDressing => _cts is not null;

	/// <summary>
	/// Removes the outfit while preserving the current appearance.
	/// </summary>
	[Button( "Clear Clothing" )]
	public void Clear() => Apply( new ClothingContainer() );

	/// <summary>
	/// Applies clothing and appearance from the selected avatar source.
	/// </summary>
	[Button( "Apply Clothing" )]
	public ValueTask Apply() => Apply( true );

	/// <summary>
	/// Applies an outfit immediately, using this Dresser's current appearance values.
	/// Missing clothing is skipped. Use UpdateAppearance to import appearance from a container.
	/// </summary>
	public void Apply( ClothingContainer clothing )
	{
		ArgumentNullException.ThrowIfNull( clothing );
		CancelDressing();
		_hasOutfitRequest = true;

		if ( !BodyTarget.IsValid() )
			return;

		ApplyClothing( clothing, BodyTarget );
		BodyTarget.MergeDescendants();
	}

	/// <summary>
	/// Applies an outfit immediately and downloads missing items. Completion uses the
	/// current appearance properties, including edits made while the download was pending.
	/// A newer outfit request, retargeting or destruction cancels this request.
	/// </summary>
	public Task ApplyAsync( ClothingContainer clothing, CancellationToken token = default )
	{
		ArgumentNullException.ThrowIfNull( clothing );
		return DressAsync( clothing, false, token );
	}

	private bool _hasOutfitRequest;
	private ClothingSource? _appearanceSource;

	private ValueTask Apply( bool loadAppearance ) => new( DressAsync( null, loadAppearance, default ) );

	private async Task DressAsync( ClothingContainer clothing, bool loadAppearance, CancellationToken cancellationToken )
	{
		cancellationToken.ThrowIfCancellationRequested();
		CancelDressing();
		_hasOutfitRequest = true;

		var body = BodyTarget;
		if ( !body.IsValid() )
			return;

		var request = CancellationTokenSource.CreateLinkedTokenSource( cancellationToken );
		_cts = request;
		var token = request.Token;

		try
		{
			if ( clothing is null )
			{
				var source = Source;
				clothing = await GetClothing( token );
				token.ThrowIfCancellationRequested();
				if ( clothing is null )
					return;

				if ( source != ClothingSource.Manual && (loadAppearance || _appearanceSource != source) )
					UpdateAppearance( clothing );

				_appearanceSource = source;
			}

			await ApplyClothingAsync( clothing, body, token );
			token.ThrowIfCancellationRequested();

			if ( body.IsValid() )
				body.MergeDescendants();
		}
		catch ( OperationCanceledException ) when ( token.IsCancellationRequested && !cancellationToken.IsCancellationRequested )
		{
			// The Dresser has replaced this request or released its target.
		}
		finally
		{
			if ( ReferenceEquals( _cts, request ) )
				_cts = null;

			request.Dispose();
		}
	}

	/// <summary>
	/// Make a random outfit
	/// </summary>
	[Button, ShowIf( nameof( Source ), ClothingSource.Manual )]
	public void Randomize()
	{
		var outfit = AvatarRandomizer.GetRandom();

		Clothing.Clear();
		Clothing.AddRange( outfit );

		var rnd = new Random();
		ManualAge = rnd.Float();
		ManualHeight = rnd.Float();
		ManualTint = rnd.Float();
		EyeColor = rnd.Float();

		_ = Apply();
	}

	private int? _editorOutfitHash;

	protected override void OnValidate()
	{
		if ( IsProxy || Scene.IsPrefabCacheSceneRoot )
			return;

		base.OnValidate();

		using var p = Scene.Push();

		if ( !BodyTarget.IsValid() )
		{
			BodyTarget = GetComponentInChildren<SkinnedModelRenderer>();
		}

		if ( Scene.IsEditor )
		{
			if ( Source == ClothingSource.Manual )
				SetClothingTints( Clothing );

			var hash = new HashCode();
			hash.Add( BodyTarget );
			hash.Add( Source );
			hash.Add( RemoveUnownedItems );
			foreach ( var entry in Clothing )
			{
				hash.Add( entry.Clothing );
				hash.Add( entry.ItemDefinitionId );
			}

			if ( WorkshopItems is not null )
			{
				foreach ( var item in WorkshopItems )
					hash.Add( item );
			}

			var outfitHash = hash.ToHashCode();
			if ( _editorOutfitHash != outfitHash )
			{
				_editorOutfitHash = outfitHash;
				_ = Apply( false );
			}
			else
			{
				UpdateAppearance();
			}
		}
	}

	/// <summary>
	/// Called when Height, Age or Tint is changed
	/// </summary>
	public void OnManualChange( float a, float b ) => UpdateAppearance();

	private bool _settingAppearance;
	private readonly Dictionary<ClothingContainer.ClothingEntry, SkinnedModelRenderer> _clothingRenderers = new();
	private readonly Dictionary<(Clothing Clothing, int ItemId), float?> _clothingTints = new();

	/// <summary>
	/// Copies appearance values and clothing tints, then updates the existing renderers.
	/// Does not rebuild the outfit or cancel pending clothing downloads.
	/// </summary>
	public void UpdateAppearance( ClothingContainer appearance )
	{
		ArgumentNullException.ThrowIfNull( appearance );
		_settingAppearance = true;
		try
		{
			ManualHeight = appearance.Height;
			ManualAge = appearance.Age;
			ManualTint = appearance.Tint;
			EyeColor = appearance.EyeColor;
			EyeAlign = appearance.EyeAlign;
			NeckSize = appearance.NeckSize;
			WaistSize = appearance.WaistSize;
			ChestSize = appearance.ChestSize;
			HeadShape = appearance.HeadShape;
			NoseSize = appearance.NoseSize;
			ChinSize = appearance.ChinSize;

			SetClothingTints( appearance.Clothing );
		}
		finally
		{
			_settingAppearance = false;
		}

		UpdateAppearance();
	}

	private void SetClothingTints( IEnumerable<ClothingContainer.ClothingEntry> clothing )
	{
		_clothingTints.Clear();
		foreach ( var entry in clothing )
		{
			if ( entry.Clothing is not null || entry.ItemDefinitionId != 0 )
				_clothingTints[GetTintKey( entry )] = entry.Tint;
		}
	}

	// Inventory IDs remain stable when a downloaded resource replaces a placeholder.
	private static (Clothing Clothing, int ItemId) GetTintKey( ClothingContainer.ClothingEntry entry ) =>
		entry.ItemDefinitionId != 0 ? (null, entry.ItemDefinitionId) : (entry.Clothing, 0);

	/// <summary>
	/// Applies current height, skin, eye and deformation values to the existing body and clothing.
	/// </summary>
	public void UpdateAppearance()
	{
		if ( _settingAppearance || !BodyTarget.IsValid() || BodyTarget.Scene.IsPrefabCacheSceneRoot )
			return;

		BodyTarget.Set( "scale_height", ApplyHeightScale ? ManualHeight.Remap( 0, 1, 0.8f, 1.2f, true ) : 1 );
		BodyTarget.Attributes.Set( "skin_age", ManualAge );
		BodyTarget.Attributes.Set( "skin_tint", ManualTint );

		foreach ( var renderer in BodyTarget.GetComponentsInChildren<SkinnedModelRenderer>( true ) )
		{
			renderer.Attributes.Set( "skin_age", ManualAge );
			renderer.Attributes.Set( "skin_tint", ManualTint );
		}

		foreach ( var (entry, renderer) in _clothingRenderers )
		{
			var clothing = entry.Clothing;
			if ( renderer.IsValid() && clothing.AllowTintSelect && _clothingTints.TryGetValue( GetTintKey( entry ), out var tint ) )
				renderer.Tint = clothing.TintSelection.Evaluate( tint?.Clamp( 0, 1 ) ?? clothing.TintDefault );
		}

		UpdateDeforms();
		UpdateEyeAttributes();
	}
}
