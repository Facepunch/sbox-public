using Sandbox.Internal;
using System.Text.RegularExpressions;

namespace Sandbox
{
	/// <summary>
	/// Collects all the relevant info (such as description, name, icon, etc) from attributes and other sources about a type or type member.
	/// </summary>
	public partial struct DisplayInfo : ITitleProvider, IDescriptionProvider, ICategoryProvider, IIconProvider, IOrderProvider
	{
		/// <summary>
		/// "Internal" class name of this type or member. This typically should be all lowercase and without weird symbols or whitespace.
		/// </summary>
		public string ClassName;

		/// <summary>
		/// Namespace of this type
		/// </summary>
		public string Namespace;

		/// <summary>
		/// Namespace.ParentClass.Class.Member
		/// </summary>
		public string Fullname;

		/// <summary>
		/// The name of this type or member.
		/// </summary>
		public string Name;

		/// <summary>
		/// The summary or description of this type or member.
		/// </summary>
		public string Description;

		/// <summary>
		/// Group or category of this type or member. (<see cref="CategoryAttribute"/>)
		/// </summary>
		public string Group;

		/// <summary>
		/// This is marked as ReadOnly
		/// </summary>
		public bool ReadOnly;

		/// <summary>
		/// Material icon of this type or member. (<see cref="IconAttribute"/>)
		/// </summary>
		public string Icon;

		/// <summary>
		/// Order of this member for UI ordering purposes. (<see cref="OrderAttribute"/>)
		/// </summary>
		public int Order;

		/// <summary>
		/// Whether this member should be visible in a properties sheet (<see cref="HideInEditorAttribute"/>)
		/// </summary>
		public bool Browsable;

		/// <summary>
		/// Placeholder text for string type properties. (<see cref="PlaceholderAttribute"/>)
		/// Placeholder text is displayed in UI when input text field is empty.
		/// </summary>
		public string Placeholder;

		/// <summary>
		/// Possible aliases for this type or member, if any. (<see cref="AliasAttribute"/>)
		/// </summary>
		public string[] Alias;

		/// <summary>
		/// Tags of this type or member. (<see cref="TagAttribute"/>)
		/// </summary>
		public string[] Tags;

		/// <summary>
		/// Returns whether this type or member has given tag. (<see cref="TagAttribute"/>)
		/// </summary>
		/// <param name="t">The tag to test.</param>
		/// <returns>Whether the tag is present or not</returns>
		public bool HasTag( string t ) => Tags?.Contains( t ) ?? false;

		/// <summary>
		/// Retrieves display info about a given type.
		/// </summary>
		/// <param name="t">The type to look up display info for.</param>
		/// <param name="inherit">Whether to load in base type's display info first, then overrides all possible fields with given type's information.</param>
		/// <returns>The display info. Will contain empty fields on failure.</returns>
		public static DisplayInfo ForType( System.Type t, bool inherit = true ) => ForMember( t, inherit );

		/// <summary>
		/// Retrieves display info about a given objects type.
		/// </summary>
		/// <param name="t">The type to look up display info for.</param>
		/// <param name="inherit">Whether to load in base type's display info first, then overrides all possible fields with given type's information.</param>
		/// <returns>The display info. Will contain empty fields on failure.</returns>
		public static DisplayInfo For( object t, bool inherit = true ) => ForMember( t?.GetType(), inherit );

		/// <summary>
		/// Retrieves display info about a given member or type.
		/// </summary>
		/// <param name="t">The member to look up display info for.</param>
		/// <param name="inherit">If member given is a <see cref="System.Type"/>, loads in base type's display info first, then overrides all possible fields with given type's information.</param>
		/// <returns>The display info. Will contain empty fields on failure.</returns>
		public static DisplayInfo ForMember( MemberInfo t, bool inherit = true ) => ForMember( t, inherit, null );

		private static Dictionary<Type, string> SystemTypeAliases { get; } = new()
		{
			{ typeof(bool), "bool" },
			{ typeof(byte), "byte" },
			{ typeof(char), "char" },
			{ typeof(decimal), "decimal" },
			{ typeof(double), "double" },
			{ typeof(float), "float" },
			{ typeof(int), "int" },
			{ typeof(long), "long" },
			{ typeof(object), "object" },
			{ typeof(sbyte), "sbyte" },
			{ typeof(short), "short" },
			{ typeof(string), "string" },
			{ typeof(uint), "uint" },
			{ typeof(ulong), "ulong" },
			{ typeof(ushort), "ushort" },
			{ typeof(void), "void" }
		};

		private static Dictionary<Type, string> SystemTypeIcons { get; } = new()
		{
			{ typeof(string), "format_quote" }
		};

		internal static DisplayInfo ForMember( MemberInfo t, bool inherit, IEnumerable<System.Attribute> cachedAttributes )
		{
			if ( t == null )
				return default;

			DisplayInfo info = default;

			if ( inherit && t is System.Type type && type.BaseType != null )
			{
				info = ForMember( type.BaseType, true );
			}

			// Inherit member descriptions from interfaces
			if ( inherit && t is not System.Type && t.DeclaringType != null )
			{
				var member = t.DeclaringType.GetInterfaces().SelectMany( x => x.GetMember( t.Name, t.MemberType, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public ) ).FirstOrDefault();
				if ( member != null ) info = ForMember( member, true );
			}

			HashSet<string> aliases = null;
			HashSet<string> tags = null;

			var name = t.Name;
			var backQuoteIndex = name.IndexOf( '`' );

			if ( backQuoteIndex != -1 )
			{
				name = name[..backQuoteIndex];
			}

			if ( t is Type { IsInterface: true } && IsInterfaceTypeName( name ) )
			{
				name = name[1..];
			}

			if ( t is MethodInfo { DeclaringType.IsInterface: true } && IsEventMethodName( name ) )
			{
				info.Icon = "bolt";
			}

			info.Name = name.ToTitleCase();
			info.ClassName = t.Name;
			info.Browsable = true;
			info.Alias = Array.Empty<string>();

			if ( t is System.Type infoType )
			{
				if ( SystemTypeAliases.TryGetValue( infoType, out var alias ) )
				{
					info.Name = alias;
				}

				if ( SystemTypeIcons.TryGetValue( infoType, out var icon ) )
				{
					info.Icon = icon;
				}

				info.Namespace = infoType.Namespace;
				info.Fullname = infoType.FullName;
			}

			if ( cachedAttributes == null )
				cachedAttributes = t.GetCustomAttributes( false ).Cast<System.Attribute>();

			foreach ( var attribute in cachedAttributes )
			{
				if ( attribute is System.ComponentModel.DataAnnotations.DisplayAttribute display )
				{
					if ( !string.IsNullOrWhiteSpace( display.Name ) ) info.Name = display.Name;
					if ( !string.IsNullOrWhiteSpace( display.GroupName ) ) info.Group = display.GroupName;
					if ( !string.IsNullOrWhiteSpace( display.Description ) ) info.Description = display.Description;
					if ( !string.IsNullOrWhiteSpace( display.ShortName ) ) info.Icon ??= display.ShortName;

					info.Order = display.GetOrder() ?? info.Order;
				}

				if ( attribute is AliasAttribute aa )
				{
					foreach ( var n in aa.Value )
					{
						aliases ??= new();
						aliases.Add( n.ToLower() );
					}
				}

				if ( attribute is TagAttribute ta )
				{
					foreach ( var n in ta.EnumerateValues() )
					{
						tags ??= new();
						tags.Add( n.ToLower() );
					}
				}

				if ( attribute is ITitleProvider titleProvider ) info.Name = titleProvider.Value ?? info.Name;
				if ( attribute is IClassNameProvider classnameProvider ) info.ClassName = classnameProvider.Value ?? info.ClassName;
				if ( attribute is IDescriptionProvider descriptionProvider ) info.Description = descriptionProvider.Value ?? info.Description;
				if ( attribute is IPlaceholderProvider placeholderProvider ) info.Placeholder = placeholderProvider.Value ?? info.Placeholder;
				if ( attribute is ICategoryProvider categoryProvider ) info.Group = categoryProvider.Value ?? info.Group;
				if ( attribute is HideAttribute ) info.Browsable = false;
				if ( attribute is System.ComponentModel.CategoryAttribute categoryAttribute ) info.Group = categoryAttribute.Category ?? info.Group;
				if ( attribute is IIconProvider iconAttribute ) info.Icon = iconAttribute.Value ?? info.Icon;
				if ( attribute is ReadOnlyAttribute ) info.ReadOnly = true;
				if ( attribute is OrderAttribute o ) info.Order = o.Value;
			}

			if ( aliases != null )
			{
				info.Alias = aliases.ToArray();
			}

			if ( tags != null )
			{
				info.Tags = tags.ToArray();
			}

			return info;
		}

		/// <summary>
		/// Returns display info for each member of an enumeration type.
		/// </summary>
		public static DisplayInfo[] ForEnumValues( System.Type t )
		{
			//
			// TODO: cache me daddy
			//

			var names = t.GetEnumNames();
			return names.Select( x => ForMember( t.GetMember( x, BindingFlags.Public | BindingFlags.Static )
				.First( m => m.MemberType == MemberTypes.Field ) ) )
				.ToArray();
		}

		/// <summary>
		/// Returns display info for each member of an enumeration type.
		/// </summary>
		public static (T value, DisplayInfo info)[] ForEnumValues<T>() where T : Enum
		{
			//
			// TODO: cache me daddy
			//

			var t = typeof( T );
			var names = t.GetEnumNames();
			var values = t.GetEnumValues();

			return Enumerable.Range( 0, names.Length )
				.Select<int, (T value, DisplayInfo info)>( i => new( (T)values.GetValue( i ), ForMember( typeof( T )
				.GetMember( names[i], BindingFlags.Public | BindingFlags.Static )
				.First( m => m.MemberType == MemberTypes.Field ) ) ) )
				.ToArray();
		}

		[GeneratedRegex( @"^I[A-Z]" )]
		private static partial Regex InterfaceTypeNameRegex();

		[GeneratedRegex( @"^On[A-Z]" )]
		private static partial Regex EventMethodNameRegex();

		private static bool IsInterfaceTypeName( string name )
		{
			return InterfaceTypeNameRegex().IsMatch( name );
		}

		private static bool IsEventMethodName( string name )
		{
			return EventMethodNameRegex().IsMatch( name );
		}

		readonly string ITitleProvider.Value => Name;

		readonly string IDescriptionProvider.Value => Description;

		readonly string ICategoryProvider.Value => Group;

		readonly string IIconProvider.Value => Icon;

		readonly int IOrderProvider.Value => Order;
	}
}
