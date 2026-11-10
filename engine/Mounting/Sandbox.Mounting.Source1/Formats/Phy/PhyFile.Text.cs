using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Sandbox.Mounting.Source1;

internal sealed partial class PhyFile
{
	private void ReadText( int offset )
	{
		var end = Array.IndexOf( data, (byte)0, offset );
		if ( end < 0 ) end = data.Length;
		for ( var i = end; i < data.Length; i++ )
			if ( data[i] != 0 )
				throw Error( "nonzero data after the KeyValues terminator." );
		string text;
		try
		{
			text = new UTF8Encoding( false, true ).GetString( data, offset, end - offset );
		}
		catch ( DecoderFallbackException exception )
		{
			throw new InvalidDataException( $"{SourceName}: PHY text is not valid UTF-8.", exception );
		}
		Text = VmtFile.ParseKeyValues( text, SourceName,
			diagnostic => throw Error( $"unsupported KeyValues condition/directive: {diagnostic.Message}" ) );
		var seen = new bool[Solids.Count];
		var constraints = new List<Constraint>();
		var pairs = new List<CollisionPair>();
		var collisionRulesSeen = false;
		foreach ( var block in Text )
		{
			if ( !block.IsBlock )
				throw Error( $"expected a block, found '{block.Name}'." );
			switch ( block.Name.ToLowerInvariant() )
			{
				case "solid":
					ReadSolid( block, seen );
					break;
				case "ragdollconstraint":
					constraints.Add( ReadConstraint( block ) );
					break;
				case "collisionrules":
					if ( collisionRulesSeen )
						throw Error( "duplicate collisionrules block." );
					collisionRulesSeen = true;
					ReadCollisionRules( block, pairs );
					break;
				case "animatedfriction":
					ValidateFields( block );
					foreach ( var entry in block.Children ) Number( block, entry.Name, 0 );
					break;
				case "editparams":
					if ( block.Values.ContainsKey( "totalmass" ) ) Number( block, "totalmass", 0 );
					break;
			}
		}
		for ( var i = 0; i < seen.Length; i++ )
			if ( !seen[i] )
				throw Error( $"missing solid text for binary solid {i}." );

		ValidateConstraintHierarchy( constraints );
		Constraints = constraints.AsReadOnly();
		CollisionPairs = pairs.AsReadOnly();
	}

	private void ReadSolid( VmtNode block, bool[] seen )
	{
		ValidateFields( block );
		var index = Index( block, "index" );
		if ( seen[index] )
			throw Error( $"duplicate solid index {index}." );
		seen[index] = true;
		var solid = Solids[index];
		solid.Name = String( block, "name" );
		solid.SurfaceProperty = block.Values.TryGetValue( "surfaceprop", out var surface ) && !string.IsNullOrWhiteSpace( surface )
			? surface.ToLowerInvariant() : "default";
		solid.Mass = Number( block, "mass", 1, positive: true );
		solid.Damping = Number( block, "damping", 0.1f );
		solid.RotationalDamping = Number( block, "rotdamping", 0.1f );
		solid.Inertia = Number( block, "inertia", 1 );
	}

	private Constraint ReadConstraint( VmtNode block )
	{
		ValidateFields( block );
		var parent = Index( block, "parent" );
		var child = Index( block, "child" );
		if ( parent == child )
			throw Error( "constraint connects a solid to itself." );
		return new Constraint( parent, child, Axis( block, "x" ), Axis( block, "y" ), Axis( block, "z" ) );
	}

	private void ReadCollisionRules( VmtNode block, List<CollisionPair> pairs )
	{
		ValidateFields( block, "collisionpair" );
		if ( block.Values.TryGetValue( "selfcollisions", out var self ) )
		{
			if ( self != "0" )
				throw Error( "selfcollisions must be 0 (Source's disable-self-collision directive)." );
			SelfCollisions = false;
		}
		var uniquePairs = new HashSet<CollisionPair>();
		foreach ( var entry in block.Children )
		{
			if ( !entry.Name.Equals( "collisionpair", StringComparison.OrdinalIgnoreCase ) ) continue;
			var indices = entry.Value.AsSpan();
			var separator = indices.IndexOf( ',' );
			if ( separator < 0 || indices[(separator + 1)..].Contains( ',' ) )
				throw Error( "collisionpair must contain two solid indices." );
			var a = ParseIndex( indices[..separator] );
			var b = ParseIndex( indices[(separator + 1)..] );
			var pair = new CollisionPair( Math.Min( a, b ), Math.Max( a, b ) );
			if ( a == b )
				throw Error( "self-referencing collisionpair." );
			if ( uniquePairs.Add( pair ) ) pairs.Add( pair );
		}
		if ( SelfCollisions == false && pairs.Count > 0 )
			throw Error( "collisionrules combines disabled self-collision with enabled collision pairs." );
	}

	private void ValidateConstraintHierarchy( List<Constraint> constraints )
	{
		var parents = new int[Solids.Count];
		Array.Fill( parents, -1 );
		foreach ( var constraint in constraints )
		{
			if ( parents[constraint.Child] != -1 )
				throw Error( $"solid {constraint.Child} has duplicate/multiple constraint parents." );
			parents[constraint.Child] = constraint.Parent;
		}
		var states = new byte[parents.Length];
		for ( var i = 0; i < parents.Length; i++ )
		{
			var current = i;
			while ( current != -1 && states[current] == 0 )
			{
				states[current] = 1;
				current = parents[current];
			}
			if ( current != -1 && states[current] == 1 )
				throw Error( "constraint hierarchy contains a cycle." );
			current = i;
			while ( current != -1 && states[current] == 1 )
			{
				states[current] = 2;
				current = parents[current];
			}
		}
	}

	private void ValidateFields( VmtNode block, string repeated = null )
	{
		var names = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		foreach ( var child in block.Children )
		{
			if ( child.IsBlock || (!names.Add( child.Name ) && !child.Name.Equals( repeated, StringComparison.OrdinalIgnoreCase )) )
				throw Error( $"nested or duplicate '{child.Name}' in {block.Name}." );
		}
	}

	private string String( VmtNode block, string key, string fallback = null )
	{
		if ( !block.Values.TryGetValue( key, out var value ) )
			return fallback ?? throw Error( $"{block.Name} is missing '{key}'." );
		if ( string.IsNullOrWhiteSpace( value ) )
			throw Error( $"{block.Name} has empty '{key}'." );
		return value;
	}
	private int Index( VmtNode block, string key ) => ParseIndex( String( block, key ) );
	private int ParseIndex( ReadOnlySpan<char> value )
	{
		if ( !int.TryParse( value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index ) || index < 0 || index >= Solids.Count )
			throw Error( $"invalid solid index '{value}'." );
		return index;
	}
	private float ParseFloat( ReadOnlySpan<char> value )
	{
		if ( !float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result ) || !float.IsFinite( result ) )
			throw Error( $"invalid number '{value}'." );
		return result;
	}
	private float Number( VmtNode block, string key, float fallback, bool positive = false )
	{
		var result = block.Values.TryGetValue( key, out var value ) ? ParseFloat( value ) : fallback;
		if ( result < 0 || (positive && result == 0) )
			throw Error( $"invalid {key} in {block.Name}." );
		return result;
	}
	private AxisLimit Axis( VmtNode block, string axis )
	{
		var min = block.Values.TryGetValue( axis + "min", out var minValue ) ? ParseFloat( minValue ) : 0;
		var max = block.Values.TryGetValue( axis + "max", out var maxValue ) ? ParseFloat( maxValue ) : 0;
		if ( min > max || min < -360 || max > 360 )
			throw Error( $"invalid {axis} constraint range [{min}, {max}]." );
		return new AxisLimit( min, max, Number( block, axis + "friction", 0 ) );
	}
}
