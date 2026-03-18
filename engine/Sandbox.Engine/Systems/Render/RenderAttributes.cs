using NativeEngine;
using Sandbox.Rendering;
using System.Runtime.CompilerServices;

namespace Sandbox;

/// <summary>
/// RenderAttributes are a set of values that are passed to the renderer.
/// They can be a variety of primitive types, textures, samplers or buffers.
/// You can access attributes in the shader by binding them to a variable:
/// <code>
/// float4 CornerRadius &lt; Attribute( "BorderRadius" ); &gt;;
/// Texture2D g_tColor 	&lt; Attribute( "Texture" ); SrgbRead( false ); &gt;;
/// </code>
/// <seealso cref="Renderer.Attributes"/>
/// <seealso cref="Graphics.DrawModel(Model, Transform, RenderAttributes)"/>
/// <seealso cref="ComputeShader.DispatchWithAttributes(RenderAttributes, int, int, int)"/>
/// </summary>
public partial class RenderAttributes
{
	private CRenderAttributes attributes;
	private bool manuallyAllocated;

	internal static RenderAttributePool Pool = new();

	internal CRenderAttributes Get()
	{
		return attributes;
	}

	internal void Set( CRenderAttributes a )
	{
		TryFree();
		attributes = a;
		manuallyAllocated = false;
	}

	public RenderAttributes()
	{
		manuallyAllocated = true;
		attributes = CRenderAttributes.Create();
	}

	internal RenderAttributes( CRenderAttributes attr )
	{
		Set( attr );
	}

	~RenderAttributes()
	{
		TryFree();
	}

	private void TryFree()
	{
		if ( manuallyAllocated )
		{
			//
			// garry: I'm deleting these on the main thread. I don't think there's actually
			// any danger here because generally when you're using these you're passing them
			// into something (like a render) and it's using it immediately and not holding on
			// to it.
			//
			var a = attributes;
			MainThread.Queue( () => a.DeleteThis() );
		}

		attributes = default;

		ClearUsedTextures();
	}

	public void Clear()
	{
		if ( !attributes.IsValid ) return;
		attributes.Clear( true, true );
		ClearUsedTextures();
	}

	internal void Clear( bool freeMemory )
	{
		if ( !attributes.IsValid ) return;
		attributes.Clear( freeMemory, true );
		ClearUsedTextures();
	}

	public void SetCombo( in StringToken k, in int value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetComboValue( k, (byte)value );
	}

	[Obsolete( "Please use SetComboEnum" )]
	public void SetCombo( in string k, in Enum value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetComboValue( k, (byte)(int)(object)value );
	}

	public void SetComboEnum<T>( in StringToken k, in T value ) where T : unmanaged, Enum
	{
		if ( !attributes.IsValid ) return;
		attributes.SetComboValue( k, (byte)value.AsInt() );
	}

	public void SetCombo( in StringToken k, in bool value )
	{
		SetCombo( k, value ? 1 : 0 );
	}

	public T GetComboEnum<T>( in StringToken k, in T defaultValue ) where T : Enum
	{
		if ( !attributes.IsValid )
			return defaultValue;

		return (T)(object)(int)attributes.GetComboValue( k, (byte)(int)(object)defaultValue );
	}

	public bool GetComboBool( in StringToken k, in bool defaultValue = false )
	{
		if ( !attributes.IsValid )
			return defaultValue;

		return attributes.GetComboValue( k, (byte)(defaultValue ? 1 : 0) ) == 1;
	}

	public int GetComboInt( in StringToken k, in int defaultValue = 0 )
	{
		if ( !attributes.IsValid )
			return defaultValue;

		return attributes.GetComboValue( k, (byte)defaultValue );
	}

	/// <summary>
	/// Internal for a reason - don't expose!
	/// </summary>
	internal void SetPointer( in StringToken k, in IntPtr value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetPtrValue( k, value );
	}

	public void Set( in StringToken k, in int value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetIntValue( k, value );
	}

	public void Set( in StringToken k, in Vector2Int value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetIntVector4DValue( k, value.x, value.y, 0, 0 );
	}

	public void Set( in StringToken k, in Vector3Int value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetIntVector4DValue( k, value.x, value.y, value.z, 0 );
	}

	public void Set( in StringToken k, in Texture value, in int mip = -1 )
	{
		if ( !attributes.IsValid ) return;

		var native = value?.native ?? default;
		Set( k, native, mip );

		SetUsedTexture( k, value );
	}

	public void Set( in StringToken k, in SamplerState value )
	{
		if ( !attributes.IsValid ) return;

		attributes.SetSamplerValue( k, new( value ) );
	}

	internal void Set( in StringToken k, in ITexture value, in int mip = -1 )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetTextureValue( k, value, mip );
	}

	public void Set( in StringToken k, in float value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetFloatValue( k, value );
	}
	public void Set( in StringToken k, in double value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetFloatValue( k, (float)value );
	}

	public void Set( in StringToken k, in string value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetStringValue( k, value );
	}

	public void Set( in StringToken k, in bool value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetBoolValue( k, value );
	}

	public void Set( in StringToken k, in Vector4 value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetVector4DValue( k, value );
	}

	public void Set( in StringToken k, in Angles value )
	{
		Set( k, new Vector3( value.pitch, value.yaw, value.roll ) );
	}

	public void Set( in StringToken k, in Vector3 value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetVectorValue( k, value );
	}

	public void Set( in StringToken k, in Vector2 value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetVector2DValue( k, value );
	}

	public void Set( in StringToken k, in GpuBuffer value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetBufferValue( k, value is not null ? value.native : default );
	}

	/// <summary>
	/// Set a constant buffer to a specific value
	/// </summary>
	public unsafe void SetData<T>( in StringToken k, Span<T> value ) where T : unmanaged
	{
		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new ArgumentException( $"{nameof( T )} must be a POD type" );

		int actualSize = value.Length * Unsafe.SizeOf<T>();
		if ( actualSize <= 0 ) return;
		fixed ( T* ptr = value )
			RenderTools.SetDynamicConstantBufferData( attributes, k, Graphics.Context, (IntPtr)ptr, actualSize );
	}

	/// <summary>
	/// Set a constant buffer to a specific value
	/// </summary>
	public unsafe void SetData<T>( in StringToken k, T value ) where T : unmanaged
	{
		if ( !SandboxedUnsafe.IsAcceptablePod<T>() )
			throw new ArgumentException( $"{nameof( T )} must be a POD type" );

		if ( !attributes.IsValid ) return;
		int actualSize = Unsafe.SizeOf<T>();
		if ( actualSize <= 0 ) return;
		RenderTools.SetDynamicConstantBufferData( attributes, k, Graphics.Context, (IntPtr)(&value), actualSize );
	}


	/// <summary>
	/// Set a constant buffer to a specific value
	/// </summary>
	public unsafe void SetData<T>( in StringToken k, T[] value ) where T : unmanaged
	{
		if ( !attributes.IsValid ) return;
		SetData( k, value.AsSpan() );
	}

	/// <summary>
	/// Set a constant buffer to a specific value
	/// </summary>
	public unsafe void SetData<T>( in StringToken k, List<T> value ) where T : unmanaged
	{
		if ( !attributes.IsValid ) return;
		SetData( k, value.ToArray() );
	}

	/// <summary>
	/// Get a bool value - else defaultValue if missing
	/// </summary>
	public bool GetBool( in StringToken name, in bool defaultValue = default )
	{
		if ( !attributes.IsValid ) return defaultValue;
		return attributes.GetBoolValue( name, defaultValue );
	}

	/// <summary>
	/// Get a vector3 value - else defaultValue if missing
	/// </summary>
	public Vector3 GetVector( in StringToken name, in Vector3 defaultValue = default )
	{
		if ( !attributes.IsValid ) return defaultValue;
		return attributes.GetVectorValue( name, defaultValue );
	}

	/// <summary>
	/// Get a vector4 value - else defaultValue if missing
	/// </summary>
	public Vector4 GetVector4( in StringToken name, in Vector4 defaultValue = default )
	{
		if ( !attributes.IsValid ) return defaultValue;
		return attributes.GetVector4DValue( name, defaultValue );
	}

	/// <summary>
	/// Get a vector4 value - else defaultValue if missing
	/// </summary>
	public Angles GetAngles( in StringToken name, in Angles defaultValue = default )
	{
		if ( !attributes.IsValid ) return defaultValue;

		var d = new Vector3( defaultValue.pitch, defaultValue.yaw, defaultValue.roll );
		d = attributes.GetVectorValue( name, d );

		return new Angles( d.x, d.y, d.z );
	}

	/// <summary>
	/// Get a float value - else defaultValue if missing
	/// </summary>
	public float GetFloat( in StringToken name, in float defaultValue = default )
	{
		if ( !attributes.IsValid ) return defaultValue;
		return attributes.GetFloatValue( name, defaultValue );
	}

	/// <summary>
	/// Get a int value - else defaultValue if missing
	/// </summary>
	public int GetInt( in StringToken name, in int defaultValue = default )
	{
		if ( !attributes.IsValid ) return defaultValue;
		return attributes.GetIntValue( name, defaultValue );
	}

	/// <summary>
	/// Get a matrix value - else defaultValue if missing
	/// </summary>
	public Matrix GetMatrix( in StringToken name, in Matrix defaultValue = default )
	{
		if ( !attributes.IsValid ) return defaultValue;
		return attributes.GetVMatrixValue( name, defaultValue );
	}

	/// <summary>
	/// Get a texture value - else defaultValue if missing
	/// </summary>
	public Texture GetTexture( in StringToken name, in Texture defaultValue = default )
	{
		if ( !attributes.IsValid ) return defaultValue;
		var texturePointer = attributes.GetTextureValue( name, default );
		if ( texturePointer.IsNull ) return defaultValue;

		return Texture.FromNative( texturePointer );
	}

	public void Set( in StringToken k, in Matrix value )
	{
		if ( !attributes.IsValid ) return;
		attributes.SetVMatrixValue( k, value );
	}

	internal void MergeTo( RenderAttributes renderAttributes )
	{
		if ( renderAttributes is null ) throw new ArgumentNullException( nameof( renderAttributes ) );
		if ( !attributes.IsValid || !renderAttributes.attributes.IsValid ) return;
		attributes.MergeToPtr( renderAttributes.attributes );
	}

	// These are for backwards compatibility with compiled shit
	public void SetCombo( [StringToken.Convert] in string k, in int value ) => SetCombo( (StringToken)k, value );
	public void SetCombo( [StringToken.Convert] in string k, in bool value ) => SetCombo( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in bool value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in int value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in Vector2Int value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in Vector3Int value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in Vector4 value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in Vector3 value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in Vector2 value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in Matrix value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in Angles value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in string value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in Texture value, in int mip = -1 ) => Set( (StringToken)k, value, mip );
	public void Set( [StringToken.Convert] in string k, in float value ) => Set( (StringToken)k, value );
	public void Set( [StringToken.Convert] in string k, in double value ) => Set( (StringToken)k, (float)value );
	public Texture GetTexture( [StringToken.Convert] in string name, in Texture defaultValue = default ) => GetTexture( (StringToken)name, defaultValue );

}
