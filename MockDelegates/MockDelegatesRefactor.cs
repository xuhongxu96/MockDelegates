using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MockDelegates.Mockers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace MockDelegates
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(MockDelegates))]
    public class MockDelegatesRefactor : CodeRefactoringProvider
    {
        private static readonly LocalizableString Title
            = new LocalizableResourceString(nameof(Resources.AnalyzerTitle), Resources.ResourceManager, typeof(Resources));

        public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
            var node = root.FindNode(context.Span);
            if (node is InterfaceDeclarationSyntax interfaceDecl)
            {
                var action = CodeAction.Create(Title.ToString(), token => MakeMockAsync(context, interfaceDecl, token));
                context.RegisterRefactoring(action);
            }
            else if (node is ClassDeclarationSyntax classDecl
                && classDecl.Modifiers.Any(SyntaxKind.AbstractKeyword))
            {
                var action = CodeAction.Create(Title.ToString(), token => MakeMockAsync(context, classDecl, token));
                context.RegisterRefactoring(action);
            }
        }

        private Project ChooseTargetProject(CodeRefactoringContext context)
        {
            var currentProject = context.Document.Project;
            var mockProjectName = currentProject.Name + ".Mock";
            var mockProject = currentProject.Solution.Projects.SingleOrDefault(o => o.Name == mockProjectName);

            if (mockProject == null)
            {
                return currentProject;
            }

            if (!mockProject.AllProjectReferences.Any(o => o.ProjectId == currentProject.Id))
            {
                mockProject = mockProject.AddProjectReference(new ProjectReference(currentProject.Id));
            }

            return mockProject;
        }

        private ClassDeclarationSyntax ImplementInterfaceOrAbstractClass(TypeDeclarationSyntax target)
        {
            var baseName = target.Identifier.Text;
            var newClassName = "Mock";
            if (baseName.StartsWith("I"))
            {
                newClassName += baseName.Substring(1);
            }
            else
            {
                newClassName += baseName;
            }

            var newClass = SyntaxFactory.ClassDeclaration(newClassName);
            newClass = newClass.AddBaseListTypes(SyntaxFactory.SimpleBaseType(SyntaxFactory.ParseTypeName(baseName)));

            bool isAbstractClass = target.IsKind(SyntaxKind.ClassDeclaration);


            foreach (var targetMember in target.Members)
            {
                switch (targetMember)
                {
                    case MethodDeclarationSyntax targetMethod:
                        if (target.IsKind(SyntaxKind.ClassDeclaration)
                            && !targetMethod.Modifiers.Any(o => o.IsKind(SyntaxKind.AbstractKeyword)))
                        {
                            // Not abstract method
                            continue;
                        }

                        newClass = newClass.AddMembers(MethodMocker.Mock(targetMethod, isAbstractClass));
                        break;
                    case PropertyDeclarationSyntax targetProp:
                        if (target.IsKind(SyntaxKind.ClassDeclaration)
                            && !targetProp.Modifiers.Any(o => o.IsKind(SyntaxKind.AbstractKeyword)))
                        {
                            // Not abstract method
                            continue;
                        }

                        newClass = newClass.AddMembers(PropertyMocker.Mock(targetProp, isAbstractClass));
                        break;
                    case ConstructorDeclarationSyntax constructorDecl:
                        {
                            var baseConstructorArgs = new List<ArgumentSyntax>();
                            foreach (var param in constructorDecl.ParameterList.Parameters)
                            {
                                baseConstructorArgs.Add(SyntaxFactory.Argument(
                                    SyntaxFactory.IdentifierName(param.Identifier)));
                            }

                            var newConstructor = constructorDecl
                                .WithBody(SyntaxFactory.Block())
                                .WithIdentifier(newClass.Identifier)
                                .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.PublicKeyword)))
                                .WithInitializer(SyntaxFactory.ConstructorInitializer(
                                    SyntaxKind.BaseConstructorInitializer,
                                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(baseConstructorArgs))).WithLeadingTrivia(SyntaxFactory.EndOfLine(string.Empty)));

                            newClass = newClass.AddMembers(newConstructor);
                        }
                        break;
                }
            }

            return newClass;
        }

        private CompilationUnitSyntax CopyUsings(SyntaxNode oldRoot)
        {
            var newRoot = SyntaxFactory.CompilationUnit();

            foreach (var usingDirective in oldRoot.DescendantNodes().Where(o => o.IsKind(SyntaxKind.UsingDirective)))
            {
                newRoot = newRoot.AddUsings(usingDirective as UsingDirectiveSyntax);
            }

            return newRoot;
        }

        private NamespaceDeclarationSyntax CopyNamespace(SyntaxNode oldRoot)
        {
            var oldNs = (NamespaceDeclarationSyntax)oldRoot.ChildNodes().Where(o => o.IsKind(SyntaxKind.NamespaceDeclaration)).First();
            var ns = SyntaxFactory.NamespaceDeclaration(oldNs.Name);

            return ns;
        }

        private async Task<Solution> MakeMockAsync(CodeRefactoringContext context, TypeDeclarationSyntax decl, CancellationToken token)
        {
            var mockProject = ChooseTargetProject(context);

            var oldRoot = await context.Document.GetSyntaxRootAsync(token);
            var newRoot = CopyUsings(oldRoot);

            var newClass = ImplementInterfaceOrAbstractClass(decl);

            var ns = CopyNamespace(oldRoot);
            ns = ns.AddMembers(newClass);
            newRoot = newRoot.AddMembers(ns);

            var newDoc = mockProject.AddDocument(newClass.Identifier.Text, newRoot, context.Document.Folders);
            return newDoc.Project.Solution;
        }
    }
}
