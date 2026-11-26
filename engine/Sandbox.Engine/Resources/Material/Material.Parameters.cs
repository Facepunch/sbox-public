namespace Sandbox;

public partial class Material
{
	/// <summary>
	/// Gets the underlying shader name for this material.
	/// </summary>
	// As code gave it, else as its compiled file names it; the material system only for one with no file to read (no
	// Borealis.Gpu content, an error material)
	public string ShaderName
	{
		get
		{
			if ( _shaderName is not null ) return _shaderName;
			_shaderName = ShaderNameFromCode ?? FileShaderName( Graphics.BorealisGpuContent, FilePath, reread: _reloaded ) ?? native.GetString( "shader", "invalid" );
			_reloaded = false;
			return _shaderName;
		}
	}

	string _shaderName;

	// Reloaded since its name was last worked out: its file is read again
	bool _reloaded;

	// The shader each compiled material file names, by the file's path
	static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> fileShaders = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// The shader a material's compiled file names (<c>m_shaderName</c>, as the material system's <c>"shader"</c> holds it),
	/// read once per file, again with <paramref name="reread"/> (it was reloaded); null when there's no file of it to read.
	/// </summary>
	internal static string FileShaderName( Borealis.Source2.ContentFiles content, string path, bool reread )
	{
		if ( string.IsNullOrEmpty( path ) || content is null ) return null;
		if ( !reread && fileShaders.TryGetValue( path, out var known ) ) return known;

		string shader = null;
		try
		{
			if ( content.TryRead( path ) is { } data ) shader = Borealis.Source2.Materials.MaterialFile.Read( data ).Shader;
		}
		catch ( Exception e ) when ( e is System.IO.InvalidDataException or System.IO.IOException )
		{
			// Unreadable: the material system's name for it
		}

		if ( shader is null ) fileShaders.TryRemove( path, out _ );
		else fileShaders[path] = shader;
		return shader;
	}

	/// <summary>
	/// Get thje first texture assigned to this material, if any.
	/// </summary>
	public Texture FirstTexture => Texture.FromNativeFile( native.GetFirstTextureAttribute() );

	// Made from an anonymous shader, which can't be loaded by the name the material has for it
	internal Shader anonymousShader;

	/// <summary>The shader <see cref="Shader"/> was last set to by code, as the material names it; null if never set.</summary>
	internal string ShaderSetInCode { get; private set; }

	/// <summary>
	/// The shader the material names as code gave it (made from a shader, or <see cref="Shader"/> set), without asking the
	/// material system; null when it's the one its compiled file names.
	/// </summary>
	internal string ShaderNameFromCode => anonymousShader?.ResourcePath ?? ShaderSetInCode ?? MadeFromShader;

	/// <summary>
	/// Gets the material's shader
	/// </summary>
	public Shader Shader
	{
		get
		{
			return anonymousShader ?? Shader.Load( ShaderName );
		}

		set
		{
			if ( value is { IsAnonymous: true } )
				throw new InvalidOperationException( "An anonymous shader can't be set by name, make the material with Material.Create( name, shader )" );

			anonymousShader = null;
			ShaderSetInCode = value?.ResourcePath ?? "invalid";
			native.Set( "shader", ShaderSetInCode );
			ShaderVersion++;
			_shaderName = null;
		}
	}

	/// <summary>
	/// Get texture parameter, by name
	/// </summary>
	public Texture GetTexture( string name )
	{
		var texture = Texture.FromNative( native.GetTexture( name ) );

		// Remember where it came from, for devices that read it from the material's file: a material's own texture has no path,
		// and one that has (the same file, once something loaded it by path) is still the material's; one already noted with the same origin isn't noted again: fog asks for the sky's cubemap every frame, and each
		// note boxed a new origin
		if ( texture is not null && !string.IsNullOrEmpty( ResourcePath ) )
		{
			var origin = new Texture.MaterialOrigin( ResourcePath, name );
			if ( !Texture.MaterialOrigins.TryGetValue( texture, out var known ) || known is not Texture.MaterialOrigin same || same != origin )
				Texture.MaterialOrigins.AddOrUpdate( texture, origin );
		}

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

	/// <summary>Moves on with every <see cref="SetInCode"/> and <see cref="FeaturesSetInCode"/> change.</summary>
	internal int SetInCodeVersion { get; private set; }

	void RememberSet( string param, object value )
	{
		// The same value again (a component setting it every frame) changes nothing a device holds
		if ( SetInCode is not null && SetInCode.TryGetValue( param, out var current ) && Equals( current, value ) ) return;

		var values = SetInCode is null ? new Dictionary<string, object>( StringComparer.OrdinalIgnoreCase ) : new Dictionary<string, object>( SetInCode, StringComparer.OrdinalIgnoreCase );
		values[param] = value;
		SetInCode = values;
		NoteSetInCode();
	}

	/// <summary>Moves <see cref="SetInCodeVersion"/> on and tells <see cref="SetInCodeChanged"/>.</summary>
	void NoteSetInCode()
	{
		SetInCodeVersion++;
		SetInCodeChanged?.Invoke( this );
	}

	/// <summary>
	/// Told when code sets a value or a feature on a material (<see cref="SetInCode"/>, <see cref="FeaturesSetInCode"/>): a
	/// device's copy of it follows.
	/// </summary>
	internal static Action<Material> SetInCodeChanged;

	/// <summary>
	/// Whether code made or changed this material (<see cref="MadeFromShader"/>, <see cref="SetInCode"/>,
	/// <see cref="FeaturesSetInCode"/>): a device can't draw it from its file alone.
	/// </summary>
	internal bool ChangedInCode => MadeFromShader is not null || SetInCode is { Count: > 0 } || FeaturesSetInCode is { Count: > 0 };

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
