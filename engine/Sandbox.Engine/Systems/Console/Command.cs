using Sandbox.Engine;
using Sandbox.Utility;
using System.Reflection;

namespace Sandbox;


partial class Command
{
	internal static Connection Caller { get; set; }

	public string Name { get; set; }
	public string Help { get; set; }

	public bool IsConCommand { get; set; }
	public bool IsVariable => !IsConCommand;

	/// <summary>
	/// Saved into config file
	/// </summary>
	public bool IsSaved { get; set; }

	/// <summary>
	/// If true then this cannot be interacted with via game code
	/// </summary>
	public bool IsProtected { get; set; }

	/// <summary>
	/// If true then this command can only be run on the server
	/// </summary>
	public bool IsServer { get; set; }

	/// <summary>
	/// If true then this command can only be run by a server administrator
	/// </summary>
	public bool IsAdmin { get; set; }

	/// <summary>
	/// Server value is replicated to clients
	/// </summary>
	public bool IsReplicated { get; set; }

	/// <summary>
	/// Not visible in auto complete and find
	/// </summary>
	public bool IsHidden { get; set; }

	/// <summary>
	/// Client values are sent to the server, accessible via Connection userinfo
	/// </summary>
	public bool IsUserInfo { get; set; }

	/// <summary>
	/// Can't access unless sv_cheats is 1
	/// </summary>
	public bool IsCheat { get; set; }

	public float? MinValue { get; set; }
	public float? MaxValue { get; set; }

	public virtual string Value { get; set; }
	public virtual string DefaultValue => default;

	public virtual bool IsFromAssembly( Assembly assembly ) => false;

	public virtual void Run( string args ) { }

	public virtual void Save() { }

	public virtual bool TryLoad( out string loaded )
	{
		loaded = null;
		return false;
	}

	public virtual string BuildDescription()
	{
		if ( IsConCommand )
		{
			return Help; // todo - command line arguments auto
		}

		return $"\"{Value}\" - Default: \"{DefaultValue}\" - {Help}";
	}

	/// <summary>
	/// If we have a command line version of the command then set it and return true
	/// </summary>
	public bool SetVariableFromCommandLine()
	{
		if ( !IsVariable ) return false;

		var value = CommandLine.GetSwitch( "+" + Name, null );
		if ( value is null ) return false;

		Value = value.TrimQuoted();
		return true;
	}
}


partial class ManagedCommand : Command
{
	internal ConVarAttribute attribute;
	internal MemberInfo member;
	internal Assembly assembly;
	CookieContainer cookies;
	bool _isMenu;

	internal ParameterInfo[] parameters = [];

	private string _defaultValue;
	public override string DefaultValue => _defaultValue;

	public ManagedCommand( Assembly assembly, MemberInfo member, ConVarAttribute attribute, CookieContainer cookies = null )
	{
		this.attribute = attribute;
		this.assembly = assembly;
		this.member = member;
		this.cookies = cookies;

		if ( member is MethodInfo method )
		{
			parameters = method.GetParameters();
		}

		Name = attribute.Name;
		Help = attribute.Help ?? "No description";
		IsConCommand = attribute is ConCmdAttribute;
		IsHidden = attribute.Flags.Contains( ConVarFlags.Hidden );
		IsServer = attribute.Flags.Contains( ConVarFlags.Server );
		IsAdmin = attribute.Flags.Contains( ConVarFlags.Admin );
		IsProtected = attribute.Flags.Contains( ConVarFlags.Protected );
		IsCheat = attribute.Flags.Contains( ConVarFlags.Cheat );
		_isMenu = attribute.Context == "menu";

		//
		// Set up default value if we have one
		//
		var type = member is PropertyInfo pi ? pi.PropertyType : member.DeclaringType;

		var defaultValue = member.GetCustomAttribute<DefaultValueAttribute>()?.Value;
		_defaultValue = defaultValue?.ToString()
			?? (type.IsValueType ? Activator.CreateInstance( type )?.ToString() : null);

		if ( attribute is ConVarAttribute cv )
		{
			IsSaved = cv.Saved;
			MinValue = cv.MinValue;
			MaxValue = cv.MaxValue;
			IsReplicated = cv.Flags.Contains( ConVarFlags.Replicated );
			IsUserInfo = cv.Flags.Contains( ConVarFlags.UserInfo );
		}

		if ( string.IsNullOrWhiteSpace( Name ) )
		{
			Name = member.Name;
		}

		// Names should be ascii letter digits only, definitely no spaces, semicolons or quotes
		if ( !Name.All( x => char.IsAsciiLetterOrDigit( x ) || x == '_' || x == '.' || x == '-' ) )
		{
			throw new ArgumentException( $"Console name \"{Name}\" is invalid, it should only contain ascii letters, digits or underscores." );
		}

		if ( string.IsNullOrWhiteSpace( Help ) )
		{
			var info = DisplayInfo.ForMember( member );
			Help = info.Description;
		}
	}

	public override void Run( string argstring )
	{
		using var contextLocal = _isMenu ? GlobalContext.MenuScope() : GlobalContext.GameScope();
		using var scope = _isMenu ? IMenuDll.Current?.PushScope() : IGameInstanceDll.Current?.PushScope();

		var caller = Caller ?? Connection.Local;

		if ( IsAdmin && !caller.IsHost )
		{
			caller.SendLog( LogLevel.Warn, "You are not allowed to run this command." );
			return;
		}

		//
		// Console command
		// 
		if ( member is MethodInfo method )
		{
			if ( method == null )
				throw new Exception( "ConsoleCommand is not a Method" );

			var callargs = new object[parameters.Length];
			var args = argstring.SplitQuotesStrings();
			var argsCount = args?.Length ?? 0;
			var parameterStartIndex = 0;
			var paramCount = parameters.Length;

			if ( paramCount > 0 && parameters[0].ParameterType == typeof( Connection ) )
			{
				parameterStartIndex = 1;
				paramCount--;
				callargs[0] = caller;
			}

			int argIndex = 0;
			for ( int i = parameterStartIndex; i < parameters.Length; i++ )
			{
				var param = parameters[i];

				if ( param.ParameterType.IsArray && param.GetCustomAttribute<ParamArrayAttribute>() != null )
				{
					var elemType = param.ParameterType.GetElementType();
					var srcValues = (args?.Skip( argIndex ) ?? Enumerable.Empty<string>())
						.Select( x => x.ToType( elemType ) )
						.ToArray();

					var paramsArray = Array.CreateInstance( elemType, srcValues.Length );

					Array.Copy( srcValues, paramsArray, srcValues.Length );

					callargs[i] = paramsArray;
					break;
				}

				if ( argIndex < argsCount )
				{
					callargs[i] = args[argIndex].ToType( param.ParameterType );
					argIndex++;
					continue;
				}

				if ( parameters[i].HasDefaultValue )
				{
					callargs[i] = param.DefaultValue;
					continue;
				}

				Log.Warning( $"Not enough arguments for command \"{Name}\"! Expected {paramCount}, got {argsCount}." );
				return;
			}

			try
			{
				method.Invoke( null, callargs );
				return;
			}
			catch ( Exception e )
			{
				Log.Error( e.InnerException, $"Exception when calling command \"{Name}\"" );
				return;
			}
		}

		//
		// Console variable
		//
		if ( member is PropertyInfo propertyInfo )
		{
			if ( argstring == null )
				return;

			Value = argstring;
			return;
		}

		return;
	}

	public override string Value
	{
		get
		{
			if ( member is PropertyInfo propertyInfo )
			{
				return propertyInfo.GetValue( null )?.ToString();
			}

			return null;
		}

		set
		{
			if ( member is not PropertyInfo propertyInfo )
			{
				Log.Warning( $"Setting {Name} and it's not a variable" );
				return;
			}

			var oldValue = propertyInfo.GetValue( null );
			var newValue = value.ToType( propertyInfo.PropertyType );

			if ( newValue is float f )
			{
				if ( MinValue.HasValue ) f = Math.Max( f, MinValue.Value );
				if ( MaxValue.HasValue ) f = Math.Min( f, MaxValue.Value );
				newValue = f;
			}
			else if ( newValue is int i )
			{
				if ( MinValue.HasValue ) i = Math.Max( i, (int)MinValue.Value );
				if ( MaxValue.HasValue ) i = Math.Min( i, (int)MaxValue.Value );
				newValue = i;
			}

			if ( object.Equals( newValue, oldValue ) )
				return;

			propertyInfo.SetValue( null, newValue );

			Save();
		}
	}

	public override bool IsFromAssembly( Assembly assembly ) => assembly == this.assembly;

	/// <summary>
	/// Todo: Add support for managed commands to return shit here
	/// Todo: We could maybe do this in a cool way, using parameters?
	///       So that for example, we could list players if it's a player etc
	/// </summary>
	public virtual string[] GetAutoComplete( string v )
	{
		return null;
	}

	public override bool TryLoad( out string loaded )
	{
		loaded = default;
		if ( !IsSaved ) return false;
		if ( cookies == null ) return false;

		return cookies.TryGetString( $"convar.{Name}", out loaded );
	}

	public override void Save()
	{
		if ( !IsSaved ) return;
		if ( cookies == null ) return;
		cookies.SetString( $"convar.{Name}", Value );
	}
}
