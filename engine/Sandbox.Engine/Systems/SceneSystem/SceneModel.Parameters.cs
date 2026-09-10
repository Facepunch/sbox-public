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

	/// <summary>
	/// Sets a boolean animation graph parameter by name.
	/// </summary>
	public void SetAnimParameter( string name, bool value )
	{
		if ( FindAnimParam( name, out var p ) )
		{
			var t = p.GetParameterType();

			if ( t == AnimParamType.Bool )
			{
				p.SetValue( value );
				return;
			}

			if ( t == AnimParamType.Int )
			{
				p.SetValue( value ? 1 : 0 );
				return;
			}

			if ( t == AnimParamType.Float )
			{
				p.SetValue( value ? 1.0f : 0.0f );
				return;
			}

			Log.Warning( $"SetBool: {t}" );
		}
	}

	/// <summary>
	/// Sets a float animation graph parameter by name.
	/// </summary>
	public void SetAnimParameter( string name, float value )
	{
		if ( !FindAnimParam( name, out var p ) )
			return;

		var t = p.GetParameterType();

		if ( t == AnimParamType.Bool )
		{
			p.SetValue( !value.AlmostEqual( 0.0f ) );
			return;
		}

		if ( t == AnimParamType.Float )
		{
			p.SetValue( value );
			return;
		}

		if ( t == AnimParamType.Int )
		{
			p.SetValue( (int)value );
			return;
		}

		if ( t == AnimParamType.Enum )
		{
			p.SetEnumValue( (int)value );
			return;
		}

		Log.Warning( $"SetFloat: {t}" );

	}

	/// <summary>
	/// Sets an enum animation graph parameter by option name (e.g. "pistol" on "holdtype").
	/// </summary>
	public bool SetAnimParameter( string name, string option )
	{
		if ( !FindAnimParam( name, out var p ) )
			return false;

		var t = p.GetParameterType();

		if ( t == AnimParamType.Enum )
		{
			if ( AnimationGraph.TryGetEnumOptionIndex( name, option, out var index ) )
			{
				p.SetEnumValue( index );
				return true;
			}

			Log.Warning( $"SetAnimParameter: enum \"{name}\" has no option \"{option}\"" );
			return false;
		}

		return false;
	}

	/// <summary>
	/// Sets a vector animation graph parameter by name.
	/// </summary>
	public void SetAnimParameter( string name, Vector3 value )
	{
		if ( FindAnimParam( name, out var p ) )
		{
			var t = p.GetParameterType();

			if ( t == AnimParamType.Vector )
			{
				p.SetValue( value );
				return;
			}

			Log.Warning( $"SetVector: {t}" );
		}
	}

	/// <summary>
	/// Sets a integer animation graph parameter by name.
	/// </summary>
	public void SetAnimParameter( string name, int value )
	{
		if ( FindAnimParam( name, out var p ) )
		{
			var t = p.GetParameterType();

			if ( t == AnimParamType.Bool )
			{
				p.SetValue( value != 0 );
				return;
			}

			if ( t == AnimParamType.Float )
			{
				p.SetValue( (float)value );
				return;
			}

			if ( t == AnimParamType.Int )
			{
				p.SetValue( value );
				return;
			}

			if ( t == AnimParamType.Enum )
			{
				p.SetEnumValue( value );
				return;
			}

			Log.Warning( $"Set int: {t}" );
		}
	}

	/// <summary>
	/// Sets a rotation animation graph parameter by name.
	/// </summary>
	public void SetAnimParameter( string name, Rotation value )
	{
		if ( FindAnimParam( name, out var p ) )
		{
			var t = p.GetParameterType();

			if ( t == AnimParamType.Rotation )
			{
				p.SetValue( value );
				return;
			}

			Log.Warning( $"Set rot: {t}" );
		}
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
	public Rotation GetRotation( string name ) => animNative.GetParameterRotation( name );

	/// <summary>
	/// Get an animated parameter
	/// </summary>
	public Vector3 GetVector3( string name ) => animNative.GetParameterVector3( name );

	/// <summary>
	/// Get an animated parameter
	/// </summary>
	public bool GetBool( string name ) => animNative.GetParameterInt( name ) != 0;

	/// <summary>
	/// Get an animated parameter
	/// </summary>
	public float GetFloat( string name ) => animNative.GetParameterFloat( name );

	/// <summary>
	/// Get an animated parameter
	/// </summary>
	public int GetInt( string name ) => animNative.GetParameterInt( name );

}
