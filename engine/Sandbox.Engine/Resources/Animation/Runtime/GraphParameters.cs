namespace Sandbox.Animation;

/// <summary>
/// Animation graph parameter access. Writes affect the live instance unless explicitly persisted.
/// Inspector values are saved with the owner and reapplied when its instance is recreated.
/// </summary>
public sealed partial class GraphParameters : IJsonPopulator
{
	private readonly AnimationAccessor _graph;

	internal GraphParameters( AnimationAccessor graph ) => _graph = graph;

	[Hide]
	public Graph Graph => _graph.Graph;

	/// <summary>Resets this graph's control parameters to their authored defaults.</summary>
	public void Reset()
	{
		var controller = _graph.Controller;
		if ( controller.IsNull )
			throw new InvalidOperationException( "No animation graph instance is running." );

		controller.ResetControlParameters();
	}

	public bool TryGetKind( string name, out GraphValueKind kind )
		=> TryGetParameter( name, out _, out _, out kind );

	private bool TryGetController( string name, out NativeEngine.AnimationGraph2Controller controller )
	{
		controller = default;
		if ( string.IsNullOrWhiteSpace( name ) )
			return false;

		controller = _graph.Controller;
		return !controller.IsNull;
	}

	private bool TryGetParameter( string name, out NativeEngine.AnimationGraph2Controller controller, out int index, out GraphValueKind kind )
	{
		index = -1;
		kind = GraphValueKind.Unknown;
		if ( !TryGetController( name, out controller ) )
			return false;

		index = controller.GetControlParameterIndex( name );
		if ( index < 0 )
			return false;

		kind = controller.GetControlParameterType( index );
		return kind != GraphValueKind.Unknown;
	}

	public bool TrySet( string name, bool value )
		=> TryGetController( name, out var controller ) && controller.SetParamBool( name, value );

	public bool TrySet( string name, float value )
		=> TryGetController( name, out var controller ) && controller.SetParamFloat( name, value );

	public bool TrySet( string name, int value ) => TrySet( name, (float)value );

	public bool TrySet( string name, string value )
		=> TryGetController( name, out var controller ) && controller.SetParamID( name, value );

	public bool TrySet( string name, Vector3 value )
		=> TryGetController( name, out var controller ) && controller.SetParamVector( name, value );

	public bool TrySet( string name, Transform value )
		=> TryGetController( name, out var controller ) && controller.SetParamTarget( name, value, true, false, default );

	public bool TrySet( string name, Rotation value ) => TrySet( name, new Transform( Vector3.Zero, value ) );

	public bool TrySetTargetBone( string name, string bone )
		=> !string.IsNullOrWhiteSpace( bone ) && TryGetController( name, out var controller )
			&& controller.SetParamTarget( name, default, true, true, bone );

	public bool TryUnsetTarget( string name )
		=> TryGetController( name, out var controller ) && controller.SetParamTarget( name, default, false, false, default );

	/// <summary>Sets a live value, or saves and reapplies it across instance recreation when <paramref name="persist"/> is true.</summary>
	public void Set( string name, bool value, bool persist = false )
	{
		if ( persist )
			Store( name, new( GraphValueKind.Bool, Bool: value ) );
		else
			RequireSuccess( TrySet( name, value ), name );
	}

	/// <inheritdoc cref="Set(string, bool, bool)"/>
	public void Set( string name, float value, bool persist = false )
	{
		if ( persist )
			Store( name, new( GraphValueKind.Float, Float: value ) );
		else
			RequireSuccess( TrySet( name, value ), name );
	}

	public void Set( string name, int value, bool persist = false ) => Set( name, (float)value, persist );

	/// <inheritdoc cref="Set(string, bool, bool)"/>
	public void Set( string name, string value, bool persist = false )
	{
		if ( persist )
			Store( name, new( GraphValueKind.Id, Id: value ?? "" ) );
		else
			RequireSuccess( TrySet( name, value ), name );
	}

	public void Set<T>( string name, T value, bool persist = false ) where T : struct, Enum => Set( name, value.ToString(), persist );

	/// <inheritdoc cref="Set(string, bool, bool)"/>
	public void Set( string name, Vector3 value, bool persist = false )
	{
		if ( persist )
			Store( name, new( GraphValueKind.Vector, Vector: value ) );
		else
			RequireSuccess( TrySet( name, value ), name );
	}

	/// <inheritdoc cref="Set(string, bool, bool)"/>
	public void Set( string name, Transform value, bool persist = false )
	{
		if ( persist )
			Store( name, new( GraphValueKind.Target, Target: new( value, null, true ) ) );
		else
			RequireSuccess( TrySet( name, value ), name );
	}

	public void Set( string name, Rotation value, bool persist = false ) => Set( name, new Transform( Vector3.Zero, value ), persist );

	public void SetTargetBone( string name, string bone, bool persist = false )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( name );
		ArgumentException.ThrowIfNullOrWhiteSpace( bone );
		if ( persist )
			Store( name, new( GraphValueKind.Target, Target: new( default, bone, true ) ) );
		else
			RequireSuccess( TrySetTargetBone( name, bone ), name );
	}

	public void UnsetTarget( string name, bool persist = false )
	{
		if ( persist )
			Store( name, new( GraphValueKind.Target ) );
		else
			RequireSuccess( TryUnsetTarget( name ), name );
	}

	private void RequireSuccess( bool success, string name )
	{
		if ( success )
			return;

		ArgumentException.ThrowIfNullOrWhiteSpace( name );

		if ( !_graph.IsValid )
			throw new InvalidOperationException( "No animation graph instance is running." );

		throw new ArgumentException( $"Animation parameter '{name}' does not exist or does not accept this value.", nameof( name ) );
	}

	public bool TryGetBool( string name, out bool value )
	{
		value = false;
		if ( !TryGetParameter( name, out var controller, out _, out var kind ) || kind is not (GraphValueKind.Bool or GraphValueKind.Float) )
			return false;

		value = controller.GetParamBool( name );
		return true;
	}

	public bool TryGetFloat( string name, out float value )
	{
		value = 0f;
		if ( !TryGetParameter( name, out var controller, out _, out var kind ) || kind is not (GraphValueKind.Float or GraphValueKind.Bool) )
			return false;

		value = controller.GetParamFloat( name );
		return true;
	}

	public bool TryGetVector( string name, out Vector3 value )
	{
		value = default;
		if ( !TryGetParameter( name, out var controller, out _, out var kind ) || kind != GraphValueKind.Vector )
			return false;

		value = controller.GetParamVector( name );
		return true;
	}

	public bool TryGetId( string name, out StringToken value )
		=> TryGetId( name, out _, out value );

	private bool TryGetId( string name, out NativeEngine.AnimationGraph2Controller controller, out StringToken value )
	{
		value = default;
		if ( !TryGetParameter( name, out controller, out var index, out var kind ) || kind != GraphValueKind.Id )
			return false;

		value = new StringToken( controller.GetControlParameterIDHash( index ) );
		return true;
	}

	/// <summary>Reads an ID's source text. Returns false if the token has no known source string.</summary>
	public bool TryGetString( string name, out string value )
	{
		value = "";
		if ( !TryGetId( name, out var controller, out var id ) )
			return false;

		if ( id.Value == 0 )
			return true;

		if ( StringToken.TryLookup( id.Value, out value ) )
			return true;

		value = controller.GetDebugIDName( id );
		return !string.IsNullOrEmpty( value );
	}

	/// <summary>Reads a transform target. Unset targets and bone targets return false.</summary>
	public bool TryGetTargetTransform( string name, out Transform value )
	{
		value = default;
		return TryGetParameter( name, out var controller, out _, out var kind )
			&& kind == GraphValueKind.Target && controller.GetParamTarget( name, out value );
	}
}
