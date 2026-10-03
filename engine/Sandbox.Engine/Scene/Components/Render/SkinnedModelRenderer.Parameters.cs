using NativeEngine;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

public sealed partial class SkinnedModelRenderer
{
	/// <summary>
	/// Anim parameter values can either be stored as the native representation (AnimVariant)
	/// or as a string option name for enum parameters. We might be storing the value before
	/// we know which anim graph it targets, so we'll have to resolve the option name later.
	/// </summary>
	/// <param name="Variant">Native representation of the value.</param>
	/// <param name="OptionName">Enum option name to be looked up later.</param>
	private readonly record struct AnimParamValue( AnimVariant Variant = default, string OptionName = null );

	/// <summary>
	/// If something sets parameters before the model is spawned, then we store them
	/// and apply them when it does spawn. This isn't ideal, but it is what it is.
	/// </summary>
	Dictionary<string, AnimParamValue> _params;

	public void Set( string v, bool value ) => SetCore( v, value );
	public void Set( string v, int value ) => SetCore( v, value );
	public void Set( string v, float value ) => SetCore( v, value );
	public void Set( string v, Vector3 value ) => SetCore( v, value );
	public void Set( string v, Rotation value ) => SetCore( v, value );

	private void SetCore( string v, AnimVariant value ) => SetCore( v, new AnimParamValue( Variant: value ) );

	private void SetCore( string v, AnimParamValue value )
	{
		_params ??= new Dictionary<string, AnimParamValue>( StringComparer.OrdinalIgnoreCase );
		_params[v] = value;

		ApplyAnimParameterToModel( v, value );
	}

	/// <summary>
	/// Set an enum parameter by option name (e.g. Set( "holdtype", "pistol" )).
	/// </summary>
	public void Set( string v, string option ) => SetCore( v, new AnimParamValue( OptionName: option ) );

	private void ApplyAnimParameterToModel( string v, AnimParamValue value )
	{
		if ( !string.IsNullOrEmpty( value.OptionName ) )
		{
			SceneModel?.SetAnimParameter( v, value.OptionName );
		}
		else if ( value.Variant.Type != AnimParamType.Unknown )
		{
			SceneModel?.SetAnimParameter( v, value.Variant );
		}
	}

	void ApplyStoredAnimParameters()
	{
		if ( _params is not null )
		{
			foreach ( var p in _params )
			{
				ApplyAnimParameterToModel( p.Key, p.Value );
			}
		}

		// Tick the animation by a frame so we're fully up to date on the first frame.
		if ( Scene.IsEditor && !CanUpdateInEditor() )
		{
			SceneModel.UpdateToBindPose( ReadBonesFromGameObjects );
		}
		else
		{
			SceneModel.Update( Time.Delta, ReadBonesFromGameObjects );
		}
	}

	/// <summary>
	/// Remove any stored parameters
	/// </summary>
	public void ClearParameters()
	{
		_params?.Clear();

		if ( SceneModel.IsValid() )
		{
			SceneModel.ResetAnimParameters();
		}
	}

	internal void ClearParameter( string name )
	{
		ResetParameter( name );

		_params?.Remove( name );
	}

	private void ResetParameter( string name )
	{
		if ( !SceneModel.IsValid() || SceneModel.AnimationGraph is not { IsValid: true } graph )
			return;

		var parameter = graph.GetParameterFromList( name );
		if ( parameter.IsNull )
			return;

		SetCore( name, parameter.GetDefaultValue() );
	}

	internal bool ContainsParameter( string name )
	{
		return _params?.ContainsKey( name ) ?? false;
	}

	/// <summary>Total number of stored (modified) anim-graph parameters across all types.</summary>
	internal int StoredParameterCount => _params?.Count ?? 0;

	//	public void Set( string v, Enum value ) => _sceneObject.SetAnimParameter( v, value );

	// TODO: fall back to checking _params if we don't have a SceneModel?

	public bool GetBool( string v ) => SceneModel?.GetBool( v ) ?? false;
	public int GetInt( string v ) => SceneModel?.GetInt( v ) ?? 0;
	public float GetFloat( string v ) => SceneModel?.GetFloat( v ) ?? 0.0f;
	public Vector3 GetVector( string v ) => SceneModel?.GetVector3( v ) ?? Vector3.Zero;
	public Rotation GetRotation( string v ) => SceneModel?.GetRotation( v ) ?? Rotation.Identity;

	/// <summary>
	/// Converts value to vector local to this entity's eyepos and passes it to SetAnimVector
	/// </summary>
	public void SetLookDirection( string name, Vector3 eyeDirectionWorld )
	{
		var delta = eyeDirectionWorld * WorldRotation.Inverse;
		Set( name, delta );
	}

	/// <summary>
	/// Converts value to vector local to this entity's eyepos and passes it to SetAnimVector. 
	/// This also sets {name}_weight to the weight value.
	/// </summary>
	public void SetLookDirection( string name, Vector3 eyeDirectionWorld, float weight )
	{
		var delta = eyeDirectionWorld * WorldRotation.Inverse;
		Set( name, delta );
		Set( $"{name}_weight", weight );
	}

	/// <summary>
	/// Sets an IK parameter. This sets 3 variables that should be set in the animgraph:
	/// 1. ik.{name}.enabled
	/// 2. ik.{name}.position
	/// 3. ik.{name}.rotation
	/// </summary>
	public void SetIk( string name, Transform tx )
	{
		// convert local to model
		tx = WorldTransform.ToLocal( tx );

		Set( $"ik.{name}.enabled", true );
		Set( $"ik.{name}.position", tx.Position );
		Set( $"ik.{name}.rotation", tx.Rotation );
	}

	/// <summary>
	/// This sets ik.{name}.enabled to false.
	/// </summary>
	public void ClearIk( string name )
	{
		Set( $"ik.{name}.enabled", false );
	}

	/// <summary>
	/// Access to the animgraph parameters for this model
	/// </summary>
	[Property, Group( "Parameters", StartFolded = true ), ShowIf( nameof( ShouldShowParametersEditor ), true )]
	public ParameterAccessor Parameters => field ??= new ParameterAccessor( this );

	public bool ShouldShowParametersEditor
	{
		get
		{
			if ( !UseAnimGraph ) return false;
			if ( !SceneModel.IsValid() ) return false;

			var graph = SceneModel.AnimationGraph;
			if ( graph is null ) return false;
			if ( graph.ParamCount <= 0 ) return false;

			return true;
		}
	}

	/// <summary>
	/// Wraps accessing animgraph parameters for a <see cref="SkinnedModelRenderer"/>,
	/// and handles (de)serializing overridden values when the renderer is saved or loaded.
	/// </summary>
	public sealed class ParameterAccessor : IJsonPopulator
	{
		public AnimationGraph Graph => _renderer.IsValid() && _renderer.SceneModel.IsValid()
			? _renderer.SceneModel.AnimationGraph
			: null;

		readonly SkinnedModelRenderer _renderer;

		internal ParameterAccessor( SkinnedModelRenderer renderer )
		{
			_renderer = renderer;
		}

		/// <summary>
		/// Clear all override values for animation graph parameters.
		/// </summary>
		public void Clear() => _renderer.ClearParameters();

		/// <summary>
		/// Set the override value of the named animation graph parameter to its default value, if it exists.
		/// </summary>
		public void Reset( string name ) => _renderer.ResetParameter( name );

		/// <summary>
		/// Remove the override value of the named animation graph parameter, if it exists.
		/// </summary>
		public void Clear( string name ) => _renderer.ClearParameter( name );

		/// <summary>
		/// Do we have an override value for the named animation graph parameter?
		/// </summary>
		public bool Contains( string name ) => _renderer.ContainsParameter( name );

		public bool GetBool( string v ) => _renderer.GetBool( v );
		public int GetInt( string v ) => _renderer.GetInt( v );
		public float GetFloat( string v ) => _renderer.GetFloat( v );
		public Vector3 GetVector( string v ) => _renderer.GetVector( v );
		public Rotation GetRotation( string v ) => _renderer.GetRotation( v );

		public void Set( string v, bool value ) => _renderer.Set( v, value );
		public void Set( string v, int value ) => _renderer.Set( v, value );
		public void Set( string v, float value ) => _renderer.Set( v, value );
		public void Set( string v, Vector3 value ) => _renderer.Set( v, value );
		public void Set( string v, Rotation value ) => _renderer.Set( v, value );
		public void Set( string v, string option ) => _renderer.Set( v, option );

		// We group parameters by type when serializing:
		// {
		//   "bools": { "param1": true, "param2": false },
		//   "ints": { "param3": 42 },
		//   "floats": { "param4": 3.14 }
		// }

		private static class GroupName
		{
			public const string Bools = "bools";
			public const string Ints = "ints";
			public const string Floats = "floats";
			public const string Vectors = "vectors";
			public const string Rotations = "rotations";
			public const string Options = "options";
		}

		JsonNode IJsonPopulator.Serialize()
		{
			var obj = new JsonObject();

			if ( _renderer._params is not { Count: > 0 } parameters )
			{
				return obj;
			}

			foreach ( var param in parameters )
			{
				if ( !string.IsNullOrEmpty( param.Value.OptionName ) )
				{
					WriteParameter( GroupName.Options, param.Key, param.Value.OptionName );
					continue;
				}

				var value = param.Value.Variant;

				switch ( value.Type )
				{
					case AnimParamType.Bool:
						WriteParameter( GroupName.Bools, param.Key, (bool)value );
						break;

					case AnimParamType.Enum:
						// Enums can only be 1 byte long, the rest could be uninitialized
						// if this comes from native so we truncate here.
						WriteParameter( GroupName.Ints, param.Key, (int)(byte)value );
						break;

					case AnimParamType.Int:
						WriteParameter( GroupName.Ints, param.Key, (int)value );
						break;

					case AnimParamType.Float:
						WriteParameter( GroupName.Floats, param.Key, (float)value );
						break;

					case AnimParamType.Vector:
						WriteParameter( GroupName.Vectors, param.Key, (Vector3)value );
						break;

					case AnimParamType.Rotation:
						WriteParameter( GroupName.Rotations, param.Key, (Rotation)value );
						break;
				}
			}

			return obj;

			void WriteParameter<T>( string groupName, string name, T value )
			{
				if ( !obj.TryGetPropertyValue( groupName, out var node ) || node is not JsonObject groupObj )
				{
					groupObj = new JsonObject();
					obj[groupName] = groupObj;
				}

				groupObj[name] = JsonSerializer.SerializeToNode( value );
			}
		}

		void IJsonPopulator.Deserialize( JsonNode e )
		{
			if ( e is not JsonObject jso )
				return;

			_renderer.ClearParameters();

			ReadParameterGroup<bool>( GroupName.Bools, Set );
			ReadParameterGroup<int>( GroupName.Ints, Set );
			ReadParameterGroup<float>( GroupName.Floats, Set );
			ReadParameterGroup<Vector3>( GroupName.Vectors, Set );
			ReadParameterGroup<Rotation>( GroupName.Rotations, Set );
			ReadParameterGroup<string>( GroupName.Options, Set );

			return;

			void ReadParameterGroup<T>( string groupName, Action<string, T> setAction )
			{
				if ( jso.TryGetPropertyValue( groupName, out var groupNode ) && groupNode is JsonObject groupObj )
				{
					foreach ( var o in groupObj )
					{
						setAction( o.Key, o.Value.Deserialize<T>() );
					}
				}
			}
		}
	}
}
