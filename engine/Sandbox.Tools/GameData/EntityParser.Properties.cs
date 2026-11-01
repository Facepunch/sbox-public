using System;
using System.Reflection;

namespace Editor;

internal static partial class EntityParser
{
	internal static void AddBaseProperties( MapClass mapClass )
	{
		//
		// Shit used by the engine? Can we just define this on our C# entities as actual properties?
		//

		mapClass.Variables.Add( new MapClassVariable() { Name = "parentname", LongName = "Parent", GroupName = "Hierarchy", Description = "The name of this entity's parent in the movement hierarchy. Entities with parents move with their parent.", PropertyTypeOverride = "target_destination" } );
		mapClass.Variables.Add( new MapClassVariable() { Name = "parentAttachmentName", LongName = "Parent Model Bone/Attachment Name", GroupName = "Hierarchy", Description = "The name of the bone or attachment to attach to on the entity's parent in the movement hierarchy. Use !bonemerge to use bone-merge style attachment.", PropertyTypeOverride = "parentAttachment" } );

		mapClass.Variables.Add( new MapClassVariable() { Name = "useLocalOffset", LongName = "Use Model Attachment offsets", GroupName = "Hierarchy", Description = "Whether to respect the specified local offset when doing the initial hierarchical attachment to its parent.", PropertyType = typeof( bool ), DefaultValue = false } );
		mapClass.Variables.Add( new MapClassVariable() { Name = "local.origin", LongName = "Model Attachment position offset", GroupName = "Hierarchy", Description = "Offset in the local space of the parent model's attachment/bone to use in hierarchy. Not used if you are not using parent attachment.", PropertyType = typeof( Vector3 ) } );
		mapClass.Variables.Add( new MapClassVariable() { Name = "local.angles", LongName = "Model Attachment angular offset", GroupName = "Hierarchy", Description = "Angular offset in the local space of the parent model's attachment/bone to use in hierarchy. Not used if you are not using parent attachment.", PropertyType = typeof( Angles ) } );
		mapClass.Variables.Add( new MapClassVariable() { Name = "local.scales", LongName = "Model Attachment scale", GroupName = "Hierarchy", Description = "Uniform scale in the local space of the parent model's attachment/bone to use in hierarchy. Not used if you are not using parent attachment.", PropertyType = typeof( Vector3 ), DefaultValue = Vector3.One } );

		mapClass.Variables.Add( new MapClassVariable() { Name = "targetname", LongName = "Name", Description = "The name that other entities refer to this entity by.", PropertyTypeOverride = "target_source" } );
		mapClass.Variables.Add( new MapClassVariable() { Name = "tags", LongName = "Tags", Description = "A list of general purpose tags for this entity, for interactions with other entities such as triggers.", PropertyTypeOverride = "tags" } );
	}


	/// <summary>
	/// Parses the properties of an entity type and adds them to the GDclass
	/// </summary>
	internal static void ParseProperties( Type type, MapClass mapClass )
	{
		var properties = type.GetProperties( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy );

		foreach ( var prop in properties )
		{
			// Skip read only properties, and properties that point to themselves.
			if ( !prop.CanWrite ) continue;
			if ( prop.PropertyType == type )
			{
				Log.Warning( $"FGDWriter: Ignoring property {type.Name}.{prop.Name} that has same type {prop.PropertyType} as its parent." );
				continue;
			}

			var skipAttr = prop.GetCustomAttribute<HideAttribute>( true );
			if ( skipAttr != null ) continue;

			var propAttr = prop.GetCustomAttribute<PropertyAttribute>( true );
			if ( propAttr == null ) continue;

			var displayInfo = DisplayInfo.ForMember( prop );

			var propName = displayInfo.ClassName;
			var propTitle = displayInfo.Name;
			var propDesc = displayInfo.Description;

			if ( prop.GetCustomAttribute<System.ObsoleteAttribute>() is ObsoleteAttribute obsolete )
			{
				propDesc = $"<font color=\"#f80\"><b>Marked as obsolete: {obsolete.Message ?? "No reason given."}</b></font><br><br>" + propDesc;
			}

			// Basic info fallbacks
			if ( string.IsNullOrEmpty( propName ) ) propName = prop.Name.ToLowerInvariant();
			if ( string.IsNullOrEmpty( propTitle ) ) propTitle = prop.Name.ToTitleCase();

			// Skip unwanted stuff from base classes.
			var shouldSkip = false;
			foreach ( var attrib in type.GetCustomAttributes<HidePropertyAttribute>() )
			{
				if ( attrib.PropertyName == propName )
				{
					shouldSkip = true;
					break;
				}
			}
			if ( shouldSkip ) continue;

			// Default value
			object defaultValue = null;

			var dv = prop.GetCustomAttribute<DefaultValueAttribute>();
			if ( dv != null && dv.Value != null )
			{
				defaultValue = dv.Value;
			}

			var variable = new MapClassVariable();

			variable.Name = propName;
			variable.LongName = propTitle;
			variable.Description = propDesc;
			variable.DefaultValue = defaultValue;
			variable.PropertyType = prop.PropertyType;
			variable.GroupName = displayInfo.Group;

			//
			// Not a fan of this stuff, but just keeping it so I don't fuck everything up now.
			//
			{
				var fgdTypeAttr = prop.GetCustomAttribute<FGDTypeAttribute>();

				if ( fgdTypeAttr != null )
				{
					variable.PropertyTypeOverride = fgdTypeAttr.Type;
					if ( prop.PropertyType.IsArray || prop.PropertyType.IsAssignableTo( typeof( IList ) ) ) variable.PropertyTypeOverride = $"array:{fgdTypeAttr.Type}";
					if ( !string.IsNullOrEmpty( fgdTypeAttr.Editor ) ) variable.Metadata["editor"] = fgdTypeAttr.Editor;
				}

				if ( prop.PropertyType.IsAssignableTo( typeof( IAsset ) ) )
				{
					var assetname = prop.PropertyType.GetCustomAttribute<LibraryAttribute>()?.Name ?? prop.PropertyType.Name;
					variable.Metadata["editor"] = $"AssetBrowse({assetname.ToLowerInvariant()})";
				}
			}

			//
			// Metadata
			//
			var metaDataAttrs = prop.GetCustomAttributes<FieldMetaDataAttribute>();
			foreach ( var mdAttr in metaDataAttrs )
			{
				mdAttr.AddMetaData( variable.Metadata );
			}

			foreach ( var meta in prop.GetCustomAttributes<MinMaxAttribute>() )
			{
				variable.Metadata["min"] = meta.MinValue.ToString();
				variable.Metadata["max"] = meta.MaxValue.ToString();
			}

			//
			// Add mapClass helpers from properties
			// TODO: Maybe make this less hardcoded if more such attributes are added
			//
			var pointLineAttr = prop.GetCustomAttributes<PointLineAttribute>();
			foreach ( var plAttr in pointLineAttr )
			{
				// TODO: Maybe also also do color with variables 2,3,4?
				mapClass.EditorHelpers.Add( new Tuple<string, string[]>( plAttr.Local ? "vecline_local" : "vecline", new string[] { variable.Name } ) );
				if ( !plAttr.Local && variable.DefaultValue == null ) variable.DefaultValue = ""; // Override the "0 0 0" default value for vectors
			}


			mapClass.Variables.Add( variable );
		}
	}
}
