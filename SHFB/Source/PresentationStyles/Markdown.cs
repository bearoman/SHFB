//===============================================================================================================
// System  : Sandcastle Tools Standard Presentation Styles
// File    : Markdown.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 04/02/2015
// Note    : Copyright 2015, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains the presentation style definition for the markdown content presentation style
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 04/02/2015  EFW  Created the code
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.Reflection;

using Sandcastle.Core;
using Sandcastle.Core.PresentationStyle;

namespace Markdown
{
    /// <summary>
    /// This defines a presentation style used to generate markdown content (GitHub flavored)
    /// </summary>
    [PresentationStyleExport("Markdown", "Markdown Content", Version = AssemblyInfo.ProductVersion,
      Copyright = AssemblyInfo.Copyright, Description = "This generates markdown content (GitHub flavored)")]
    public sealed class MarkdownPresentationStyle : PresentationStyleSettings
    {
        /// <inheritdoc />
        public override string Location
        {
            get { return ComponentUtilities.AssemblyFolder(Assembly.GetExecutingAssembly()); }
        }

        /// <summary>
        /// Constructor
        /// </summary>
        public MarkdownPresentationStyle()
        {
            this.BasePath = "Markdown";

            this.SupportedFormats = HelpFileFormats.Markdown;

            this.SupportsNamespaceGrouping = true;

            // If relative, these paths are relative to the base path
            this.ResourceItemsPath = "Content";
            this.ToolResourceItemsPath = "SHFBContent";

            this.DocumentModelTransformation = new TransformationFile(
                @"%SHFBROOT%\ProductionTransforms\ApplyVSDocModel.xsl", new Dictionary<string, string>
                {
                    { "IncludeAllMembersTopic", "false" },
                    { "IncludeInheritedOverloadTopics", "false" },
                    { "project", "{@ProjectNodeIDOptional}" }
                });

            this.IntermediateTocTransformation = new TransformationFile(
                @"%SHFBROOT%\ProductionTransforms\CreateVSToc.xsl");

            this.ConceptualBuildConfiguration = @"Configuration\SHFBConceptual.config";
            this.ReferenceBuildConfiguration = @"Configuration\SHFBReference.config";

            // Note that UNIX based web servers may be case-sensitive with regard to folder and filenames so
            // match the case of the folder and filenames in the literals to their actual casing on the file
            // system.
            this.ContentFiles.Add(new ContentFiles(this.SupportedFormats, @"media\*.*"));
            this.ContentFiles.Add(new ContentFiles(this.SupportedFormats, null, @"MarkdownContent\*.*",
                String.Empty, new[] { ".md" }));

            this.TransformComponentArguments.Add(new TransformComponentArgument("maxVersionParts", false, true,
                null, "The maximum number of assembly version parts to show in API member topics.  Set to 2, " +
                "3, or 4 to limit it to 2, 3, or 4 parts or leave it blank for all parts including the " +
                "assembly file version value if specified."));
        }
    }
}
