public static partial class ServerSettings
{
	public const string DefaultGroup = "Other";

	/// <summary>
	/// Puts a server setting under a heading on its page.
	/// </summary>
	[AttributeUsage( AttributeTargets.Property )]
	public sealed class SectionAttribute( string name ) : Attribute
	{
		public string Name { get; } = name;
	}

	public record Setting( PropertyDescription Property, ConVarAttribute ConVar )
	{
		public string Name => ConVar.Name ?? Property.Name;
		public string Group => string.IsNullOrEmpty( Property.Group ) ? DefaultGroup : Property.Group;

		/// <summary>
		/// Uses the phrase server.name if there is one, otherwise the [Title].
		/// </summary>
		public string Title => PhraseOr( $"server.{ShortName}", Property.Title ?? Name );

		public string Description => PhraseOr( $"server.{ShortName}.description", ConVar.Help ?? Property.Description );

		public string Section => Property.GetCustomAttribute<SectionAttribute>()?.Name;

		string ShortName => Name.StartsWith( "sandbox." ) ? Name["sandbox.".Length..] : Name;
	}

	static IReadOnlyList<Setting> _all;
	static Scene _scannedFor;

	public static IReadOnlyList<Setting> All
	{
		get
		{
			if ( _all is null || _scannedFor != Game.ActiveScene )
			{
				_all = FindConVars( ConVarFlags.Replicated | ConVarFlags.Server | ConVarFlags.GameSetting );
				_scannedFor = Game.ActiveScene;
			}

			return _all;
		}
	}

	/// <summary>
	/// One page per group, Gameplay first and Other last.
	/// </summary>
	public static IEnumerable<string> Groups => All
		.Select( x => x.Group )
		.Distinct()
		.OrderBy( x => x == "Gameplay" ? 0 : x == DefaultGroup ? 2 : 1 )
		.ThenBy( x => LocalizedText.Resolve( GroupTitle( x ) ), StringComparer.CurrentCultureIgnoreCase );

	public static string GroupTitle( string group ) => PhraseOr( $"server.group.{group.ToLowerInvariant()}", group );

	public static bool CanEdit => Permissions.Has( Connection.Local, Permissions.Admin );

	/// <summary>
	/// Changes a replicated convar through the host. Only works for admins.
	/// </summary>
	public static void Set( string name, object value ) => RpcSet( name, value );

	// The host turns the value into text itself, so "0,5" and "0.5" can't get mixed up
	[Rpc.Host]
	static void RpcSet( string name, object value )
	{
		if ( !Permissions.Has( Rpc.Caller, Permissions.Admin ) ) return;

		// only replicated game convars, not personal or engine ones
		var setting = FindConVars( ConVarFlags.Replicated ).FirstOrDefault( x => x.Name == name );
		if ( setting is null ) return;

		var e = new AdminActionEvent { Action = AdminAction.ChangeSetting, Setting = name, Value = value?.ToString() };
		if ( !e.Run() ) return;

		var previous = ConsoleSystem.GetValue( name );
		ConsoleSystem.SetValue( name, value );

		var current = ConsoleSystem.GetValue( name );
		if ( current == previous ) return;

		e.Done();

		var shown = bool.TryParse( current, out var on ) ? (on ? "#spawnmenu.common.on" : "#spawnmenu.common.off") : LocalizedText.Literal( current );

		GameManager.Current?.NotifyLocalized( "#server.setting_changed", new()
		{
			{ "name", setting.Title.StartsWith( '#' ) ? setting.Title : LocalizedText.Literal( setting.Title ) },
			{ "value", shown }
		} );
	}

	public static List<SerializedProperty> CreateRows( string group )
	{
		return All.Where( x => x.Group == group )
			.OrderBy( x => x.Property.Order )
			.Select( CreateRow )
			.Where( x => x is not null )
			.ToList();
	}

	static SerializedProperty CreateRow( Setting setting )
	{
		var attributes = new List<Attribute> { new PropertyAttribute(), new DescriptionAttribute( setting.Description ), new OrderAttribute( setting.Property.Order ), new ApplyOnReleaseAttribute() };
		if ( setting.Property.GetCustomAttribute<RangeAttribute>() is { } range ) attributes.Add( range );
		if ( setting.Property.GetCustomAttribute<StepAttribute>() is { } step ) attributes.Add( step );

		// the sheet draws a heading for each group
		if ( setting.Section is { } section ) attributes.Add( new GroupAttribute( PhraseOr( $"server.section.{section.ToLowerInvariant()}", section ) ) );

		var type = setting.Property.PropertyType;
		var title = setting.Title;
		var name = setting.Name;
		var property = setting.Property;

		if ( type == typeof( bool ) )
			return Game.TypeLibrary.CreateProperty<bool>( title, () => (bool)property.GetValue( null ), v => Set( name, v ), attributes.ToArray() );

		if ( type == typeof( int ) )
		{
			if ( !attributes.OfType<StepAttribute>().Any() ) attributes.Add( new StepAttribute( 1 ) );
			return Game.TypeLibrary.CreateProperty<int>( title, () => (int)property.GetValue( null ), v => Set( name, v ), attributes.ToArray() );
		}

		if ( type == typeof( float ) )
			return Game.TypeLibrary.CreateProperty<float>( title, () => (float)property.GetValue( null ), v => Set( name, v ), attributes.ToArray() );

		if ( type == typeof( string ) )
			return Game.TypeLibrary.CreateProperty<string>( title, () => (string)property.GetValue( null ), v => Set( name, v ), attributes.ToArray() );

		// the sheet shows a dropdown, the convar takes the value's name
		if ( type.IsEnum )
		{
			var row = Game.TypeLibrary.GetType( typeof( EnumRow<> ) ).CreateGeneric<IEnumRow>( [type] );
			return row.Create( title, () => property.GetValue( null ), v => Set( name, v.ToString() ), attributes.ToArray() );
		}

		return null;
	}

	interface IEnumRow
	{
		SerializedProperty Create( string title, Func<object> get, Action<object> set, Attribute[] attributes );
	}

	/// <summary>
	/// Rows are made with a generic call, so enums get one of these for their type.
	/// </summary>
	sealed class EnumRow<T> : IEnumRow where T : struct, Enum
	{
		public SerializedProperty Create( string title, Func<object> get, Action<object> set, Attribute[] attributes )
		{
			return Game.TypeLibrary.CreateProperty<T>( title, () => (T)get(), v => set( v ), attributes );
		}
	}

	static List<Setting> FindConVars( ConVarFlags flags )
	{
		return Game.TypeLibrary.GetTypes()
			.SelectMany( type => type.Properties )
			.Where( p => p.IsStatic )
			.Select( p => new Setting( p, p.GetCustomAttribute<ConVarAttribute>() ) )
			.Where( x => x.ConVar is not null && (x.ConVar.Flags & flags) == flags && (x.ConVar.Flags & ConVarFlags.Protected) == 0 )
			.DistinctBy( x => x.Name )
			.ToList();
	}

	static string PhraseOr( string phrase, string fallback ) => Game.Language.GetPhrase( phrase ) != phrase ? $"#{phrase}" : fallback;
}
