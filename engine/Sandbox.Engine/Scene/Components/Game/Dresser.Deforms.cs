namespace Sandbox;

public sealed partial class Dresser
{
	private const string DeformsPrefab = "models/citizen/citizen_deforms.prefab";
	private GameObject _deformRoot;
	private bool _generatedDeforms;
	private bool _deformsEnabled = true;

	private static bool IsCitizen( Model model ) => string.Equals( (model?.BaseModel ?? model)?.Name,
		"models/citizen/citizen.vmdl", StringComparison.OrdinalIgnoreCase );

	/// <summary>
	/// Normalized neck deformation. Clothing loads initialize this value; subsequent edits control the model.
	/// </summary>
	[Property, Sync, Group( "Deforms" ), Title( "Neck Size" ), Range( 0, 1 ), Order( 0 )]
	[ShowIf( nameof( Source ), ClothingSource.Manual )]
	public float NeckSize
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.NeckSize;
			if ( field == value )
				return;

			field = value;
			UpdateDeforms();
		}
	} = AvatarDefaults.NeckSize;

	/// <summary>
	/// Normalized waist deformation. Clothing loads initialize this value; subsequent edits control the model.
	/// </summary>
	[Property, Sync, Group( "Deforms" ), Title( "Waist Size" ), Range( 0, 1 ), Order( 1 )]
	[ShowIf( nameof( Source ), ClothingSource.Manual )]
	public float WaistSize
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.WaistSize;
			if ( field == value )
				return;

			field = value;
			UpdateDeforms();
		}
	} = AvatarDefaults.WaistSize;

	/// <summary>
	/// Normalized chest deformation. Clothing loads initialize this value; subsequent edits control the model.
	/// </summary>
	[Property, Sync, Group( "Deforms" ), Title( "Chest Size" ), Range( 0, 1 ), Order( 2 )]
	[ShowIf( nameof( Source ), ClothingSource.Manual )]
	public float ChestSize
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.ChestSize;
			if ( field == value )
				return;

			field = value;
			UpdateDeforms();
		}
	} = AvatarDefaults.ChestSize;

	/// <summary>
	/// Normalized head deformation. Clothing loads initialize this value; subsequent edits control the model.
	/// </summary>
	[Property, Sync, Group( "Deforms" ), Title( "Head Shape" ), Range( 0, 1 ), Order( 3 )]
	[ShowIf( nameof( Source ), ClothingSource.Manual )]
	public float HeadShape
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.HeadShape;
			if ( field == value )
				return;

			field = value;
			UpdateDeforms();
		}
	} = AvatarDefaults.HeadShape;

	/// <summary>
	/// Normalized nose deformation. Clothing loads initialize this value; subsequent edits control the model.
	/// </summary>
	[Property, Sync, Group( "Deforms" ), Title( "Nose Size" ), Range( 0, 1 ), Order( 4 )]
	[ShowIf( nameof( Source ), ClothingSource.Manual )]
	public float NoseSize
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.NoseSize;
			if ( field == value )
				return;

			field = value;
			UpdateDeforms();
		}
	} = AvatarDefaults.NoseSize;

	/// <summary>
	/// Normalized chin deformation. Clothing loads initialize this value; subsequent edits control the model.
	/// </summary>
	[Property, Sync, Group( "Deforms" ), Title( "Chin Size" ), Range( 0, 1 ), Order( 5 )]
	[ShowIf( nameof( Source ), ClothingSource.Manual )]
	public float ChinSize
	{
		get;
		set
		{
			value = float.IsFinite( value ) ? value.Clamp( 0, 1 ) : AvatarDefaults.ChinSize;
			if ( field == value )
				return;

			field = value;
			UpdateDeforms();
		}
	} = AvatarDefaults.ChinSize;

	private void UpdateDeforms()
	{
		if ( _settingAppearance || !BodyTarget.IsValid() || BodyTarget.Scene.IsPrefabCacheSceneRoot )
			return;

		using var scope = BodyTarget.Scene.Push();
		if ( !_deformsEnabled || !IsCitizen( BodyTarget.Model ) )
		{
			ReleaseDeforms();
			return;
		}

		if ( !_deformRoot.IsValid() || _deformRoot.IsDestroyed )
		{
			// An authored prefab takes precedence; only generated instances belong to us.
			_deformRoot = BodyTarget.GameObject.Children.FirstOrDefault( x => !x.IsDestroyed && x.Name == "citizen_deforms" );
			_generatedDeforms = !_deformRoot.IsValid();
			if ( _generatedDeforms )
			{
				var prefab = ResourceLibrary.Get<PrefabFile>( DeformsPrefab )
					?? Game.Resources.LoadGameResource<PrefabFile>( DeformsPrefab, EngineFileSystem.CoreContent );
				if ( prefab is null )
					return;

				_deformRoot = GameObject.Clone( prefab, new CloneConfig { Parent = BodyTarget.GameObject, Transform = global::Transform.Zero, StartEnabled = false } );
				_deformRoot.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
				_deformRoot.NetworkMode = NetworkMode.Never;
			}
		}

		var deformers = _deformRoot.GetComponentsInChildren<ModelDeformer>( true ).ToArray();
		SetWeight( "deform_neck", NeckSize, 0.5f, -0.2f );
		SetWeight( "deform_waist", WaistSize, -0.6f, 0.6f );
		SetWeight( "deform_chest", ChestSize, 0, 0.8f );
		SetWeight( "deform_head", HeadShape, -0.4f, 0.4f );
		SetWeight( "deform_nose", NoseSize, 0, 0.8f );
		SetWeight( "deform_chin", ChinSize, 0, 1 );

		void SetWeight( string name, float value, float min, float max )
		{
			var deformer = deformers.FirstOrDefault( x => x.GameObject.Name == name );
			if ( deformer is not null )
				deformer.Weight = value.Remap( 0, 1, min, max, false );
		}

		if ( _generatedDeforms )
			_deformRoot.Enabled = true;
	}

	private void ReleaseDeforms()
	{
		if ( _generatedDeforms && _deformRoot.IsValid() )
		{
			_deformRoot.Enabled = false;
			_deformRoot.Destroy();
		}

		_deformRoot = null;
		_generatedDeforms = false;
	}

	internal GameObject UpdateDeforms( ClothingContainer appearance, bool enabled )
	{
		_settingAppearance = true;
		try
		{
			NeckSize = appearance.NeckSize;
			WaistSize = appearance.WaistSize;
			ChestSize = appearance.ChestSize;
			HeadShape = appearance.HeadShape;
			NoseSize = appearance.NoseSize;
			ChinSize = appearance.ChinSize;
			_deformsEnabled = enabled;
		}
		finally
		{
			_settingAppearance = false;
		}

		UpdateDeforms();
		return _deformRoot;
	}
}
