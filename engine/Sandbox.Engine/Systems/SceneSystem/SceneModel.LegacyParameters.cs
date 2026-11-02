using NativeEngine;

namespace Sandbox;

#pragma warning disable CS0618

public sealed partial class SceneModel
{
	bool FindLegacyParameter( string name, out IAnimParameterInstance p )
	{
		p = default;

		if ( !AnimationGraph.IsValid() )
			return false;

		if ( !AnimationGraph.TryGetParameterIndex( name, out var index ) )
			return false;

		p = animNative.GetAnimParameter( index );
		return p.IsValid;
	}

	internal void SetLegacyParameter( string name, AnimVariant value )
	{
		if ( !FindLegacyParameter( name, out var p ) )
			return;

		var dstType = p.GetParameterType();
		if ( !value.TryConvertTo( dstType, out var converted ) )
		{
			Log.Warning( $"SetAnimParameter( \"{name}\" ): can't convert from {value.Type} to {dstType}." );
			return;
		}

		p.SetValue( converted );
	}

	internal AnimVariant GetLegacyParameter( string name )
	{
		if ( !FindLegacyParameter( name, out var p ) )
			return default;

		return p.GetValue();
	}

	/// <summary>Sets a legacy animation graph boolean parameter.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void SetAnimParameter( string name, bool value ) => SetLegacyParameter( name, (AnimVariant)value );

	/// <summary>Sets a legacy animation graph integer parameter.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void SetAnimParameter( string name, int value ) => SetLegacyParameter( name, (AnimVariant)value );

	/// <summary>Sets a legacy animation graph float parameter.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void SetAnimParameter( string name, float value ) => SetLegacyParameter( name, (AnimVariant)value );

	/// <summary>Sets a legacy animation graph vector parameter.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void SetAnimParameter( string name, Vector3 value ) => SetLegacyParameter( name, (AnimVariant)value );

	/// <summary>Sets a legacy animation graph rotation parameter.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void SetAnimParameter( string name, Rotation value ) => SetLegacyParameter( name, (AnimVariant)value );

	/// <summary>Sets a legacy animation graph enum parameter by option name.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public bool SetAnimParameter( string name, string option )
	{
		if ( !FindLegacyParameter( name, out var p ) )
			return false;

		var type = p.GetParameterType();
		if ( type != AnimParamType.Enum )
		{
			Log.Warning( $"SetAnimParameter( \"{name}\" ): not an enum parameter." );
			return false;
		}

		if ( !AnimationGraph.TryGetEnumOptionIndex( name, option, out var index ) )
		{
			Log.Warning( $"SetAnimParameter( \"{name}\" ): no enum option \"{option}\"" );
			return false;
		}

		p.SetValue( (AnimVariant)(byte)index );
		return true;
	}

	/// <summary>Sets the position and rotation parameters of a legacy animation graph transform.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void SetAnimParameter( string name, Transform value )
	{
		SetAnimParameter( $"{name}.position", value.Position );
		SetAnimParameter( $"{name}.rotation", value.Rotation );
	}

	/// <summary>Resets legacy animation graph parameters to their defaults.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public void ResetAnimParameters() => animNative.ResetGraphParameters();

	/// <summary>Reads a legacy animation graph boolean parameter.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public bool GetBool( string name ) => GetLegacyParameter( name ).TryConvertTo( AnimParamType.Bool, out var result ) && (bool)result;

	/// <summary>Reads a legacy animation graph integer parameter.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public int GetInt( string name ) => GetLegacyParameter( name ).TryConvertTo( AnimParamType.Int, out var result ) ? (int)result : 0;

	/// <summary>Reads a legacy animation graph float parameter.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public float GetFloat( string name ) => GetLegacyParameter( name ).TryConvertTo( AnimParamType.Float, out var result ) ? (float)result : 0f;

	/// <summary>Reads a legacy animation graph vector parameter.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public Vector3 GetVector3( string name ) => GetLegacyParameter( name ).TryConvertTo( AnimParamType.Vector, out var result ) ? (Vector3)result : Vector3.Zero;

	/// <summary>Reads a legacy animation graph rotation parameter.</summary>
	[Obsolete( "AnimGraph1 is obsolete. Use Animation.Parameters instead." )]
	public Rotation GetRotation( string name ) => GetLegacyParameter( name ).TryConvertTo( AnimParamType.Rotation, out var result ) ? (Rotation)result : Rotation.Identity;
}
