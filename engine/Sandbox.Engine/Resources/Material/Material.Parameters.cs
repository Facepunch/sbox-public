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
			ShaderVersion++;
		}
	}

	/// <summary>
	/// Get texture parameter, by name
	/// </summary>
	public Texture GetTexture( string name )
	{
		var texture = Texture.FromNative( native.GetTexture( name ) );

		// Remember where it came from, for devices that read it from the material's file: a material's own texture has no path,
		// and one that has (the same file, once something loaded it by path) is still the material's
		if ( texture is not null && !string.IsNullOrEmpty( ResourcePath ) )
			Texture.MaterialOrigins.AddOrUpdate( texture, new Texture.MaterialOrigin( ResourcePath, name ) );

		return texture;
	}

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
		RememberSet( param, value );
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
		RememberSet( param, texture );
		return true;
	}

	/// <summary>
	/// What code set on this material (<see cref="Set(string, Vector4)"/>, <see cref="Set(string, Texture)"/>) by parameter:
	/// <see cref="Vector4"/> or <see cref="Texture"/> values, for devices that read the material's file and apply these over
	/// it. Replaced whole on each set, so a reader on another thread sees one version. Null until something's set.
	/// </summary>
	internal IReadOnlyDictionary<string, object> SetInCode { get; private set; }

	/// <summary>Moves on with every <see cref="SetInCode"/> change.</summary>
	internal int SetInCodeVersion { get; private set; }

	void RememberSet( string param, object value )
	{
		// The same value again (a component setting it every frame) changes nothing a device holds
		if ( SetInCode is not null && SetInCode.TryGetValue( param, out var current ) && Equals( current, value ) ) return;

		var values = SetInCode is null ? new Dictionary<string, object>( StringComparer.OrdinalIgnoreCase ) : new Dictionary<string, object>( SetInCode, StringComparer.OrdinalIgnoreCase );
		values[param] = value;
		SetInCode = values;
		SetInCodeVersion++;
		SetInCodeChanged?.Invoke( this );
	}

	/// <summary>Told when code sets a value on a material (<see cref="SetInCode"/>): a device's copy of it follows.</summary>
	internal static Action<Material> SetInCodeChanged;

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
