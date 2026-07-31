using Sandbox.Network;
using System;
using System.Collections.Generic;

namespace Sandbox;

internal class ReplicatedConvars
{
	public StringTable StringTable { get; init; }

	public ReplicatedConvars( string name )
	{
		StringTable = new( name, true );
		StringTable.OnChangeOrAdd += OnTableEntryUpdated;
		StringTable.OnRemoved += OnTableEntryRemoved;
		StringTable.OnSnapshot += OnTableSnapshot;

		ConVarSystem.ConVarChanged += OnConVarChanged;
	}

	/// <summary>
	/// Should be called after the assemblies are loaded. We'll update all the replicated convars now.
	/// </summary>
	public void OnAssembliesLoaded()
	{
		if ( !Networking.IsHost )
			return;

		foreach ( var convar in ConVarSystem.Members.Values )
		{
			if ( !convar.IsReplicated ) continue;

			StringTable.SetString( convar.Name, convar.Value );
		}
	}

	public void Reset()
	{
		StringTable.Reset();

		// what OnWrappedGet reads for replicated convars, including sv_cheats
		_values.Clear();
	}

	/// <summary>
	/// Called any time a ConVar changes.
	/// </summary>
	void OnConVarChanged( Command convar, string oldValue )
	{
		if ( !Networking.IsHost ) return;
		if ( !convar.IsReplicated ) return;

		StringTable.SetString( convar.Name, convar.Value );
	}

	void OnTableEntryUpdated( StringTable.Entry entry )
	{
		if ( Networking.IsHost ) return; // host table shouldn't update host table!

		var newValue = entry.ReadAsString();

		// no change
		if ( _values.GetValueOrDefault( entry.Name ) == newValue )
			return;

		_values[entry.Name] = newValue;

		Log.Info( $"Replicated Var Changed: {entry.Name} = {newValue}" );

		// we only ever see sv_cheats through this table, so no change notification fires for it
		if ( entry.Name.Equals( ConVarSystem.CheatsVariableName, StringComparison.OrdinalIgnoreCase ) && !newValue.ToBool() )
		{
			ConVarSystem.ResetCheatConVars();
		}

		// TODO - if we have a notice flag, broadcast to the game somehow
	}

	void OnTableEntryRemoved( StringTable.Entry entry )
	{
		_values.Remove( entry.Name );
	}

	void OnTableSnapshot()
	{
		_values.Clear();

		foreach ( var (_, entry) in StringTable.Entries )
		{
			OnTableEntryUpdated( entry );
		}
	}

	private readonly Dictionary<string, string> _values = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Get the value of a replicated ConVar.
	/// </summary>
	public bool TryGetValue( string name, out string value )
	{
		return _values.TryGetValue( name, out value );
	}
}
