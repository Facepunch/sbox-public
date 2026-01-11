using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

public sealed partial class SkinnedModelRenderer
{
	/// <summary>
	/// If something sets parameters before the model is spawned, then we store them
	/// and apply them when it does spawn. This isn't ideal, but it is what it is.
	/// </summary>
	readonly Dictionary<string, object> parameters = new( StringComparer.OrdinalIgnoreCase );

	public void Set( string v, Vector3 value )
	{
		parameters[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}

	public void Set( string v, int value )
	{
		parameters[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}

	public void Set( string v, float value )
	{
		parameters[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}
	public void Set( string v, bool value )
	{
		parameters[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}

	public void Set( string v, Rotation value )
	{
		parameters[v] = value;
		SceneModel?.SetAnimParameter( v, value );
	}

	void ApplyStoredAnimParameters()
	{
		foreach ( var p in parameters )
		{
			if ( p.Value is Vector3 v ) SceneModel.SetAnimParameter( p.Key, v );
			if ( p.Value is float f ) SceneModel.SetAnimParameter( p.Key, f );
			if ( p.Value is int i ) SceneModel.SetAnimParameter( p.Key, i );
			if ( p.Value is bool b ) SceneModel.SetAnimParameter( p.Key, b );
			if ( p.Value is Rotation r ) SceneModel.SetAnimParameter( p.Key, r );
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
		parameters.Clear();

		if ( SceneModel.IsValid() )
		{
			SceneModel.ResetAnimParameters();
		}
	}

	internal void ClearParameter( string name )
	{
		parameters.Remove( name );
	}

	internal bool ContainsParameter( string name )
	{
		return parameters.ContainsKey( name );
	}

	//	public void Set( string v, Enum value ) => _sceneObject.SetAnimParameter( v, value );

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

	ParameterAccessor _parameters;

	/// <summary>
	/// Access to the animgraph parameters for this model
	/// </summary>
	[Property, Group( "Parameters", StartFolded = true ), ShowIf( nameof( ShouldShowParametersEditor ), true )]
	public ParameterAccessor Parameters
	{
		get
		{
			_parameters ??= new( this );
			return _parameters;
		}
	}

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

	public sealed class ParameterAccessor : IJsonPopulator
	{
		public AnimationGraph Graph => _renderer.IsValid() && _renderer.SceneModel.IsValid() ?
			_renderer.SceneModel.AnimationGraph : null;

		readonly SkinnedModelRenderer _renderer;
		readonly Dictionary<string, bool> _bools = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, int> _ints = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, float> _floats = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, Vector3> _vectors = new( StringComparer.OrdinalIgnoreCase );
		readonly Dictionary<string, Rotation> _rotations = new( StringComparer.OrdinalIgnoreCase );

		internal ParameterAccessor( SkinnedModelRenderer renderer )
		{
			_renderer = renderer;
		}

		public void Clear()
		{
			_bools.Clear();
			_ints.Clear();
			_floats.Clear();
			_vectors.Clear();
			_rotations.Clear();

			_renderer.ClearParameters();
		}

		public void Reset( string name )
		{
			var parameter = Graph.GetParameterFromList( name );
			if ( parameter.IsNull )
				return;

			var defaultValue = parameter.GetDefaultValue();

			switch ( parameter.GetParameterType() )
			{
				case NativeEngine.AnimParamType.Float:
					Set( name, defaultValue.GetValue<float>() );
					break;
				case NativeEngine.AnimParamType.Int:
					Set( name, defaultValue.GetValue<int>() );
					break;
				case NativeEngine.AnimParamType.Enum:
					Set( name, defaultValue.GetValue<byte>() );
					break;
				case NativeEngine.AnimParamType.Bool:
					Set( name, defaultValue.GetValue<bool>() );
					break;
				case NativeEngine.AnimParamType.Vector:
					Set( name, defaultValue.GetValue<Vector3>() );
					break;
				case NativeEngine.AnimParamType.Rotation:
					Set( name, defaultValue.GetValue<Rotation>() );
					break;
				default:
					throw new NotSupportedException( $"Unsupported parameter type: {parameter.GetParameterType()}" );
			}
		}

		public void Clear( string name )
		{
			Reset( name );

			_bools.Remove( name );
			_ints.Remove( name );
			_floats.Remove( name );
			_vectors.Remove( name );
			_rotations.Remove( name );

			_renderer.ClearParameter( name );
		}

		public bool Contains( string name )
		{
			return _renderer.ContainsParameter( name );
		}

		public bool GetBool( string v ) => _renderer.GetBool( v );
		public int GetInt( string v ) => _renderer.GetInt( v );
		public float GetFloat( string v ) => _renderer.GetFloat( v );
		public Vector3 GetVector( string v ) => _renderer.GetVector( v );
		public Rotation GetRotation( string v ) => _renderer.GetRotation( v );

		public void Set( string v, Vector3 value )
		{
			_vectors[v] = value;
			_renderer.Set( v, value );
		}

		public void Set( string v, int value )
		{
			_ints[v] = value;
			_renderer.Set( v, value );
		}

		public void Set( string v, float value )
		{
			_floats[v] = value;
			_renderer.Set( v, value );
		}

		public void Set( string v, bool value )
		{
			_bools[v] = value;
			_renderer.Set( v, value );
		}

		public void Set( string v, Rotation value )
		{
			_rotations[v] = value;
			_renderer.Set( v, value );
		}

		JsonNode IJsonPopulator.Serialize()
		{
			var obj = new JsonObject();

			var boolsObj = new JsonObject();
			var intsObj = new JsonObject();
			var floatsObj = new JsonObject();
			var vectorsObj = new JsonObject();
			var rotationsObj = new JsonObject();

			foreach ( var value in _bools )
			{
				boolsObj.Add( value.Key, value.Value );
			}

			foreach ( var value in _ints )
			{
				intsObj.Add( value.Key, value.Value );
			}

			foreach ( var value in _floats )
			{
				floatsObj.Add( value.Key, value.Value );
			}

			foreach ( var value in _vectors )
			{
				vectorsObj.Add( value.Key, JsonSerializer.SerializeToNode( value.Value ) );
			}

			foreach ( var value in _rotations )
			{
				rotationsObj.Add( value.Key, JsonSerializer.SerializeToNode( value.Value ) );
			}

			obj.Add( "bools", boolsObj );
			obj.Add( "ints", intsObj );
			obj.Add( "floats", floatsObj );
			obj.Add( "vectors", vectorsObj );
			obj.Add( "rotations", rotationsObj );

			return obj;
		}

		void IJsonPopulator.Deserialize( JsonNode e )
		{
			if ( e is not JsonObject jso )
				return;

			_bools.Clear();
			_ints.Clear();
			_floats.Clear();
			_vectors.Clear();
			_rotations.Clear();

			if ( jso.TryGetPropertyValue( "bools", out var boolsNode ) && boolsNode is JsonObject boolsObj )
			{
				foreach ( var o in boolsObj )
				{
					Set( o.Key, o.Value.GetValue<bool>() );
				}
			}

			if ( jso.TryGetPropertyValue( "ints", out var intsNode ) && intsNode is JsonObject intsObj )
			{
				foreach ( var o in intsObj )
				{
					Set( o.Key, o.Value.GetValue<int>() );
				}
			}

			if ( jso.TryGetPropertyValue( "floats", out var floatsNode ) && floatsNode is JsonObject floatsObj )
			{
				foreach ( var o in floatsObj )
				{
					Set( o.Key, o.Value.GetValue<float>() );
				}
			}

			if ( jso.TryGetPropertyValue( "vectors", out var vectorsNode ) && vectorsNode is JsonObject vectorsObj )
			{
				foreach ( var o in vectorsObj )
				{
					Set( o.Key, JsonSerializer.Deserialize<Vector3>( o.Value ) );
				}
			}

			if ( jso.TryGetPropertyValue( "rotations", out var rotationsNode ) && rotationsNode is JsonObject rotationsObj )
			{
				foreach ( var o in rotationsObj )
				{
					Set( o.Key, JsonSerializer.Deserialize<Rotation>( o.Value ) );
				}
			}
		}
	}
}
