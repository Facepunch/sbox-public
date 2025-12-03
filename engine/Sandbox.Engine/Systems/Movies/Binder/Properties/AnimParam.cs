using System;
using System.Xml.Linq;
using static Sandbox.SkinnedModelRenderer;

namespace Sandbox.MovieMaker.Properties;

#nullable enable

/// <summary>
/// Reads / writes an anim graph parameter on a <see cref="SkinnedModelRenderer"/>.
/// </summary>
file sealed record AnimParamProperty<T>( ITrackProperty<ParameterAccessor?> Parent, string Name ) : ITrackProperty<T>
{
	private IAnimParamAccessor<T> Accessor { get; } = DefaultAnimParamAccessor.Instance as IAnimParamAccessor<T> ?? throw new NotImplementedException();

	public bool IsBound => Parent.Value?.Graph?.GetParameterType( Name ) == typeof( T );

	public T Value
	{
		get => Parent.Value is { } accessor ? Accessor.Get( accessor, Name ) : default!;
		set
		{
			if ( Parent.Value is { } accessor )
			{
				Accessor.Set( accessor, Name, value );
			}
		}
	}

	ITrackTarget ITrackProperty.Parent => Parent;
}

[Expose]
file sealed class AnimParamPropertyFactory : ITrackPropertyFactory<ITrackProperty<ParameterAccessor?>>
{
	public IEnumerable<string> GetPropertyNames( ITrackProperty<ParameterAccessor?> parent )
	{
		var graph = parent is { IsBound: true } ? parent.Value?.Graph : null;

		if ( graph is null ) yield break;

		for ( var i = 0; i < graph.ParamCount; ++i )
		{
			yield return graph.GetParameterName( i );
		}
	}

	public string GetCategoryName( ITrackProperty<ParameterAccessor?> parent, string name ) => "Anim Graph";

	/// <summary>
	/// Any property in a <see cref="ParameterAccessor"/> is an anim graph parameter, but we
	/// can only determine the type if it actually exists.
	/// </summary>
	public Type GetTargetType( ITrackProperty<ParameterAccessor?> parent, string name )
	{
		var graph = parent is { IsBound: true } ? parent.Value?.Graph : null;

		return graph?.GetParameterType( name ) ?? typeof( Unknown );
	}

	public ITrackProperty<T> CreateProperty<T>( ITrackProperty<ParameterAccessor?> parent, string name ) =>
		new AnimParamProperty<T>( parent, name );
}

file interface IAnimParamAccessor<T>
{
	T Get( ParameterAccessor accessor, string name );
	void Set( ParameterAccessor accessor, string name, T value );
}

file sealed class DefaultAnimParamAccessor :
	IAnimParamAccessor<bool>, IAnimParamAccessor<byte>, IAnimParamAccessor<int>, IAnimParamAccessor<float>,
	IAnimParamAccessor<Vector3>, IAnimParamAccessor<Rotation>
{
	public static DefaultAnimParamAccessor Instance { get; } = new();

	bool IAnimParamAccessor<bool>.Get( ParameterAccessor accessor, string name ) => accessor.GetBool( name );
	byte IAnimParamAccessor<byte>.Get( ParameterAccessor accessor, string name ) => (byte)accessor.GetInt( name );
	int IAnimParamAccessor<int>.Get( ParameterAccessor accessor, string name ) => accessor.GetInt( name );
	float IAnimParamAccessor<float>.Get( ParameterAccessor accessor, string name ) => accessor.GetFloat( name );
	Vector3 IAnimParamAccessor<Vector3>.Get( ParameterAccessor accessor, string name ) => accessor.GetVector( name );
	Rotation IAnimParamAccessor<Rotation>.Get( ParameterAccessor accessor, string name ) => accessor.GetRotation( name );

	void IAnimParamAccessor<bool>.Set( ParameterAccessor accessor, string name, bool value ) => accessor.Set( name, value );
	void IAnimParamAccessor<byte>.Set( ParameterAccessor accessor, string name, byte value ) => accessor.Set( name, value );
	void IAnimParamAccessor<int>.Set( ParameterAccessor accessor, string name, int value ) => accessor.Set( name, value );
	void IAnimParamAccessor<float>.Set( ParameterAccessor accessor, string name, float value ) => accessor.Set( name, value );
	void IAnimParamAccessor<Vector3>.Set( ParameterAccessor accessor, string name, Vector3 value ) => accessor.Set( name, value );
	void IAnimParamAccessor<Rotation>.Set( ParameterAccessor accessor, string name, Rotation value ) => accessor.Set( name, value );
}
