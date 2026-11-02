using System.Text.Json.Nodes;

namespace Sandbox.Animation;

public sealed partial class GraphParameters
{
	private readonly Dictionary<string, Value> _values = new( StringComparer.OrdinalIgnoreCase );

	private readonly record struct TargetValue( Transform Transform, string Bone, bool IsSet );
	private readonly record struct Value( GraphValueKind Kind, bool Bool = default, float Float = default,
		Vector3 Vector = default, string Id = default, TargetValue Target = default );

	internal int StoredCount => _values.Count;

	private void Store( string name, Value value )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		if ( value.Kind == GraphValueKind.Target && value.Target.IsSet && value.Target.Bone is not null )
			ArgumentException.ThrowIfNullOrWhiteSpace( value.Target.Bone );

		_values[name] = value;
		if ( _graph.IsValid )
			Apply( name, value );
	}

	internal void ApplyStoredValues()
	{
		if ( !_graph.IsValid )
			return;

		foreach ( var (name, value) in _values )
			Apply( name, value );
	}

	private void Apply( string name, Value value )
	{
		var success = value.Kind switch
		{
			GraphValueKind.Bool => TrySet( name, value.Bool ),
			GraphValueKind.Float => TrySet( name, value.Float ),
			GraphValueKind.Vector => TrySet( name, value.Vector ),
			GraphValueKind.Id => TrySet( name, value.Id ),
			GraphValueKind.Target when !value.Target.IsSet => TryUnsetTarget( name ),
			GraphValueKind.Target when value.Target.Bone is not null => TrySetTargetBone( name, value.Target.Bone ),
			GraphValueKind.Target => TrySet( name, value.Target.Transform ),
			_ => throw new InvalidOperationException( $"Unsupported animation parameter kind: {value.Kind}." )
		};

		if ( !success )
			Log.Warning( $"Animation parameter override '{name}' does not exist or does not accept this value." );
	}

	/// <summary>Whether this parameter has a saved value.</summary>
	public bool Contains( string name ) => _values.ContainsKey( name );

	public bool GetBool( string name ) => TryGetBool( name, out var value )
		? value : _values.GetValueOrDefault( name ).Bool;
	public float GetFloat( string name ) => TryGetFloat( name, out var value )
		? value : _values.GetValueOrDefault( name ).Float;
	public Vector3 GetVector( string name ) => TryGetVector( name, out var value )
		? value : _values.GetValueOrDefault( name ).Vector;
	public string GetString( string name ) => TryGetString( name, out var value )
		? value : _values.GetValueOrDefault( name ).Id ?? "";

	/// <summary>Clears saved values and resets the live parameters to their authored defaults.</summary>
	public void Clear()
	{
		_values.Clear();
		if ( _graph.IsValid )
			Reset();
	}

	/// <summary>Removes a saved value and restores the live parameter's authored default.</summary>
	public void Clear( string name )
	{
		if ( TryGetController( name, out var controller ) )
			controller.ResetControlParameter( name );
		_values.Remove( name );
	}

	internal void ClearStoredValues()
	{
		var controller = _graph.Controller;
		if ( !controller.IsNull )
		{
			foreach ( var name in _values.Keys )
				controller.ResetControlParameter( name );
		}

		_values.Clear();
	}

	JsonNode IJsonPopulator.Serialize()
	{
		var result = new JsonObject();
		foreach ( var (name, value) in _values )
		{
			switch ( value.Kind )
			{
				case GraphValueKind.Bool: Write( "bools", name, value.Bool ); break;
				case GraphValueKind.Float: Write( "floats", name, value.Float ); break;
				case GraphValueKind.Vector: Write( "vectors", name, value.Vector ); break;
				case GraphValueKind.Id: Write( "ids", name, value.Id ); break;
				case GraphValueKind.Target: Write( "targets", name, value.Target ); break;
				default: throw new InvalidOperationException( $"Unsupported animation parameter kind: {value.Kind}." );
			}
		}

		return result;

		void Write<T>( string groupName, string name, T value )
		{
			if ( result[groupName] is not JsonObject group )
				result[groupName] = group = new JsonObject();
			group[name] = Json.ToNode( value );
		}
	}

	void IJsonPopulator.Deserialize( JsonNode node )
	{
		if ( node is not null && node is not JsonObject )
			throw new ArgumentException( "Animation parameters must be a JSON object.", nameof( node ) );

		ClearStoredValues();
		if ( node is not JsonObject obj )
			return;

		Read<bool>( "bools", ( name, value ) => Set( name, value, persist: true ) );
		Read<float>( "floats", ( name, value ) => Set( name, value, persist: true ) );
		Read<Vector3>( "vectors", ( name, value ) => Set( name, value, persist: true ) );
		Read<string>( "ids", ( name, value ) => Set( name, value, persist: true ) );
		Read<TargetValue>( "targets", ( name, value ) => Store( name, new( GraphValueKind.Target, Target: value ) ) );

		void Read<T>( string groupName, Action<string, T> set )
		{
			if ( !obj.TryGetPropertyValue( groupName, out var groupNode ) )
				return;
			if ( groupNode is not JsonObject group )
				throw new ArgumentException( $"Animation parameter group '{groupName}' must be a JSON object.", nameof( node ) );

			foreach ( var (name, value) in group )
				set( name, Json.FromNode<T>( value ) );
		}
	}
}
