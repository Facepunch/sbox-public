using System;
using System.IO;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	// Hammer reads embedded booleans with scanf("%i"), including octal and hex values.
	internal static bool HammerFlag( string value )
	{
		var text = value.AsSpan().TrimStart();
		if ( !text.IsEmpty && text[0] is '+' or '-' ) text = text[1..];
		if ( text.StartsWith( "0x", StringComparison.OrdinalIgnoreCase ) )
		{
			text = text[2..].TrimStart( '0' );
			return !text.IsEmpty && char.IsAsciiHexDigit( text[0] );
		}
		bool octal = text.StartsWith( "0" );
		text = text.TrimStart( '0' );
		return !text.IsEmpty && text[0] >= '1' && text[0] <= (octal ? '7' : '9');
	}

	// Hammer applies each selected choice after ordinary variable overrides.
	internal static Dictionary<string, string> ResolveChoices( Element[] groups, Dictionary<string, string> overrides )
	{
		var values = overrides;
		if ( groups is null ) return values;
		foreach ( var group in groups )
		{
			if ( group is null ) continue;
			var selection = overrides?.GetValueOrDefault( group.Name ) ?? group.Get( "m_ActiveValue", "" );
			var choice = group.Get( "m_Choices", Array.Empty<Element>() ).FirstOrDefault( c => c is not null && string.Equals( c.Name, selection, StringComparison.OrdinalIgnoreCase ) );
			if ( choice is null ) continue;

			var keys = group.Get( "m_ChoiceVariables", Array.Empty<string>() );
			var selected = choice.Get( "m_ChoiceValues", Array.Empty<string>() );
			if ( keys.Length != selected.Length ) throw new InvalidDataException( $"Choice '{choice.Name}' in '{group.Name}' has {selected.Length} values for {keys.Length} variables." );
			if ( values == overrides ) values = overrides is null ? new( StringComparer.OrdinalIgnoreCase ) : new( overrides, StringComparer.OrdinalIgnoreCase );
			for ( int i = 0; i < keys.Length; i++ ) values[keys[i]] = selected[i];
		}
		return values;
	}

	sealed partial class Conversion
	{
		readonly Dictionary<SourceNode, List<(Guid Id, SourceNode Node)>> _bindings = new();
		readonly Dictionary<SourceNode, (bool Enabled, bool EditorOnly)> _visibilityDefaults = new();

		public void ApplyVisibility( GameObject obj, SourceNode node, bool enabled )
		{
			if ( node.VariableBindings is { Length: > 0 } )
				_visibilityDefaults[node] = (enabled, obj.Flags.Contains( GameObjectFlags.EditorOnly ));
			if ( node.EditorOnly || node.Hidden ) obj.Flags |= GameObjectFlags.EditorOnly;
			obj.Enabled = enabled && !node.Hidden;
		}

		HashSet<string> entityClasses;

		// A placement can wrap its linked prefab alongside projected overlays or instance children.
		static GameObject PlacedPrefab( GameObject placement ) => placement.IsPrefabInstanceRoot
			? placement
			: placement.Children.FirstOrDefault( child => child.IsPrefabInstanceRoot );

		public void FixupNames( GameObject placement, SourceNode prefab, string path, Dictionary<string, string> overrides )
		{
			if ( !_bindings.TryGetValue( prefab.Target, out var bindings ) ) return;
			placement = PlacedPrefab( placement );
			if ( placement is null ) return;
			var ids = placement.PrefabInstance.PrefabToInstanceLookup;
			foreach ( var (id, node) in bindings )
			{
				if ( !ids.TryGetValue( id, out var guid ) ||
					placement.Scene.Directory.FindByGuid( guid ) is not { } obj ) continue;
				if ( node.TypeString == "CMapPrefab" && node.Target is not null && node.EntityKeys is null )
				{
					// Nested fixups use the complete placement path, even if an ancestor disabled its own fixup.
					FixupNames( obj, node, $"{path}_{node.NodeId}", ResolveChoices( node.Target.ChoiceGroups, node.VariableOverrides ) );
					continue;
				}
				if ( !prefab.FixupEntityNames || node.Kind != NodeKind.Entity ) continue;
				var name = node.Entity.TargetName;
				if ( string.IsNullOrEmpty( name ) || name[0] is '@' or '!' or '*' or '?' ) continue;
				entityClasses ??= GameData.EntityClasses.Select( x => x.Name ).ToHashSet( StringComparer.OrdinalIgnoreCase );
				if ( entityClasses.Contains( name ) ) continue;
				if ( overrides is not null && node.VariableBindings is { } variables &&
					variables.Any( v => v.Key.Equals( "targetname", StringComparison.OrdinalIgnoreCase ) && overrides.ContainsKey( v.Name ) ) ) continue;
				obj.Name = $"{path}_{name}";
			}
		}

		public void VisitPrefabTargets( GameObject placement, SourceNode prefab, Action<string, GameObject> visit )
		{
			if ( !_bindings.TryGetValue( prefab.Target, out var bindings ) ) return;
			placement = PlacedPrefab( placement );
			if ( placement is null ) return;
			var ids = placement.PrefabInstance.PrefabToInstanceLookup;
			foreach ( var (id, node) in bindings )
			{
				if ( !ids.TryGetValue( id, out var guid ) || placement.Scene.Directory.FindByGuid( guid ) is not { } obj ) continue;
				if ( node.TypeString == "CMapPrefab" && node.Target is not null && node.EntityKeys is null )
					VisitPrefabTargets( obj, node, visit );
				else if ( node.Kind == NodeKind.Entity && !string.IsNullOrEmpty( node.Entity.TargetName ) )
					visit( obj.Name, obj );
			}
		}

		public void ApplyOverrides( GameObject placement, SourceNode prefab, Dictionary<string, string> values )
		{
			if ( !_bindings.TryGetValue( prefab.Target, out var bindings ) ) return;
			placement = PlacedPrefab( placement );
			if ( placement is null ) return;
			var ids = placement.PrefabInstance.PrefabToInstanceLookup;
			foreach ( var (id, node) in bindings )
			{
				if ( node.VariableBindings is null ) continue;
				Dictionary<string, string> changes = null;
				foreach ( var (name, key) in node.VariableBindings )
				{
					if ( !values.TryGetValue( name, out var value ) ) continue;
					changes ??= new( StringComparer.OrdinalIgnoreCase );
					changes[key] = value;
				}
				if ( changes is null ) continue;
				if ( !ids.TryGetValue( id, out var guid ) ||
					placement.Scene.Directory.FindByGuid( guid ) is not { } obj )
				{
					Warnings.Add( $"Skipped variable overrides for missing node {node.NodeId} in '{prefab.TargetMapPath}'." );
					continue;
				}

				var defaults = _visibilityDefaults[node];
				bool hidden = (changes.Remove( "Force Hidden", out var forceHidden ) ? HammerFlag( forceHidden ) : node.ForceHidden) || node.SelectionHidden;
				bool editorOnly = changes.Remove( "Editor Only", out var editor ) ? HammerFlag( editor ) : node.EditorOnly;
				obj.Flags = obj.Flags.WithFlag( GameObjectFlags.EditorOnly, defaults.EditorOnly || editorOnly || hidden );
				obj.Enabled = defaults.Enabled && !hidden;

				if ( changes.TryGetValue( "angles", out var angles ) )
					obj.WorldRotation = placement.WorldRotation * Angles.Parse( angles ).ToRotation();
				if ( changes.TryGetValue( "origin", out var origin ) )
					obj.WorldPosition = placement.WorldTransform.PointToWorld( Vector3.Parse( origin ) );
				if ( changes.TryGetValue( "scales", out var scales ) )
					obj.WorldScale = placement.WorldScale * Vector3.Parse( scales );
				if ( changes.Count == 0 ) continue;

				if ( node.ImagePlane is { } imagePlane && obj.Components.Get<SpriteRenderer>( FindMode.EverythingInSelf ) is { } plane )
				{
					if ( changes.TryGetValue( "Image", out var image ) ) SetPlaneImage( plane, node.Transform.Scale, imagePlane.Map, image );
					if ( changes.TryGetValue( "Opacity", out var opacity ) && float.TryParse( opacity, System.Globalization.CultureInfo.InvariantCulture, out var alpha ) )
						plane.Color = plane.Color.WithAlpha( alpha );
				}

				if ( node.TargetMapPath is { Length: > 0 } )
				{
					changes.Remove( "angles" );
					changes.Remove( "origin" );
					changes.Remove( "scales" );
					if ( changes.Count == 0 ) continue;
					var nestedValues = node.VariableOverrides is null ? changes : new Dictionary<string, string>( node.VariableOverrides, StringComparer.OrdinalIgnoreCase );
					if ( nestedValues != changes )
						foreach ( var (key, value) in changes ) nestedValues[key] = value;
					ApplyOverrides( obj, node, ResolveChoices( node.Target?.ChoiceGroups, nestedValues ) );
					continue;
				}

				if ( node.EntityKeys is not null )
				{
					var keys = new Dictionary<string, string>( node.EntityKeys, StringComparer.OrdinalIgnoreCase );
					if ( !string.IsNullOrEmpty( node.Entity.TargetName ) ) keys["targetname"] = obj.Name;
					foreach ( var (key, value) in changes ) keys[key] = value;
					obj.Enabled = ConvertEntity( CreateEntityEntry( keys, obj.WorldTransform ), obj, Warnings, node, applyingOverrides: true ) && !hidden;
					if ( node.ResolveSkyboxOverride is { } resolve && changes.TryGetValue( "targetMapName", out var path ) )
						AttachSkybox( obj, string.IsNullOrWhiteSpace( path ) ? null : resolve( path ), path, keys );
					else if ( node.Entity.TypeName == "skybox_reference" && SkyboxComponent( obj ) is { } skybox )
						skybox.GameObject.Enabled = SkyboxEnabled( keys );
				}
				if ( changes.TryGetValue( "rendercolor", out var color ) && Color32.Parse( color ) is { } tint )
					foreach ( var mesh in obj.Components.GetAll<MeshComponent>( FindMode.EverythingInSelf ) ) mesh.Color = tint;
			}
		}
	}
}
