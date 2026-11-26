using System;
using Sandbox.ModelEditor.Nodes;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	static void ConvertProp( GameObject target, MapLoader.ObjectEntry data, SourceNode source, List<string> warnings )
	{
		var model = data.GetResource<Model>( "model" );
		var choices = ReadModelChoices( data, out var materialGroup, out var error );
		var tint = ReadPropTint( data.GetString( "rendercolor" ), data.GetString( "renderamt" ) );
		if ( error is not null ) warnings.Add( $"Invalid model settings on '{target.Name}': {error}" );
		bool isStatic = data.TypeName == "prop_static";
		bool missing = model is not { IsError: false };
		bool baked = isStatic && BakesPropToWorld( data, model, source );
		// Hammer removes these placements from the world draw list before attempting to bake them.
		bool omitGeometry = baked && missing;
		if ( missing ) warnings.Add( $"Could not load prop model '{data.GetString( "model" )}'." );
		if ( isStatic ) tint = HammerRenderTint( tint );
		if ( isStatic && source.Children.Length == 0 && source.Connections is null &&
			string.IsNullOrEmpty( data.ParentName ) && string.IsNullOrEmpty( data.TargetName ) )
		{
			target.IsStatic = baked;
		}

		if ( source.CulledProp )
		{
			SetPropCollision( target, UsesModelCollision( data ) ? model : null, source.BonePose, warnings );
			return;
		}

		// Keep the same component IDs when a prefab placement overrides shadows or collision.
		if ( isStatic && (source.BonePose is not null || source.TileMaterials is { Count: > 0 } || source.Entity.GetValue<bool>( "disableshadows" ) ||
			!UsesModelCollision( source.Entity ) || source.VariableBindings?.Any( v =>
				v.Key.Equals( "disableshadows", StringComparison.OrdinalIgnoreCase ) ||
				v.Key.Equals( "solid", StringComparison.OrdinalIgnoreCase ) ||
				v.Key.Equals( "collisions", StringComparison.OrdinalIgnoreCase ) ) == true) )
		{
			ModelRenderer renderer = source.BonePose is null ? target.Components.GetOrCreate<ModelRenderer>() : target.Components.GetOrCreate<SkinnedModelRenderer>();
			renderer.Enabled = !omitGeometry;
			renderer.Model = model;
			renderer.MaterialGroup = materialGroup;
			renderer.Tint = tint;
			renderer.RenderType = data.GetValue<bool>( "disableshadows" ) ? ModelRenderer.ShadowRenderType.Off : ModelRenderer.ShadowRenderType.On;
			if ( model is { IsError: false } )
			{
				renderer.BodyGroups = BodyGroupMask( model, choices );
				if ( renderer is SkinnedModelRenderer skinned && source.BonePose is not null )
					ApplyPropPose( skinned, source.BonePose );
				if ( source.TileMaterials is { Count: > 0 } materials )
				{
					var slots = model.Materials;
					for ( int i = 0; i < slots.Length; i++ )
						renderer.Materials.SetOverride( i, slots[i] is { } slot && materials.TryGetValue( slot.Name, out var path ) ? Material.Load( path ) : null );
				}
			}

			SetPropCollision( target, !missing && UsesModelCollision( data ) ? model : null, source.BonePose, warnings );
			return;
		}

		if ( !isStatic && (source.TileMaterials is { Count: > 0 } || data.GetValue<bool>( "disableshadows" )) )
			warnings.Add( $"Dynamic prop '{target.Name}' uses rendering overrides its scene component cannot store." );

		var prop = target.Components.GetOrCreate<Prop>();
		prop.Enabled = !omitGeometry;
		prop.Model = model;
		prop.MaterialGroup = materialGroup;
		prop.Tint = tint;
		if ( model is { IsError: false } && (choices is not null || model.Parts.Count > 0) )
			prop.BodyGroups = BodyGroupMask( model, choices );
		prop.IsStatic = data.TypeName != "prop_physics" || data.GetValue<bool>( "static" );
		prop.StartAsleep = data.TypeName == "prop_physics" && (data.GetValue<int>( "spawnflags" ) & 1) != 0;
	}

	static void ApplyPropPose( SkinnedModelRenderer renderer, Dictionary<string, Transform> pose )
	{
		renderer.CreateBoneObjects = true;
		var objects = renderer.Model.CreateBoneObjects( renderer.GameObject );
		var transforms = new Transform[renderer.Model.Bones.AllBones.Count];
		foreach ( var bone in renderer.Model.Bones.AllBones )
		{
			var go = objects[bone];
			go.Flags |= GameObjectFlags.ProceduralBone;
			var parent = bone.Parent;
			var parentPose = parent is null ? Transform.Zero : transforms[parent.Index];
			// Hammer stores overrides in model space. Other bones keep their parent-relative bind transform.
			if ( pose.TryGetValue( bone.Name, out var transform ) )
				go.LocalTransform = parentPose.ToLocal( transform );
			else
				go.LocalTransform = parent is null ? bone.LocalTransform : parent.LocalTransform.ToLocal( bone.LocalTransform );
			transforms[bone.Index] = parentPose.ToWorld( go.LocalTransform );
		}
	}

	// Hammer's static prop physics builder emits model collision only for SOLID_VPHYSICS.
	static bool UsesModelCollision( MapLoader.ObjectEntry data ) =>
		data.GetValue( "solid", 6 ) == 6 && data.GetValue( "collisions", 1 ) != 0;

	static bool BakesPropToWorld( MapLoader.ObjectEntry data, Model model, SourceNode source )
	{
		if ( data.GetValue<bool>( "baketoworld" ) || source.BonePose is not null ) return true;
		int lighting = data.GetValue( "bakelighting", -1 );
		return lighting == -1 ? model is not { IsError: false } || (model.GetData<ModelPropData>()?.BakeLighting ?? true) : lighting != 0;
	}

	static IEnumerable<(int Mesh, int Draw)> PropDrawCalls( Model model, MapLoader.ObjectEntry entity )
	{
		var meshes = model.MeshInfo.Meshes;
		int lod = Math.Max( 0, Math.Min( HammerInteger( entity.GetString( "lodlevel" ), 0 ), model.MeshInfo.LodCount - 1 ) );
		var choices = ReadModelChoices( entity, out _, out _ );
		ulong mask = BodyGroupMask( model, choices );
		// Hammer treats an empty builder mask as a request for the model's default meshes.
		bool useDefault = choices is null || mask == 0;
		if ( useDefault ) mask = model.Parts.DefaultMask;
		for ( int i = 0; i < meshes.Length; i++ )
		{
			var mesh = meshes[i];
			if ( meshes.Length > 1 && (mesh.LodMask & (1 << lod)) == 0 ) continue;
			if ( !(meshes.Length == 1 && useDefault) && (mesh.MeshGroupMask & mask) == 0 ) continue;
			for ( int d = 0; d < mesh.DrawCalls.Length; d++ )
				yield return (i, d);
		}
	}

	static JsonObject ReadModelChoices( MapLoader.ObjectEntry entity, out string materialGroup, out string error )
	{
		var state = entity.GetString( "model_state" );
		var groups = entity.GetString( "bodygroups" );
		materialGroup = entity.GetString( "skin", "default" );
		error = null;
		bool hasState = !string.IsNullOrWhiteSpace( state );
		if ( !hasState && string.IsNullOrWhiteSpace( groups ) ) return null;
		var settings = new Kv3Text( hasState ? state : groups ).ReadModelSettings( out error );
		if ( !hasState ) return settings;
		materialGroup = settings["material_group"] is JsonValue group && group.TryGetValue<string>( out var name ) ? name : "default";
		return settings["body_groups"] as JsonObject ?? new JsonObject();
	}

	static ulong BodyGroupMask( Model model, JsonObject choices )
	{
		if ( choices is null ) return model.Parts.DefaultMask;
		ulong mask = model.Parts.DefaultMask;
		foreach ( var part in model.Parts.All )
		{
			if ( part.Choices.Count == 0 ) continue;
			int choice = BodyGroupChoice( choices?[part.Name] );
			if ( choice >= part.Choices.Count ) choice = 0;
			mask = (mask & ~part.Mask) | (choice < 0 ? 0 : part.Choices[choice].Mask);
		}
		return mask;
	}
	internal static int BodyGroupChoice( JsonNode node )
	{
		if ( node is not JsonValue value ) return 0;
		if ( value.TryGetValue<long>( out var integer ) ) return unchecked((int)integer);
		if ( value.TryGetValue<ulong>( out var unsigned ) ) return unchecked((int)unsigned);
		if ( value.TryGetValue<bool>( out var flag ) ) return flag ? 1 : 0;
		if ( value.TryGetValue<double>( out var number ) ) return double.ConvertToIntegerNative<int>( number );
		if ( !value.TryGetValue<string>( out var text ) ) return 0;
		return double.ConvertToIntegerNative<int>( HammerNumber( text.AsSpan().TrimStart() ) );
	}

	// KV3 converts quoted numbers through V_atofloat64, which reads a numeric prefix.
	static double HammerNumber( ReadOnlySpan<char> text )
	{
		int sign = 1;
		if ( !text.IsEmpty && text[0] is '-' or '+' )
		{
			if ( text[0] == '-' ) sign = -1;
			text = text[1..];
		}
		double number = 0;
		if ( text.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ) )
		{
			foreach ( var c in text[2..] )
			{
				if ( !Uri.IsHexDigit( c ) ) break;
				number = number * 16 + Uri.FromHex( c );
			}
			return sign * number;
		}
		if ( !text.IsEmpty && text[0] == '\'' ) return text.Length > 1 ? sign * text[1] : 0;

		int decimals = -1;
		for ( int i = 0; i < text.Length; i++ )
		{
			char c = text[i];
			if ( c == '.' ) decimals = 0;
			else if ( char.IsAsciiDigit( c ) )
			{
				number = number * 10 + (c - '0');
				if ( decimals >= 0 ) decimals++;
			}
			else
			{
				if ( c is 'e' or 'E' )
				{
					var exponent = text[(i + 1)..].TrimStart();
					int length = !exponent.IsEmpty && exponent[0] is '+' or '-' ? 1 : 0;
					while ( length < exponent.Length && char.IsAsciiDigit( exponent[length] ) ) length++;
					if ( int.TryParse( exponent[..length], CultureInfo.InvariantCulture, out int power ) ) number *= Math.Pow( 10, power );
				}
				break;
			}
		}
		while ( decimals-- > 0 ) number /= 10;
		return sign * number;
	}

}
