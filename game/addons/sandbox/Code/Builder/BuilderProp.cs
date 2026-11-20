namespace Sandbox;

/// <summary>
/// Recreates the model locally on every peer and after loading a save/dupe.
/// Only the recipe is persistent; runtime model resource names are not portable.
/// </summary>
[Title( "Builder Piece" ), Category( "Game" )]
public sealed class BuilderProp : Component
{
	/// <summary>
	/// Every building block weighs the same, so huge blocks don't make physics work out enormous masses.
	/// </summary>
	public const float Mass = 2500f;

	/// <summary>A spawn menu catalog piece. When set, it's used instead of the custom recipe.</summary>
	[Property, Sync( SyncFlags.FromHost )] public string PieceId { get; set; }

	/// <summary>The custom recipe: <see cref="Shape"/> stretched to fill <see cref="Size"/>, with its <see cref="Parameters"/>.</summary>
	[Property, Sync( SyncFlags.FromHost )] public BuilderCatalog.Shape Shape { get; set; }
	[Property, Sync( SyncFlags.FromHost )] public Vector3 Size { get; set; }

	/// <summary>The shape's own settings by name, like <c>angle=90;thickness=6</c>. See <see cref="BuilderShapes"/>.</summary>
	[Property, Sync( SyncFlags.FromHost )] public string Parameters { get; set; }

	[Property] public ModelRenderer Renderer { get; set; }
	[Property] public ModelCollider Collider { get; set; }

	/// <summary>A workshop material still downloading; the block wears <see cref="BuilderMaterials.Downloading"/> until it arrives.</summary>
	[Property] public string PendingMaterial { get; set; }

	string _builtPieceId;
	BuilderCatalog.Shape _builtShape;
	Vector3 _builtSize;
	string _builtParameters;
	int _builtGeometryVersion;
	Model _applied;
	BuilderShapeKey? _leased;
	int _leaseGeneration;
	BuilderShapeKey? _queued;
	BuilderRecipe _parsedRecipe;
	BuilderShapeKey? _parsedKey;

	public BuilderRecipe Recipe => new( PieceId, Shape, Size, Parameters );

	/// <summary>The key the current model was built from, without parsing the recipe again. Null until it's built.</summary>
	public BuilderShapeKey? BuiltKey => _leased;

	/// <summary>
	/// The model key for the current recipe. False when the recipe is invalid, e.g. an unknown piece or a tampered dupe.
	/// </summary>
	public bool TryGetKey( out BuilderShapeKey key ) => Recipe.TryGetKey( out key );

	/// <summary>Like <see cref="TryGetKey"/>, but only parses the recipe again once it changes.</summary>
	bool TryGetRecipeKey( out BuilderShapeKey key )
	{
		var recipe = Recipe;
		if ( recipe != _parsedRecipe )
		{
			_parsedRecipe = recipe;
			_parsedKey = recipe.TryGetKey( out var parsed ) ? parsed : null;
		}

		key = _parsedKey ?? default;
		return _parsedKey is not null;
	}

	/// <summary>
	/// Build and assign the model right now, even on a proxy. Use before rendering something immediately (dupe icons).
	/// </summary>
	public void Rebuild()
	{
		if ( !Renderer.IsValid() || !Collider.IsValid() ) return;
		if ( !TryGetKey( out var key ) ) return;

		Apply( key );
	}

	protected override void OnStart()
	{
		Refresh();
		if ( !IsProxy && !string.IsNullOrEmpty( PendingMaterial ) ) LoadPendingMaterial();
	}

	async void LoadPendingMaterial()
	{
		var path = PendingMaterial;
		var placeholder = BuilderMaterials.Downloading;
		if ( Renderer.IsValid() && Renderer.MaterialOverride is null ) Renderer.MaterialOverride = placeholder;

		var material = await BuilderMaterials.LoadAsync( path );
		if ( !this.IsValid() || !Renderer.IsValid() || PendingMaterial != path ) return;

		PendingMaterial = null;
		if ( Renderer.MaterialOverride != placeholder ) return;

		if ( material is null || material.IsError )
		{
			var owner = GetComponent<Ownable>()?.Owner;
			Log.Warning( $"Builder: couldn't load material '{path}' for {owner?.DisplayName}, using the default" );
			if ( owner is not null ) Sandbox.UI.Notices.SendLocalizedNotice( owner, "warning", Color.Orange, "#builder.material_failed", null, 3 );
			material = Material.Load( BuilderCatalog.DefaultMaterial );
		}

		Renderer.MaterialOverride = material;
		GameObject.Network?.Refresh();
	}

	protected override void OnUpdate() => Refresh();

	protected override void OnDestroy()
	{
		if ( _leased is { } key ) BuilderModelCache.Release( key, _leaseGeneration );
		_leased = null;
	}

	void Refresh()
	{
		if ( !Renderer.IsValid() || !Collider.IsValid() ) return;

		if ( _applied is not null && Renderer.Model == _applied && Collider.Model == _applied
			&& _builtPieceId == PieceId && _builtShape == Shape && _builtSize == Size && _builtParameters == Parameters
			&& _builtGeometryVersion == BuilderGeometry.GeometryVersion )
			return;

		if ( !TryGetRecipeKey( out var key ) ) return;

		if ( IsProxy && !BuilderModelCache.Contains( key ) )
		{
			if ( _queued == key ) return;

			_queued = key;
			BuilderBuildQueue.Get( Scene )?.Enqueue( key );

			if ( Renderer.Model is { IsError: true } ) Renderer.Model = null;
			return;
		}

		Apply( key );
	}

	void Apply( BuilderShapeKey key )
	{
		var model = BuilderModelCache.Acquire( key );
		if ( _leased is { } old ) BuilderModelCache.Release( old, _leaseGeneration );

		_leased = key;
		_leaseGeneration = BuilderModelCache.Generation;
		_queued = null;
		_applied = model;
		_builtPieceId = PieceId;
		_builtShape = Shape;
		_builtSize = Size;
		_builtParameters = Parameters;
		_builtGeometryVersion = BuilderGeometry.GeometryVersion;

		Renderer.Model = model;
		Collider.Model = model;
	}

	/// <summary>
	/// Create a building block for <paramref name="owner"/> the way the Builder tool does: checks their permission and
	/// building block limit, counts it towards that limit and adds it to their undo history. Host only; null when the
	/// recipe is invalid or the owner isn't allowed. With no owner it's a server-owned block: no permission, limit or undo.
	/// <paramref name="material"/> is a mounted path or a workshop ident. Remove a block with <see cref="GameObject.Destroy"/>. <paramref name="tint"/> colours its renderer.
	/// </summary>
	public static GameObject Spawn( BuilderRecipe recipe, Transform transform, Connection owner, string material = null, Surface surface = null, bool collisions = true, bool frozen = true, Color? tint = null )
	{
		if ( !Networking.IsHost || recipe is null || !recipe.TryGetKey( out var key ) ) return null;

		var limits = LimitsSystem.Current;
		if ( owner is not null )
		{
			if ( !BuilderPolicy.CheckCanBuild( owner ) ) return null;
			if ( limits is not null && !limits.CheckBuilderLimit( owner, 1 ) ) return null;
		}

		var loaded = BuilderMaterials.LoadLocal( material );
		var pieceId = string.IsNullOrEmpty( recipe.PieceId ) ? null : recipe.PieceId;
		var go = Create( key, pieceId, transform, loaded, surface, collisions, frozen, owner, loaded is null ? material : null, tint );

		if ( owner is not null )
		{
			limits?.TrackNow( owner, new List<GameObject> { go } );

			if ( Player.FindForConnection( owner ) is { } player && player.IsValid() )
			{
				var undo = player.Undo.Create();
				undo.Name = "#tool.undo.builder";
				undo.Icon = "🧱";
				undo.Add( go );
			}
		}

		return go;
	}

	/// <summary>
	/// Create a networked building block on the host without any checks, tracking or undo (see <see cref="Spawn"/>).
	/// Pass a catalog <paramref name="pieceId"/>, or null to use the custom recipe in <paramref name="key"/>. With no
	/// <paramref name="material"/> yet, pass a workshop ident as <paramref name="pendingMaterial"/>.
	/// </summary>
	public static GameObject Create( BuilderShapeKey key, string pieceId, Transform transform, Material material, Surface surface, bool collisions, bool frozen, Connection owner, string pendingMaterial = null, Color? tint = null )
	{
		var go = new GameObject( false, pieceId ?? $"Builder {key.Shape}" );
		go.Tags.Add( "removable" );
		if ( !collisions ) go.Tags.Add( GameManager.NoPropCollisionsTag );
		go.WorldTransform = transform;

		var prop = go.AddComponent<BuilderProp>();
		if ( pieceId is not null )
		{
			prop.PieceId = pieceId;
		}
		else
		{
			prop.Shape = key.Shape;
			prop.Size = key.Size;
			prop.Parameters = BuilderShapes.Format( key );
		}

		prop.Renderer = go.AddComponent<ModelRenderer>();
		prop.Renderer.MaterialOverride = material ?? (pendingMaterial is not null ? BuilderMaterials.Downloading : null);
		if ( tint is { } color ) prop.Renderer.Tint = color;
		prop.PendingMaterial = material is null ? pendingMaterial : null;
		prop.Collider = go.AddComponent<ModelCollider>();
		if ( surface is not null ) prop.Collider.Surface = surface;
		prop.Rebuild();

		var body = go.AddComponent<Rigidbody>();
		body.MassOverride = Mass;
		body.MotionEnabled = !frozen;

		if ( owner is not null ) Ownable.Set( go, owner );
		go.NetworkSpawn( true, null );
		return go;
	}
}
