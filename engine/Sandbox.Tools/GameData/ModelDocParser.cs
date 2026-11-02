using System;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Editor;

internal static partial class ModelDocParser
{
	internal static MapClass ParseType( Type type )
	{
		if ( type.GetInterface( "Sandbox.IModelBreakCommand" ) == null && !type.HasAttribute( typeof( Sandbox.ModelEditor.GameDataAttribute ) ) )
		{
			Log.Warning( $"Unsupported ModelDoc type {type}" );
			return null;
		}

		string nodeName;
		string description;
		var gdAttr = type.GetCustomAttribute<Sandbox.ModelEditor.GameDataAttribute>( false );
		if ( gdAttr == null )
		{
			var libAttr = type.GetCustomAttribute<LibraryAttribute>( false );
			if ( libAttr == null ) return null;

			nodeName = libAttr.Name;
			description = libAttr.Description;
		}
		else
		{
			nodeName = gdAttr.Name;
			description = gdAttr.Description;
		}

		var descAtt = type.GetCustomAttributes<DescriptionAttribute>().FirstOrDefault();
		if ( string.IsNullOrEmpty( description ) && descAtt != null && descAtt.Value != null ) description = descAtt.Value;

		var classType = GameDataClassType.ModelGameData;

		if ( type.GetInterface( "Sandbox.IModelBreakCommand" ) != null && gdAttr == null )
			classType = GameDataClassType.ModelBreakCommand;

		// TODO: @ModelAnimEvent

		var mapClass = new MapClass( nodeName );
		mapClass.Description = description;
		mapClass.ClassType = classType;

		if ( gdAttr != null && gdAttr.AllowMultiple )
		{
			mapClass.EditorHelpers.Add( new Tuple<string, string[]>( "game_data_list", new string[] { gdAttr.ListName ?? $"{gdAttr.Name}_list" } ) );
		}

		// TODO: Have ModelDoc display some kind of nice name for these nodes?
		//var displayInfo = DisplayInfo.ForType( type, false );
		//mapClass.DisplayName = displayInfo.Name ?? libAttr.Name;
		//mapClass.Icon = displayInfo.Icon;

		ParseProperties( type, mapClass );
		EntityParser.ParseAttributes( type, mapClass );

		return mapClass;
	}

	// I hate duplicating code
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

			// Basic property info
			var displayInfo = DisplayInfo.ForMember( prop );

			string propTitle = displayInfo.Name ?? prop.Name.ToTitleCase();
			string propDesc = displayInfo.Description ?? "";
			string propName = prop.Name.ToLowerInvariant();

			if ( prop.GetCustomAttribute<System.ObsoleteAttribute>() is ObsoleteAttribute obsolete )
			{
				propDesc = $"<font color=\"#f80\"><b>Marked as obsolete: {obsolete.Message ?? "No reason given."}</b></font><br><br>" + propDesc;
			}

			var jsonName = prop.GetCustomAttribute<JsonPropertyNameAttribute>( true );
			if ( jsonName != null ) propName = jsonName.Name;

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

			// TODO: Structs
			/*var isStruct = prop.PropertyType.IsValueType && !prop.PropertyType.IsPrimitive && !prop.PropertyType.IsEnum;
			if ( structOut != null && (prop.PropertyType.IsClass || isStruct) )
			{
				AssetStructToString( t, structOut );
			}*/

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

