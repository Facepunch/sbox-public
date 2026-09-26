using Sandbox;
using System;
using System.Globalization;

namespace RasLib;

// data/database/ragdolls/<set>.txt: hull geometry kf2 + weighted bones + per-joint swing/twist limits
public class RagdollFile
{
	public class BoneDef
	{
		public string Name;
		public float Weight = 1f;
	}

	public class JointDef
	{
		public string Bone1;
		public string Bone2;
		public Vector3 TwistAxis = new( 1, 0, 0 );
		public float TwistMin, TwistMax, ConeMin, ConeMax, PlaneMin, PlaneMax;
	}

	public string GeometryFile { get; private set; }
	public List<BoneDef> Bones { get; } = [];
	public List<JointDef> Joints { get; } = [];

	public static RagdollFile Parse( string text )
	{
		var ragdoll = new RagdollFile();
		var section = "";
		BoneDef bone = null;
		JointDef joint = null;

		foreach ( var rawLine in text.Split( '\n' ) )
		{
			var line = rawLine;
			var comment = line.IndexOf( "//", StringComparison.Ordinal );
			if ( comment >= 0 ) line = line[..comment];
			line = line.Trim();
			if ( line.Length == 0 ) continue;

			if ( line.StartsWith( '[' ) )
			{
				var name = line.Trim( '[', ']' );
				switch ( name )
				{
					case "ParticipatingBones":
					case "Joints":
					case "Ragdoll":
					case "Geometry":
					case "Properties":
						section = name;
						break;
					case "Bone" when section == "ParticipatingBones":
						bone = new BoneDef();
						ragdoll.Bones.Add( bone );
						break;
					case "Joint" when section == "Joints":
						joint = new JointDef();
						ragdoll.Joints.Add( joint );
						break;
				}
				continue;
			}

			var eq = line.IndexOf( '=' );
			if ( eq < 0 ) continue;

			var key = line[..eq].Trim();
			var value = line[(eq + 1)..].Trim().TrimEnd( ';' ).Trim();

			if ( section == "Geometry" && key == "ExportData" )
			{
				ragdoll.GeometryFile = value.Split( ';' )[0].Trim().Trim( '"' );
				continue;
			}

			if ( bone is not null && section == "ParticipatingBones" )
			{
				switch ( key )
				{
					case "Name": bone.Name = value.Trim( '"' ); break;
					case "RelativeWeight": bone.Weight = Float( value ); break;
				}
				continue;
			}

			if ( joint is not null && section == "Joints" )
			{
				switch ( key )
				{
					case "Bone1": joint.Bone1 = value.Trim( '"' ); break;
					case "Bone2": joint.Bone2 = value.Trim( '"' ); break;
					case "TwistAxis": joint.TwistAxis = Vec( value ); break;
					case "TwistMin": joint.TwistMin = Float( value ); break;
					case "TwistMax": joint.TwistMax = Float( value ); break;
					case "ConeMin": joint.ConeMin = Float( value ); break;
					case "ConeMax": joint.ConeMax = Float( value ); break;
					case "PlaneMin": joint.PlaneMin = Float( value ); break;
					case "PlaneMax": joint.PlaneMax = Float( value ); break;
				}
			}
		}

		return ragdoll;
	}

	static float Float( string value )
		=> float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f ) ? f : 0f;

	static Vector3 Vec( string value )
	{
		var parts = value.Trim( '(', ')' ).Split( ',', StringSplitOptions.TrimEntries );
		return parts.Length == 3 ? new Vector3( Float( parts[0] ), Float( parts[1] ), Float( parts[2] ) ) : Vector3.Zero;
	}
}
