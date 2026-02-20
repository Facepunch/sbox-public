using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Sandbox;

/// <summary>
/// Renders particles as models, using the particle's position, rotation, and size.
/// </summary>
[Expose]
[Title( "Particle Model Renderer" )]
[Category( "Particles" )]
[Icon( "category" )]
public sealed class ParticleModelRenderer : ParticleController, Component.ExecuteInEditor
{
	/// <summary>
	/// Render options for advanced rendering.
	/// </summary>
	[Property, Order( -100 ), InlineEditor( Label = false ), Group( "Advanced Rendering", StartFolded = true )]
	public RenderOptions RenderOptions { get; } = new RenderOptions( null );

	/// <summary>
	/// Entry for a model, including its material group and body group settings.
	/// </summary>
	[Expose]
	public sealed class ModelEntry
	{
		private Model _model;

		/// <summary>
		/// The model associated with this entry.
		/// </summary>
		[KeyProperty]
		public Model Model
		{
			get => _model;
			set
			{
				if ( _model == value )
					return;

				_model = value;

				MaterialGroup = default;
				BodyGroups = _model?.Parts.DefaultMask ?? default;
			}
		}

		/// <summary>
		/// Material group for the model.
		/// </summary>
		[Model.MaterialGroup, ShowIf( nameof( HasMaterialGroups ), true )]
		public string MaterialGroup { get; set; }

		/// <summary>
		/// Body group mask for the model.
		/// </summary>
		[Model.BodyGroupMask, ShowIf( nameof( HasBodyGroups ), true )]
		public ulong BodyGroups { get; set; }

		/// <summary>
		/// Indicates whether the model has material groups.
		/// </summary>
		[Hide, JsonIgnore]
		public bool HasMaterialGroups => Model?.MaterialGroupCount > 0;

		/// <summary>
		/// Indicates whether the model has body groups.
		/// </summary>
		[Hide, JsonIgnore]
		public bool HasBodyGroups => Model?.Parts.All.Sum( x => x.Choices.Count ) > 1;

		/// <summary>
		/// Converts a <see cref="Model"/> to a <see cref="ModelEntry"/>.
		/// </summary>
		/// <param name="model">The model to convert.</param>
		/// <returns>A new <see cref="ModelEntry"/> instance.</returns>
		public static implicit operator ModelEntry( Model model ) => new() { Model = model };
	}

	/// <summary>
	/// List of models for rendering. This property is obsolete; use <see cref="Choices"/> instead.
	/// </summary>
	[Hide, Obsolete( "Use Choices" )]
	public List<Model> Models { get; set; } = new();

	/// <summary>
	/// List of model entries available for rendering.
	/// </summary>
	[Property]
	public List<ModelEntry> Choices { get; set; } = new List<ModelEntry> { Model.Cube };

	/// <summary>
	/// Material override for rendering.
	/// </summary>
	[Property]
	public Material MaterialOverride { get; set; }

	/// <summary>
	/// If true, the models will rotate relative to the this GameObject
	/// </summary>
	[Property]
	public bool RotateWithGameObject { get; set; }

	/// <summary>
	/// Scale factor for particle rendering.
	/// </summary>
	[Property]
	public ParticleFloat Scale { get; set; } = 1;

	/// <summary>
	/// Indicates whether particles cast shadows.
	/// </summary>
	[Property]
	public bool CastShadows { get; set; } = true;

	/// <summary>
	/// Called when a particle is created.
	/// </summary>
	/// <param name="p">The particle being created.</param>
	protected override void OnParticleCreated( Particle p )
	{
		p.AddListener( new ParticleModel( this ), this );
	}

	/// <summary>
	/// Version of the component.
	/// </summary>
	public override int ComponentVersion => 1;

	/// <summary>
	/// Upgrades the JSON representation of the particle model renderer to version 1.
	/// </summary>
	/// <param name="obj">The JSON object to upgrade.</param>
	[Expose, JsonUpgrader( typeof( ParticleModelRenderer ), 1 )]
	static void Upgrader_v1( JsonObject obj )
	{
		if ( obj.TryGetPropertyValue( "Models", out var node ) )
		{
			var choices = new JsonArray();

			foreach ( var model in node.AsArray() )
			{
				if ( model is null )
					continue;

				choices.Add( new JsonObject { ["Model"] = model.ToString() } );
			}

			obj["Choices"] = choices;
			obj.Remove( "Models" );
		}
	}
}

/// <summary>
/// Represents a particle model listener that updates the scene object based on particle properties.
/// </summary>
file class ParticleModel : Particle.BaseListener
{
	/// <summary>
	/// Renderer associated with this particle model.
	/// </summary>
	public ParticleModelRenderer Renderer;

	SceneObject so;

	/// <summary>
	/// Initializes a new instance of the <see cref="ParticleModel"/> class.
	/// </summary>
	/// <param name="renderer">The particle model renderer.</param>
	public ParticleModel( ParticleModelRenderer renderer )
	{
		Renderer = renderer;
	}

	/// <summary>
	/// Called when the particle is enabled.
	/// </summary>
	/// <param name="p">The particle being enabled.</param>
	public override void OnEnabled( Particle p )
	{
		var entry = Random.Shared.FromList( Renderer.Choices );
		var model = entry?.Model;
		so = new SceneObject( Renderer.Scene.SceneWorld, model ?? Model.Cube );
		so.Tags.SetFrom( Renderer.GameObject.Tags );

		if ( model is not null )
		{
			so.MeshGroupMask = entry.BodyGroups;
			so.SetMaterialGroup( entry.MaterialGroup );
		}
	}

	/// <summary>
	/// Called when the particle is disabled.
	/// </summary>
	/// <param name="p">The particle being disabled.</param>
	public override void OnDisabled( Particle p )
	{
		if ( !so.IsValid() ) return;

		so.Delete();
	}

	/// <summary>
	/// Updates the particle.
	/// </summary>
	/// <param name="p">The particle being updated.</param>
	/// <param name="dt">The delta time since the last update.</param>
	public override void OnUpdate( Particle p, float dt )
	{
		if ( !so.IsValid() ) return;

		var rot = p.Angles.ToRotation();

		if ( Renderer.RotateWithGameObject )
		{
			// Rotate the particle with the object
			rot = Renderer.WorldRotation * rot;
		}

		so.Transform = new Transform( p.Position, rot, p.Size * Renderer.Scale.Evaluate( p, 2356 ) );
		so.ColorTint = p.Color.WithAlphaMultiplied( p.Alpha );
		so.Flags.CastShadows = Renderer.CastShadows;
		so.SetMaterialOverride( Renderer.MaterialOverride );

		Renderer.RenderOptions.Apply( so );
	}
}
