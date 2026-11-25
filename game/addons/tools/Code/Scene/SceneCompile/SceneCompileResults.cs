using Sandbox.UI;
using System.Collections.Generic;

namespace Editor;

/// <summary>
/// Output sizes, geometry diagnostics and stage timings in the compile window.
/// </summary>
internal sealed class SceneCompileResults : Panel
{
	sealed record Row( string Text, Component Target = null, Row[] Children = null, bool MakeStatic = false );

	readonly SceneCompileSession session = SceneCompileSession.Current;
	readonly Sandbox.UI.TreeView<Row> tree;
	SceneCompileReport report;
	string[] summary;
	string error;
	int warnings;
	int stageCount;

	/// <summary>
	/// Builds the result tree and hooks up actions for its source objects.
	/// </summary>
	public SceneCompileResults()
	{
		tree = AddChild<Sandbox.UI.TreeView<Row>>();
		tree.ExpandOnClick = true;
		tree.GetChildren = r => r.Children;
		tree.OnRow = ( row, item ) => row.Text = item.Text;
		tree.OnSelect = item =>
		{
			if ( item?.Target is { } component && component.IsValid() )
			{
				var editor = SceneEditorSession.Resolve( component.GameObject );
				if ( editor is null ) return;
				using var scope = editor.Scene.Push();
				editor.Selection.Set( component.GameObject );
				editor.FrameTo( component.GameObject.GetBounds() );
			}
		};
		tree.OnContextMenu = item =>
		{
			if ( item?.MakeStatic != true || session.Running || Game.IsPlaying ) return;
			var menu = new Sandbox.UI.Menu();
			menu.AddOption( "Make all static", "push_pin", () => MakeStatic( item.Children ) );
			menu.Open( this, Sandbox.UI.Popup.PositionMode.UnderMouse );
		};
	}

	internal void Refresh()
	{
		if ( report == session.Report && summary == session.Summary && error == session.Error
			&& warnings == session.Warnings.Count && stageCount == session.Stages.Count ) return;
		report = session.Report;
		summary = session.Summary;
		error = session.Error;
		warnings = session.Warnings.Count;
		stageCount = session.Stages.Count;

		var rows = new List<Row>();
		if ( error is not null ) rows.Add( new( error ) );
		if ( summary is not null ) rows.AddRange( summary.Select( text => new Row( text ) ) );
		if ( session.Statistics is { } s )
			rows.Add( new( $"{s.VertexCount:n0} vertices, {s.TriangleCount:n0} triangles, {s.FragmentCount:n0} aggregate fragments" ) );
		if ( warnings > 0 )
			rows.Add( new( $"{warnings:n0} warnings", Children: session.Warnings.Select( w => new Row( w.Message, w.Component ) ).ToArray() ) );
		if ( stageCount > 0 )
			rows.Add( new( "Stage timings", Children: session.Stages.Select( s => new Row( $"{s.Name} — {s.Duration.TotalSeconds:n2} s" ) ).ToArray() ) );
		if ( report is not null )
		{
			rows.Add( new( $"{report.MeshCount:n0} meshes and {report.PropCount:n0} props selected for aggregation" ) );
			var reasons = EditorTypeLibrary.GetEnumDescription( typeof( SceneCompileSkipReason ) );
			foreach ( var group in report.Skipped.GroupBy( s => (s.Label, s.Reason) ).OrderByDescending( g => g.Count() ) )
			{
				var children = group.Select( s => new Row( s.Component.IsValid() ? s.Component.GameObject.Name : "(Deleted object)", s.Component ) ).ToArray();
				rows.Add( new( $"{group.Count():n0} {group.Key.Label} excluded — {reasons.GetEntry( group.Key.Reason ).Title}", Children: children, MakeStatic: group.Key.Reason == SceneCompileSkipReason.NotStatic ) );
			}
		}

		tree.Roots = rows;
	}

	void MakeStatic( Row[] rows )
	{
		if ( session.Running || Game.IsPlaying || SceneEditorSession.Active is not { IsPrefabSession: false, IsMounted: false } editor || editor.Scene != session.Scene ) return;
		var objects = rows.Where( r => r.Target.IsValid() && r.Target.Scene == editor.Scene ).Select( r => r.Target.GameObject ).Where( go => !go.IsStatic ).Distinct().ToArray();
		if ( objects.Length == 0 ) return;

		using var scene = editor.Scene.Push();
		using ( editor.UndoScope( "Make Objects Static" ).WithGameObjectChanges( objects, GameObjectUndoFlags.Properties ).Push() )
		{
			foreach ( var go in objects ) go.IsStatic = true;
		}
		session.Refresh();
	}
}
