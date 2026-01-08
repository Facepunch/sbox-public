using Facepunch.ActionGraphs;
using Sandbox;
using Sandbox.ActionGraphs;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using PropertyAttribute = Sandbox.PropertyAttribute;

namespace Editor.ActionGraphs;

#nullable enable

partial class ActionGraphView
{
	private static bool PropertyCanContainGraphs( [NotNullWhen( true )] SerializedProperty? property )
	{
		if ( property is null ) return false;
		if ( !property.IsValid ) return false;

		return property.PropertyType == typeof( ActionGraph )
			|| property.PropertyType.IsAssignableTo( typeof( IActionGraphDelegate ) )
			|| property.PropertyType.IsAssignableTo( typeof( Delegate ) );
	}

	private static bool PropertyContainsGraph( SerializedProperty? property, ActionGraph graph )
	{
		if ( property is null ) return false;
		if ( !property.IsValid ) return false;

		try
		{
			if ( property.PropertyType == typeof( ActionGraph ) )
			{
				return property.GetValue<ActionGraph>() == graph;
			}

			if ( property.PropertyType.IsAssignableTo( typeof( IActionGraphDelegate ) ) )
			{
				return property.GetValue<IActionGraphDelegate>()?.Graph == graph;
			}

			if ( property.PropertyType.IsAssignableTo( typeof( Delegate ) ) )
			{
				return property.GetValue<Delegate>()?.GetActionGraphInstances().Any( x => x.Graph == graph ) ?? false;
			}

			return false;
		}
		catch
		{
			return false;
		}
	}

	private static bool PropertyContainsDelegate( SerializedProperty? property, IActionGraphDelegate agDelegate )
	{
		if ( !PropertyCanContainGraphs( property ) ) return false;

		try
		{
			if ( property.PropertyType == typeof( ActionGraph ) )
			{
				return false;
			}

			if ( property.PropertyType.IsAssignableTo( typeof( IActionGraphDelegate ) ) )
			{
				return property.GetValue<IActionGraphDelegate>() == agDelegate;
			}

			if ( property.PropertyType.IsAssignableTo( typeof( Delegate ) ) )
			{
				return property.GetValue<Delegate>()?.GetActionGraphInstances().Contains( agDelegate ) ?? false;
			}

			return false;
		}
		catch
		{
			return false;
		}
	}

	private static SerializedProperty? ResolveProperty( object? hostObject, ActionGraph? graph, IActionGraphDelegate? agDelegate )
	{
		if ( graph is null && agDelegate is null ) return null;
		if ( hostObject is IValid { IsValid: false } ) return null;

		if ( graph?.SourceLocation is GameResourceSourceLocation { Resource: { } res } )
		{
			// If there isn't a HostObject, look in the source GameResource

			hostObject ??= res;
		}

		switch ( hostObject )
		{
			case ActionGraphResource agRes:
				return agRes.GetSerialized().GetProperty( nameof( ActionGraphResource.Graph ) );

			case GameResource gameRes:
				return FindProperty( gameRes, graph, agDelegate );

			case GameObject go:
				{
					foreach ( var cmp in go.Components.GetAll() )
					{
						if ( FindProperty( cmp, graph, agDelegate ) is { } prop )
						{
							return prop;
						}
					}

					break;
				}
		}

		return null;
	}

	private static bool IsPropertyValueSerialized( object inst, SerializedProperty property )
	{
		// TODO: This is repeating the work of ReflectionQueryCache, do we make that public?

		if ( property.IsMethod ) return false;

		// These types are serialized as references, not the actual value

		if ( property.PropertyType.IsAssignableTo( typeof( GameObject ) ) ) return false;
		if ( property.PropertyType.IsAssignableTo( typeof( Component ) ) ) return false;
		if ( property.PropertyType.IsAssignableTo( typeof( Resource ) ) ) return false;

		// Value typed containers not supported yet

		if ( property.PropertyType.IsValueType ) return false;

		if ( property.HasAttribute<JsonIgnoreAttribute>() ) return false;
		if ( property.HasAttribute<JsonIncludeAttribute>() ) return true;
		if ( property.HasAttribute<PropertyAttribute>() ) return true;
		if ( !property.IsPublic ) return false;

		// GameObject / Component members need a Property attribute

		return inst is not GameObject and not Component;
	}

	/// <summary>
	/// Look for a serializable property inside <paramref name="root"/> that contains the given <paramref name="graph"/> or <paramref name="agDelegate"/>.
	/// We want to find this so we know whether the graph is referenced anywhere that can be written to disk, otherwise any changes to the graph will be lost.
	/// </summary>
	private static SerializedProperty? FindProperty( IValid root, ActionGraph? graph, IActionGraphDelegate? agDelegate )
	{
		if ( graph is null && agDelegate is null ) return null;

		// Breadth-first search

		var queue = new Queue<(object Object, SerializedObject So, int Depth)>();
		var visited = new HashSet<object>( ReferenceEqualityComparer.Instance ) { root };

		queue.Enqueue( (root, root.GetSerialized(), 0) );

		const int maxPropertySearchDepth = 32;

		while ( queue.Count > 0 )
		{
			var (obj, nextSo, depth) = queue.Dequeue();

			// Protect against infinite recursion

			if ( depth > maxPropertySearchDepth )
			{
				var path = nextSo.ParentProperty?.FindPathInScene();

				Log.Warning( $"We recursed too deep! {path?.FullName ?? nextSo.ToString()}" );
				continue;
			}

			foreach ( var property in nextSo )
			{
				// We're specifically looking for properties that are serialized

				if ( !IsPropertyValueSerialized( obj, property ) ) continue;

				// Can this property directly contain the graph?

				if ( PropertyCanContainGraphs( property ) )
				{
					if ( agDelegate is not null && PropertyContainsDelegate( property, agDelegate ) )
					{
						return property;
					}

					if ( graph is not null && PropertyContainsGraph( property, graph ) )
					{
						return property;
					}

					continue;
				}

				// Otherwise add the value of this property to the queue

				try
				{
					if ( property.GetValue<object>() is { } value && property.TryGetAsObject( out var so ) )
					{
						// Protect against infinite recursion

						if ( !visited.Add( value ) ) continue;

						queue.Enqueue( (value, so, depth + 1) );
					}
				}
				catch
				{
					// Gracefully handle TryGetAsObject etc throwing, we don't care about the error
				}
			}
		}

		return null;
	}

	public void LogInstances()
	{
		var actionGraph = EditorGraph.Graph;

		var instances = actionGraph.GetDelegates()
			.Select( x => (Delegate: x, Property: ResolveProperty( x.GetEmbeddedTarget(), x.Graph, x )) )
			.Where( x => x.Property is not null )
			.ToArray();

		var writer = new StringWriter();

		writer.WriteLine( $"Instances of {actionGraph.Guid}: {instances.Length}" );

		foreach ( var (instance, property) in instances )
		{
			writer.WriteLine( $"  {instance.DelegateType}" );

			AppendProperty( writer, property! );
		}

		writer.WriteLine( "Current target property:" );

		AppendProperty( writer, Property );

		Log.Info( writer.ToString() );
	}

	private static bool AppendProperty( TextWriter writer, SerializedProperty property )
	{
		if ( property?.FindPathInScene() is not { } path )
		{
			return false;
		}

		foreach ( var prop in path.Properties.Reverse() )
		{
			writer.WriteLine( $"    in {prop.Name}" );
		}

		var target = path.Targets.FirstOrDefault();

		while ( target is not null )
		{
			switch ( target )
			{
				case Component cmp:
					writer.WriteLine( $"    in {cmp} ({RuntimeHelpers.GetHashCode( cmp ):x8})" );
					target = cmp.GameObject;
					break;

				case Scene scene:
					writer.WriteLine( $"    in {scene.Name} ({RuntimeHelpers.GetHashCode( scene ):x8})" );
					target = scene.Source;

					if ( SceneEditorSession.Resolve( scene ) is { } session && session.Scene == scene )
					{
						writer.WriteLine( $"      Editor session (IsPrefab: {session.IsPrefabSession})" );
					}
					break;

				case GameObject go:
					writer.WriteLine( $"    in {go} ({RuntimeHelpers.GetHashCode( go ):x8})" );
					target = go.Parent ?? go.Scene;
					break;

				case GameResource res:
					writer.WriteLine( $"    in {res.ResourcePath} ({RuntimeHelpers.GetHashCode( res ):x8})" );
					target = null;
					break;

				default:
					writer.WriteLine( $"    in {target}" );
					target = null;
					break;
			}
		}

		return true;
	}
}
