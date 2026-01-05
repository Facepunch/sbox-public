using Facepunch.ActionGraphs;
using Sandbox;
using Sandbox.ActionGraphs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Editor.ActionGraphs;

public sealed class SceneRefGizmo
{
	private static ConditionalWeakTable<GameObject, SceneRefGizmo> Instances { get; } = new();
	private static HashSet<GameObject> KnownTargetObjects { get; } = new();
	private static HashSet<GameObject> RemovedTargetObjects { get; } = new();

	public static void Draw( Scene scene )
	{
		var graphs = EditorNodeLibrary.GetGraphs();

		KnownTargetObjects.Clear();
		RemovedTargetObjects.Clear();

		foreach ( var pair in Instances )
		{
			pair.Value.ClearDelegates();

			RemovedTargetObjects.Add( pair.Key );
		}

		foreach ( var graph in graphs )
		{
			foreach ( var deleg in graph.GetDelegates() )
			{
				if ( deleg.GetEmbeddedTarget() is not GameObject target ) continue;
				if ( target.Scene != scene ) continue;

				KnownTargetObjects.Add( target );
				RemovedTargetObjects.Remove( target );

				var inst = Instances.GetOrAdd( target, x => new SceneRefGizmo( x ) )!;

				inst.AddDelegate( deleg );
			}
		}

		foreach ( var target in RemovedTargetObjects )
		{
			Instances.Remove( target );
		}

		foreach ( var target in KnownTargetObjects )
		{
			if ( Instances.TryGetValue( target, out var inst ) )
			{
				inst!.Draw();
			}
		}
	}

	public static void Trigger( SceneReferenceTriggeredEvent ev )
	{
		if ( !Instances.TryGetValue( ev.Source, out var inst ) ) return;

		inst?.Trigger( ev.Target, ev.Node );
	}

	private GameObject GameObject { get; }

	private HashSet<IActionGraphDelegate> Delegates { get; } = new();
	private HashSet<SceneReferenceNode> References { get; } = new();
	private List<SceneReferenceNode> SortedReferences { get; } = new();
	private Dictionary<SceneReferenceNode, RealTimeSince> LastTriggered { get; } = new();

	public SceneRefGizmo( GameObject go )
	{
		GameObject = go;
	}

	private void ClearDelegates()
	{
		Delegates.Clear();
		References.Clear();
		SortedReferences.Clear();
	}

	private void AddDelegate( IActionGraphDelegate deleg )
	{
		if ( !Delegates.Add( deleg ) ) return;

		foreach ( var sceneRef in deleg.GetSceneReferences() )
		{
			References.Add( sceneRef );
		}
	}

	private void Draw()
	{
		using var gizmoScope = Gizmo.Scope();

		var showAll = Gizmo.IsSelected || Game.ActiveScene is { IsEditor: true };

		SortedReferences.Clear();
		SortedReferences.AddRange( References );
		SortedReferences.Sort( CompareReferences );

		Gizmo.Draw.IgnoreDepth = true;
		Gizmo.Hitbox.DepthBias = 0.1f;

		var screenRect = new Rect( 0f, Gizmo.Camera.Size );

		foreach ( var group in SortedReferences.GroupBy( x => x.TargetObject ) )
		{
			var targetObject = group.Key;
			var minTimeSince = group.Min( x => (float)LastTriggered.GetValueOrDefault( x, float.PositiveInfinity ) );

			if ( !IsActionGraphLinkInRange( targetObject, out var distSqr ) )
			{
				continue;
			}

			if ( Gizmo.Camera.ToScreen( new Line( GameObject.WorldPosition, targetObject.WorldPosition ) ) is not { } screenLine )
			{
				continue;
			}

			if ( screenLine.Clip( screenRect ) is null )
			{
				continue;
			}

			var refPos = group.Key.WorldPosition;
			var camPos = Gizmo.Camera.Position;
			var camForward = Gizmo.CameraTransform.Forward;
			var anyHovered = false;

			const float margin = 18f;
			const string fontFamily = "Roboto";
			const float fontSize = 14f;
			const int fontWeight = 500;
			const TextFlag textFlags = TextFlag.Center | TextFlag.SingleLine;

			var alpha = (16f - MathF.Sqrt( distSqr ) * 16f / MaxActionGraphLinkDebugRange).Clamp( 0f, 1f );

			var textFlipped = screenLine.End.x < screenLine.Start.x;
			var textAngle = textFlipped ? screenLine.AngleDegrees + 180f : screenLine.AngleDegrees;
			var textOffset = screenLine.Tangent.Perpendicular * (textFlipped ? margin : -margin);
			var relTextPos = textOffset;

			foreach ( var item in group )
			{
				if ( (screenLine + relTextPos).Clip( screenRect ) is not { } textLine )
				{
					continue;
				}

				var text = item.Node.ActionGraph.Title;
				var clip = new Vector2( textLine.Length - 8f, 1000f );
				var texture = TextRendering.GetOrCreateTexture( new TextRendering.Scope( text, Gizmo.Draw.Color, fontSize, fontFamily, fontWeight ), clip, textFlags );

				if ( texture.Width <= 16f )
				{
					continue;
				}

				using var itemScope = Gizmo.ObjectScope( GameObject, Transform.Zero );

				var hovered = textLine
					.WithLength( texture.Width - margin )
					.Distance( Gizmo.CursorPosition ) < margin * 0.5f;

				var timeSince = LastTriggered.GetValueOrDefault( item, float.PositiveInfinity );

				var textScope = new TextRendering.Scope( text,
					color: GetSceneReferenceGizmoColor( showAll, Color.White.Darken( hovered ? 0f : 0.2f ), timeSince, alpha ),
					font: fontFamily,
					size: fontSize );

				Gizmo.Draw.ScreenText( textScope, new Rect( textLine.Center, clip ), textAngle, flags: textFlags );

				relTextPos += textOffset;

				if ( hovered && !anyHovered )
				{
					Gizmo.Hitbox.TrySetHovered( 0f );
				}

				if ( Gizmo.WasClicked && Gizmo.Settings.Selection )
				{
					Gizmo.Select();
				}

				if ( Gizmo.IsDoubleClicked )
				{
					var view = ActionGraphView.Open( item.Node.ActionGraph );

					view.SelectNode( item.Node );
					view.CenterOnSelection();
				}

				anyHovered |= hovered;
			}

			var pulse = 1f + MathF.Pow( Math.Max( 1f - minTimeSince, 0f ), 8f ) * 3f;
			var dist = (refPos - camPos).Dot( camForward );

			Gizmo.Draw.Color = GetSceneReferenceGizmoColor( showAll, SceneRefDebugLineColor.Desaturate( anyHovered ? 0.75f : 0f ), minTimeSince, alpha );
			Gizmo.Draw.LineThickness = pulse;

			using ( Gizmo.Hitbox.LineScope() )
			{
				Gizmo.Draw.Line( GameObject.WorldPosition, refPos );
			}

			Gizmo.Draw.LineCircle( refPos, Gizmo.LocalCameraTransform.Rotation.Forward, pulse * dist / 32f );
		}
	}

	private const float MaxActionGraphLinkDebugRange = 2048f;
	private static Color SceneRefDebugLineColor { get; } = Color.Parse( "#E6DB74" )!.Value;

	private bool IsActionGraphLinkInRange( GameObject referenced, out float distSqr )
	{
		if ( referenced == GameObject )
		{
			distSqr = 0f;
			return false;
		}

		var line = new Line( GameObject.WorldPosition, referenced.WorldPosition );

		distSqr = line.SqrDistance( Gizmo.CameraTransform.Position );

		return distSqr < MaxActionGraphLinkDebugRange * MaxActionGraphLinkDebugRange;
	}

	private static Color GetSceneReferenceGizmoColor( bool selected, Color baseColor, float time, float distanceAlpha )
	{
		var alpha = selected ? 1f : (4f - time * 4f).Clamp( 0f, 1f );
		var t = MathF.Pow( Math.Max( 1f - time, 0f ), 8f );

		return Color.Lerp( baseColor, Color.White, t ).WithAlpha( alpha * distanceAlpha );
	}

	private int CompareReferences( SceneReferenceNode a, SceneReferenceNode b )
	{
		if ( a.TargetObject != b.TargetObject )
		{
			return a.TargetObject.Id.CompareTo( b.TargetObject.Id );
		}

		float aLastTriggered = LastTriggered.GetValueOrDefault( a, float.PositiveInfinity );
		float bLastTriggered = LastTriggered.GetValueOrDefault( b, float.PositiveInfinity );

		var lastTriggeredComparison = aLastTriggered.CompareTo( bLastTriggered );

		if ( lastTriggeredComparison != 0 )
		{
			return lastTriggeredComparison;
		}

		if ( a.TargetComponent != b.TargetComponent )
		{
			return (a.TargetComponent?.Id ?? Guid.Empty).CompareTo( b.TargetComponent?.Id );
		}

		return a.Node.Id.CompareTo( b.Node.Id );
	}

	private void Trigger( IValid target, Node node )
	{
		var go = target as GameObject ?? (target as Component)?.GameObject;
		var cmp = target as Component;

		if ( go is null ) return;

		var sceneRef = new SceneReferenceNode( node, go, cmp );

		if ( !References.Contains( sceneRef ) ) return;

		LastTriggered[sceneRef] = 0f;
	}
}
