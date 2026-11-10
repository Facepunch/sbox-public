using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sandbox;

/// <summary>
/// Symbols whose check reported nothing, shared by the walkers of one compilation and rule set.
/// </summary>
sealed class AccessCheckCache
{
	internal readonly ConcurrentDictionary<ISymbol, byte> Methods = new( SymbolEqualityComparer.Default );
	internal readonly ConcurrentDictionary<ISymbol, byte> Fields = new( SymbolEqualityComparer.Default );
	internal readonly ConcurrentDictionary<ISymbol, byte> Types = new( SymbolEqualityComparer.Default );
	internal readonly ConcurrentDictionary<ISymbol, byte> HardBlockTypes = new( SymbolEqualityComparer.Default );
}

sealed class AccessWalker : CSharpSyntaxWalker
{
	public List<Diagnostic> Diagnostics { get; } = [];

	public static readonly DiagnosticDescriptor Rule = new( "SB500", "Prohibited API", "Prohibited API '{0}' used", "Sandbox.Compiling", DiagnosticSeverity.Error, true );

	readonly SemanticModel _model;
	readonly AccessRules _rules;
	readonly HashSet<(int, string)> _seen = [];
	readonly AccessCheckCache _clean;
	int _reportAttempts;

	static readonly Dictionary<string, string> InteropAttributes = new()
	{
		["System.Runtime.InteropServices.ComImportAttribute"] = "System.Runtime.InteropServices.ComImportAttribute",
		["System.Runtime.InteropServices.FieldOffsetAttribute"] = "System.Runtime.InteropServices.FieldOffset",
		["System.Runtime.InteropServices.MarshalAsAttribute"] = "System.Runtime.InteropServices.MarshalAs",
		["System.Runtime.InteropServices.DllImportAttribute"] = "System.Runtime.InteropServices.DllImportAttribute",
		["System.Runtime.InteropServices.LibraryImportAttribute"] = "System.Runtime.InteropServices.LibraryImportAttribute",
	};

	internal AccessWalker( SemanticModel model, AccessRules rules = null, AccessCheckCache cache = null )
	{
		_model = model;
		_rules = rules ?? AccessRules.Instance;
		_clean = cache ?? new();
	}

	public override void DefaultVisit( SyntaxNode node )
	{
		// The runtime and compiler honour some types by name from any assembly, so a package can't declare its own copy
		if ( node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax && _model.GetDeclaredSymbol( node ) is INamedTypeSymbol declared )
		{
			var declaredName = SymbolSignature.Type( declared );
			if ( _rules.IsBlocked( declaredName ) )
				Report( node, declaredName );
		}

		ISymbol symbol = InvokedMethod( node );
		if ( symbol is null )
		{
			var info = _model.GetSymbolInfo( node );
			symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
		}

		if ( symbol is not null )
			CheckSymbol( node, symbol );

		// from/join/let lower to Cast<T>, SelectMany, Join and Select calls the clause syntax doesn't bind to
		if ( node is QueryClauseSyntax clause )
		{
			var query = _model.GetQueryClauseInfo( clause );
			if ( (query.CastInfo.Symbol ?? query.CastInfo.CandidateSymbols.FirstOrDefault()) is IMethodSymbol cast )
				CheckMethod( node, cast );
			if ( (query.OperationInfo.Symbol ?? query.OperationInfo.CandidateSymbols.FirstOrDefault()) is IMethodSymbol operation )
				CheckMethod( node, operation );
		}

		if ( node is ExpressionSyntax expression )
			CheckConversion( expression );

		base.DefaultVisit( node );
	}

	/// <summary>
	/// The method a name being invoked binds to, taken from the invocation. Asking the name itself makes Roslyn
	/// gather every extension method in scope as a method group candidate, which was most of the analysis time.
	/// Delegate invokes are left alone: there the name is the delegate, not the method.
	/// </summary>
	IMethodSymbol InvokedMethod( SyntaxNode node )
	{
		var group = node switch
		{
			SimpleNameSyntax { Parent: MemberAccessExpressionSyntax access } when access.Name == node => access,
			SimpleNameSyntax { Parent: MemberBindingExpressionSyntax binding } when binding.Name == node => binding,
			_ => node,
		};

		if ( group.Parent is not InvocationExpressionSyntax invocation || invocation.Expression != group )
			return null;

		return _model.GetSymbolInfo( invocation ).Symbol is IMethodSymbol { MethodKind: not MethodKind.DelegateInvoke } method ? method : null;
	}

	public override void VisitInvocationExpression( InvocationExpressionSyntax node )
	{
		// nameof only produces a string, nothing in it is accessed
		if ( node.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" } && _model.GetSymbolInfo( node ).Symbol is null && _model.GetConstantValue( node ).HasValue )
			return;

		base.VisitInvocationExpression( node );
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
		if ( node.ArgumentList is not { } args )
			return;

		foreach ( var arg in args.Arguments )
		{
			// Attribute constructors stay type-gated, but named arguments are member writes.
			if ( arg.NameEquals?.Name is { } named )
			{
				var info = _model.GetSymbolInfo( named );
				switch ( info.Symbol ?? info.CandidateSymbols.FirstOrDefault() )
				{
					case IPropertySymbol { SetMethod: { } setter }:
						CheckMethod( named, setter );
						break;
					case IFieldSymbol field:
						CheckType( named, field.Type );
						break;
				}
			}

			Visit( arg.Expression );
		}
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
		CheckForEach( node );
		base.VisitForEachStatement( node );
	}

	public override void VisitForEachVariableStatement( ForEachVariableStatementSyntax node )
	{
		CheckForEach( node );
		CheckDeconstruction( node, _model.GetDeconstructionInfo( node ) );
		base.VisitForEachVariableStatement( node );
	}

	public override void VisitUsingStatement( UsingStatementSyntax node )
	{
		var isAsync = node.AwaitKeyword != default;

		if ( node.Declaration is { } declaration )
			CheckDisposal( node, _model.GetTypeInfo( declaration.Type ).Type, isAsync );
		else if ( node.Expression is { } expression )
			CheckDisposal( node, _model.GetTypeInfo( expression ).Type, isAsync );

		base.VisitUsingStatement( node );
	}

	public override void VisitLocalDeclarationStatement( LocalDeclarationStatementSyntax node )
	{
		// `using var x = ...;` is a local declaration, not a UsingStatement
		if ( node.UsingKeyword != default )
			CheckDisposal( node, _model.GetTypeInfo( node.Declaration.Type ).Type, node.AwaitKeyword != default );

		base.VisitLocalDeclarationStatement( node );
	}

	public override void VisitLockStatement( LockStatementSyntax node )
	{
		var type = _model.GetTypeInfo( node.Expression ).Type;

		if ( type is not null && SymbolSignature.Type( type ) == "System.Threading.Lock" )
		{
			CheckNamedMethods( node, type, "EnterScope" );
		}
		else if ( _model.Compilation.GetTypeByMetadataName( "System.Threading.Monitor" ) is { } monitor )
		{
			CheckNamedMethods( node, monitor, "Enter", "Exit" );
		}

		base.VisitLockStatement( node );
	}

	public override void VisitAssignmentExpression( AssignmentExpressionSyntax node )
	{
		if ( node.Left is TupleExpressionSyntax or DeclarationExpressionSyntax )
			CheckDeconstruction( node, _model.GetDeconstructionInfo( node ) );

		base.VisitAssignmentExpression( node );
	}

	public override void VisitInitializerExpression( InitializerExpressionSyntax node )
	{
		// Each element is an Add call with no call syntax to bind to.
		if ( node.IsKind( SyntaxKind.CollectionInitializerExpression ) )
		{
			foreach ( var element in node.Expressions )
			{
				var info = _model.GetCollectionInitializerSymbolInfo( element );
				if ( (info.Symbol ?? info.CandidateSymbols.FirstOrDefault()) is IMethodSymbol add )
					CheckMethod( element, add );
			}
		}

		base.VisitInitializerExpression( node );
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

	public override void VisitAwaitExpression( AwaitExpressionSyntax node )
	{
		var info = _model.GetAwaitExpressionInfo( node );
		CheckOptionalMethod( node, info.GetAwaiterMethod );
		CheckOptionalMethod( node, info.IsCompletedProperty?.GetMethod );
		CheckOptionalMethod( node, info.GetResultMethod );
		base.VisitAwaitExpression( node );
	}

	public override void VisitConstructorDeclaration( ConstructorDeclarationSyntax node )
	{
		if ( node.Initializer is null && _model.GetDeclaredSymbol( node ) is { IsStatic: false } constructor )
			CheckImplicitBaseConstructor( node, constructor.ContainingType );

		base.VisitConstructorDeclaration( node );
	}

	public override void VisitClassDeclaration( ClassDeclarationSyntax node )
	{
		CheckDefaultConstructor( node );
		CheckInterfaceMapping( node );
		base.VisitClassDeclaration( node );
	}

	public override void VisitRecordDeclaration( RecordDeclarationSyntax node )
	{
		CheckDefaultConstructor( node );
		CheckInterfaceMapping( node );
		base.VisitRecordDeclaration( node );
	}

	public override void VisitStructDeclaration( StructDeclarationSyntax node )
	{
		CheckInterfaceMapping( node );
		base.VisitStructDeclaration( node );
	}

	// An interface this type adds can be implemented by an inherited member, and calling it through
	// the interface then runs that member with no call to it in source. Interfaces the base already
	// implements are checked where they're called.
	void CheckInterfaceMapping( TypeDeclarationSyntax node )
	{
		if ( _model.GetDeclaredSymbol( node ) is not { } type )
			return;

		var inherited = type.BaseType?.AllInterfaces ?? [];
		foreach ( var member in type.AllInterfaces.Except( inherited, SymbolEqualityComparer.Default ).OfType<INamedTypeSymbol>().SelectMany( i => i.GetMembers() ) )
		{
			switch ( type.FindImplementationForInterfaceMember( member ) )
			{
				case IMethodSymbol m when Gated( m.ContainingType ):
					CheckMethod( node, m );
					break;
				case IPropertySymbol p when Gated( p.ContainingType ):
					CheckOptionalMethod( node, p.GetMethod );
					CheckOptionalMethod( node, p.SetMethod );
					break;
				case IEventSymbol e when Gated( e.ContainingType ):
					CheckOptionalMethod( node, e.AddMethod );
					CheckOptionalMethod( node, e.RemoveMethod );
					break;
			}
		}
	}

	// A class without constructors, or with a primary constructor and no base arguments, still calls base().
	void CheckDefaultConstructor( TypeDeclarationSyntax node )
	{
		if ( _model.GetDeclaredSymbol( node ) is not { } type )
			return;

		if ( node.BaseList?.Types.Any( t => t is PrimaryConstructorBaseTypeSyntax ) == true )
			return;

		var synthesized = node.ParameterList is not null
			|| type.InstanceConstructors.All( c => c.IsImplicitlyDeclared && c.DeclaringSyntaxReferences.IsEmpty );

		if ( synthesized )
			CheckImplicitBaseConstructor( node, type );
	}

	void CheckImplicitBaseConstructor( SyntaxNode node, INamedTypeSymbol type )
	{
		if ( type is not { TypeKind: TypeKind.Class, IsStatic: false, BaseType: { } baseType } )
			return;

		// Overload resolution prefers a parameterless constructor; otherwise any all-optional one applies.
		var applicable = baseType.InstanceConstructors
			.Where( c => c.Parameters.All( p => p.IsOptional || p.IsParams ) )
			.ToArray();

		var parameterless = applicable.Where( c => c.Parameters.Length == 0 ).ToArray();
		foreach ( var constructor in parameterless.Length > 0 ? parameterless : applicable )
			CheckMethod( node, constructor );
	}

	void CheckSymbol( SyntaxNode node, ISymbol symbol )
	{
		switch ( symbol )
		{
			// C# never calls a finalizer directly, so naming one only makes a delegate that runs it on a live object
			case IMethodSymbol m when IsFinalizer( m ):
				Report( node, SymbolSignature.Method( m ) );
				break;
			case IMethodSymbol { MethodKind: MethodKind.LocalFunction } local:
				foreach ( var a in local.TypeArguments )
					CheckType( node, a );
				break;
			// Built-in operators (enum ==, pointer +) compile to opcodes, not calls
			case IMethodSymbol { MethodKind: MethodKind.BuiltinOperator }:
				break;
			case IMethodSymbol m:
				CheckMethod( node, CallTarget( node, m ) );
				break;
			case IPropertySymbol p:
				var (read, write) = GetPropertyAccess( node );
				// A ref-returning property has no setter, assigning to it calls the getter
				if ( p.ReturnsByRef || p.ReturnsByRefReadonly )
					read = true;
				if ( read && p.GetMethod is { } getter )
					CheckMethod( node, CallTarget( node, getter ) );
				if ( write && p.SetMethod is { } setter )
					CheckMethod( node, CallTarget( node, setter ) );
				break;
			case IFieldSymbol { IsConst: false } f:
				CheckField( node, f );
				break;
			// A constant inlines, but an enum value keeps its type once boxed
			case IFieldSymbol { ContainingType.TypeKind: TypeKind.Enum } e when IsBoxed( node ):
				CheckType( node, e.ContainingType );
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

	void CheckForEach( CommonForEachStatementSyntax node )
	{
		var info = _model.GetForEachStatementInfo( node );

		CheckOptionalMethod( node, info.GetEnumeratorMethod );
		CheckOptionalMethod( node, info.MoveNextMethod );
		CheckOptionalMethod( node, info.CurrentProperty?.GetMethod );
		CheckOptionalMethod( node, info.DisposeMethod );

		if ( info.IsAsynchronous )
		{
			CheckAwaitable( node, info.MoveNextMethod?.ReturnType );
			CheckAwaitable( node, info.DisposeMethod?.ReturnType );
		}
	}

	// Lowered awaits have no await syntax to bind, so resolve the awaiter pattern by hand.
	void CheckAwaitable( SyntaxNode node, ITypeSymbol awaitable )
	{
		if ( awaitable is null )
			return;

		foreach ( var getAwaiter in awaitable.GetMembers( "GetAwaiter" ).OfType<IMethodSymbol>() )
		{
			if ( getAwaiter.Parameters.Length > 0 )
				continue;

			CheckMethod( node, getAwaiter );

			foreach ( var isCompleted in getAwaiter.ReturnType.GetMembers( "IsCompleted" ).OfType<IPropertySymbol>() )
				CheckOptionalMethod( node, isCompleted.GetMethod );

			CheckNamedMethods( node, getAwaiter.ReturnType, "GetResult" );
		}
	}

	void CheckDeconstruction( SyntaxNode node, DeconstructionInfo info )
	{
		CheckOptionalMethod( node, info.Method );

		foreach ( var nested in info.Nested )
			CheckDeconstruction( node, nested );
	}

	void CheckDisposal( SyntaxNode node, ITypeSymbol type, bool isAsync )
	{
		if ( type is null )
			return;

		var interfaceName = isAsync ? "System.IAsyncDisposable" : "System.IDisposable";
		var disposeMethod = _model.Compilation.GetTypeByMetadataName( interfaceName )
			?.GetMembers()
			.OfType<IMethodSymbol>()
			.FirstOrDefault();

		if ( disposeMethod is not null && type.FindImplementationForInterfaceMember( disposeMethod ) is IMethodSymbol implementation )
		{
			CheckMethod( node, implementation );
			if ( isAsync )
				CheckAwaitable( node, implementation.ReturnType );
			return;
		}

		// ref structs satisfy `using` with a pattern-based Dispose, without the interface
		foreach ( var method in type.GetMembers( isAsync ? "DisposeAsync" : "Dispose" ).OfType<IMethodSymbol>() )
		{
			if ( method.Parameters.Length > 0 )
				continue;

			CheckMethod( node, method );
			if ( isAsync )
				CheckAwaitable( node, method.ReturnType );
		}
	}

	/// <summary>
	/// Checks every overload, since we can't tell which one lowering will pick. A rule has to cover
	/// all of them to stay silent.
	/// </summary>
	void CheckNamedMethods( SyntaxNode node, ITypeSymbol type, params string[] names )
	{
		foreach ( var name in names )
			foreach ( var method in type.GetMembers( name ).OfType<IMethodSymbol>() )
				CheckMethod( node, method );
	}

	// A user-defined conversion operator is a call the source never spells out.
	void CheckConversion( ExpressionSyntax node )
	{
		var info = _model.GetTypeInfo( node );
		if ( info.Type is null || info.ConvertedType is null )
			return;

		if ( SymbolEqualityComparer.Default.Equals( info.Type, info.ConvertedType ) )
			return;

		var conversion = _model.Compilation.ClassifyConversion( info.Type, info.ConvertedType );
		if ( conversion.IsUserDefined )
			CheckOptionalMethod( node, conversion.MethodSymbol );
	}

	void CheckOptionalMethod( SyntaxNode node, IMethodSymbol method )
	{
		if ( method is not null )
			CheckMethod( node, method );
	}

	// A check only depends on the symbol and the rules, so one that reported nothing never will
	void CheckMethod( SyntaxNode node, IMethodSymbol m )
	{
		if ( _clean.Methods.ContainsKey( m ) )
			return;

		var attempts = _reportAttempts;
		CheckMethodUncached( node, m );
		if ( _reportAttempts == attempts )
			_clean.Methods.TryAdd( m, 0 );
	}

	void CheckMethodUncached( SyntaxNode node, IMethodSymbol m )
	{
		var container = m.ContainingType;
		var sig = SymbolSignature.Method( m );
		if ( _rules.IsBlocked( sig ) )
		{
			Report( node, sig );
			return;
		}

		if ( Gated( container ) )
			CheckAllowed( node, container, sig );

		CheckType( node, m.ReturnType );
		foreach ( var p in m.Parameters )
			CheckType( node, p.Type );
		foreach ( var a in m.TypeArguments )
			CheckType( node, a );
		for ( var outer = container; outer is not null; outer = outer.ContainingType )
		{
			foreach ( var a in outer.TypeArguments )
				CheckType( node, a );
		}
	}

	// A virtual call names the method that introduced the slot, not the override it binds to; base.X() names the override
	static IMethodSymbol CallTarget( SyntaxNode node, IMethodSymbol m )
	{
		if ( AccessTarget( node ) is MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax } )
			return m;

		while ( m.OverriddenMethod is { } overridden )
			m = overridden;

		return m;
	}

	bool IsBoxed( SyntaxNode node )
	{
		var target = AccessTarget( node );
		if ( target is not ExpressionSyntax expression )
			return false;

		if ( _model.GetTypeInfo( expression ).ConvertedType is { IsReferenceType: true } )
			return true;

		return target.Parent is CastExpressionSyntax cast && _model.GetTypeInfo( cast ).Type is { IsReferenceType: true };
	}

	static bool IsFinalizer( IMethodSymbol m )
	{
		if ( m.MethodKind == MethodKind.Destructor )
			return true;

		for ( var current = m; current is not null; current = current.OverriddenMethod )
		{
			if ( current.Name == "Finalize" && current.Parameters.Length == 0 && current.ContainingType.SpecialType == SpecialType.System_Object )
				return true;
		}

		return false;
	}

	// A field passes on its owner type, or on its own member rule like a method (Path.* covers Path.DirectorySeparatorChar)
	void CheckField( SyntaxNode node, IFieldSymbol f )
	{
		if ( _clean.Fields.ContainsKey( f ) )
			return;

		var attempts = _reportAttempts;
		CheckFieldUncached( node, f );
		if ( _reportAttempts == attempts )
			_clean.Fields.TryAdd( f, 0 );
	}

	void CheckFieldUncached( SyntaxNode node, IFieldSymbol f )
	{
		var container = f.ContainingType;
		var sig = SymbolSignature.Type( container.OriginalDefinition ) + "." + f.Name;
		if ( _rules.IsBlocked( sig ) )
		{
			Report( node, sig );
			return;
		}

		if ( Gated( container ) && !_rules.WholesaleAssembly( container.ContainingAssembly.Name )
			&& _rules.IsAllowed( ImplementationAssembly.Of( container ) + "/" + sig ) )
		{
			foreach ( var a in container.TypeArguments )
				CheckType( node, a );
		}
		else
		{
			CheckType( node, container );
		}

		CheckType( node, f.Type );
	}

	void CheckType( SyntaxNode node, ITypeSymbol type, bool checkAllowList = true )
	{
		if ( type is null )
			return;

		var clean = checkAllowList ? _clean.Types : _clean.HardBlockTypes;
		if ( clean.ContainsKey( type ) )
			return;

		var attempts = _reportAttempts;
		CheckTypeUncached( node, type, checkAllowList );
		if ( _reportAttempts == attempts )
			clean.TryAdd( type, 0 );
	}

	void CheckTypeUncached( SyntaxNode node, ITypeSymbol type, bool checkAllowList )
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

				if ( checkAllowList && Gated( n ) )
					CheckAllowed( node, n, sig );
				for ( var outer = n; outer is not null; outer = outer.ContainingType )
				{
					foreach ( var arg in outer.TypeArguments )
						CheckType( node, arg, checkAllowList );
				}
				return;
		}
	}

	void CheckAllowed( SyntaxNode node, INamedTypeSymbol container, string sig )
	{
		if ( _rules.WholesaleAssembly( container.ContainingAssembly.Name ) )
			return;

		var qualified = ImplementationAssembly.Of( container ) + "/" + sig;
		if ( !_rules.IsAllowed( qualified ) )
			Report( node, qualified );
	}

	static bool Gated( ISymbol symbol )
	{
		var assembly = symbol?.ContainingAssembly;
		return assembly is not null && !assembly.Name.StartsWith( "package.", System.StringComparison.OrdinalIgnoreCase );
	}

	/// <summary>
	/// The expression that reads or writes the member named by <paramref name="node"/>. Stepping any
	/// further would confuse `a.Prop.Field = x` (a read of Prop) with `a.Prop = x`.
	/// </summary>
	static SyntaxNode AccessTarget( SyntaxNode node )
	{
		var current = node;
		if ( current.Parent is MemberAccessExpressionSyntax access && access.Name == current )
			current = access;
		else if ( current.Parent is MemberBindingExpressionSyntax binding && binding.Name == current )
			current = binding;

		while ( true )
		{
			if ( current.Parent is ParenthesizedExpressionSyntax )
				current = current.Parent;
			else if ( current.Parent is ArgumentSyntax { Parent: TupleExpressionSyntax tuple } )
				current = tuple;
			else
				return current;
		}
	}

	static (bool Read, bool Write) GetPropertyAccess( SyntaxNode node )
	{
		var current = AccessTarget( node );

		if ( current.Parent is AssignmentExpressionSyntax assignment && assignment.Left == current )
		{
			// `Prop = { ... }` in an object initializer assigns through the getter
			if ( assignment.Right is InitializerExpressionSyntax )
				return (true, false);

			return assignment.IsKind( SyntaxKind.SimpleAssignmentExpression ) ? (false, true) : (true, true);
		}

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
		var current = AccessTarget( node );
		return current.Parent is AssignmentExpressionSyntax a && a.Left == current && a.IsKind( SyntaxKind.SubtractAssignmentExpression );
	}

	bool IsExplicitLayout( AttributeSyntax node )
	{
		if ( node.ArgumentList?.Arguments.FirstOrDefault()?.Expression is not { } expr )
			return false;

		// StructLayout has an Int16 overload too
		return _model.GetConstantValue( expr ) is { HasValue: true, Value: System.IConvertible value } && value.ToInt64( null ) == 2;
	}

	void Report( SyntaxNode node, string name )
	{
		_reportAttempts++;

		var line = node.GetLocation().GetMappedLineSpan().StartLinePosition.Line;
		if ( !_seen.Add( (line, name) ) )
			return;

		Diagnostics.Add( Diagnostic.Create( Rule, node.GetLocation(), name ) );
	}
}
