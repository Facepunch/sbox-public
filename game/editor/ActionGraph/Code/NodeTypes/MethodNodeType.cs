using System;
using System.Collections.Generic;
using System.Linq;
using Facepunch.ActionGraphs;
using Sandbox;
using Sandbox.ActionGraphs;

namespace Editor.ActionGraphs;

#nullable enable

/// <summary>
/// Node creation menu entry representing a method call from <see cref="Sandbox.Internal.TypeLibrary"/>.
/// </summary>
public class MethodNodeType : LibraryNodeType
{
	[Event( FindReflectionNodeTypesEvent.EventName )]
	public static void OnFindReflectionNodeTypes( FindReflectionNodeTypesEvent e )
	{
		var methods = e.Members
			.OfType<MethodDescription>()
			.Where( x => !x.IsSpecialName )
			.Where( x => x.AreParametersActionGraphSafe() )
			.GroupBy( x => (x.Name, x.IsStatic) );

		foreach ( var methodGroup in methods )
		{
			try
			{
				e.Output.Add( new MethodNodeType( methodGroup.ToArray() ) );
			}
			catch ( Exception ex )
			{
				var first = methodGroup.First();

				Log.Error( ex, $"Exception when adding node type for method: {first.TypeDescription.TargetType.ToSimpleString()}::{first.Name}" );
			}
		}
	}

	public const int ExpressionsOrder = 200;
	public const int ActionsOrder = 300;

	public MethodDescription[] Methods { get; }

	public override bool AutoExpand { get; }
	public override bool IsCommon => false;

	private static IReadOnlyList<Menu.PathElement> GetStaticPath( MethodDescription method )
	{
		var isPure = method.IsPure( EditorNodeLibrary );
		var path = new List<Menu.PathElement>();

		path.AddRange( MemberPath( method.TypeDescription ) );
		path.Add( isPure
			? new Menu.PathElement( "Static Expressions",
				Order: ExpressionsOrder - 50,
				IsHeading: true )
			: new Menu.PathElement( "Static Actions",
				Order: ActionsOrder - 50,
				IsHeading: true ) );
		path.AddRange( Menu.GetSplitPath( method ) );

		return path;
	}

	private static IReadOnlyDictionary<string, object?> GetProperties( MethodDescription method )
	{
		return new Dictionary<string, object?>
		{
			{ ParameterNames.Type, method.TypeDescription.TargetType },
			{ ParameterNames.Name, method.Name },
			{ ParameterNames.IsStatic, method.IsStatic }
		};
	}

	public MethodNodeType( MethodDescription[] methods )
		: base( EditorNodeLibrary.CallMethod, methods[0].IsStatic ? GetStaticPath( methods[0] ) : null, GetProperties( methods[0] ) )
	{
		Methods = methods;
		AutoExpand = methods[0].GetCustomAttribute<ActionGraphIncludeAttribute>()?.AutoExpand ?? false;
	}
}
