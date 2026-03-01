using Sandbox.Internal;

namespace Sandbox;

/// <summary>
/// Wraps <see cref="MemberInfo">MemberInfo</see> but with caching and sandboxing.
///
/// Returned by <see cref="Internal.TypeLibrary"/> and <see cref="Sandbox.TypeDescription"/>.
/// </summary>
[SkipHotload]
public class MemberDescription : ISourceLineProvider, IMemberNameProvider, ITitleProvider, ICategoryProvider
{
	readonly TypeLibrary library;
	internal MemberInfo MemberInfo;
	internal DisplayInfo displayInfo;

	/// <summary>
	/// The type that we're a member of
	/// </summary>
	public TypeDescription TypeDescription { get; protected set; }

	/// <summary>
	/// The type that actually defined this member. This may be different from <see cref="TypeDescription"/> if this member is inherited from a base class.
	/// </summary>
	public TypeDescription DeclaringType
	{
		get => library.GetType( MemberInfo.DeclaringType );
	}

	/// <summary>
	/// Unique identifier based on full name
	/// </summary>
	internal string Ident { get; set; }

	/// <summary>
	/// Name of this type member.
	/// </summary>
	public string Name { get; private set; }

	/// <summary>
	/// An integer that represents this member. Based off its type and name.
	/// </summary>
	public int Identity { get; private set; }

	/// <summary>
	/// Display name or title of this type member.
	/// </summary>
	public string Title => displayInfo.Name;

	/// <summary>
	/// Description of this type member. This usually provided from the summary XML comment above the definition.
	/// </summary>
	public string Description => displayInfo.Description;

	/// <summary>
	/// The icon for this, if provided via the [Icon] attribute
	/// </summary>
	public string Icon => displayInfo.Icon;

	/// <summary>
	/// The group - usually provided via the [Group] attribute
	/// </summary>
	public string Group => displayInfo.Group;

	/// <summary>
	/// If this is marked as [ReadOnly]
	/// </summary>
	public bool ReadOnly => displayInfo.ReadOnly;

	/// <summary>
	/// The display order - usually provided via the [Order] attribute
	/// </summary>
	public int Order => displayInfo.Order;

	/// <summary>
	/// Tags are usually provided via the [Tags] attribute
	/// </summary>
	public string[] Tags { get; internal set; }

	/// <summary>
	/// Aliases allow this to be found by alternative names.
	/// </summary>
	public string[] Aliases => displayInfo.Alias;

	/// <summary>
	/// Attributes on this member
	/// </summary>
	public Attribute[] Attributes { get; internal set; }

	/// <summary>
	/// Access the full DisplayInfo for this type. This is faster than creating the DisplayInfo every time we need it.
	/// </summary>
	public DisplayInfo GetDisplayInfo() => displayInfo;

	/// <summary>
	/// True if static
	/// </summary>
	public bool IsStatic { get; protected set; }

	/// <summary>
	/// True if publicly accessible
	/// </summary>
	public bool IsPublic { get; protected set; }

	/// <inheritdoc cref="MethodBase.IsFamily"/>
	public bool IsFamily { get; protected set; }

	/// <summary>
	/// True if we're a method
	/// </summary>
	public virtual bool IsMethod => false;

	/// <summary>
	/// True if we're a property
	/// </summary>
	public virtual bool IsProperty => false;

	/// <summary>
	/// True if we're a field
	/// </summary>
	public virtual bool IsField => false;

	/// <summary>
	/// The line number of this member
	/// </summary>
	public int SourceLine { get; internal set; }

	/// <summary>
	/// The file containing this member
	/// </summary>
	public string SourceFile { get; internal set; }

	string ISourcePathProvider.Path => SourceFile ?? TypeDescription?.SourceFile;
	int ISourceLineProvider.Line => SourceLine;
	string IMemberNameProvider.MemberName => Name;

	string ITitleProvider.Value => Title;
	string ICategoryProvider.Value => Group;

	internal MemberDescription( TypeLibrary tl )
	{
		library = tl;
	}

	protected void Init( MemberInfo x )
	{
		MemberInfo = x;
		Name = MemberInfo.Name;

		CaptureAttributes( MemberInfo );

		displayInfo = DisplayInfo.ForMember( x, false, Attributes );

		if ( !string.IsNullOrWhiteSpace( displayInfo.ClassName ) )
		{
			TypeLibrary.OnClassName?.Invoke( displayInfo.ClassName );
		}

		Identity = GetIdentityHash();
	}

	/// <summary>
	/// Generate a unique hash to identity this member.
	/// </summary>
	/// <returns></returns>
	protected virtual int GetIdentityHash()
	{
		return $"{TypeDescription.FullName}.{Name}".FastHash();
	}

	public override string ToString() => MemberInfo.ToString();

	protected void CaptureAttributes( MemberInfo member )
	{
		HashSet<string> tags = null;
		List<Attribute> attrList = null;

		SourceLine = 0;
		SourceFile = null;

		// TODO: why not inherit? we lose [RequireComponentAttribute] from base classes
		var attributes = member.GetCustomAttributes( false );
		foreach ( var attr in attributes )
		{
			if ( attr is IMemberAttribute ma )
			{
				ma.MemberDescription = this;
			}

			if ( attr is TagAttribute tag )
			{
				foreach ( var n in tag.EnumerateValues() )
				{
					tags ??= new();
					tags.Add( n.ToLower() );
				}
			}

			if ( attr is SourceLocationAttribute location && SourceFile is null )
			{
				SourceLine = location.Line;
				SourceFile = location.Path;
			}

			attrList ??= new( attributes.Length );
			attrList.Add( attr as Attribute );
		}

		Tags = tags?.ToArray() ?? Array.Empty<string>();
		Attributes = attrList?.ToArray() ?? Array.Empty<Attribute>();
	}

	/// <summary>
	/// Utility function to check whether this string matches this type. Will search name and classname.
	/// </summary>
	public bool IsNamed( string name )
	{
		if ( string.Equals( name, Name, StringComparison.OrdinalIgnoreCase ) )
			return true;

		if ( displayInfo.ClassName != null && string.Equals( name, displayInfo.ClassName, StringComparison.OrdinalIgnoreCase ) )
			return true;

		if ( displayInfo.Fullname != null && string.Equals( name, displayInfo.Fullname, StringComparison.OrdinalIgnoreCase ) )
			return true;

		return false;
	}

	/// <summary>
	/// Returns true if Tags contains this tag
	/// </summary>
	public bool HasTag( string tag ) => Tags.Contains( tag );

	/// <summary>
	/// TODO - create MethodDescription?
	/// </summary>
	internal static MemberDescription Create( TypeDescription td, MemberInfo member, MemberDescription previous )
	{
		return member switch
		{
			MethodInfo mi => MethodDescription.Create( mi, td, previous ),
			PropertyInfo pi => PropertyDescription.Create( pi, td, previous ),
			FieldInfo fi => FieldDescription.Create( fi, td, previous ),
			_ => null //new MemberDescription( member ) { TypeDescription = td };
		};
	}

	/// <summary>
	/// Whether or not this has at least one of the specified attribute.
	/// </summary>
	public bool HasAttribute<T>() where T : System.Attribute
	{
		return Attributes?.OfType<T>().Any() ?? false;
	}

	/// <summary>
	/// Whether or not this has at least one of the specified attribute.
	/// </summary>
	public bool HasAttribute( Type t )
	{
		return Attributes?.Any( x => t.IsAssignableFrom( x.GetType() ) ) ?? false;
	}

	/// <summary>
	/// Returns the first of Attributes of the passed in type. Or null.
	/// </summary>
	public T GetCustomAttribute<T>() where T : System.Attribute
	{
		return Attributes?.OfType<T>().FirstOrDefault() ?? null;
	}

	internal virtual void Dispose()
	{
		MemberInfo = null;
		Attributes = null;
	}
}
