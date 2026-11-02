using NativeEngine;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox;

#pragma warning disable CS0618

public sealed partial class SkinnedModelRenderer
{
	/// <summary>
	/// Anim parameter values can either be stored as the native representation (AnimVariant)
	/// or as a string option name for enum parameters. We might be storing the value before
	/// we know which anim graph it targets, so we'll have to resolve the option name later.
	/// </summary>
	/// <param name="Variant">Native representation of the value.</param>
	/// <param name="OptionName">Enum option name to be looked up later.</param>
	private readonly record struct LegacyParameterValue( AnimVariant Variant = default, string OptionName = null );

	/// <summary>
	/// If something sets parameters before the model is spawned, then we store them
	/// and apply them when it does spawn. This isn't ideal, but it is what it is.
	/// </summary>
	Dictionary<string, LegacyParameterValue> _legacyParameters;

	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void Set( string v, bool value ) => SetLegacyParameterCore( v, value );
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void Set( string v, int value ) => SetLegacyParameterCore( v, value );
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void Set( string v, float value ) => SetLegacyParameterCore( v, value );
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void Set( string v, Vector3 value ) => SetLegacyParameterCore( v, value );
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void Set( string v, Rotation value ) => SetLegacyParameterCore( v, value );

	private void SetLegacyParameterCore( string v, AnimVariant value ) => SetLegacyParameterCore( v, new LegacyParameterValue( Variant: value ) );

	private void SetLegacyParameterCore( string v, LegacyParameterValue value )
	{
		_legacyParameters ??= new Dictionary<string, LegacyParameterValue>( StringComparer.OrdinalIgnoreCase );
		_legacyParameters[v] = value;

		ApplyLegacyParameterToModel( v, value );
	}

	/// <summary>
	/// Set an enum parameter by option name (e.g. Set( "holdtype", "pistol" )).
	/// </summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void Set( string v, string option ) => SetLegacyParameterCore( v, new LegacyParameterValue( OptionName: option ) );

	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void Set( string v, Transform value )
	{
		Set( $"{v}.position", value.Position );
		Set( $"{v}.rotation", value.Rotation );
	}

	private void ApplyLegacyParameterToModel( string v, LegacyParameterValue value )
	{
		if ( !string.IsNullOrEmpty( value.OptionName ) )
		{
			SceneModel?.SetAnimParameter( v, value.OptionName );
		}
		else if ( value.Variant.Type != AnimParamType.Unknown )
		{
			SceneModel?.SetLegacyParameter( v, value.Variant );
		}
	}

	void ApplyStoredLegacyParameters()
	{
		if ( _legacyParameters is not null )
		{
			foreach ( var p in _legacyParameters )
			{
				ApplyLegacyParameterToModel( p.Key, p.Value );
			}
		}
	}

	/// <summary>
	/// Remove any stored parameters
	/// </summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void ClearParameters()
	{
		_legacyParameters?.Clear();

		if ( SceneModel.IsValid() )
		{
			SceneModel.ResetAnimParameters();
		}
	}

	internal void ClearLegacyParameter( string name )
	{
		ResetLegacyParameter( name );
		ClearStoredLegacyParameter( name );
	}

	private void ClearStoredLegacyParameter( string name )
	{
		_legacyParameters?.Remove( name );
	}

	private void ResetLegacyParameter( string name )
	{
		if ( !SceneModel.IsValid() || SceneModel.AnimationGraph is not { IsValid: true } graph )
			return;

		var parameter = graph.GetParameterFromList( name );
		if ( parameter.IsNull )
			return;

		SetLegacyParameterCore( name, parameter.GetDefaultValue() );
	}

	internal bool ContainsLegacyParameter( string name )
	{
		return _legacyParameters?.ContainsKey( name ) ?? false;
	}

	/// <summary>Total number of stored (modified) anim-graph parameters across all types.</summary>
	internal int StoredLegacyParameterCount => _legacyParameters?.Count ?? 0;

	/// <summary>
	/// Set a legacy enum parameter from a C# enum - the value's name is used as
	/// the option (e.g. Set( "holdtype", HoldType.Pistol ) sets "Pistol").
	/// </summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void Set<T>( string v, T value ) where T : struct, Enum => Set( v, value.ToString() );

	// TODO: fall back to checking _legacyParameters if we don't have a SceneModel?

	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public bool GetBool( string v ) => SceneModel?.GetBool( v ) ?? false;
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public int GetInt( string v ) => SceneModel?.GetInt( v ) ?? 0;
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public float GetFloat( string v ) => SceneModel?.GetFloat( v ) ?? 0.0f;
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public Vector3 GetVector( string v ) => SceneModel?.GetVector3( v ) ?? Vector3.Zero;
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public Rotation GetRotation( string v ) => SceneModel?.GetRotation( v ) ?? Rotation.Identity;

	/// <summary>
	/// Converts value to vector local to this entity's eyepos and passes it to SetAnimVector
	/// </summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void SetLookDirection( string name, Vector3 eyeDirectionWorld )
	{
		var delta = eyeDirectionWorld * WorldRotation.Inverse;
		Set( name, delta );
	}

	/// <summary>
	/// Converts value to vector local to this entity's eyepos and passes it to SetAnimVector. 
	/// This also sets {name}_weight to the weight value.
	/// </summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void SetLookDirection( string name, Vector3 eyeDirectionWorld, float weight )
	{
		var delta = eyeDirectionWorld * WorldRotation.Inverse;
		Set( name, delta );
		Set( GetLegacyLookWeightParameter( name ), weight );
	}

	// Keep generated names with the renderer, so custom names don't accumulate in a global cache.
	Dictionary<string, string> _legacyLookWeightParameters;
	Dictionary<string, (string Enabled, string Position, string Rotation)> _legacyIkParameters;

	string GetLegacyLookWeightParameter( string name )
	{
		_legacyLookWeightParameters ??= new( StringComparer.Ordinal );
		if ( !_legacyLookWeightParameters.TryGetValue( name, out var parameter ) )
			_legacyLookWeightParameters[name] = parameter = $"{name}_weight";

		return parameter;
	}

	(string Enabled, string Position, string Rotation) GetLegacyIkParameters( string name )
	{
		// Interpolation treated null as an empty name before these strings were cached.
		name ??= "";
		_legacyIkParameters ??= new( StringComparer.Ordinal );
		if ( !_legacyIkParameters.TryGetValue( name, out var parameters ) )
			_legacyIkParameters[name] = parameters = ($"ik.{name}.enabled", $"ik.{name}.position", $"ik.{name}.rotation");

		return parameters;
	}

	/// <summary>
	/// Sets an IK parameter. This sets 3 variables that should be set in the animgraph:
	/// 1. ik.{name}.enabled
	/// 2. ik.{name}.position
	/// 3. ik.{name}.rotation
	/// </summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void SetIk( string name, Transform tx )
	{
		// convert local to model
		tx = WorldTransform.ToLocal( tx );

		var parameters = GetLegacyIkParameters( name );
		Set( parameters.Enabled, true );
		Set( parameters.Position, tx.Position );
		Set( parameters.Rotation, tx.Rotation );
	}

	/// <summary>
	/// This sets ik.{name}.enabled to false.
	/// </summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void ClearIk( string name )
	{
		Set( GetLegacyIkParameters( name ).Enabled, false );
	}

	/// <summary>
	/// Access to the animgraph parameters for this model
	/// </summary>
	[Property, Hide]
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public ParameterAccessor Parameters => field ??= new ParameterAccessor( this );

	[Hide, Obsolete( "AnimGraph1 is obsolete. Use Animation.ShouldShowParameters instead." )]
	public bool ShouldShowParametersEditor => UseAnimGraph && SceneModel.IsValid()
		&& Animation.Graph is null && SceneModel.AnimationGraph is { ParamCount: > 0 };

	/// <summary>
	/// Wraps accessing animgraph parameters for a <see cref="SkinnedModelRenderer"/>,
	/// and handles (de)serializing overridden values when the renderer is saved or loaded.
	/// </summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Sandbox.Animation.GraphParameters instead." )]
	public sealed class ParameterAccessor : IJsonPopulator
	{
		[Hide]
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
		public void Reset( string name ) => _renderer.ResetLegacyParameter( name );

		/// <summary>
		/// Remove the override value of the named animation graph parameter, if it exists.
		/// </summary>
		public void Clear( string name ) => _renderer.ClearLegacyParameter( name );

		/// <summary>
		/// Do we have an override value for the named animation graph parameter?
		/// </summary>
		public bool Contains( string name ) => _renderer.ContainsLegacyParameter( name );

		public bool GetBool( string v ) => _renderer.GetBool( v );
		public int GetInt( string v ) => _renderer.GetInt( v );
		public float GetFloat( string v ) => _renderer.GetFloat( v );
		public Vector3 GetVector( string v ) => _renderer.GetVector( v );
		public Rotation GetRotation( string v ) => _renderer.GetRotation( v );
		public string GetString( string v ) => _renderer._legacyParameters?.GetValueOrDefault( v ).OptionName ?? "";

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

			if ( _renderer._legacyParameters is not { Count: > 0 } parameters )
			{
				return obj;
			}

			foreach ( var param in parameters )
			{
				if ( param.Value.OptionName is not null )
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
			ReadParameterGroup<string>( "strings", Set );

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
