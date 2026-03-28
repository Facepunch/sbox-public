using Sandbox.Network;
using System.Reflection;

namespace Sandbox;

internal static partial class ConVarSystem
{
	public delegate void ConVarChangedDelegate( Command command, string oldValue );

	/// <summary>
	/// Called when the ConVar is changed.
	/// </summary>
	public static ConVarChangedDelegate ConVarChanged { get; set; }

	internal static readonly Dictionary<string, Command> Members = new( StringComparer.OrdinalIgnoreCase );
	internal static readonly Dictionary<string, CookieContainer> CookieContainers = new( StringComparer.OrdinalIgnoreCase );

	static CookieContainer GetCookies( string name )
	{
		if ( CookieContainers.TryGetValue( name, out var cookies ) ) return cookies;

		cookies = new( $"convar/{name}" );
		CookieContainers[name] = cookies;
		return cookies;
	}

	/// <summary>
	/// Add this assembly to the console library, which will scan it for console commands and make them available.
	/// </summary>
	internal static void AddAssembly( Assembly assembly, string cookies, string context = null )
	{
		if ( assembly == null )
			return;

		foreach ( var t in assembly.GetTypes() )
		{
			var methods = t.GetMembers( BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public );
			foreach ( var member in methods )
			{
				var attribute = member.GetCustomAttribute<ConVarAttribute>();
				if ( attribute == null ) continue;
				if ( attribute.Context != context ) continue;

				try
				{
					if ( member is PropertyInfo prop )
					{
						var get = prop.GetGetMethod( true );
						var set = prop.GetSetMethod( true );

						if ( get == null || set == null )
							continue;

						if ( !get.IsStatic )
							continue;

						AddConVar( new ManagedCommand( assembly, member, attribute, GetCookies( cookies ) ) );
					}

					if ( member is MethodInfo method )
					{
						if ( !method.IsStatic )
							continue;

						AddCommand( new ManagedCommand( assembly, member, attribute, GetCookies( cookies ) ) );
					}
				}
				catch ( Exception e )
				{
					Log.Error( e );
				}
			}
		}
	}

	/// <summary>
	/// Remove this assembly and its console commands.
	/// </summary>
	internal static void RemoveAssembly( Assembly assembly )
	{
		if ( assembly == null )
			return;

		foreach ( var (name, member) in Members.Where( x => x.Value.IsFromAssembly( assembly ) ).ToArray() )
		{
			RememberValue( member );
			Members.Remove( name );
		}
	}

	/// <summary>
	/// Add this command to the library. Any existing commands named the same will be over-written.
	/// </summary>
	internal static void AddCommand( Command command )
	{
		if ( Members.TryAdd( command.Name, command ) )
			return;

		Log.Warning( $"Command {command.Name} already exists - not overwriting ({Members[command.Name].Help})" );
	}

	/// <summary>
	/// Add this ConVar to the library. Any existing commands named the same will be over-written.
	/// </summary>
	internal static void AddConVar( Command command )
	{
		if ( !Members.TryAdd( command.Name, command ) )
		{
			Log.Warning( $"Convar {command.Name} already exists - not overwriting" );
			return;
		}

		if ( command.MinValue.HasValue && command.MaxValue.HasValue && command.MinValue > command.MaxValue )
			(command.MinValue, command.MaxValue) = (command.MaxValue, command.MinValue);

		// Load it from cookies
		if ( command.TryLoad( out var loadedValue ) )
		{
			command.Value = loadedValue;
		}

		// Set it from command line
		command.SetVariableFromCommandLine();

		// This is a convar that was added before, maybe a hotload, set it
		if ( TryGetRememberedValue( command.Name, out var rememberedValue ) )
		{
			command.Value = rememberedValue;
		}
	}

	internal static Command Find( string name )
	{
		return Members.GetValueOrDefault( name );
	}

	//
	// We want to remember ConVar values between hotloads
	//
	static readonly Dictionary<string, string> RememberedConVarValues = new();

	private static void RememberValue( Command var )
	{
		if ( !var.IsVariable ) return;

		var value = GetValue( var.Name, null, false );
		if ( value == null ) return;

		RememberedConVarValues[var.Name] = value;
	}

	private static bool TryGetRememberedValue( string member, out string val )
	{
		return RememberedConVarValues.TryGetValue( member, out val );
	}

	internal static void ClearRememberedValues()
	{
		RememberedConVarValues.Clear();
	}

	/// <summary>
	/// Get a ConVar value as a string.
	/// </summary>
	public static string GetValue( string name, string defaultValue, bool allowEngineVariable )
	{
		var cmd = Find( name );

		if ( cmd is null ) return defaultValue;
		if ( !cmd.IsVariable ) return defaultValue;
		if ( !allowEngineVariable && cmd.IsProtected ) return defaultValue;

		return cmd.Value;
	}

	/// <summary>
	/// Get a ConVar value as an integer. If the ConVar value is a boolean, this will return
	/// the value in its integer form.
	/// </summary>
	public static int GetInt( string name, int defaultValue, bool allowEngineVariable )
	{
		var value = GetValue( name, defaultValue.ToString(), allowEngineVariable );

		if ( bool.TryParse( value, out var boolean ) )
			return boolean ? 1 : 0;

		return int.TryParse( value, out var integer ) ? integer : defaultValue;
	}

	/// <summary>
	/// Get a ConVar value as a float.
	/// </summary>
	public static float GetFloat( string name, float defaultValue, bool allowEngineVariable )
	{
		return float.TryParse( GetValue( name, defaultValue.ToString(), allowEngineVariable ), out var value ) ? value : defaultValue;
	}

	/// <summary>
	/// Try to set a ConVar. You will only be able to set variables that you have permission to set.
	/// </summary>
	public static void SetValue( string name, string value, bool allowProtected )
	{
		var cmd = Find( name );

		if ( cmd is null ) return;
		if ( !cmd.IsVariable ) return;
		if ( cmd.IsProtected && !allowProtected ) return;

		cmd.Value = value;

		// Let the ConVar save itself, if it wants to
		cmd.Save();
	}

	/// <summary>
	/// Try to set a ConVar. You will only be able to set variables that you have permission to set.
	/// </summary>
	public static void SetInt( string name, int value, bool allowProtected )
	{
		SetValue( name, value.ToString(), allowProtected );
	}

	/// <summary>
	/// Try to set a ConVar. You will only be able to set variables that you have permission to set.
	/// </summary>
	public static void SetFloat( string name, float value, bool allowProtected )
	{
		SetValue( name, value.ToString(), allowProtected );
	}

	/// <summary>
	/// Save all the convars.
	/// </summary>
	[ConCmd( "convars_save", ConVarFlags.Protected )]
	public static void SaveAll()
	{
		foreach ( var convar in Members.Values )
		{
			if ( convar.IsConCommand ) continue;
			if ( !convar.IsSaved ) continue;

			convar.Save();
		}

		foreach ( var container in CookieContainers.Values )
		{
			container.Save();
		}
	}

	/// <summary>
	/// Run a single command. [command] [args]
	/// </summary>
	internal static void RunSingle( string v, bool allowProtected = true )
	{
		var parts = v.Split( ' ', 2, StringSplitOptions.RemoveEmptyEntries );

		if ( !Members.TryGetValue( parts[0], out var command ) )
		{
			Log.Warning( $"Unknown Command '{parts[0]}'" );
			return;
		}

		if ( !allowProtected && command.IsProtected )
		{
			Log.Warning( $"Can't run protected command '{command.Name}'" );
			return;
		}

		var hasArguments = parts.Length > 1;

		if ( !hasArguments && command.IsVariable )
		{
			Log.Info( $"{command.Name} - {command.BuildDescription()}" );
			return;
		}

		if ( command.IsCheat && !Game.CheatsEnabled )
		{
			Log.Info( "Cheats are not enabled on the server." );
			return;
		}

		var args = string.Join( " ", parts.Skip( 1 ) );

		if ( command.IsVariable )
		{
			command.Value = args.SplitQuotesStrings()[0];
			return;
		}

		if ( Networking.IsActive && !Networking.IsHost && (command.IsServer || command.IsAdmin) )
		{
			var msg = new ServerCommand { Command = command.Name, Args = args };
			Connection.Host?.SendMessage( msg, NetFlags.Reliable );
			return;
		}

		command.Run( args );
	}

	/// <summary>
	/// Run a potential string of commands, separated by newlines or ;
	/// </summary>
	internal static void Run( string v, bool allowProtected = true )
	{
		ThreadSafe.AssertIsMainThread();

		if ( string.IsNullOrWhiteSpace( v ) ) return;

		foreach ( var part in SplitCommands( v ) )
		{
			if ( string.IsNullOrWhiteSpace( part ) ) continue;

			RunSingle( part, allowProtected );
		}
	}

	/// <summary>
	/// Split a command string on ';' and '\n', but respect quoted sections.
	/// </summary>
	internal static IEnumerable<string> SplitCommands( string input )
	{
		var inQuotes = false;
		int start = 0;

		for ( var i = 0; i < input.Length; i++ )
		{
			var c = input[i];

			if ( c == '\\' && i + 1 < input.Length )
			{
				i++;
				continue;
			}

			if ( c == '"' )
			{
				inQuotes = !inQuotes;
				continue;
			}

			if ( inQuotes || (c != ';' && c != '\n') )
				continue;

			yield return input.Substring( start, i - start );
			start = i + 1;
		}

		if ( start < input.Length )
		{
			yield return input[start..];
		}
	}

	/// <summary>
	/// Should be called any time a ConVar is changed. This should be called from two places, the managed side and the native side.
	/// Managed side comes from when the property value actually changes. The native side comes from a on change callback.
	/// CAVEAT: This will only get called for ConVars that use CODEGEN!
	/// </summary>
	internal static void OnConVarChanged<T>( string name, T value, T previous )
	{
		if ( Members.TryGetValue( name, out var command ) )
		{
			MainThread.Queue( () => InvokeConVarChanged( command, previous?.ToString() ) );
		}
	}

	internal static void InvokeConVarChanged( Command command, string oldValue )
	{
		if ( command.IsUserInfo && Networking.IsActive )
		{
			if ( !Networking.IsHost )
			{
				var msg = new UserInfoUpdate { Command = command.Name, Value = command.Value };
				Connection.Host?.SendMessage( msg, NetFlags.Reliable );
			}
			else
			{
				Connection.Local?.SetUserData( command.Name, command.Value );
			}
		}

		ConVarChanged?.Invoke( command, oldValue );
	}
}
