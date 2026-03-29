using System.Text.Json.Nodes;

namespace Sandbox;

public partial class SkinnedModelRenderer
{
	MorphAccessor _morphs;

	/// <summary>
	/// Access to the morphs for this model
	/// </summary>
	[Property, Group( "Morphs", StartFolded = true ), ShowIf( "ShouldShowMorphsEditor", true )]
	public MorphAccessor Morphs
	{
		get
		{
			_morphs ??= new( this );
			return _morphs;
		}
	}

	public bool ShouldShowMorphsEditor
	{
		get
		{
			if ( Model is null ) return false;
			if ( Model.Morphs.Count == 0 ) return false;

			// todo - should we hide if we're bone merged?

			return true;
		}
	}

	public sealed class MorphAccessor : IJsonPopulator
	{
		Dictionary<string, float> _values = new Dictionary<string, float>( StringComparer.OrdinalIgnoreCase );

		ModelRenderer renderer;

		internal MorphAccessor( ModelRenderer renderer )
		{
			this.renderer = renderer;
		}

		public string[] Names
		{
			get
			{
				if ( renderer.Model is not null )
				{
					return renderer.Model.Morphs.Names;
				}

				return null;
			}
		}

		/// <summary>
		/// Sets a morph override value.
		/// Uses a default blend time to smoothly transition from
		/// the animation-driven morph to this override.
		/// </summary>
		public void Set( string name, float weight )
		{
			_values[name] = weight;

			if ( renderer.SceneObject is SceneModel model )
			{
				model.Morphs.Set( name, weight );
			}
		}

		/// <summary>
		/// Sets a morph override value with blending.
		/// fadeTime controls how long it takes to blend between
		/// the animation-driven morph and this override.
		/// </summary>
		public void Set( string name, float weight, float fadeTime )
		{
			_values[name] = weight;

			if ( renderer.SceneObject is SceneModel model )
			{
				model.Morphs.Set( name, weight, fadeTime );
			}
		}

		/// <summary>
		/// Returns true if we have this value overridden (set). False means its value is likely
		/// being driven by animation etc.
		/// </summary>
		public bool ContainsOverride( string name )
		{
			return _values.ContainsKey( name );
		}

		/// <summary>
		/// Get this value
		/// </summary>
		public float Get( string name )
		{
			if ( _values.TryGetValue( name, out var value ) )
			{
				return value;
			}

			if ( renderer.SceneObject is SceneModel model )
			{
				return model.Morphs.Get( name );
			}

			return default;
		}

		/// <summary>
		/// Clears the morph override and returns control to the animation.
		/// Uses the default blend time to smoothly transition back.
		/// </summary>
		public void Clear( string name )
		{
			_values.Remove( name );

			if ( renderer.SceneObject is SceneModel model )
			{
				model.Morphs.Reset( name );
			}
		}

		/// <summary>
		/// Clears the morph override and returns control to the animation.
		/// fadeTime controls how long it takes to blend back to the animation-driven morph.
		/// </summary>
		public void Clear( string name, float fadeTime )
		{
			_values.Remove( name );

			if ( renderer.SceneObject is SceneModel model )
			{
				model.Morphs.Reset( name, fadeTime );
			}
		}

		internal void Apply()
		{
			if ( renderer.SceneObject is not SceneModel model )
				return;

			model.Morphs.ResetAll();

			foreach ( var value in _values )
			{
				model.Morphs.Set( value.Key, value.Value );
			}
		}

		JsonNode IJsonPopulator.Serialize()
		{
			var obj = new JsonObject();

			foreach ( var value in _values )
			{
				obj.Add( value.Key, value.Value );
			}

			return obj;
		}

		void IJsonPopulator.Deserialize( JsonNode e )
		{
			if ( e is not JsonObject jso )
				return;

			_values.Clear();

			foreach ( var o in jso )
			{
				_values[o.Key] = o.Value.GetValue<float>();
			}
		}
	}

}


