using static Sandbox.Component;

namespace Sandbox;

/// <summary>
/// Renders a model in the world
/// </summary>
[Title( "Model Renderer" )]
[Category( "Rendering" )]
[Icon( "free_breakfast" )]
[Alias( "ModelComponentMate", "ModelComponent" )]
public partial class ModelRenderer : Renderer, ExecuteInEditor, ITintable, IMaterialSetter, IHasBounds, IHasModel
{
	Model _model;

	private ulong _bodyGroups = ulong.MaxValue;

	[Property]
	public Model Model
	{
		get => _model;
		set
		{
			if ( _model == value ) return;

			_model = value;

			if ( !GameObject.Flags.Contains( GameObjectFlags.Deserializing ) )
			{
				_bodyGroups = ulong.MaxValue;

				if ( _model is not null && _model.native.GetNumMeshGroups() > 0 )
				{
					_bodyGroups = _model.native.GetDefaultMeshGroupMask();
				}
			}

			UpdateObject();
			OnModelChanged();
		}
	}

	Color _tint = "#FFFFFF";

	[Property]
	public Color Tint
	{
		get => _tint;
		set
		{
			if ( _tint == value ) return;
			_tint = value;

			if ( _sceneObject.IsValid() )
			{
				_sceneObject.ColorTint = Tint;
			}
		}
	}

	[Property]
	public bool CreateAttachments
	{
		get => _createAttachments;
		set
		{
			if ( _createAttachments == value ) return;
			_createAttachments = value;

			UpdateObject();
		}
	}

	[Property, Model.BodyGroupMask, MakeDirty, ShowIf( nameof( HasBodyGroups ), true )]
	[DefaultValue( ulong.MaxValue )]
	public ulong BodyGroups { get => _bodyGroups; set => _bodyGroups = value; }

	public bool HasBodyGroups => Model?.Parts.All.Sum( x => x.Choices.Count ) > 1;

	[Property, Model.MaterialGroup, MakeDirty, ShowIf( nameof( HasMaterialGroups ), true )]
	[DefaultValue( default )]
	public string MaterialGroup { get; set; }

	public bool HasMaterialGroups => Model?.MaterialGroupCount > 0 && MaterialOverride is null;

	[Title( "Cast Shadows" ), Property, Category( "Lighting" ), MakeDirty]
	public ShadowRenderType RenderType { get; set; } = ShadowRenderType.On;

	private int? _lodOverride;

	/// <summary>
	/// Force a level of detail.
	/// </summary>
	[Property, Hide]
	public int? LodOverride
	{
		get => _lodOverride;
		set
		{
			if ( _lodOverride == value ) return;
			_lodOverride = value;

			if ( _sceneObject.IsValid() )
			{
				_sceneObject.LodOverride = _lodOverride ?? -1;
			}
		}
	}

	/// <summary>
	/// Set body group value by name
	/// </summary>
	public void SetBodyGroup( string name, int value )
	{
		if ( Model is null || Model.native.IsNull )
			return;

		int part = Model.native.GetBodyPartForName( name );
		if ( part < 0 ) return;

		SetBodyGroup( part, value );
	}

	/// <summary>
	/// Set body group value by name and choice
	/// </summary>
	public void SetBodyGroup( string name, string choice )
	{
		if ( Model is null || Model.native.IsNull ) return;

		var partIndex = Model.native.GetBodyPartForName( name );
		if ( partIndex < 0 ) return;

		var parts = Model.Parts;
		if ( partIndex >= parts.Count ) return;

		var choiceIndex = parts.All[partIndex].GetChoiceIndex( choice );
		if ( choiceIndex < 0 ) return;

		SetBodyGroup( partIndex, choiceIndex );
	}

	/// <summary>
	/// Set body group value by index
	/// </summary>
	public void SetBodyGroup( int part, int value )
	{
		if ( Model is null || Model.native.IsNull )
			return;

		ulong mask = Model.native.GetBodyPartMask( part );
		ulong modelMask = Model.native.GetBodyPartMeshMask( part, value );

		var groups = BodyGroups;
		groups &= ~mask; // remove the whole mask
		groups |= modelMask; // apply the chosen part flag

		BodyGroups = groups;
	}

	/// <summary>
	/// Get body group value by name
	/// </summary>
	public int GetBodyGroup( string name )
	{
		if ( Model is null || Model.native.IsNull )
			throw new ArgumentException( "Invalid Model" );

		return GetBodyGroup( Model.native.GetBodyPartForName( name ) );
	}

	/// <summary>
	/// Get body group value by index
	/// </summary>
	public int GetBodyGroup( int part )
	{
		if ( Model is null || Model.native.IsNull )
			throw new ArgumentException( "Invalid Model" );

		if ( part < 0 || part >= Model.native.GetNumBodyParts() )
			throw new ArgumentOutOfRangeException( nameof( part ) );

		return Model.native.FindMeshIndexForMask( part, BodyGroups );
	}

	internal SceneObject _sceneObject;
	public SceneObject SceneObject => _sceneObject;

	Color ITintable.Color { get => Tint; set => Tint = value; }

	internal void OnModelReloaded()
	{
		if ( !Active ) return;

		UpdateObject();
		OnModelChanged();
	}

	internal Action ModelChanged;

	internal void OnModelChanged()
	{
		ModelChanged?.Invoke();
	}

	protected virtual void UpdateObject()
	{
		BuildAttachmentHierarchy();

		if ( !_sceneObject.IsValid() )
			return;

		var model = Model ?? Model.Load( "models/dev/box.vmdl" );

		_sceneObject.ColorTint = Tint;
		_sceneObject.Model = model;
		_sceneObject.MeshGroupMask = BodyGroups;
		_sceneObject.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
		_sceneObject.RenderingEnabled = model.HasRenderMeshes();

		if ( _lodOverride.HasValue )
			_sceneObject.LodOverride = _lodOverride.Value;

		RenderOptions.Apply( _sceneObject );

		if ( RenderType == ShadowRenderType.ShadowsOnly )
			_sceneObject.Flags.SetFlag( Rendering.SceneObjectFlags.ExcludeGameLayer, true );

		if ( HasMaterialGroups )
		{
			_sceneObject.SetMaterialGroup( MaterialGroup );
		}
		else
		{
			_sceneObject.SetMaterialOverride( MaterialOverride );
		}

	}

	protected override void OnEnabled()
	{
		Assert.True( !_sceneObject.IsValid(), "_sceneObject should be null - disable wasn't called" );
		Assert.NotNull( Scene, "Scene should not be null" );

		var model = Model ?? Model.Load( "models/dev/box.vmdl" );

		_sceneObject = new SceneObject( Scene.SceneWorld, model, WorldTransform );
		OnSceneObjectCreated( _sceneObject );

		Transform.OnTransformChanged += OnTransformChanged;
	}

	internal override void OnSceneObjectCreated( SceneObject o )
	{
		base.OnSceneObjectCreated( o );

		if ( o.IsValid() )
		{
			o.Transform = WorldTransform;
			o.Component = this;
			o.Tags.SetFrom( GameObject.Tags );
		}

		ApplyMaterialOverrides();
		UpdateObject();
		OnTransformChanged();
	}

	internal override void OnDisabledInternal()
	{
		try
		{
			ClearAttachmentHierarchy();
		}
		finally
		{
			Transform.OnTransformChanged -= OnTransformChanged;

			BackupRenderAttributes( _sceneObject?.Attributes );
			_sceneObject?.Delete();
			_sceneObject = null;
		}

		base.OnDisabledInternal();
	}

	private void OnTransformChanged()
	{
		if ( _sceneObject.IsValid() )
		{
			_sceneObject.Transform = Transform.InterpolatedWorld;
		}
	}

	/// <summary>
	/// Tags have been updated - lets update our scene object tags
	/// </summary>
	protected override void OnTagsChanged()
	{
		if ( !_sceneObject.IsValid() ) return;

		_sceneObject.Tags.SetFrom( GameObject.Tags );
	}

	public enum ShadowRenderType
	{
		// Render the model with shadows
		[Icon( "wb_shade" )]
		On,
		// Render the model without shadows
		[Icon( "wb_twilight" )]
		Off,
		// Render ONLY the models shadows
		[Icon( "hide_source" )]
		[Title( "Shadows Only" )]
		ShadowsOnly,
	}

	protected override void OnDirty()
	{
		UpdateObject();
		ApplyMaterialOverrides();
	}

	protected override void OnRenderOptionsChanged()
	{
		if ( _sceneObject.IsValid() )
		{
			RenderOptions.Apply( _sceneObject );
		}
	}

	public void SetMaterial( Material material, int triangle = -1 )
	{
		MaterialOverride = material;
	}

	public Material GetMaterial( int triangle = -1 )
	{
		return MaterialOverride;
	}

	/// <summary>
	/// Copy everything from another renderer
	/// </summary>
	public override void CopyFrom( Renderer other )
	{
		base.CopyFrom( other );

		if ( other is ModelRenderer mr )
		{
			Model = mr.Model;
			Tint = mr.Tint;
			BodyGroups = mr.BodyGroups;
			MaterialGroup = mr.MaterialGroup;
		}
	}

	void IHasModel.OnModelReloaded()
	{
		OnModelReloaded();
	}
}
