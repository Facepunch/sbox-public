using System;
using System.Globalization;

namespace Sandbox.Mounting.Source1;

internal sealed partial class SourceModelBuilder
{
	private void AddHitboxes( ModelBuilder builder, MdlFile mdl, string[] boneNames )
	{
		if ( mdl.HitboxSetCount == 0 )
			return;
		if ( mdl.GetHitboxSet( 0 ).Hitboxes.Count == 0 )
		{
			if ( Enumerable.Range( 1, mdl.HitboxSetCount - 1 ).Any( i => mdl.GetHitboxSet( i ).Hitboxes.Count > 0 ) )
				Log.Trace( $"{FullPath}: the default hitbox set is empty; alternate sets are not imported because the runtime builder would make one active." );
			return;
		}
		if ( mdl.HasAmbiguousHitboxNames )
			Log.Trace( $"{FullPath}: MDL v44 hitbox name offsets vary between compilers; using stable generated hitbox names." );

		var setNames = new HashSet<string>( StringComparer.Ordinal );
		for ( var setIndex = 0; setIndex < mdl.HitboxSetCount; setIndex++ )
		{
			var source = mdl.GetHitboxSet( setIndex );
			var setName = GetUniqueHitboxName( source.Name, setIndex == 0 ? "default" : $"hitboxset_{setIndex}", setNames );
			var set = builder.AddHitboxSet( setName );
			var names = new HashSet<string>( StringComparer.Ordinal );
			for ( var index = 0; index < source.Hitboxes.Count; index++ )
			{
				var hitbox = source.Hitboxes[index];
				var name = GetUniqueHitboxName( hitbox.Name, $"hitbox_{index}", names );
				var target = set.AddBox( name, boneNames[hitbox.Bone], new BBox( hitbox.Min, hitbox.Max ) )
					.WithSurface( Host.GetSurfaceBindingName( mdl.GetBoneSurfaceProperty( hitbox.Bone ), FullPath ) )
					.AddTag( $"hitgroup_{hitbox.Group.ToString( CultureInfo.InvariantCulture )}" );
				var tag = hitbox.Group switch
				{
					0 => "generic",
					1 => "head",
					2 => "chest",
					3 => "stomach",
					4 => "left_arm",
					5 => "right_arm",
					6 => "left_leg",
					7 => "right_leg",
					10 => "gear",
					_ => null
				};
				if ( tag != null )
					target.AddTag( tag );
			}
		}
	}

	private static string GetUniqueHitboxName( string authoredName, string fallback, HashSet<string> names )
	{
		var source = string.IsNullOrWhiteSpace( authoredName ) ? fallback : authoredName;
		var name = source;
		for ( var suffix = 1; !names.Add( name ); suffix++ )
			name = $"{source}_{suffix}";
		return name;
	}
}
