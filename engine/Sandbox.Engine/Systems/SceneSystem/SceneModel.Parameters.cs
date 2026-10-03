using NativeEngine;

namespace Sandbox;

/// <summary>
/// A model scene object that supports animations and can be rendered within a <see cref="SceneWorld"/>.
/// </summary>
public sealed partial class SceneModel : SceneObject
{
	bool FindAnimParam( string name, out IAnimParameterInstance p )
	{
		p = default;

		if ( !AnimationGraph.IsValid() )
			return false;

		if ( !AnimationGraph.TryGetParameterIndex( name, out var index ) )
			return false;

		p = animNative.GetAnimParameter( index );

		return p.IsValid;
	}

	internal void SetAnimParameter( string name, AnimVariant value )
	{
		if ( !FindAnimParam( name, out var p ) )
		{
			return;
		}

		var dstType = p.GetParameterType();

		if ( !value.TryConvertTo( dstType, out var converted ) )
		{
			Log.Warning( $"SetAnimParameter( \"{name}\" ): can't convert from {value.Type} to {dstType}." );
			return;
		}

		p.SetValue( converted );
	}

	internal AnimVariant GetAnimParameter( string name )
	{
		if ( !FindAnimParam( name, out var p ) )
		{
			return default;
		}

		return p.GetValue();
	}

	/// <summary>
	/// Sets a boolean animation graph parameter by name.
	/// </summary>
	public void SetAnimParameter( string name, bool value ) => SetAnimParameter( name, (AnimVariant)value );

	/// <summary>
	/// Sets an integer animation graph parameter by name.
	/// </summary>
	public void SetAnimParameter( string name, int value ) => SetAnimParameter( name, (AnimVariant)value );

	/// <summary>
	/// Sets a float animation graph parameter by name.
	/// </summary>
	public void SetAnimParameter( string name, float value ) => SetAnimParameter( name, (AnimVariant)value );

	/// <summary>
	/// Sets a vector animation graph parameter by name.
	/// </summary>
	public void SetAnimParameter( string name, Vector3 value ) => SetAnimParameter( name, (AnimVariant)value );

	/// <summary>
	/// Sets a rotation animation graph parameter by name.
	/// </summary>
	public void SetAnimParameter( string name, Rotation value ) => SetAnimParameter( name, (AnimVariant)value );

	/// <summary>
	/// Sets an enum animation graph parameter by option name (e.g. "pistol" on "holdtype").
	/// </summary>
	public bool SetAnimParameter( string name, string option )
	{
		if ( !FindAnimParam( name, out var p ) )
			return false;

		var t = p.GetParameterType();

		if ( t != AnimParamType.Enum )
		{
			Log.Warning( $"SetAnimParameter( \"{name}\" ): not an enum parameter." );
			return false;
		}

		if ( !AnimationGraph.TryGetEnumOptionIndex( name, option, out var index ) )
		{
			Log.Warning( $"SetAnimParameter( \"{name}\" ): no enum option \"{option}\"" );
			return false;
		}

		AnimVariant value = (byte)index;

		Assert.AreEqual( t, value.Type );

		p.SetValue( value );
		return true;
	}

	/// <summary>
	/// Reset all animgraph parameters to their default values.
	/// </summary>
	public void ResetAnimParameters()
	{
		animNative.ResetGraphParameters();
	}

	/// <summary>
	/// Get an animated parameter
	/// </summary>
	public bool GetBool( string name ) => GetAnimParameter( name ).TryConvertTo( AnimParamType.Bool, out var result ) && (bool)result;

	/// <summary>
	/// Get an animated parameter
	/// </summary>
	public int GetInt( string name ) => GetAnimParameter( name ).TryConvertTo( AnimParamType.Int, out var result ) ? (int)result : 0;

	/// <summary>
	/// Get an animated parameter
	/// </summary>
	public float GetFloat( string name ) => GetAnimParameter( name ).TryConvertTo( AnimParamType.Float, out var result ) ? (float)result : 0f;

	/// <summary>
	/// Get an animated parameter
	/// </summary>
	public Vector3 GetVector3( string name ) => GetAnimParameter( name ).TryConvertTo( AnimParamType.Vector, out var result ) ? (Vector3)result : Vector3.Zero;

	/// <summary>
	/// Get an animated parameter
	/// </summary>
	public Rotation GetRotation( string name ) => GetAnimParameter( name ).TryConvertTo( AnimParamType.Rotation, out var result ) ? (Rotation)result : Rotation.Identity;

}
