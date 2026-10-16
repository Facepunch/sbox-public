using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// A single fragment of an <see cref="AggregateRenderer"/>, as stored in the scene. Its position in
/// the list is the draw call it draws. Bounds are in the fragment's local coordinate space.
/// </summary>
[method: JsonConstructor]
public readonly record struct AggregateFragmentInfo( BBox Bounds )
{
	/// <summary>
	/// Original fragment transform relative to this renderer.
	/// </summary>
	public Transform LocalTransform { get; init; } = Transform.Zero;
}

/// <summary>
/// Draws compiled geometry. Each draw call is drawn and culled as a fragment, transformed with
/// this object and its fragment's local transform, and submitted together as a single indirect draw.
/// </summary>
[Hide]
[Title( "Aggregate Renderer" )]
[Category( "Rendering" )]
[Icon( "dynamic_feed" )]
public sealed class AggregateRenderer : Component, Component.ExecuteInEditor, IHasModel
{
	public static int MaxFragments => SceneAggregateObject.MaxFragments;

	/// <summary>
	/// Baked at compile time - one model per material, one draw call per fragment.
	/// </summary>
	[Property, Hide]
	public Model Model
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			if ( Active ) Rebuild();
		}
	}

	/// <summary>
	/// Baked at compile time - one entry per draw call of <see cref="Model"/>.
	/// </summary>
	[Property, Hide]
	public List<AggregateFragmentInfo> Fragments { get; set; } = new();

	[Property, Title( "Cast Shadows" )]
	public bool CastShadows
	{
		get;
		set
		{
			if ( field == value ) return;

			field = value;

			if ( Active ) Rebuild();
		}
	} = true;

	/// <summary>
	/// Multiplied with the material. One tint covers the whole aggregate, so geometry is only baked
	/// together when it shares a tint.
	/// </summary>
	[Property]
	public Color Tint
	{
		get;
		set
		{
			field = value;

			if ( _aggregate.IsValid() ) _aggregate.ColorTint = value;
		}
	} = Color.White;

	SceneAggregateObject _aggregate;

	protected override void OnEnabled()
	{
		Transform.OnTransformChanged += Rebuild;
		Rebuild();
	}

	protected override void OnDisabled()
	{
		Transform.OnTransformChanged -= Rebuild;
		_aggregate?.Delete();
		_aggregate = null;
	}

	protected override void OnTagsChanged()
	{
		base.OnTagsChanged();

		_aggregate?.Tags.SetFrom( Tags );
	}

	void Rebuild()
	{
		_aggregate?.Delete();
		_aggregate = null;

		if ( Model is null || Fragments is null || Fragments.Count == 0 )
			return;

		var fragments = new AggregateFragment[Fragments.Count];
		var transform = Transform.InterpolatedWorld;

		for ( int i = 0; i < fragments.Length; i++ )
		{
			var fragmentTransform = transform.ToWorld( Fragments[i].LocalTransform );
			var bounds = Fragments[i].Bounds.Transform( fragmentTransform );

			fragments[i] = new AggregateFragment
			{
				Transform = fragmentTransform,
				BoundsMin = bounds.Mins,
				BoundsMax = bounds.Maxs,
				Tint = Color.White,
				DrawDescriptorIndex = i
			};
		}

		// An aggregate is only buildable from the compiler's own output. A model that's since been
		// recompiled into something else shouldn't take the scene load down with it.
		try
		{
			_aggregate = new SceneAggregateObject( Scene.SceneWorld, Model, fragments, CastShadows );
		}
		catch ( ArgumentException e )
		{
			Log.Warning( $"Can't draw {Model.ResourcePath} as an aggregate: {e.Message}" );
			return;
		}

		_aggregate.Tags.SetFrom( Tags );
		_aggregate.ColorTint = Tint;
	}

	/// <summary>
	/// Retire the old aggregate and rebuild from the new model. Native code retains the old mesh
	/// allocation until any already submitted draws have finished.
	/// </summary>
	void IHasModel.OnModelReloaded()
	{
		if ( Active ) Rebuild();
	}
}
