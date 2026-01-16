
using System;
using System.Linq;
using Sandbox;
using Sandbox.ActionGraphs;

namespace Editor.ActionGraphs;

partial class ActionGraphView
{
	public override void CleanUp()
	{
		var changed = false;

		changed |= FixSceneReferences();

		if ( changed )
		{
			Log.Info( $"Finished clean up" );

			RebuildFromGraph();
			Save();
		}
		else
		{
			Log.Info( $"Nothing to clean up!" );
		}
	}

	private bool FixSceneReferences()
	{
		var constNodes = ActionGraph.Nodes.Values
			.Where( x => x.Definition == EditorNodeLibrary.Constant )
			.Where( x => x.Properties.Value.Value is Component or GameObject or Resource )
			.ToArray();

		if ( constNodes.Length == 0 )
		{
			return false;
		}

		Log.Info( $"Replacing {constNodes.Length} invalid references..." );

		var links = constNodes
			.SelectMany( x => x.Outputs.Result.Links )
			.ToArray();

		var replacements = constNodes.ToDictionary( x => x, x =>
		{
			if ( x.Properties.Value.Value is Resource resource )
			{
				var resourceRef = ActionGraph.AddNode( EditorNodeLibrary.Get( "resource.ref" )!, x.Parent );

				resourceRef.Properties["T"].Value = resource.GetType();
				resourceRef.Properties["value"].Value = resource;

				return resourceRef;
			}

			var sceneRef = ActionGraph.AddNode( EditorNodeLibrary.Get( "scene.ref" )!, x.Parent );
			var properties = x.Properties.Value.Value switch
			{
				Component comp => ActionGraphEditorExtensions.GetNodeProperties( comp ),
				GameObject go => ActionGraphEditorExtensions.GetNodeProperties( go ),
				_ => throw new NotImplementedException()
			};

			foreach ( var property in properties )
			{
				sceneRef.Properties[property.Key].Value = property.Value;
			}

			return sceneRef;
		} );

		foreach ( var link in links )
		{
			var newSource = replacements[link.Source.Node].Outputs.Result;

			if ( link.IsArrayElement )
			{
				link.Target.SetLink( newSource, link.ArrayIndex );
			}
			else
			{
				link.Target.SetLink( newSource );
			}
		}

		foreach ( var oldNode in constNodes )
		{
			oldNode.Remove();
		}

		return true;
	}
}
