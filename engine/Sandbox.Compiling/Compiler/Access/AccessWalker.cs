using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sandbox;

sealed class AccessWalker : CSharpSyntaxWalker
{
	public List<Diagnostic> Diagnostics { get; } = [];

	public static readonly DiagnosticDescriptor Rule = new( "SB500", "Prohibited API", "Prohibited API '{0}' used", "Sandbox.Compiling", DiagnosticSeverity.Error, true );

	readonly SemanticModel _model;
	readonly AccessRules _rules;
	readonly HashSet<(int, string)> _seen = [];
	readonly bool _checkAllowList;

	static readonly Dictionary<string, string> InteropAttributes = new()
	{
		["System.Runtime.InteropServices.ComImportAttribute"] = "System.Runtime.InteropServices.ComImportAttribute",
		["System.Runtime.InteropServices.FieldOffsetAttribute"] = "System.Runtime.InteropServices.FieldOffset",
		["System.Runtime.InteropServices.MarshalAsAttribute"] = "System.Runtime.InteropServices.MarshalAs",
		["System.Runtime.InteropServices.DllImportAttribute"] = "System.Runtime.InteropServices.DllImportAttribute",
		["System.Runtime.InteropServices.LibraryImportAttribute"] = "System.Runtime.InteropServices.LibraryImportAttribute",
	};

	internal AccessWalker( SemanticModel model, bool checkAllowList = true, AccessRules rules = null )
	{
		_model = model;
		_checkAllowList = checkAllowList;
		_rules = rules ?? AccessRules.Instance;
	}

	public override void DefaultVisit( SyntaxNode node )
	{
		var info = _model.GetSymbolInfo( node );
		var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
		if ( symbol is not null )
			CheckSymbol( node, symbol );

		base.DefaultVisit( node );
	}

	public override void VisitAttribute( AttributeSyntax node )
	{
		var symbol = _model.GetSymbolInfo( node ).Symbol;
		var attrType = _model.GetTypeInfo( node ).Type as INamedTypeSymbol
			?? (symbol as IMethodSymbol)?.ContainingType
			?? _model.GetSymbolInfo( node.Name ).Symbol as INamedTypeSymbol;

		if ( attrType is not null )
		{
			var name = SymbolSignature.Type( attrType );
			if ( _rules.IsBlocked( name ) )
			{
				Report( node, name );
				VisitArguments( node );
				return;
			}

			if ( name == "System.Runtime.InteropServices.StructLayoutAttribute" )
			{
				if ( IsExplicitLayout( node ) )
					Report( node, "System.Runtime.InteropServices.StructLayout" );
				return;
			}

			if ( InteropAttributes.TryGetValue( name, out var reported ) )
			{
				Report( node, reported );
				return;
			}

			CheckType( node.Name, attrType );
		}

		VisitArguments( node );
	}

	void VisitArguments( AttributeSyntax node )
	{
		if ( node.ArgumentList is { } args )
			foreach ( var arg in args.Arguments )
				Visit( arg.Expression );
	}

	public override void VisitBaseList( BaseListSyntax node )
	{
		foreach ( var t in node.Types )
			CheckType( t.Type, _model.GetTypeInfo( t.Type ).Type );

		base.VisitBaseList( node );
	}

	public override void VisitVariableDeclaration( VariableDeclarationSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitVariableDeclaration( node );
	}

	public override void VisitParameter( ParameterSyntax node )
	{
		if ( node.Type is { } t )
			CheckType( t, _model.GetTypeInfo( t ).Type );
		base.VisitParameter( node );
	}

	public override void VisitMethodDeclaration( MethodDeclarationSyntax node )
	{
		CheckType( node.ReturnType, _model.GetTypeInfo( node.ReturnType ).Type );
		base.VisitMethodDeclaration( node );
	}

	public override void VisitObjectCreationExpression( ObjectCreationExpressionSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitObjectCreationExpression( node );
	}

	public override void VisitTypeOfExpression( TypeOfExpressionSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitTypeOfExpression( node );
	}

	public override void VisitCastExpression( CastExpressionSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitCastExpression( node );
	}

	public override void VisitPropertyDeclaration( PropertyDeclarationSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitPropertyDeclaration( node );
	}

	public override void VisitIndexerDeclaration( IndexerDeclarationSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitIndexerDeclaration( node );
	}

	public override void VisitEventDeclaration( EventDeclarationSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitEventDeclaration( node );
	}

	public override void VisitDelegateDeclaration( DelegateDeclarationSyntax node )
	{
		CheckType( node.ReturnType, _model.GetTypeInfo( node.ReturnType ).Type );
		base.VisitDelegateDeclaration( node );
	}

	public override void VisitOperatorDeclaration( OperatorDeclarationSyntax node )
	{
		CheckType( node.ReturnType, _model.GetTypeInfo( node.ReturnType ).Type );
		base.VisitOperatorDeclaration( node );
	}

	public override void VisitConversionOperatorDeclaration( ConversionOperatorDeclarationSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitConversionOperatorDeclaration( node );
	}

	public override void VisitLocalFunctionStatement( LocalFunctionStatementSyntax node )
	{
		CheckType( node.ReturnType, _model.GetTypeInfo( node.ReturnType ).Type );
		base.VisitLocalFunctionStatement( node );
	}

	public override void VisitDefaultExpression( DefaultExpressionSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitDefaultExpression( node );
	}

	public override void VisitBinaryExpression( BinaryExpressionSyntax node )
	{
		if ( node.IsKind( SyntaxKind.IsExpression ) || node.IsKind( SyntaxKind.AsExpression ) )
			CheckType( node.Right, _model.GetTypeInfo( node.Right ).Type );
		base.VisitBinaryExpression( node );
	}

	public override void VisitDeclarationPattern( DeclarationPatternSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitDeclarationPattern( node );
	}

	public override void VisitTypePattern( TypePatternSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitTypePattern( node );
	}

	public override void VisitConstantPattern( ConstantPatternSyntax node )
	{
		var info = _model.GetSymbolInfo( node.Expression );
		var type = info.Symbol as ITypeSymbol ?? info.CandidateSymbols.OfType<ITypeSymbol>().FirstOrDefault();
		if ( type is not null )
			CheckType( node.Expression, type );

		base.VisitConstantPattern( node );
	}

	public override void VisitCaseSwitchLabel( CaseSwitchLabelSyntax node )
	{
		var info = _model.GetSymbolInfo( node.Value );
		var type = info.Symbol as ITypeSymbol ?? info.CandidateSymbols.OfType<ITypeSymbol>().FirstOrDefault();
		if ( type is not null )
			CheckType( node.Value, type );
		base.VisitCaseSwitchLabel( node );
	}

	public override void VisitRecursivePattern( RecursivePatternSyntax node )
	{
		if ( node.Type is { } rt )
			CheckType( rt, _model.GetTypeInfo( rt ).Type );
		base.VisitRecursivePattern( node );
	}

	public override void VisitArrayCreationExpression( ArrayCreationExpressionSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitArrayCreationExpression( node );
	}

	public override void VisitCatchDeclaration( CatchDeclarationSyntax node )
	{
		if ( node.Type is { } type )
			CheckType( type, _model.GetTypeInfo( type ).Type );
		base.VisitCatchDeclaration( node );
	}

	public override void VisitForEachStatement( ForEachStatementSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitForEachStatement( node );
	}

	public override void VisitTypeConstraint( TypeConstraintSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitTypeConstraint( node );
	}

	public override void VisitSizeOfExpression( SizeOfExpressionSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitSizeOfExpression( node );
	}

	public override void VisitStackAllocArrayCreationExpression( StackAllocArrayCreationExpressionSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitStackAllocArrayCreationExpression( node );
	}

	public override void VisitDeclarationExpression( DeclarationExpressionSyntax node )
	{
		CheckType( node.Type, _model.GetTypeInfo( node.Type ).Type );
		base.VisitDeclarationExpression( node );
	}

	public override void VisitFunctionPointerType( FunctionPointerTypeSyntax node )
	{
		foreach ( var parameter in node.ParameterList.Parameters )
			CheckType( parameter.Type, _model.GetTypeInfo( parameter.Type ).Type );
		base.VisitFunctionPointerType( node );
	}

	void CheckSymbol( SyntaxNode node, ISymbol symbol )
	{
		switch ( symbol )
		{
			case IMethodSymbol m when m.MethodKind != MethodKind.LocalFunction:
				CheckMethod( node, m );
				break;
			case IPropertySymbol p:
				var (read, write) = GetPropertyAccess( node );
				if ( read && p.GetMethod is { } getter )
					CheckMethod( node, getter );
				if ( write && p.SetMethod is { } setter )
					CheckMethod( node, setter );
				break;
			case IFieldSymbol { IsConst: false } f:
				CheckType( node, f.ContainingType );
				CheckType( node, f.Type );
				break;
			case IEventSymbol e:
				var eventAccessor = (IsSubtract( node ) ? e.RemoveMethod : e.AddMethod) ?? e.AddMethod ?? e.RemoveMethod;
				if ( eventAccessor is not null )
					CheckMethod( node, eventAccessor );
				CheckType( node, e.Type );
				break;
			case ITypeSymbol type:
				// Bare type references apply hard blocks; explicit type syntax owns allow-list checks.
				CheckType( node, type, checkAllowList: false );
				break;
		}
	}

	void CheckMethod( SyntaxNode node, IMethodSymbol m )
	{
		var container = m.ContainingType;
		var sig = SymbolSignature.Method( m );
		if ( _rules.IsBlocked( sig ) )
		{
			Report( node, sig );
			return;
		}

		if ( _checkAllowList && Gated( container ) && !_rules.WholesaleAssembly( container.ContainingAssembly.Name ) && !_rules.IsAllowed( sig ) )
			Report( node, sig );

		CheckType( node, m.ReturnType );
		foreach ( var p in m.Parameters )
			CheckType( node, p.Type );
		foreach ( var a in m.TypeArguments )
			CheckType( node, a );
		foreach ( var a in container.TypeArguments )
			CheckType( node, a );
	}

	void CheckType( SyntaxNode node, ITypeSymbol type, bool checkAllowList = true )
	{
		switch ( type )
		{
			case null:
				return;
			case IArrayTypeSymbol a:
				CheckType( node, a.ElementType, checkAllowList );
				return;
			case IPointerTypeSymbol p:
				CheckType( node, p.PointedAtType, checkAllowList );
				return;
			case ITypeParameterSymbol:
				return;
			case IDynamicTypeSymbol:
				Report( node, "dynamic" );
				return;
			case INamedTypeSymbol n:
				var sig = SymbolSignature.Type( n );
				if ( _rules.IsBlocked( sig ) )
				{
					Report( node, sig );
					return;
				}

				if ( checkAllowList && _checkAllowList && Gated( n ) && !_rules.WholesaleAssembly( n.ContainingAssembly.Name ) && !_rules.IsAllowed( sig ) )
					Report( node, sig );
				foreach ( var arg in n.TypeArguments )
					CheckType( node, arg, checkAllowList );
				return;
		}
	}

	static bool Gated( ISymbol symbol )
	{
		var assembly = symbol?.ContainingAssembly;
		return assembly is not null && !assembly.Name.StartsWith( "package.", System.StringComparison.OrdinalIgnoreCase );
	}

	static (bool Read, bool Write) GetPropertyAccess( SyntaxNode node )
	{
		var current = node;
		while ( current.Parent is MemberAccessExpressionSyntax or MemberBindingExpressionSyntax or ParenthesizedExpressionSyntax )
			current = current.Parent;

		if ( current.Parent is AssignmentExpressionSyntax assignment && assignment.Left == current )
			return assignment.IsKind( SyntaxKind.SimpleAssignmentExpression ) ? (false, true) : (true, true);

		if ( current.Parent is PrefixUnaryExpressionSyntax prefix &&
			(prefix.IsKind( SyntaxKind.PreIncrementExpression ) || prefix.IsKind( SyntaxKind.PreDecrementExpression )) )
			return (true, true);

		if ( current.Parent is PostfixUnaryExpressionSyntax postfix &&
			(postfix.IsKind( SyntaxKind.PostIncrementExpression ) || postfix.IsKind( SyntaxKind.PostDecrementExpression )) )
			return (true, true);

		return (true, false);
	}

	static bool IsSubtract( SyntaxNode node )
	{
		for ( var cur = node; cur is ExpressionSyntax; cur = cur.Parent )
		{
			if ( cur.Parent is AssignmentExpressionSyntax a && a.Left == cur )
				return a.IsKind( SyntaxKind.SubtractAssignmentExpression );
			if ( cur.Parent is not MemberAccessExpressionSyntax )
				break;
		}
		return false;
	}

	bool IsExplicitLayout( AttributeSyntax node )
	{
		if ( node.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not { } expr )
			return false;

		return _model.GetConstantValue( expr ) is { HasValue: true, Value: 2 };
	}

	void Report( SyntaxNode node, string name )
	{
		var line = node.GetLocation().GetMappedLineSpan().StartLinePosition.Line;
		if ( !_seen.Add( (line, name) ) )
			return;

		Diagnostics.Add( Diagnostic.Create( Rule, node.GetLocation(), name ) );
	}
}
