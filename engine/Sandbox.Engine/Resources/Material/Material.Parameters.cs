namespace Sandbox;

public partial class Material
{
	/// <summary>
	/// Gets the underlying shader name for this material.
	/// </summary>
	public string ShaderName => native.GetString( "shader", "invalid" );

	/// <summary>
	/// Get thje first texture assigned to this material, if any.
	/// </summary>
	public Texture FirstTexture => Texture.FromNative( native.GetFirstTextureAttribute() );

	// Made from an anonymous shader, which can't be loaded by the name the material has for it
	internal Shader anonymousShader;

	/// <summary>
	/// Gets the material's shader
	/// </summary>
	public Shader Shader
	{
		get
		{
			return anonymousShader ?? Shader.Load( native.GetString( "shader", "invalid" ) );
		}

		set
		{
			if ( value is { IsAnonymous: true } )
				throw new InvalidOperationException( "An anonymous shader can't be set by name, make the material with Material.Create( name, shader )" );

			anonymousShader = null;
			native.Set( "shader", value?.ResourcePath ?? "invalid" );
		}
	}

	/// <summary>
	/// Get texture parameter, by name
	/// </summary>
	public Texture GetTexture( string name ) => Texture.FromNative( native.GetTexture( name ) );

	/// <summary>
	/// Get Vector4 parameter, by name
	/// </summary>
	public Vector4 GetVector4( string name ) => native.GetVector4( name );

	/// <summary>
	/// Get Color parameter, by name
	/// </summary>
	public Color GetColor( string name ) => native.GetVector4( name );

	/// <summary>
	/// Overrides/Sets an Vector4 within the material
	/// </summary>
	public unsafe bool Set( string param, Vector4 value )
	{
		native.Set( param, value );
		native.SetEdited( true );
		return true;
	}

	/// <summary>
	/// Override/Sets texture parameter (Color, Normal, etc)
	/// </summary>
	public unsafe bool Set( string param, Texture texture )
	{
		if ( texture == null || texture.native.IsNull ) return false;

		//
		// Legacy support for old style param names - 16/10/2025
		// Newer games should be putting the exact param name in
		//
		if ( Application.GamePackage?.ApiVersion < 22 && !param.StartsWith( "g_t" ) )
		{
			param = $"g_t{param}";
		}

		native.Set( param, texture.native );

		native.SetEdited( true );
		return true;
	}

	/// <summary>
	/// Overrides/Sets an color within the material as a color value within the material
	/// </summary>
	public bool Set( string param, Color value ) => Set( param, new Vector4( value ) );

	/// <summary>
	/// Overrides/Sets an Vector3 within the material
	/// </summary>
	public bool Set( string param, Vector3 value ) => Set( param, new Vector4( value, 0.0f ) );

	/// <summary>
	/// Overrides/Sets an Vector2 within the material
	/// </summary>
	public bool Set( string param, Vector2 value ) => Set( param, new Vector4( value.x, value.y, 0.0f, 0.0f ) );

	/// <summary>
	/// Overrides/Sets an float within the material
	/// </summary>
	public bool Set( string param, float value ) => Set( param, new Vector4( value, 0.0f, 0.0f, 0.0f ) );

	/// <summary>
	/// Overrides/Sets an int within the material
	/// </summary>
	public bool Set( string param, int value ) => Set( param, new Vector4( value, 0.0f, 0.0f, 0.0f ) );

	/// <summary>
	/// Overrides/Sets an bool within the material
	/// </summary>
	public bool Set( string param, bool value ) => Set( param, new Vector4( value ? 1.0f : 0.0f, 0.0f, 0.0f, 0.0f ) );
}
