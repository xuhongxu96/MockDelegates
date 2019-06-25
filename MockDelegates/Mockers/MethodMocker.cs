using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MockDelegates.Mockers
{
    public static class MethodMocker
    {
        private static StatementSyntax DefaultMockMethodBlock(MethodDeclarationSyntax target)
        {
            var stmts = new List<StatementSyntax>();
            foreach (var param in target.ParameterList.Parameters)
            {
                if (param.Modifiers.Any(o => o.IsKind(SyntaxKind.OutKeyword)))
                {
                    var assignExp = SyntaxFactory.AssignmentExpression(
                        kind: SyntaxKind.SimpleAssignmentExpression,
                        left: SyntaxFactory.IdentifierName(param.Identifier.Text),
                        right: SyntaxFactory.DefaultExpression(param.Type));

                    stmts.Add(SyntaxFactory.ExpressionStatement(assignExp));
                }
            }

            if (target.ReturnType.IsKind(SyntaxKind.PredefinedType)
                && ((PredefinedTypeSyntax)target.ReturnType).Keyword.Text == "void")
            {
                stmts.Add(SyntaxFactory.ReturnStatement());
            }
            else
            {
                stmts.Add(SyntaxFactory.ReturnStatement(SyntaxFactory.DefaultExpression(target.ReturnType.WithoutTrivia())));
            }

            return SyntaxFactory.Block(stmts);
        }

        private static InvocationExpressionSyntax InvokeEventExpression(MethodDeclarationSyntax targetMethod, string eventName)
        {
            var args = new List<ArgumentSyntax>();
            foreach (var param in targetMethod.ParameterList.Parameters)
            {
                var arg = SyntaxFactory.Argument(SyntaxFactory.IdentifierName(param.Identifier));
                foreach (var modifier in param.Modifiers)
                {
                    switch (modifier.Kind())
                    {
                        case SyntaxKind.InKeyword:
                            arg = arg.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.InKeyword));
                            break;
                        case SyntaxKind.OutKeyword:
                            arg = arg.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.OutKeyword));
                            break;
                        case SyntaxKind.RefKeyword:
                            arg = arg.WithRefKindKeyword(SyntaxFactory.Token(SyntaxKind.RefKeyword));
                            break;
                    }
                }

                args.Add(arg);
            }

            return SyntaxFactory.InvocationExpression(
                                        expression: SyntaxFactory.IdentifierName(eventName),
                                        argumentList: SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(args)));
        }

        public static MemberDeclarationSyntax[] Mock(MethodDeclarationSyntax targetMethod, bool needOverride)
        {
            var newEventName = $"On{targetMethod.Identifier.Text}";
            var newHandlerDelegateName = $"{newEventName}Handler";

            var newHandlerDelegate = SyntaxFactory.DelegateDeclaration(
                attributeLists: SyntaxFactory.List<AttributeListSyntax>(),
                modifiers: SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)),
                returnType: targetMethod.ReturnType.WithoutTrivia(),
                identifier: SyntaxFactory.Identifier(newHandlerDelegateName),
                typeParameterList: targetMethod.TypeParameterList,
                parameterList: targetMethod.ParameterList,
                constraintClauses: targetMethod.ConstraintClauses);

            var newEvent = SyntaxFactory.EventFieldDeclaration(
                attributeLists: SyntaxFactory.List<AttributeListSyntax>(),
                modifiers: SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)),
                declaration: SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.ParseTypeName(newHandlerDelegateName),
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(newEventName))));

            var methodBody = new List<StatementSyntax>();

            var ifNoHandlerStmt = SyntaxFactory.IfStatement(
                condition: SyntaxFactory.ParseExpression($"{newEventName} == null"),
                statement: DefaultMockMethodBlock(targetMethod));

            methodBody.Add(ifNoHandlerStmt);

            var invokeEventExp = InvokeEventExpression(targetMethod, newEventName);

            if (targetMethod.ReturnType.IsKind(SyntaxKind.PredefinedType)
                && ((PredefinedTypeSyntax)targetMethod.ReturnType).Keyword.Text == "void")
            {
                methodBody.Add(SyntaxFactory.ExpressionStatement(invokeEventExp));
                methodBody.Add(SyntaxFactory.ReturnStatement());
            }
            else
            {
                methodBody.Add(SyntaxFactory.ReturnStatement(invokeEventExp));
            }

            SyntaxTokenList modifiers;
            if (needOverride)
            {
                modifiers = SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword),
                    SyntaxFactory.Token(SyntaxKind.OverrideKeyword));
            }
            else
            {
                modifiers = SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword));
            }

            var newMethod = targetMethod
                .WithModifiers(modifiers)
                .WithBody(SyntaxFactory.Block(methodBody));

            newMethod = newMethod.ReplaceToken(newMethod.SemicolonToken, SyntaxFactory.Token(SyntaxKind.None));

            return new MemberDeclarationSyntax[]
            {
                newHandlerDelegate,
                newEvent,
                newMethod,
            };
        }
    }
}
