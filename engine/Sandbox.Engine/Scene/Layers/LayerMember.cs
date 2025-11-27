namespace Sandbox;

/// <summary>
/// Component that assigns a GameObject to a layer.
/// Attach this component to any GameObject that should be part of a layer.
/// </summary>
[Title( "Layer Member" )]
[Category( "Organization" )]
[Icon( "layers" )]
[Expose]
public sealed class LayerMember : Component, ExecuteInEditor
{
	private Guid _layerId;
	private Layer _cachedLayer;

	/// <summary>
	/// The ID of the layer this object belongs to.
	/// </summary>
	[Property, Hide]
	public Guid LayerId
	{
		get => _layerId;
		set
		{
			if ( _layerId == value ) return;

			var oldLayer = Layer;
			_layerId = value;
			_cachedLayer = null;

			var newLayer = Layer;
			OnLayerChanged( oldLayer, newLayer );
		}
	}

	/// <summary>
	/// The layer this object belongs to.
	/// </summary>
	[Property]
	public Layer Layer
	{
		get
		{
			if ( _cachedLayer is not null && _cachedLayer.Id == _layerId )
				return _cachedLayer;

			_cachedLayer = LayerManager.Get( Scene )?.GetLayer( _layerId );
			return _cachedLayer;
		}
		set
		{
			var newId = value?.Id ?? Guid.Empty;
			if ( _layerId == newId ) return;

			var oldLayer = Layer;
			_layerId = newId;
			_cachedLayer = value;

			OnLayerChanged( oldLayer, value );
		}
	}

	/// <summary>
	/// When true, this object's visibility is controlled by the layer's visibility.
	/// </summary>
	[Property, Group( "Layer Behavior" )]
	public bool AffectedByLayerVisibility { get; set; } = true;

	/// <summary>
	/// When true, this object's opacity/intensity is controlled by the layer's opacity.
	/// </summary>
	[Property, Group( "Layer Behavior" )]
	public bool AffectedByLayerOpacity { get; set; } = true;

	/// <summary>
	/// When true, this object inherits the layer assignment from its parent.
	/// </summary>
	[Property, Group( "Layer Behavior" )]
	public bool InheritFromParent { get; set; } = false;

	/// <summary>
	/// Cached original enabled state before layer visibility was applied.
	/// </summary>
	private bool _originalEnabled = true;

	/// <summary>
	/// Whether we're currently suppressed due to layer visibility.
	/// </summary>
	private bool _layerSuppressed = false;

	/// <summary>
	/// Original tint color for models affected by layer opacity.
	/// </summary>
	private Color? _originalTint;

	/// <summary>
	/// Original volume for sound components affected by layer opacity.
	/// </summary>
	private float? _originalVolume;

	protected override void OnEnabled()
	{
		base.OnEnabled();

		var manager = LayerManager.Get( Scene );
		manager?.RegisterMember( this );

		// Apply initial layer state
		ApplyLayerState();
	}

	protected override void OnDisabled()
	{
		base.OnDisabled();

		var manager = LayerManager.Get( Scene );
		manager?.UnregisterMember( this );

		// Restore original state when disabled
		RestoreOriginalState();
	}

	protected override void OnDestroy()
	{
		base.OnDestroy();

		var manager = LayerManager.Get( Scene );
		manager?.UnregisterMember( this );
	}

	private void OnLayerChanged( Layer oldLayer, Layer newLayer )
	{
		var manager = LayerManager.Get( Scene );
		manager?.OnMemberLayerChangedInternal( this, oldLayer, newLayer );

		// Restore old state and apply new layer state
		RestoreOriginalState();
		ApplyLayerState();
	}

	/// <summary>
	/// Gets the effective layer, considering parent inheritance.
	/// </summary>
	public Layer GetEffectiveLayer()
	{
		if ( !InheritFromParent || Layer is not null )
			return Layer;

		var parent = GameObject?.Parent;
		while ( parent is not null )
		{
			var parentMember = parent.Components.Get<LayerMember>();
			if ( parentMember is not null )
			{
				var parentLayer = parentMember.GetEffectiveLayer();
				if ( parentLayer is not null )
					return parentLayer;
			}
			parent = parent.Parent;
		}

		return null;
	}

	/// <summary>
	/// Applies the current layer's visibility and opacity state to this object.
	/// </summary>
	internal void ApplyLayerState()
	{
		var layer = GetEffectiveLayer();
		if ( layer is null ) return;

		ApplyVisibility( layer );
		ApplyOpacity( layer );
	}

	private void ApplyVisibility( Layer layer )
	{
		if ( !AffectedByLayerVisibility ) return;
		if ( GameObject is null || !GameObject.IsValid() ) return;

		var effectiveVisible = layer.EffectiveVisible;

		if ( !effectiveVisible && !_layerSuppressed )
		{
			// Store original state and hide
			_originalEnabled = GameObject.Enabled;
			_layerSuppressed = true;
			GameObject.Enabled = false;
		}
		else if ( effectiveVisible && _layerSuppressed )
		{
			// Restore original state
			_layerSuppressed = false;
			GameObject.Enabled = _originalEnabled;
		}
	}

	private void ApplyOpacity( Layer layer )
	{
		if ( !AffectedByLayerOpacity ) return;
		if ( GameObject is null || !GameObject.IsValid() ) return;

		var effectiveOpacity = layer.EffectiveOpacity;

		// Apply opacity to various component types
		ApplyOpacityToRenderer( effectiveOpacity );
		ApplyOpacityToLight( effectiveOpacity );
		ApplyOpacityToSound( effectiveOpacity );
		ApplyOpacityToParticles( effectiveOpacity );
	}

	private void ApplyOpacityToRenderer( float opacity )
	{
		var renderer = GameObject.Components.Get<ModelRenderer>();
		if ( renderer is null ) return;

		if ( !_originalTint.HasValue )
		{
			_originalTint = renderer.Tint;
		}

		var tint = _originalTint.Value;
		renderer.Tint = tint.WithAlpha( tint.a * opacity );
	}

	/// <summary>
	/// Original light color for restoring after opacity changes.
	/// </summary>
	private Color? _originalLightColor;

	private void ApplyOpacityToLight( float opacity )
	{
		var light = GameObject.Components.Get<Light>();
		if ( light is null ) return;

		if ( !_originalLightColor.HasValue )
		{
			_originalLightColor = light.LightColor;
		}

		// Scale the light color RGB by opacity (intensity multiplier)
		var original = _originalLightColor.Value;
		light.LightColor = new Color( original.r * opacity, original.g * opacity, original.b * opacity, original.a );
	}

	private void ApplyOpacityToSound( float opacity )
	{
		var sound = GameObject.Components.Get<BaseSoundComponent>();
		if ( sound is null ) return;

		if ( !_originalVolume.HasValue )
		{
			_originalVolume = sound.Volume;
		}

		sound.Volume = _originalVolume.Value * opacity;
	}

	private void ApplyOpacityToParticles( float opacity )
	{
		var particles = GameObject.Components.Get<ParticleSystem>();
		if ( particles is null ) return;

		// Particle systems use tint for opacity
		if ( !_originalTint.HasValue )
		{
			_originalTint = particles.Tint;
		}

		var tint = _originalTint.Value;
		particles.Tint = tint.WithAlpha( tint.a * opacity );
	}

	/// <summary>
	/// Restores the original state before layer effects were applied.
	/// </summary>
	private void RestoreOriginalState()
	{
		if ( GameObject is null || !GameObject.IsValid() ) return;

		// Restore enabled state
		if ( _layerSuppressed )
		{
			_layerSuppressed = false;
			GameObject.Enabled = _originalEnabled;
		}

		// Restore renderer tint
		if ( _originalTint.HasValue )
		{
			var renderer = GameObject.Components.Get<ModelRenderer>();
			if ( renderer is not null )
			{
				renderer.Tint = _originalTint.Value;
			}

			var particles = GameObject.Components.Get<ParticleSystem>();
			if ( particles is not null )
			{
				particles.Tint = _originalTint.Value;
			}

			_originalTint = null;
		}

		// Restore light color
		if ( _originalLightColor.HasValue )
		{
			var light = GameObject.Components.Get<Light>();
			if ( light is not null )
			{
				light.LightColor = _originalLightColor.Value;
			}
			_originalLightColor = null;
		}

		// Restore sound volume
		if ( _originalVolume.HasValue )
		{
			var sound = GameObject.Components.Get<BaseSoundComponent>();
			if ( sound is not null )
			{
				sound.Volume = _originalVolume.Value;
			}
			_originalVolume = null;
		}
	}

	/// <summary>
	/// Called when the layer's visibility changes.
	/// </summary>
	internal void OnLayerVisibilityChanged()
	{
		var layer = GetEffectiveLayer();
		if ( layer is null ) return;

		ApplyVisibility( layer );
	}

	/// <summary>
	/// Called when the layer's opacity changes.
	/// </summary>
	internal void OnLayerOpacityChanged()
	{
		var layer = GetEffectiveLayer();
		if ( layer is null ) return;

		ApplyOpacity( layer );
	}

	protected override void OnValidate()
	{
		base.OnValidate();

		// Clear cached layer when properties change in editor
		_cachedLayer = null;
		ApplyLayerState();
	}
}
