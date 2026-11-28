namespace Sandbox.Engine;

/// <summary>
/// 
/// A collection of action binds. 
/// 
///  BindCollection
///    - Action: attack1
///      - Slot0: mouse1
///    - Action: selectall
///      - Slot0: ctrl + a
///      
/// The bind collection can be saved and loaded from disk via the BindSaveConfig class.
/// 
/// The bind collection can have a base collection which it will fall back to if it contains
/// the same binds. This allows us to have a "common" collection which can be shared between
/// all games, but can also let the games + users to override those binds if they choose.
/// 
/// </summary>
public class BindCollection
{
	/// <summary>
	/// The base collection. Game binds have this set to the common binds.
	/// </summary>
	public BindCollection Base { get; set; }

	/// <summary>
	/// Will be either "common" or the ident of the current game.
	/// </summary>
	public string CollectionName { get; set; }

	/// <summary>
	/// The location of the config file to load from in EngineFileSystem.Config
	/// </summary>
	public string ConfigPath { get; set; }

	/// <summary>
	/// The actual collection of binds.
	/// </summary>
	public CaseInsensitiveDictionary<ActionBind> Actions = new CaseInsensitiveDictionary<ActionBind>();

	/// <summary>
	/// Creates a collection and tries to load it from disk.
	/// </summary>
	public BindCollection( string name )
	{
		CollectionName = name;
		ConfigPath = $"/input/{CollectionName}.json";

		FillDefaultCommonInputs();

		BindSaveConfig.Load( ConfigPath, this );
	}

	/// <summary>
	/// If we're the common collection and have no binds (because we haven't
	/// been able to load a customized config from disk) then we'll fill in
	/// the defaults based on Input.CommonInputs.
	/// </summary>
	void FillDefaultCommonInputs()
	{
		if ( CollectionName != "common" ) return;
		if ( Actions.Count != 0 ) return;

		foreach ( var input in Input.CommonInputs )
		{
			if ( !string.IsNullOrWhiteSpace( input.KeyboardCode ) )
			{
				var bind = GetBind( input.Name );
				bind.Set( 0, input.KeyboardCode );
				bind.Default = input.KeyboardCode;
			}
		}
	}

	/// <summary>
	/// Get the bind, create if it doesn't exist
	/// </summary>
	public ActionBind GetBind( string actionName, bool create = true )
	{
		if ( !Actions.TryGetValue( actionName, out var bind ) )
		{
			if ( create )
			{
				bind = new ActionBind() { Name = actionName };
				Actions[actionName] = bind;
			}
		}

		return bind;
	}

	/// <summary>
	/// Set the bind value for this action. This will overwrite what's in this slot.
	/// </summary>
	public ActionBind Set( string actionName, int slot, string buttonName )
	{
		var bind = GetBind( actionName );
		bind.Set( slot, buttonName );
		return bind;
	}

	/// <summary>
	/// Get the bind value at this slot
	/// </summary>
	public string Get( string actionName, int slot )
	{
		Actions.TryGetValue( actionName, out var bind );
		if ( bind == null ) return null;

		var entry = bind.Get( slot );
		return entry.FullString;
	}

	/// <summary>
	/// Save the collection to disk
	/// </summary>
	public void SaveToDisk()
	{
		BindSaveConfig.Save( ConfigPath, this );
	}

	/// <summary>
	/// Reset the collection to the default values.
	/// </summary>
	internal void ResetToDefaults()
	{
		if ( CollectionName == "common" )
		{
			Actions.Clear();
			FillDefaultCommonInputs();
		}

		foreach ( var a in Actions )
		{
			a.Value.Set( 0, a.Value.Default );
			a.Value.Set( 1, null );
		}
	}

	/// <summary>
	/// Enumerate all actions that contain this button: this collection's, or with no actions set (an older project) its base's.
	/// A struct, walked on every key press and release.
	/// </summary>
	internal ButtonBinds EnumerateWithButton( string button )
	{
		var collection = this;
		while ( collection is { Actions.Count: 0 } ) collection = collection.Base;
		return new ButtonBinds( collection, button );
	}

	internal readonly struct ButtonBinds( BindCollection collection, string button )
	{
		public Enumerator GetEnumerator() => new( collection, button );

		public struct Enumerator( BindCollection collection, string button )
		{
			Dictionary<string, ActionBind>.Enumerator actions = collection is null ? default : collection.Actions.GetEnumerator();
			readonly bool any = collection is not null;

			public ActionBind Current { get; private set; }

			public bool MoveNext()
			{
				if ( !any ) return false;

				while ( actions.MoveNext() )
				{
					var action = actions.Current.Value;
					if ( !action.HasButton( button ) ) continue;

					Current = action;
					return true;
				}

				return false;
			}
		}
	}

	/// <summary>
	/// The action list has changed, we just got the config from the server.
	/// Here we'll clear the actions, add all of the defaults from the new config
	/// and then load the user config if it exists.
	/// </summary>
	internal void UpdateActions( List<InputAction> inputActions )
	{
		Actions.Clear();

		foreach ( var action in inputActions )
		{
			// if this doesn't have a default bind, skip it
			if ( string.IsNullOrEmpty( action.KeyboardCode ) ) continue;

			var bind = GetBind( action.Name );
			bind.Set( 0, action.KeyboardCode );
			bind.Default = action.KeyboardCode;
			bind.IsCommon = Base?.Actions.ContainsKey( action.Name ) ?? false;
		}

		BindSaveConfig.Load( ConfigPath, this );
	}

	public class ActionBind
	{
		public string Name { get; set; }
		public BindEntry[] Slots = new BindEntry[2];

		/// <summary>
		/// If this is set then we want to read the value from the base collection
		/// </summary>
		public bool IsCommon { get; set; }

		public string Default { get; set; }

		internal BindEntry Get( int slot )
		{
			return Slots[slot];
		}

		internal void Set( int slot, string buttonString )
		{
			Slots[slot].Set( buttonString );
			IsCommon = false;
		}

		internal bool HasButton( string button )
		{
			return Slots[0].HasButton( button ) || Slots[1].HasButton( button );
		}

		internal bool Test( string button, HashSet<string> activeButtons )
		{
			return Slots[0].Test( button, activeButtons ) || Slots[1].Test( button, activeButtons );
		}
	}

	public struct BindEntry
	{
		public string FullString { get; set; }

		HashSet<string> ButtonList;

		internal bool HasButton( string button )
		{
			return ButtonList?.Contains( button ) ?? false;
		}

		internal bool Matches( IReadOnlyList<string> buttons ) => ButtonList?.SetEquals( buttons ) == true;

		internal void Set( string buttonString )
		{
			buttonString = buttonString ?? "";
			FullString = buttonString;
			ButtonList = FullString.Split( new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries ).ToHashSet( StringComparer.OrdinalIgnoreCase );
		}

		internal bool Test( string button, HashSet<string> activeButtons )
		{
			if ( ButtonList == null ) return false;
			if ( !HasButton( button ) ) return false;
			return ButtonList.IsSubsetOf( activeButtons );
		}
	}

}
