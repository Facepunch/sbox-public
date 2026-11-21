using System;
using Facepunch.ActionGraphs;

namespace Editor.MapEditor;

static partial class MapSceneImporter
{
	sealed partial class WorldConversion
	{
		// Names are scoped to the source world or instance definition. Attach the
		// references before cloning so each placement points to its own objects.
		void ResolveReferences( SourceNode root )
		{
			var named = new Dictionary<string, List<GameObject>>( StringComparer.OrdinalIgnoreCase );
			var sources = new List<SourceNode>();
			void AddTarget( string name, GameObject obj )
			{
				if ( !named.TryGetValue( name, out var list ) ) named[name] = list = [];
				list.Add( obj );
			}
			void Visit( SourceNode node )
			{
				if ( node.Connections is not null || node.Kind == NodeKind.Entity && IsConstraint( node.Entity.TypeName ) ) sources.Add( node );
				var targetName = node.Kind == NodeKind.Entity ? node.Entity.TargetName : node.PrefabTargetName;
				if ( _objects.TryGetValue( node, out var obj ) )
				{
					if ( !string.IsNullOrEmpty( targetName ) ) AddTarget( targetName, obj );
					var prefab = node.TypeString == "CMapPrefab" ? node : node.Target;
					if ( prefab is { TypeString: "CMapPrefab", Target: not null, EntityKeys: null } )
						conversion.VisitPrefabTargets( obj, prefab, AddTarget );
				}
				foreach ( var child in node.Children )
					if ( !_hiddenDefinitions.Contains( child ) ) Visit( child );
			}
			Visit( root );
			foreach ( var node in sources )
			{
				var obj = _objects.GetValueOrDefault( node );
				if ( node.Connections is not null )
					ConnectOutputs( node, obj, named );
				if ( obj is not null && node.Kind == NodeKind.Entity && IsConstraint( node.Entity.TypeName ) )
					ConnectConstraint( node.Entity, obj, name => named.GetValueOrDefault( name )?.FirstOrDefault(), conversion.Warnings );
			}
		}

		void ConnectOutputs( SourceNode node, GameObject owner, Dictionary<string, List<GameObject>> named )
		{
			var prop = owner?.Components.Get<Prop>( FindMode.EverythingInSelf );
			var library = Sandbox.Internal.GlobalToolsNamespace.EditorNodeLibrary;
			ActionGraph graph = null;
			foreach ( var link in node.Connections )
			{
				var output = link.Get( "outputName", "" );
				var name = link.Get( "targetName", "" );
				var input = link.Get( "inputName", "" );
				if ( prop is not null && output.Equals( "OnBreak", StringComparison.OrdinalIgnoreCase ) &&
					link.Get( "delay", 0f ) == 0 && link.Get( "timesToFire", -1 ) != 0 &&
					named.TryGetValue( name, out var targets ) )
				{
					bool connected = true;
					foreach ( var target in targets )
					{
						var kill = input.Equals( "Break", StringComparison.OrdinalIgnoreCase );
						Component receiver = kill ? target.Components.Get<Prop>( FindMode.EverythingInSelf )
							: input.Equals( "TurnOff", StringComparison.OrdinalIgnoreCase ) ? target.Components.Get<Light>( FindMode.EverythingInSelf ) : null;
						if ( receiver is null ) { connected = false; continue; }

						if ( graph is null )
						{
							var action = ActionGraph.CreateDelegate<Action>( library );
							graph = action.Graph;
							prop.OnPropBreak = action;
						}
						var call = graph.AddNode( kill ? library.CallMethod : library.Property );
						call.Properties.Type.Value = kill ? typeof( Prop ) : typeof( Component );
						call.Properties.Name.Value = kill ? nameof( Prop.Kill ) : nameof( Component.Enabled );
						call.Inputs.Target.Value = receiver;
						if ( !kill )
						{
							call.Properties.Kind.Value = AssignmentKind.Set;
							call.Inputs["_value"].Value = false;
						}
						call.UpdateParameters();
						call.Inputs.Signal.SetLink( graph.InputNode.Outputs.Signal );
					}
					if ( connected ) continue;
				}
				conversion.Warnings.Add( $"Hammer output {node.NodeId}.{output} to '{name}.{input}' requires manual setup." );
			}
		}
	}
}
