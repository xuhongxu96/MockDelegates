using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace MockDelegates.Mockers
{
    public static class PropertyMocker
    {
        private static StatementSyntax DefaultMockGetMethodBlock(PropertyDeclarationSyntax target)
        {
            return SyntaxFactory.ReturnStatement(SyntaxFactory.DefaultExpression(target.Type.WithoutTrivia()));
        }

        private static InvocationExpressionSyntax InvokeGetEventExpression(string eventName)
        {
            return SyntaxFactory.InvocationExpression(
                expression: SyntaxFactory.IdentifierName(eventName),
                argumentList: SyntaxFactory.ArgumentList());
        }

        private static StatementSyntax DefaultMockSetMethodBlock(PropertyDeclarationSyntax target)
        {
            return SyntaxFactory.ParseStatement("value = default;");
        }

        private static InvocationExpressionSyntax InvokeSetEventExpression(string eventName)
        {
            return SyntaxFactory.InvocationExpression(
                expression: SyntaxFactory.IdentifierName(eventName),
                argumentList: SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument(SyntaxFactory.ParseExpression("value")))));
        }

        private enum GetOrSet
        {
            Get, Set
        };

        private static AccessorDeclarationSyntax MockGetOrSet(
            GetOrSet getOrSet,
            PropertyDeclarationSyntax targetProperty,
            out MemberDeclarationSyntax[] members)
        {
            var nameSuffix = getOrSet.ToString();
            var newEventName = $"On{targetProperty.Identifier.Text}{nameSuffix}";
            var newHandlerDelegateName = $"{newEventName}Handler";

            DelegateDeclarationSyntax newHandlerDelegate;
            if (getOrSet == GetOrSet.Get)
            {
                newHandlerDelegate = SyntaxFactory.DelegateDeclaration(
                attributeLists: SyntaxFactory.List<AttributeListSyntax>(),
                modifiers: SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)),
                returnType: targetProperty.Type.WithoutTrivia(),
                identifier: SyntaxFactory.Identifier(newHandlerDelegateName),
                typeParameterList: null,
                parameterList: SyntaxFactory.ParameterList(),
                constraintClauses: SyntaxFactory.List<TypeParameterConstraintClauseSyntax>());
            }
            else
            {
                newHandlerDelegate = SyntaxFactory.DelegateDeclaration(
                    attributeLists: SyntaxFactory.List<AttributeListSyntax>(),
                    modifiers: SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)),
                    returnType: targetProperty.Type.WithoutTrivia(),
                    identifier: SyntaxFactory.Identifier(newHandlerDelegateName),
                    typeParameterList: null,
                    parameterList: SyntaxFactory.ParameterList(),
                    constraintClauses: SyntaxFactory.List<TypeParameterConstraintClauseSyntax>());
            }

            var newEvent = SyntaxFactory.EventFieldDeclaration(
                attributeLists: SyntaxFactory.List<AttributeListSyntax>(),
                modifiers: SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)),
                declaration: SyntaxFactory.VariableDeclaration(
                    SyntaxFactory.ParseTypeName(newHandlerDelegateName),
                    SyntaxFactory.SingletonSeparatedList(SyntaxFactory.VariableDeclarator(newEventName))));

            var methodBody = new List<StatementSyntax>();

            var ifNoHandlerStmt = SyntaxFactory.IfStatement(
                condition: SyntaxFactory.ParseExpression($"{newEventName} == null"),
                statement: getOrSet == GetOrSet.Get ? DefaultMockGetMethodBlock(targetProperty) : DefaultMockSetMethodBlock(targetProperty));

            methodBody.Add(ifNoHandlerStmt);

            if (getOrSet == GetOrSet.Get)
            {
                var invokeEventExp = InvokeGetEventExpression(newEventName);
                methodBody.Add(SyntaxFactory.ReturnStatement(invokeEventExp));
            }
            else
            {
                var invokeEventExp = InvokeSetEventExpression(newEventName);
                methodBody.Add(SyntaxFactory.ExpressionStatement(invokeEventExp));
            }

            members = new MemberDeclarationSyntax[]
            {
                newHandlerDelegate,
                newEvent,
            };

            return SyntaxFactory.AccessorDeclaration(
                kind: SyntaxKind.GetAccessorDeclaration,
                body: SyntaxFactory.Block(methodBody));
        }

        public static MemberDeclarationSyntax[] Mock(PropertyDeclarationSyntax targetProperty, bool needOverride)
        {
            var accessors = new List<AccessorDeclarationSyntax>();
            var members = new List<MemberDeclarationSyntax>();

            if (targetProperty.AccessorList.Accessors.Any(o => o.IsKind(SyntaxKind.GetAccessorDeclaration)))
            {
                var accessor = MockGetOrSet(GetOrSet.Get, targetProperty, out var getMembers);
                accessors.Add(accessor);
                members.AddRange(getMembers);
            }

            if (targetProperty.AccessorList.Accessors.Any(o => o.IsKind(SyntaxKind.SetAccessorDeclaration)))
            {
                var accessor = MockGetOrSet(GetOrSet.Set, targetProperty, out var setMembers);
                accessors.Add(accessor);
                members.AddRange(setMembers);
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

            var newProp = targetProperty
                .WithModifiers(modifiers)
                .WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.List(accessors)));

            members.Add(newProp);

            return members.ToArray();
        }
    }
}
