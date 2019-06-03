﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Analyzer.Utilities;
using Analyzer.Utilities.Extensions;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Microsoft.CodeAnalysis.Analyzers.MetaAnalyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp, LanguageNames.VisualBasic)]
    public class CompareSymbolsCorrectlyAnalyzer : DiagnosticAnalyzer
    {
        private static readonly LocalizableString s_localizableTitle = new LocalizableResourceString(nameof(CodeAnalysisDiagnosticsResources.CompareSymbolsCorrectlyTitle), CodeAnalysisDiagnosticsResources.ResourceManager, typeof(CodeAnalysisDiagnosticsResources));
        private static readonly LocalizableString s_localizableMessage = new LocalizableResourceString(nameof(CodeAnalysisDiagnosticsResources.CompareSymbolsCorrectlyMessage), CodeAnalysisDiagnosticsResources.ResourceManager, typeof(CodeAnalysisDiagnosticsResources));
        private static readonly LocalizableString s_localizableDescription = new LocalizableResourceString(nameof(CodeAnalysisDiagnosticsResources.CompareSymbolsCorrectlyDescription), CodeAnalysisDiagnosticsResources.ResourceManager, typeof(CodeAnalysisDiagnosticsResources));

        private static readonly string s_symbolTypeFullName = typeof(ISymbol).FullName;
        private static readonly string[] s_comparerTypeFullName = new[] {
            typeof(IEqualityComparer<>).FullName,
            typeof(IComparer<>).FullName
        };

        public static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(
            DiagnosticIds.CompareSymbolsCorrectlyRuleId,
            s_localizableTitle,
            s_localizableMessage,
            DiagnosticCategory.MicrosoftCodeAnalysisCorrectness,
            DiagnosticHelpers.DefaultDiagnosticSeverity,
            isEnabledByDefault: DiagnosticHelpers.EnabledByDefaultIfNotBuildingVSIX,
            description: s_localizableDescription,
            customTags: WellKnownDiagnosticTags.Telemetry);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);

        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

            context.RegisterCompilationStartAction(context =>
            {
                var compilation = context.Compilation;
                var symbolType = compilation.GetTypeByMetadataName(s_symbolTypeFullName);

                if (symbolType is null)
                {
                    return;
                }

                context.RegisterOperationAction(context => HandleBinaryOperator(in context, symbolType), OperationKind.BinaryOperator);

                var comparerTypes = s_comparerTypeFullName
                    .Select(comparerTypeFullName => compilation.GetTypeByMetadataName(comparerTypeFullName))
                    .WhereNotNull()
                    .ToImmutableArray();

                if (comparerTypes.IsDefaultOrEmpty)
                {
                    return;
                }

                context.RegisterOperationAction(context => HandleOperation(in context, comparerTypes), OperationKind.ObjectCreation);
            });
        }

        private void HandleOperation(in OperationAnalysisContext context, ImmutableArray<INamedTypeSymbol> comparerTypes)
        {
            if (context.Operation is IObjectCreationOperation objectCreation)
            {
                var typeBeingCreated = (INamedTypeSymbol)objectCreation.Type;

                // Does the type have constructors that take comparers? 
                var constructorsWithComparers = typeBeingCreated.Constructors.WhereAsArray(constructor => hasSymbolComparer(constructor, comparerTypes));

                if (constructorsWithComparers.Length == 0)
                {
                    return;
                }

                // Is the constructor being used one of the ones that takes a comparer? 
                var correctConstructorBeingUsed = constructorsWithComparers.Contains(objectCreation.Constructor);

                if (!correctConstructorBeingUsed)
                {
                    context.ReportDiagnostic(objectCreation.Syntax.GetLocation().CreateDiagnostic(Rule));
                }
            }

            // Local functions

            static bool hasSymbolComparer(IMethodSymbol methodSymbol, ImmutableArray<INamedTypeSymbol> comparerTypes)
                => methodSymbol.Parameters
                    .Select(param => param.Type)
                    .OfType<INamedTypeSymbol>()
                    .WhereAsArray(t => comparerTypes.Contains(t.ConstructedFrom))
                    .Any();
        }

        private void HandleBinaryOperator(in OperationAnalysisContext context, INamedTypeSymbol symbolType)
        {
            var binary = (IBinaryOperation)context.Operation;
            if (binary.OperatorKind != BinaryOperatorKind.Equals && binary.OperatorKind != BinaryOperatorKind.NotEquals)
            {
                return;
            }

            // Allow user-defined operators
            if (binary.OperatorMethod?.ContainingSymbol is INamedTypeSymbol containingType
                && containingType.SpecialType != SpecialType.System_Object)
            {
                return;
            }

            // If either operand is 'null' or 'default', do not analyze
            if (binary.LeftOperand.HasNullConstantValue() || binary.RightOperand.HasNullConstantValue())
            {
                return;
            }

            if (!IsSymbolType(binary.LeftOperand, symbolType)
                && !IsSymbolType(binary.RightOperand, symbolType))
            {
                return;
            }

            if (binary.Language == LanguageNames.VisualBasic)
            {
                if (IsSymbolClassType(binary.LeftOperand) || IsSymbolClassType(binary.RightOperand))
                {
                    return;
                }
            }

            if (IsExplicitCastToObject(binary.LeftOperand) || IsExplicitCastToObject(binary.RightOperand))
            {
                return;
            }

            context.ReportDiagnostic(binary.Syntax.GetLocation().CreateDiagnostic(Rule));
        }

        private static bool IsSymbolType(IOperation operation, INamedTypeSymbol symbolType)
        {
            if (operation.Type is object)
            {
                if (operation.Type.Equals(symbolType))
                {
                    return true;
                }

                if (operation.Type.AllInterfaces.Contains(symbolType))
                {
                    return true;
                }
            }

            if (operation is IConversionOperation conversion)
            {
                return IsSymbolType(conversion.Operand, symbolType);
            }

            return false;
        }

        private static bool IsSymbolClassType(IOperation operation)
        {
            if (operation.Type is object)
            {
                if (operation.Type.TypeKind == TypeKind.Class
                    && operation.Type.SpecialType != SpecialType.System_Object)
                {
                    return true;
                }
            }

            if (operation is IConversionOperation conversion)
            {
                return IsSymbolClassType(conversion.Operand);
            }

            return false;
        }

        private static bool IsExplicitCastToObject(IOperation operation)
        {
            if (!(operation is IConversionOperation conversion))
            {
                return false;
            }

            if (conversion.IsImplicit)
            {
                return false;
            }

            return conversion.Type?.SpecialType == SpecialType.System_Object;
        }
    }
}
