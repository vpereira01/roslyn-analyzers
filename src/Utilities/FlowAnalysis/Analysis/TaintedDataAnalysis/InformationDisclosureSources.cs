﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using Analyzer.Utilities.Extensions;

namespace Analyzer.Utilities.FlowAnalysis.Analysis.TaintedDataAnalysis
{
    internal static class InformationDisclosureSources
    {
        /// <summary>
        /// <see cref="SourceInfo"/>s for information disclosure tainted data sources.
        /// </summary>
        public static ImmutableHashSet<SourceInfo> SourceInfos { get; }

        /// <summary>
        /// Statically constructs.
        /// </summary>
        static InformationDisclosureSources()
        {
            ImmutableHashSet<SourceInfo>.Builder builder = ImmutableHashSet.CreateBuilder<SourceInfo>();

            builder.AddSourceInfo(
                WellKnownTypes.SystemException,
                isInterface: false,
                taintedProperties: new[] {
                    "Message",
                    "StackTrace",
                },
                taintedMethods: new[] {
                    "ToString",
                });

            SourceInfos = builder.ToImmutable();
        }
    }
}