namespace Sandbox;

internal static partial class ConVarSystem
{
	/// <summary>
	/// Called from native as a result of calling RefreshNativeVariables
	/// </summary>
	internal static void RegisterNativeVar( NativeEngine.ConVar value )
	{
		var command = new NativeConVar( value );
		AddCommand( command );
	}

	/// <summary>
	/// Called from native as a result of calling RefreshNativeVariables
	/// </summary>
	internal static void RegisterNativeCommand( NativeEngine.ConCommand value )
	{
		var command = new NativeCommand( value );
		AddCommand( command );
	}

	internal static void ClearNativeCommands()
	{
		if ( Members.Count == 0 )
			return;

		System.Collections.Generic.List<string> nativeKeys = null;

		foreach ( var (name, command) in Members )
		{
			if ( command is NativeCommand || command is NativeConVar )
			{
				nativeKeys ??= new System.Collections.Generic.List<string>();
				nativeKeys.Add( name );
			}
		}

		if ( nativeKeys is null )
			return;

		foreach ( var name in nativeKeys )
		{
			Members.Remove( name );
		}
	}
}


file class NativeCommand : Command
{
	NativeEngine.ConCommand _native;

	public NativeCommand( NativeEngine.ConCommand command )
	{
		_native = command;
		IsConCommand = true;
		Name = _native.GetName();
		Help = _native.GetHelpText();
		IsProtected = true; // game code can't run ANY native commands
	}

	public override void Run( string args )
	{
		_native.Run( $"{Name} {args}\n" );
	}
}

file class NativeConVar : Command
{
	NativeEngine.ConVar _native;

	public NativeConVar( NativeEngine.ConVar command )
	{
		_native = command;
		IsConCommand = false;
		Name = _native.GetName();
		Help = _native.GetHelpText();
		IsSaved = _native.GetFlags().Contains( ConVarFlags_t.FCVAR_ARCHIVE );
		IsReplicated = _native.GetFlags().Contains( ConVarFlags_t.FCVAR_REPLICATED );
		IsHidden = _native.GetFlags().Contains( ConVarFlags_t.FCVAR_HIDDEN );
		IsCheat = _native.GetFlags().Contains( ConVarFlags_t.FCVAR_CHEAT );
		IsProtected = true; // game code can't run ANY native commands

		if ( _native.HasMin() ) MinValue = _native.GetMinValue();
		if ( _native.HasMax() ) MaxValue = _native.GetMaxValue();
	}

	public override void Run( string args )
	{
		if ( args is null )
			return;

		Value = args;
	}

	public override string Value
	{
		get => _native.GetString();
		set
		{
			var oldValue = Value;
			if ( oldValue == value ) return;

			_native.SetValue( value );
		}
	}

	public override string DefaultValue => _native.GetDefault();
}

