using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Sandbox.Generator
{
	internal class CodeGen
	{
		[Flags]
		internal enum Flags
		{
			WrapPropertyGet = 1,
			WrapPropertySet = 2,
			WrapMethod = 4,
			Static = 8,
			Instance = 16
		}

		/// <summary>
		/// Find anything marked with [CodeGen] and perform the appropriate code generation.
		/// </summary>
		internal static void VisitMethod( ref MethodDeclarationSyntax node, IMethodSymbol symbol, Worker master )
		{
			// This will be true for abstract methods..
			if ( (node.Body == null && node.ExpressionBody == null) || symbol.IsAbstract ) return;

			bool hasTarget = false;
			var attributesToWrite = new List<string>();
			var attributes = symbol.GetAttributes();

			foreach ( var attribute in attributes )
			{
				foreach ( var cg in GetCodeGeneratorAttributes( attribute ) )
				{
					var type = (Flags)int.Parse( cg.GetArgumentValue( 0, "Type", "0" ) );
					var callbackName = cg.GetArgumentValue( 1, "CallbackName", string.Empty );
					if ( !type.Contains( Flags.WrapMethod ) ) continue;

					hasTarget = HandleWrapCall( attribute, type, callbackName, ref node, symbol, master ) || hasTarget;
				}

				// include ALL the attributes when passing to the thing
				AddAttributeString( attribute, attributesToWrite );
			}

			if ( hasTarget && attributesToWrite.Count > 0 )
			{
				var methodIdentity = MakeMethodIdentitySafe( GetUniqueMethodIdentity( symbol ) );
				master.AddToCurrentClass( $"[global::Sandbox.SkipHotload] static readonly global::System.Attribute[] __{methodIdentity}__Attrs = new global::System.Attribute[] {{ {string.Join( ", ", attributesToWrite )} }};\n", false );
			}
		}

		private struct PropertyWrapperData
		{
			public AttributeData Attribute { get; set; }
			public string CallbackName { get; set; }
			public int Priority { get; set; }
			public Flags Type { get; set; }
		}

		internal static void VisitProperty( ref PropertyDeclarationSyntax node, IPropertySymbol symbol, Worker master )
		{
			var attributesToWrite = new List<string>();
			var attributes = symbol.GetAttributes();
			var originalNode = node;
			var data = new List<PropertyWrapperData>();

			foreach ( var attribute in attributes )
			{
				foreach ( var cg in GetCodeGeneratorAttributes( attribute ) )
				{
					var type = (Flags)int.Parse( cg.GetArgumentValue( 0, "Type", "0" ) );
					var callbackName = cg.GetArgumentValue( 1, "CallbackName", string.Empty );
					var priority = int.Parse( cg.GetArgumentValue( 2, "Priority", "0" ) );

					if ( type.Contains( Flags.WrapPropertySet ) || type.Contains( Flags.WrapPropertyGet ) )
					{
						data.Add( new()
						{
							Attribute = attribute,
							CallbackName = callbackName,
							Priority = priority,
							Type = type
						} );
					}

					AddAttributeString( attribute, attributesToWrite );
				}
			}

			data.Sort( ( a, b ) => b.Priority.CompareTo( a.Priority ) );

			foreach ( var w in data )
			{
				if ( w.Type.Contains( Flags.WrapPropertySet ) )
				{
					HandleWrapSet( w.Attribute, w.Type, w.CallbackName, ref node, symbol, master );
				}

				if ( w.Type.Contains( Flags.WrapPropertyGet ) )
				{
					HandleWrapGet( w.Attribute, w.Type, w.CallbackName, ref node, symbol, master );
				}
			}

			if ( attributesToWrite.Count > 0 )
			{
				master.AddToCurrentClass( $"[global::Sandbox.SkipHotload] static readonly global::System.Attribute[] __{symbol.Name}__Attrs = new global::System.Attribute[] {{ {string.Join( ", ", attributesToWrite )} }};\n", false );
			}
		}

		private static void AddAttributeString( AttributeData attribute, List<string> list )
		{
			var sn = attribute.ApplicationSyntaxReference?.GetSyntax() as AttributeSyntax;
			if ( sn is null ) return;

			var attributeClassName = attribute.AttributeClass.FullName();
			var propertyArguments = new List<(string, string)>();
			var regularArguments = new List<string>();

			if ( !attributeClassName.EndsWith( "Attribute" ) )
				attributeClassName += "Attribute";

			var arguments = sn.ArgumentList?.Arguments.ToArray() ?? [];
			if ( arguments.Length == 0 )
			{
				list.Add( $"new {attributeClassName}()" );
				return;
			}

			foreach ( var syntax in arguments )
			{
				if ( syntax.NameColon is not null )
					propertyArguments.Add( (syntax.NameColon.Name.ToString(), syntax.Expression.ToString()) );
				else if ( syntax.NameEquals != null )
					propertyArguments.Add( (syntax.NameEquals.Name.ToString(), syntax.Expression.ToString()) );
				else
					regularArguments.Add( syntax.Expression.ToString() );
			}

			var output = $"new {attributeClassName}( {string.Join( ",", regularArguments )} ) {{ ";

			for ( var i = 0; i < propertyArguments.Count; i++ )
			{
				var (k, v) = propertyArguments[i];
				output += $"{k} = {v}";

				if ( i < propertyArguments.Count - 1 )
				{
					output += ", ";
				}
			}

			list.Add( $"{output} }}" );
		}

		#region Property Wrapping
		private static void HandleWrapSet( AttributeData attribute, Flags type, string callbackName, ref PropertyDeclarationSyntax node, IPropertySymbol symbol, Worker master )
		{
			if ( symbol.IsStatic && !type.Contains( Flags.Static ) )
				return;

			if ( !symbol.IsStatic && !type.Contains( Flags.Instance ) )
				return;

			var typeToInvokeOn = symbol.ContainingType;
			var methodToInvoke = callbackName;
			var splitCallbackName = callbackName.Split( '.' );
			var isStaticCallback = false;

			if ( splitCallbackName.Length > 1 )
			{
				isStaticCallback = true;
				methodToInvoke = splitCallbackName[splitCallbackName.Length - 1];

				var typeToLookFor = string.Join( ".", splitCallbackName.Take( splitCallbackName.Length - 1 ) );
				typeToInvokeOn = master.GetOrCreateTypeByMetadataName( typeToLookFor );

				if ( typeToInvokeOn is null )
				{
					master.AddError( node.GetLocation(),
						$"Unable to find {typeToLookFor} required for {attribute.AttributeClass?.Name}. Ensure that a fully qualified callback name is used." );
					return;
				}
			}

			if ( typeToInvokeOn is null || !ValidateSetterCallback( symbol.ContainingType, typeToInvokeOn, methodToInvoke, isStaticCallback, symbol.Type ) )
			{
				master.AddError( node.GetLocation(),
					$"A method {callbackName}( WrappedPropertySet ) is required on {typeToInvokeOn?.Name}." );

				return;
			}

			var propertyType = symbol.Type.FullName();
			var accessors = new List<AccessorDeclarationSyntax>();

			var existingGetter = node.AccessorList?.Accessors.FirstOrDefault( a => a.Kind() == SyntaxKind.GetAccessorDeclaration );
			var existingSetter = node.AccessorList?.Accessors.FirstOrDefault( a => a.Kind() == SyntaxKind.SetAccessorDeclaration );

			if ( existingSetter is null )
			{
				// There is no setter to wrap.
				return;
			}

			// GET accessor
			if ( existingGetter is not null )
			{
				accessors.Add( existingGetter );
			}

			// SET accessor
			{
				BlockSyntax setterInnerBody;

				if ( existingSetter.ExpressionBody is not null )
				{
					var expr = existingSetter.ExpressionBody.Expression;

					setterInnerBody = Block( ExpressionStatement( expr ) );
				}
				else if ( existingSetter.Body is not null )
				{
					setterInnerBody = existingSetter.Body;
				}
				else
				{
					// Auto-setter: generate field = value;
					var assign = ExpressionStatement(
						AssignmentExpression(
							SyntaxKind.SimpleAssignmentExpression,
							FieldExpression(),
							IdentifierName( "value" ) ) );

					setterInnerBody = Block( assign );
				}

				var setterLambda = ParenthesizedLambdaExpression(
					ParameterList(
						SingletonSeparatedList(
							Parameter( Identifier( "v" ) ) ) ),
					setterInnerBody );

				var memberIdentity = $"{symbol.ContainingType.GetFullMetadataName().Replace( "global::", "" )}.{symbol.Name}";
				var memberHash = memberIdentity.FastHash();

				var wrappedType = ParseTypeName( $"global::Sandbox.WrappedPropertySet<{propertyType}>" );

				var wrappedInitializerExpressions = new List<ExpressionSyntax>
				{
					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "Value" ),
						IdentifierName( "value" ) ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "Object" ),
						symbol.IsStatic
							? LiteralExpression( SyntaxKind.NullLiteralExpression )
							: ThisExpression() ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "Setter" ),
						setterLambda ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "Getter" ),
						ParenthesizedLambdaExpression( IdentifierName( symbol.Name ) ) ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "IsStatic" ),
						LiteralExpression( symbol.IsStatic ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression ) ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "TypeName" ),
						ParseExpression( symbol.ContainingType.FullName().Replace( "global::", "" ).QuoteSafe() ) ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "PropertyName" ),
						ParseExpression( symbol.Name.QuoteSafe() ) ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "MemberIdent" ),
						LiteralExpression( SyntaxKind.NumericLiteralExpression, Literal( memberHash ) ) ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "Attributes" ),
						IdentifierName( $"__{symbol.Name}__Attrs" ) )
				};

				var parameterStructExpr =
					ObjectCreationExpression( wrappedType )
						.WithInitializer(
							InitializerExpression(
								SyntaxKind.ObjectInitializerExpression,
								SeparatedList( wrappedInitializerExpressions ) ) );

				var callbackExpr = ParseExpression( callbackName );
				var argList = ArgumentList(
					SingletonSeparatedList(
						Argument( parameterStructExpr ) ) );

				var invocation = InvocationExpression( callbackExpr, argList );

				StatementSyntax[] statements =
				[
					ExpressionStatement( invocation )
				];

				var set = AccessorDeclaration( SyntaxKind.SetAccessorDeclaration )
					.WithBody( Block( statements ) )
					.WithModifiers( existingSetter.Modifiers );

				accessors.Add( set );

				node = node.WithAccessorList( AccessorList( List( accessors ) ) )
					.NormalizeWhitespace();
			}
		}

		private static void HandleWrapGet( AttributeData attribute, Flags type, string callbackName, ref PropertyDeclarationSyntax node, IPropertySymbol symbol, Worker master )
		{
			if ( symbol.IsStatic && !type.Contains( Flags.Static ) )
				return;

			if ( !symbol.IsStatic && !type.Contains( Flags.Instance ) )
				return;

			var typeToInvokeOn = symbol.ContainingType;
			var methodToInvoke = callbackName;
			var splitCallbackName = callbackName.Split( '.' );
			var isStaticCallback = false;

			if ( splitCallbackName.Length > 1 )
			{
				isStaticCallback = true;
				methodToInvoke = splitCallbackName[splitCallbackName.Length - 1];

				var typeToLookFor = string.Join( ".", splitCallbackName.Take( splitCallbackName.Length - 1 ) );
				typeToInvokeOn = master.GetOrCreateTypeByMetadataName( typeToLookFor );

				if ( typeToInvokeOn is null )
				{
					master.AddError( node.GetLocation(),
						$"Unable to find {typeToLookFor} required for {attribute.AttributeClass?.Name}. Ensure that a fully qualified callback name is used." );
					return;
				}
			}

			var propertyType = symbol.Type.FullName();

			if ( typeToInvokeOn is null || !ValidateGetterCallback( symbol.ContainingType, typeToInvokeOn, methodToInvoke, isStaticCallback, symbol.Type ) )
			{
				master.AddError( node.GetLocation(),
					$"A method {symbol.Type.Name} {methodToInvoke}( WrappedPropertyGet ) is required on {typeToInvokeOn?.Name}." );

				return;
			}

			var accessors = new List<AccessorDeclarationSyntax>();

			var existingGetter = node.AccessorList?.Accessors.FirstOrDefault( a => a.Kind() == SyntaxKind.GetAccessorDeclaration );
			var existingSetter = node.AccessorList?.Accessors.FirstOrDefault( a => a.Kind() == SyntaxKind.SetAccessorDeclaration );

			if ( existingGetter is null )
			{
				// There is no getter to wrap.
				return;
			}

			// SET accessor
			if ( existingSetter is not null )
			{
				accessors.Add( existingSetter );
			}

			// GET accessor
			{
				var statements = new List<StatementSyntax>();
				ExpressionSyntax defaultValueExpression;

				if ( existingGetter.ExpressionBody is not null )
				{
					defaultValueExpression = existingGetter.ExpressionBody.Expression;
				}
				else if ( existingGetter.Body is not null )
				{
					var body = existingGetter.Body;

					var declarator = VariableDeclarator( Identifier( "getValue" ) )
						.WithInitializer( EqualsValueClause( ParenthesizedLambdaExpression( body ) ) );

					statements.Add( LocalDeclarationStatement(
						VariableDeclaration( IdentifierName( "var" ) )
							.WithVariables( SingletonSeparatedList( declarator ) ) ) );

					defaultValueExpression = InvocationExpression( IdentifierName( "getValue" ) );
				}
				else
				{
					// Auto-getter: use the backing field directly
					defaultValueExpression = FieldExpression();
				}

				var memberIdentity = $"{symbol.ContainingType.GetFullMetadataName().Replace( "global::", "" )}.{symbol.Name}";
				var memberHash = memberIdentity.FastHash();

				var wrappedType = ParseTypeName( $"global::Sandbox.WrappedPropertyGet<{propertyType}>" );

				var wrappedInitializerExpressions = new List<ExpressionSyntax>
				{
					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "Value" ),
						defaultValueExpression ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "Object" ),
						symbol.IsStatic
							? LiteralExpression( SyntaxKind.NullLiteralExpression )
							: ThisExpression() ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "IsStatic" ),
						LiteralExpression( symbol.IsStatic ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression ) ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "TypeName" ),
						ParseExpression( symbol.ContainingType.FullName().Replace( "global::", "" ).QuoteSafe() ) ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "PropertyName" ),
						ParseExpression( symbol.Name.QuoteSafe() ) ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "MemberIdent" ),
						LiteralExpression( SyntaxKind.NumericLiteralExpression, Literal( memberHash ) ) ),

					AssignmentExpression(
						SyntaxKind.SimpleAssignmentExpression,
						IdentifierName( "Attributes" ),
						IdentifierName( $"__{symbol.Name}__Attrs" ) )
				};

				var parameterStructExpr =
					ObjectCreationExpression( wrappedType )
						.WithInitializer(
							InitializerExpression(
								SyntaxKind.ObjectInitializerExpression,
								SeparatedList( wrappedInitializerExpressions ) ) );

				var callbackExpr = ParseExpression( callbackName );
				var argList = ArgumentList(
					SingletonSeparatedList(
						Argument( parameterStructExpr ) ) );

				var invocation = InvocationExpression( callbackExpr, argList );

				var returnTypeSyntax = ParseTypeName( propertyType );

				statements.Add(
					ReturnStatement(
						CastExpression(
							returnTypeSyntax,
							invocation ) ) );

				var get = AccessorDeclaration( SyntaxKind.GetAccessorDeclaration )
					.WithBody( Block( statements ) )
					.WithModifiers( existingGetter.Modifiers );

				accessors.Add( get );

				node = node.WithAccessorList( AccessorList( List( accessors ) ) )
					.NormalizeWhitespace();
			}
		}
		#endregion

		#region Method Wrapping

		private static ExpressionSyntax BuildWrappedMethodExpression( IMethodSymbol symbol, CSharpSyntaxNode resumeBodyNode, int methodIdentity, bool usesObjectFallback = false )
		{
			var hasReturn = !symbol.ReturnsVoid;

			string parameterStructGenericType;

			if ( !hasReturn )
			{
				parameterStructGenericType = string.Empty;
			}
			else if ( usesObjectFallback )
			{
				// Use object (or Task<object> for async Task<T>)
				var fullReturnType = symbol.ReturnType.FullName();
				parameterStructGenericType = fullReturnType.StartsWith( "global::System.Threading.Tasks.Task<" ) ? "<global::System.Threading.Tasks.Task<object>>" : "<object>";
			}
			else
			{
				parameterStructGenericType = $"<{symbol.ReturnType.FullName()}>";
			}

			var wrappedTypeName = $"global::Sandbox.WrappedMethod{parameterStructGenericType}";
			var wrappedType = ParseTypeName( wrappedTypeName );

			var resumeLambda = ParenthesizedLambdaExpression( resumeBodyNode );
			if ( symbol.IsAsync )
			{
				resumeLambda = resumeLambda.WithAsyncKeyword( Token( SyntaxKind.AsyncKeyword ) );
			}

			var typeName = symbol.ContainingType.FullName().Replace( "global::", "" );
			var attrsFieldName = $"__{MakeMethodIdentitySafe( methodIdentity )}__Attrs";

			ExpressionSyntax genericArgsExpression;

			if ( symbol.IsGenericMethod )
			{
				var typeofExpressions = symbol.TypeArguments
					.Select( t => TypeOfExpression( ParseTypeName( t.ToDisplayString( SymbolDisplayFormat.FullyQualifiedFormat ) ) ) )
					.ToArray();

				genericArgsExpression = ImplicitArrayCreationExpression(
					InitializerExpression(
						SyntaxKind.ArrayInitializerExpression,
						SeparatedList<ExpressionSyntax>( typeofExpressions ) ) );
			}
			else
			{
				genericArgsExpression = LiteralExpression( SyntaxKind.NullLiteralExpression );
			}

			var assignments = new List<ExpressionSyntax>
			{
				AssignmentExpression(
					SyntaxKind.SimpleAssignmentExpression,
					IdentifierName( "Resume" ),
					resumeLambda ),

				AssignmentExpression(
					SyntaxKind.SimpleAssignmentExpression,
					IdentifierName( "Object" ),
					symbol.IsStatic
						? LiteralExpression( SyntaxKind.NullLiteralExpression )
						: ThisExpression() ),

				AssignmentExpression(
					SyntaxKind.SimpleAssignmentExpression,
					IdentifierName( "MethodIdentity" ),
					LiteralExpression( SyntaxKind.NumericLiteralExpression, Literal( methodIdentity ) ) ),

				AssignmentExpression(
					SyntaxKind.SimpleAssignmentExpression,
					IdentifierName( "MethodName" ),
					ParseExpression( symbol.Name.QuoteSafe() ) ),

				AssignmentExpression(
					SyntaxKind.SimpleAssignmentExpression,
					IdentifierName( "TypeName" ),
					ParseExpression( typeName.QuoteSafe() ) ),

				AssignmentExpression(
					SyntaxKind.SimpleAssignmentExpression,
					IdentifierName( "IsStatic" ),
					LiteralExpression( symbol.IsStatic ? SyntaxKind.TrueLiteralExpression : SyntaxKind.FalseLiteralExpression ) ),

				AssignmentExpression(
					SyntaxKind.SimpleAssignmentExpression,
					IdentifierName( "Attributes" ),
					IdentifierName( attrsFieldName ) ),

				AssignmentExpression(
					SyntaxKind.SimpleAssignmentExpression,
					IdentifierName( "GenericArguments" ),
					genericArgsExpression )
			};

			return ObjectCreationExpression( wrappedType )
				.WithInitializer(
					InitializerExpression(
						SyntaxKind.ObjectInitializerExpression,
						SeparatedList( assignments ) ) );
		}

		private static ExpressionSyntax BuildCallbackInvocation( string callbackName, ExpressionSyntax wrappedMethodExpr, IEnumerable<IParameterSymbol> parameters )
		{
			var callbackExpr = ParseExpression( callbackName );

			var args = new List<ArgumentSyntax>
			{
				Argument( wrappedMethodExpr )
			};

			args.AddRange(
				parameters.Select(
					p => Argument( IdentifierName( p.Name ) ) ) );

			return InvocationExpression(
				callbackExpr,
				ArgumentList( SeparatedList( args ) ) );
		}

		private static bool HandleWrapCall( AttributeData attribute, Flags type, string callbackName, ref MethodDeclarationSyntax node, IMethodSymbol symbol, Worker master )
		{
			if ( node.Body == null && node.ExpressionBody == null ) return false;

			var parameterCount = symbol.Parameters.Count();

			if ( symbol.IsStatic && !type.Contains( Flags.Static ) )
				return false;

			if ( !symbol.IsStatic && !type.Contains( Flags.Instance ) )
				return false;

			var usesObjectFallback = false;
			var typeToInvokeOn = symbol.ContainingType;
			var methodToInvoke = callbackName;
			var splitCallbackName = callbackName.Split( '.' );
			var isStaticCallback = false;

			if ( splitCallbackName.Length > 1 )
			{
				isStaticCallback = true;
				methodToInvoke = splitCallbackName[splitCallbackName.Length - 1];

				var typeToLookFor = string.Join( ".", splitCallbackName.Take( splitCallbackName.Length - 1 ) );
				typeToInvokeOn = master.GetOrCreateTypeByMetadataName( typeToLookFor );

				if ( typeToInvokeOn is null )
				{
					master.AddError( node.GetLocation(),
						$"Unable to find {typeToLookFor} required for {attribute.AttributeClass?.Name}. Ensure that a fully qualified callback name is used." );
					return false;
				}
			}

			var success = false;

			if ( typeToInvokeOn is not null )
			{
				success = ValidateMethodCallback( symbol.ContainingType, typeToInvokeOn, methodToInvoke,
					isStaticCallback, !symbol.ReturnsVoid ? symbol.ReturnType : null, parameterCount, out usesObjectFallback );
			}

			if ( !success )
			{
				var returnType = symbol.ReturnsVoid ? string.Empty : $"{symbol.ReturnType.Name} ";
				var paramsString = string.Join( ", ", Enumerable.Repeat( "Object", parameterCount ) );

				if ( symbol.ReturnsVoid )
				{
					master.AddError( node.GetLocation(),
						parameterCount > 0
							? $"A method {returnType}{methodToInvoke}( WrappedMethod, {paramsString} ) is required on {typeToInvokeOn?.Name}."
							: $"A method {returnType}{methodToInvoke}( WrappedMethod ) is required on {typeToInvokeOn?.Name}." );
				}
				else
				{
					master.AddError( node.GetLocation(),
						parameterCount > 0
							? $"A method {returnType}{methodToInvoke}( WrappedMethod<{symbol.ReturnType.Name}>, {paramsString} ) is required on {typeToInvokeOn?.Name}."
							: $"A method {returnType}{methodToInvoke}( WrappedMethod<{symbol.ReturnType.Name}> ) is required on {typeToInvokeOn?.Name}." );
				}

				return false;
			}

			// Capture original body/expression before we replace them
			var originalBody = node.Body;
			var originalExpressionBody = node.ExpressionBody;

			CSharpSyntaxNode resumeBodyNode;

			if ( originalBody is not null )
			{
				resumeBodyNode = originalBody;
			}
			else
			{
				resumeBodyNode = originalExpressionBody.Expression;
			}

			var methodIdentity = GetUniqueMethodIdentity( symbol );
			var wrappedMethodExpr = BuildWrappedMethodExpression( symbol, resumeBodyNode, methodIdentity, usesObjectFallback );
			var callbackInvocation = BuildCallbackInvocation( callbackName, wrappedMethodExpr, symbol.Parameters );

			var fullReturnType = symbol.ReturnType.FullName();
			var isGenericTaskType = fullReturnType.StartsWith( "global::System.Threading.Tasks.Task<" );
			var isTaskType = fullReturnType == "global::System.Threading.Tasks.Task";

			if ( originalExpressionBody is null )
			{
				List<StatementSyntax> statements;

				if ( symbol.IsAsync )
				{
					if ( isGenericTaskType )
					{
						if ( usesObjectFallback )
						{
							var innerType = (symbol.ReturnType as INamedTypeSymbol)?.TypeArguments[0];
							var innerTypeSyntax = ParseTypeName( innerType.FullName() );

							statements =
							[
								LocalDeclarationStatement(
									VariableDeclaration( IdentifierName( "var" ) )
										.WithVariables( SingletonSeparatedList(
											VariableDeclarator( "__result" )
												.WithInitializer( EqualsValueClause( AwaitExpression( callbackInvocation ) ) ) ) ) ),
								ReturnStatement(
									CastExpression( innerTypeSyntax, IdentifierName( "__result" ) ) )
							];
						}
						else
						{
							// return await Callback(...);
							statements =
							[
								ReturnStatement(
									AwaitExpression( callbackInvocation ) )
							];
						}
					}
					else if ( isTaskType )
					{
						// await Callback(...); return;
						statements =
						[
							ExpressionStatement(
								AwaitExpression( callbackInvocation ) ),

							ReturnStatement()
						];
					}
					else if ( symbol.ReturnsVoid )
					{
						// Callback(...);
						statements = [ExpressionStatement( callbackInvocation )];
					}
					else
					{
						// return Callback(...);
						statements = [ReturnStatement( callbackInvocation )];
					}
				}
				else
				{
					var list = new List<StatementSyntax>();
					if ( symbol.ReturnsVoid )
					{
						list.Add( ExpressionStatement( callbackInvocation ) );
					}
					else
					{
						list.Add( ReturnStatement( callbackInvocation ) );
					}

					statements = list;
				}

				var block = Block( statements );

				var newBody = block.WithCloseBraceToken(
					block.CloseBraceToken.WithTrailingTrivia( SyntaxTriviaList.Empty ) );

				node = node
					.WithBody( newBody )
					.WithExpressionBody( null )
					.WithSemicolonToken( Token( SyntaxKind.None ) )
					.NormalizeWhitespace();
			}
			else
			{
				if ( symbol.IsAsync && isTaskType )
				{
					var awaitExpr = AwaitExpression(
						Token( SyntaxKind.AwaitKeyword ),
						callbackInvocation );

					var statements = new StatementSyntax[]
					{
						ExpressionStatement( awaitExpr ),
						ReturnStatement()
					};

					node = node
						.WithExpressionBody( null )
						.WithSemicolonToken( Token( SyntaxKind.None ) )
						.WithBody( Block( statements ) )
						.NormalizeWhitespace();
				}
				else
				{
					ExpressionSyntax expression = callbackInvocation;

					if ( symbol.IsAsync && isGenericTaskType )
					{
						expression = AwaitExpression( callbackInvocation );
					}

					node = node.WithExpressionBody(
						ArrowExpressionClause( expression ) )
						.NormalizeWhitespace();
				}
			}

			return true;
		}
		#endregion

		private static readonly Dictionary<string, string> TypeAliases = new()
		{
			["object"] = "System.Object",
			["string"] = "System.String",
			["bool"] = "System.Boolean",
			["byte"] = "System.Byte",
			["sbyte"] = "System.SByte",
			["short"] = "System.Int16",
			["ushort"] = "System.UInt16",
			["int"] = "System.Int32",
			["uint"] = "System.UInt32",
			["long"] = "System.Int64",
			["ulong"] = "System.UInt64",
			["float"] = "System.Single",
			["double"] = "System.Double",
			["decimal"] = "System.Decimal",
			["char"] = "System.Char"
		};

		private static string SanitizeTypeName( ITypeSymbol type, bool fullName = false )
		{
			if ( type is IArrayTypeSymbol a ) return $"{SanitizeTypeName( a.ElementType )}[]";

			if ( !fullName )
			{
				return TypeAliases.TryGetValue( type.Name, out var alias ) ? alias : type.Name;
			}

			return type.FullName()
				.Replace( "global::", "" )
				.Split( '<' )
				.FirstOrDefault();
		}

		private static string GetUniqueMethodIdentityString( IMethodSymbol method )
		{
			// Needs to keep in sync with Sandbox.MethodDescription.GetIdentityHashString()

			var returnTypeName = method.ReturnsVoid ? "Void" : SanitizeTypeName( method.ReturnType );
			return $"{returnTypeName}.{SanitizeTypeName( method.ContainingType, true )}.{method.Name}.{string.Join( ",", method.Parameters.Select( p => SanitizeTypeName( p.Type ) ) )}";
		}

		private static int GetUniqueMethodIdentity( IMethodSymbol method )
		{
			return GetUniqueMethodIdentityString( method ).FastHash();
		}

		private static string MakeMethodIdentitySafe( int identity )
		{
			return identity.ToString().Replace( "-", "m_" );
		}

		private static IEnumerable<IMethodSymbol> FetchValidMethods( INamedTypeSymbol parent, string methodName, bool isStatic = false, bool isRootType = false )
		{
			var validMethods = parent.GetMembers().OfType<IMethodSymbol>()
				.Where( s => (!isStatic || s.IsStatic) && s.Name == methodName )
				.Where( s => s.DeclaredAccessibility != Accessibility.Private || isRootType );

			foreach ( var symbol in validMethods )
			{
				yield return symbol;
			}

			// If our target method is static we shouldn't look at base types.
			if ( isStatic )
				yield break;

			if ( parent.BaseType is null )
				yield break;

			foreach ( var symbol in FetchValidMethods( parent.BaseType, methodName ) )
			{
				yield return symbol;
			}
		}

		private static bool ValidateMethodCallback( INamedTypeSymbol containingType, INamedTypeSymbol parent, string methodName, bool isStatic, ITypeSymbol returnType, int argCount, out bool usesObjectFallback )
		{
			usesObjectFallback = false;
			var validMethods = FetchValidMethods( parent, methodName, isStatic, SymbolEqualityComparer.Default.Equals( containingType, parent ) );

			foreach ( var method in validMethods )
			{
				if ( IsValidMethodCallback( method, returnType, argCount, requireExactType: true ) )
					return true;
			}

			// Second pass: look for object fallback (only if we have a return type)
			if ( returnType is null )
				return false;

			foreach ( var method in validMethods )
			{
				if ( !IsValidMethodCallback( method, returnType, argCount, requireExactType: false ) )
					continue;

				usesObjectFallback = true;
				return true;
			}

			return false;
		}

		private static bool IsValidMethodCallback( IMethodSymbol method, ITypeSymbol returnType, int argCount, bool requireExactType )
		{
			var hasObjectParams = method.Parameters.Length > 1 && method.Parameters[1].IsParams && method.Parameters[1].Type.FullName() == "object[]";

			if ( !hasObjectParams && method.Parameters.Length != argCount + 1 )
				return false;

			var firstParameterType = method.Parameters[0].Type;
			var firstParameterName = firstParameterType.FullName();

			if ( returnType is null )
			{
				return firstParameterName == "global::Sandbox.WrappedMethod";
			}

			if ( !firstParameterName.StartsWith( "global::Sandbox.WrappedMethod<" ) )
				return false;

			var namedParam = firstParameterType as INamedTypeSymbol;
			var wrappedArg = namedParam?.TypeArguments[0];

			if ( wrappedArg is null )
				return false;

			if ( requireExactType )
			{
				// Exact match or compatible generic
				if ( !SymbolEqualityComparer.Default.Equals( wrappedArg, returnType )
					&& !IsTypeCompatible( wrappedArg, returnType ) )
				{
					return false;
				}

				var cbReturn = method.ReturnType;

				if ( !SymbolEqualityComparer.Default.Equals( cbReturn, returnType )
					&& !IsTypeCompatible( cbReturn, returnType )
					&& cbReturn is not ITypeParameterSymbol )
				{
					return false;
				}
			}
			else
			{
				if ( !IsObjectOrTaskOfObject( wrappedArg, returnType ) )
					return false;

				var cbReturn = method.ReturnType;
				if ( !IsObjectOrTaskOfObject( cbReturn, returnType ) && cbReturn is not ITypeParameterSymbol )
					return false;
			}

			return true;
		}

		private static bool IsObjectOrTaskOfObject( ITypeSymbol candidate, ITypeSymbol targetForShape )
		{
			if ( candidate.SpecialType == SpecialType.System_Object )
				return true;

			// Check for Task<object> when the target is Task<T>
			if ( candidate is not INamedTypeSymbol namedCandidate || targetForShape is not INamedTypeSymbol namedTarget )
				return false;

			// Both must be generic Task<T>
			var isCandidateGenericTask = namedCandidate.Name == "Task"
				 && namedCandidate.TypeArguments.Length == 1
				 && namedCandidate.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";

			var isTargetGenericTask = namedTarget.Name == "Task"
				  && namedTarget.TypeArguments.Length == 1
				  && namedTarget.ContainingNamespace?.ToDisplayString() == "System.Threading.Tasks";

			if ( !isCandidateGenericTask || !isTargetGenericTask )
				return false;

			// Candidate should be Task<object>
			return namedCandidate.TypeArguments[0].SpecialType == SpecialType.System_Object;
		}

		private static bool IsTypeCompatible( ITypeSymbol candidate, ITypeSymbol target )
		{
			if ( candidate is ITypeParameterSymbol )
				return true;

			if ( candidate is not INamedTypeSymbol namedCandidate || target is not INamedTypeSymbol namedTarget )
				return false;

			if ( !SymbolEqualityComparer.Default.Equals( namedCandidate.OriginalDefinition, namedTarget.OriginalDefinition ) )
				return false;

			var candidateArgs = namedCandidate.TypeArguments;
			var targetArgs = namedTarget.TypeArguments;

			if ( candidateArgs.Length != targetArgs.Length )
				return false;

			for ( var i = 0; i < candidateArgs.Length; i++ )
			{
				var candidateArg = candidateArgs[i];
				var targetArg = targetArgs[i];

				if ( SymbolEqualityComparer.Default.Equals( candidateArg, targetArg ) )
					continue;

				if ( candidateArg is ITypeParameterSymbol )
					continue;

				if ( candidateArg is not INamedTypeSymbol candidateNamedArg || targetArg is not INamedTypeSymbol targetNamedArg )
					return false;

				if ( !IsTypeCompatible( candidateNamedArg, targetNamedArg ) )
					return false;
			}

			return true;
		}

		private static bool ValidateSetterCallback( INamedTypeSymbol containingType, INamedTypeSymbol parent, string methodName, bool isStatic, ITypeSymbol propertyType )
		{
			var validMethods = FetchValidMethods( parent, methodName, isStatic, SymbolEqualityComparer.Default.Equals( containingType, parent ) );

			foreach ( var method in validMethods )
			{
				if ( method.Parameters.Count() != 1 )
					continue;

				if ( !method.Parameters[0].Type.FullName().StartsWith( "global::Sandbox.WrappedPropertySet<" ) )
					continue;

				var namedParameterType = method.Parameters[0].Type as INamedTypeSymbol;
				if ( !SymbolEqualityComparer.Default.Equals( namedParameterType?.TypeArguments[0], propertyType )
					 && namedParameterType?.TypeArguments[0] is not ITypeParameterSymbol )
					continue;

				return true;
			}

			return false;
		}

		private static bool ValidateGetterCallback( INamedTypeSymbol containingType, INamedTypeSymbol parent, string methodName, bool isStatic, ITypeSymbol propertyType )
		{
			var validMethods = FetchValidMethods( parent, methodName, isStatic, SymbolEqualityComparer.Default.Equals( containingType, parent ) );

			foreach ( var method in validMethods )
			{
				if ( method.Parameters.Count() != 1 )
					continue;

				if ( !method.Parameters[0].Type.FullName().StartsWith( "global::Sandbox.WrappedPropertyGet<" ) )
					continue;

				var namedParameterType = method.Parameters[0].Type as INamedTypeSymbol;
				if ( !SymbolEqualityComparer.Default.Equals( namedParameterType?.TypeArguments[0], propertyType )
					 && namedParameterType?.TypeArguments[0] is not ITypeParameterSymbol )
					continue;

				return true;
			}

			return false;
		}

		private static bool IsCodeGeneratorAttribute( AttributeData attribute )
		{
			return attribute.AttributeClass.FullName() == "global::Sandbox.CodeGeneratorAttribute";
		}

		private static IEnumerable<AttributeData> GetCodeGeneratorAttributes( AttributeData parent )
		{
			return parent.AttributeClass?.GetAttributes().Where( IsCodeGeneratorAttribute );
		}
	}
}
