namespace Sandbox;

public partial class Material
{
	/// <summary>
	/// Access flags on this material, which usually hint about the contents. These are generally added by 
	/// the shader procedurally - but developers can add these in material editor too.
	/// </summary>
	public FlagsAccessor Flags => new FlagsAccessor( this );

	public readonly ref struct FlagsAccessor
	{
		private readonly Material material;

		public FlagsAccessor( Material material )
		{
			this.material = material;
		}

		public int GetInt( string name, int defaultValue = 0 )
		{
			return this.material.native.GetIntAttributeOrDefault( name, defaultValue );
		}

		public float GetFloat( string name, float defaultValue = 0 )
		{
			return this.material.native.GetFloatAttributeOrDefault( name, defaultValue );
		}

		public bool GetBool( string name, bool defaultValue = false )
		{
			return this.material.native.GetBoolAttributeOrDefault( name, defaultValue );
		}

		public bool IsSky => GetInt( "sky" ) != 0;
		public bool IsTranslucent => GetInt( "translucent" ) != 0;
		public bool IsAlphaTest => GetInt( "alphatest" ) != 0;

		[Obsolete( "Decal materials are obsolete" )]
		public bool IsDecal => GetInt( "decal" ) != 0;
	}
}
