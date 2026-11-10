using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.ModelEditor.Nodes;

namespace Sandbox.Mounting.Source1;

internal sealed class SourcePropData
{
	private readonly Dictionary<string, VmtNode> definitions = new( StringComparer.OrdinalIgnoreCase );

	internal void AddScript( IReadOnlyList<VmtNode> roots, string path )
	{
		if ( roots.Count != 1 || !roots[0].IsBlock )
			throw new InvalidDataException( $"{path}: expected one propdata block." );
		foreach ( var block in roots[0].Children )
		{
			if ( block.IsBlock )
				definitions.TryAdd( block.Name, block );
		}
	}

	internal ModelPropData Read( IReadOnlyList<VmtNode> roots, string path, Action<string> report, bool hasAuthoredPieces = false )
	{
		if ( roots.Count == 1 && roots[0].IsBlock && roots[0].Name.Equals( "mdlkeyvalue", StringComparison.OrdinalIgnoreCase ) )
			roots = roots[0].Children;
		var prop = roots.FirstOrDefault( node => node.IsBlock && node.Name.Equals( "prop_data", StringComparison.OrdinalIgnoreCase ) );
		if ( prop is null )
			return null;

		var values = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		var bases = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		var flammable = false;
		Apply( prop );
		foreach ( var block in roots )
			ReadInteractions( block );
		var parameters = new VmtParameters( values, path );
		var damage = parameters.Float( "explosive_damage", -1 );
		var radius = parameters.Float( "explosive_radius", -1 );
		if ( radius >= int.MaxValue )
			throw new InvalidDataException( $"{path}: explosive_radius exceeds Source's integer range." );
		if ( flammable || damage > 0 || radius > 0 )
			report( $"{path}: fire and explosion gameplay was disabled because stock Prop cannot safely apply it to mounted map hierarchies; health and break pieces are retained." );
		if ( !hasAuthoredPieces && parameters.Integer( "breakable_count" ) > 0
			&& !string.IsNullOrWhiteSpace( parameters.String( "breakable_model" ) ) )
			report( $"{path}: script-selected random breakable chunks are not supported." );

		return new ModelPropData
		{
			Health = parameters.Integer( "health", -1 ),
			Flammable = false,
			Explosive = false,
			ExplosionDamage = damage,
			ExplosionRadius = radius
		};

		void Apply( VmtNode section )
		{
			if ( section.Values.TryGetValue( "base", out var name ) && !string.IsNullOrWhiteSpace( name ) )
			{
				if ( bases.Count >= 64 || !bases.Add( name ) )
					throw new InvalidDataException( $"{path}: cyclic propdata base '{name}'." );
				if ( !definitions.TryGetValue( name, out var parent ) )
					throw new InvalidDataException( $"{path}: missing propdata base '{name}'." );
				Apply( parent );
				bases.Remove( name );
			}
			foreach ( var entry in section.Values )
				values[entry.Key] = entry.Value;
			foreach ( var block in section.Children )
				ReadInteractions( block );
		}

		void ReadInteractions( VmtNode block )
		{
			if ( block.IsBlock && block.Name.Equals( "fire_interactions", StringComparison.OrdinalIgnoreCase )
				&& block.Values.TryGetValue( "flammable", out var value ) )
				flammable = value.Equals( "yes", StringComparison.OrdinalIgnoreCase );
		}
	}
}
