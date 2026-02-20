using NativeEngine;

namespace Sandbox;

/// <summary>
/// Anim param values contain any value for a limited set of types
/// </summary>
public struct AnimParam<T>
{
	public string Name;
	public T MinValue;
	public T MaxValue;
	public T DefaultValue;
	public string[] OptionNames;
}

public sealed partial class AnimationGraph : Resource
{
	internal HAnimationGraph native;

	public override bool IsValid => native.IsValid;

	/// <summary>
	/// Whether the animation graph is invalid, or has not yet loaded.
	/// </summary>
	public bool IsError => native.IsNull || !native.IsStrongHandleValid();

	/// <summary>
	/// Animation graph file name.
	/// </summary>
	public string Name { get; internal set; }

	/// <summary>
	/// Private constructor, use <see cref="FromNative(HAnimationGraph, string)"/>
	/// </summary>
	private AnimationGraph( HAnimationGraph native, string name )
	{
		if ( native.IsNull ) throw new Exception( "Animation Graph pointer cannot be null!" );

		this.native = native;
		Name = name;

		UpdateNameToIndexMapping();
		RegisterWeakResourceId( Name );
	}

	internal override void OnReloaded()
	{
		base.OnReloaded();

		UpdateNameToIndexMapping();
	}

	private void UpdateNameToIndexMapping()
	{
		_nameToIndex.Clear();

		for ( var i = 0; i < ParamCount; i++ )
		{
			_nameToIndex[GetParameterFromList( i ).GetName()] = i;
		}
	}

	~AnimationGraph()
	{
		var n = native;
		native = default;

		MainThread.Queue( () => n.DestroyStrongHandle() );
	}

	internal IAnimParameterList ParameterList => native.GetParameterList();
	internal IAnimParameter GetParameterFromList( int index ) => ParameterList.GetParameter( index );
	internal IAnimParameter GetParameterFromList( string name ) => ParameterList.GetParameter( name );

	/// <summary>
	/// Number of parameters in this animgraph
	/// </summary>
	public int ParamCount => ParameterList.Count();

	private Dictionary<string, int> _nameToIndex = new();

	private static readonly Type[] _types =
	[
		null,
		typeof( bool ),
		typeof( byte ),
		typeof( int ),
		typeof( float ),
		typeof( Vector3 ),
		typeof( Rotation ),
	];

	/// <summary>
	/// Get value type of parameter at given index
	/// </summary>
	public Type GetParameterType( int index )
	{
		return _types[(int)GetParameterFromList( index ).GetParameterType()];
	}

	/// <summary>
	/// Get value type of parameter with the given <paramref name="name"/>, or <see langword="null"/> if not found.
	/// </summary>
	public Type GetParameterType( string name )
	{
		if ( GetParameterFromList( name ) is not { IsValid: true } param )
		{
			return null;
		}

		var parameterType = param.GetParameterType();

		return _types[(int)parameterType];
	}

	/// <summary>
	/// Get name of parameter at given index
	/// </summary>
	public string GetParameterName( int index )
	{
		return GetParameterFromList( index ).GetName();
	}

	internal AnimParam<T> GetParameterInternal<T>( IAnimParameter param )
	{
		if ( !param.IsValid )
		{
			throw new ArgumentException( $"Invalid parameter" );
		}

		var parameterType = param.GetParameterType();
		var type = _types[(int)parameterType];

		if ( type != typeof( T ) )
		{
			throw new ArgumentException( $"Invalid parameter type {typeof( T )}, expected {type}" );
		}

		return new()
		{
			Name = param.GetName(),
			MinValue = param.GetMinValue().GetValue<T>(),
			MaxValue = param.GetMaxValue().GetValue<T>(),
			DefaultValue = param.GetDefaultValue().GetValue<T>(),
			OptionNames = parameterType == AnimParamType.Enum ? Enumerable.Range( 0, param.GetNumOptionNames() )
				.Select( param.GetOptionName )
				.ToArray() : null
		};
	}

	/// <summary>
	/// Try to get parameter index at given name
	/// </summary>
	public bool TryGetParameterIndex( string name, out int index )
	{
		return _nameToIndex.TryGetValue( name, out index );
	}

	/// <summary>
	/// Get parameter at given name
	/// </summary>
	public AnimParam<T> GetParameter<T>( string name )
	{
		return GetParameterInternal<T>( GetParameterFromList( name ) );
	}

	/// <summary>
	/// Get parameter at given index
	/// </summary>
	public AnimParam<T> GetParameter<T>( int index )
	{
		return GetParameterInternal<T>( GetParameterFromList( index ) );
	}
}
