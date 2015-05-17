//===============================================================================================================
// System  : Sandcastle Help File Builder Utilities
// File    : BuildProcess.Transform.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 05/15/2015
// Note    : Copyright 2006-2015, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains the code used to transform and generate the files used to define and compile the help file.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
// Version     Date     Who  Comments
// ==============================================================================================================
#region Older history
// 1.0.0.0  08/07/2006  EFW  Created the code
// 1.3.3.0  11/24/2006  EFW  Added support for component configurations
// 1.5.0.2  07/18/2007  EFW  Added support for the MRefBuilder API filter
// 1.5.1.0  07/25/2007  EFW  Allowed for a format specifier in replacement tags and for nested tags within
//                           project values.
// 1.5.2.0  09/10/2007  EFW  Exposed some members for use by plug-ins and added support for calling the plug-ins
// 1.6.0.2  11/01/2007  EFW  Reworked to support better handling of components
// 1.6.0.4  01/18/2008  EFW  Added support for the JavaScript syntax generator
// 1.6.0.5  02/04/2008  EFW  Added support for SandcastleHtmlExtract tool and the ability to transform general
//                           text strings.  Added support for FeedbackEMailLinkText property.  Added support for
//                           the <inheritdoc /> tag.
// 1.6.0.7  03/21/2008  EFW  Various updates to support new project properties.  Added support for conceptual
//                           build configurations.
// 1.8.0.0  07/14/2008  EFW  Various updates to support new project structure
// 1.8.0.1  01/16/2009  EFW  Added support for ShowMissingIncludeTargets
// 1.8.0.3  07/05/2009  EFW  Added support for the F# syntax filter
// 1.8.0.3  11/10/2009  EFW  Updated to support custom language syntax filters
// 1.8.0.3  12/06/2009  EFW  Removed support for ShowFeedbackControl.  Added support for resource item files.
// 1.9.0.0  06/06/2010  EFW  Added support for multi-format build output
#endregion
// 1.9.1.0  07/09/2010  EFW  Updated for use with .NET 4.0 and MSBuild 4.0
// 1.9.2.0  01/16/2011  EFW  Updated to support selection of Silverlight Framework versions
// 1.9.3.2  08/20/2011  EFW  Updated to support selection of .NET Portable Framework versions
// 1.9.4.0  04/08/2012  EFW  Merged changes for VS2010 style from Don Fehr.  Added BuildAssemblerVerbosity
//                           property.  Added Support for XAML configuration files.
// 1.9.5.0  09/10/2012  EFW  Updated to use the new framework definition file for the .NET Framework versions
// 1.9.6.0  10/25/2012  EFW  Updated to use the new presentation style definition files
// -------  11/29/2013  EFW  Added support for the new MRefBuilder visibility settings.  Added support for
//                           namespace grouping based on changes submitted by Stazzz.
//          12/17/2013  EFW  Removed the SandcastlePath property and all references to it
//          12/26/2013  EFW  Updated to use MEF to load BuildAssembler build components
//          08/05/2014  EFW  Added support for getting a list of syntax generator resource item files
//          05/03/2015  EFW  Removed support for the MS Help 2 file format
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Xml;
using System.Xml.XPath;

using Sandcastle.Core;
using Sandcastle.Core.BuildAssembler.BuildComponent;
using Sandcastle.Core.Frameworks;

using SandcastleBuilder.Utils.BuildComponent;

namespace SandcastleBuilder.Utils.BuildEngine
{
    partial class BuildProcess
    {
        #region Private data members
        //=====================================================================

        private static Regex reField = new Regex(@"{@(?<Field>\w*?)(:(?<Format>.*?))?}");

        private MatchEvaluator fieldMatchEval;
        #endregion

        // TODO: Remove and use SubstitutionTagReplacements instance
        /// <summary>
        /// Transform the specified template text by inserting the necessary values into the place holders tags
        /// </summary>
        /// <param name="templateText">The template text to transform</param>
        /// <param name="args">An optional list of arguments to format into the  template before transforming it</param>
        /// <returns>The transformed text</returns>
        public string TransformText(string templateText, params object[] args)
        {
            if(String.IsNullOrEmpty(templateText))
                return String.Empty;

            if(args.Length != 0)
                templateText = String.Format(CultureInfo.InvariantCulture, templateText, args);

            try
            {
                // Use a regular expression to find and replace all field tags with a matching value from the
                // project.  They can be nested.
                while(reField.IsMatch(templateText))
                    templateText = reField.Replace(templateText, fieldMatchEval);
            }
            catch(BuilderException )
            {
                throw;
            }
            catch(Exception ex)
            {
                throw new BuilderException("BE0018", String.Format(CultureInfo.CurrentCulture,
                    "Unable to transform template text '{0}': {1}", templateText, ex.Message), ex);
            }

            return templateText;
        }

        // TODO: Remove and use SubstitutionTagReplacements instance
        /// <summary>
        /// Transform the specified template by inserting the necessary values into the place holders and saving
        /// it to the working folder.
        /// </summary>
        /// <param name="template">The template to transform</param>
        /// <param name="sourceFolder">The folder where the template is located</param>
        /// <param name="destFolder">The folder in which to save the transformed file</param>
        /// <returns>The path to the transformed file</returns>
        public string TransformTemplate(string template, string sourceFolder, string destFolder)
        {
            Encoding enc = Encoding.Default;
            string templateText, transformedFile;

            if(template == null)
                throw new ArgumentNullException("template");

            if(sourceFolder == null)
                throw new ArgumentNullException("sourceFolder");

            if(destFolder == null)
                throw new ArgumentNullException("destFolder");

            if(sourceFolder.Length != 0 && sourceFolder[sourceFolder.Length - 1] != '\\')
                sourceFolder += @"\";

            if(destFolder.Length != 0 && destFolder[destFolder.Length - 1] != '\\')
                destFolder += @"\";

            try
            {
                // When reading the file, use the default encoding but
                // detect the encoding if byte order marks are present.
                templateText = Utility.ReadWithEncoding(sourceFolder + template, ref enc);

                // Use a regular expression to find and replace all field
                // tags with a matching value from the project.  They can
                // be nested.
                while(reField.IsMatch(templateText))
                    templateText = reField.Replace(templateText, fieldMatchEval);

                transformedFile = destFolder + template;

                // Write the file back out using its original encoding
                using(StreamWriter sw = new StreamWriter(transformedFile, false, enc))
                {
                    sw.Write(templateText);
                }
            }
            catch(BuilderException )
            {
                throw;
            }
            catch(Exception ex)
            {
                throw new BuilderException("BE0019", String.Format(CultureInfo.CurrentCulture,
                    "Unable to transform template '{0}': {1}", template, ex.Message), ex);
            }

            return transformedFile;
        }

        // TODO: Remove and use SubstitutionTagReplacements instance
        /// <summary>
        /// Replace a field tag with a value from the project
        /// </summary>
        /// <param name="match">The match that was found</param>
        /// <returns>The string to use as the replacement</returns>
        private string OnFieldMatch(Match match)
        {
            StringBuilder sb;
            string replaceWith, fieldName;

            fieldName = match.Groups["Field"].Value.ToLowerInvariant();

            switch(fieldName)
            {
                case "disablecodeblockcomponent":
                    replaceWith = project.DisableCodeBlockComponent.ToString().ToLowerInvariant();
                    break;

                case "htmlhelpname":
                    replaceWith = project.HtmlHelpName;
                    break;

                case "htmlenchelpname":
                    replaceWith = HttpUtility.HtmlEncode(project.HtmlHelpName);
                    break;

                case "helpviewersetupname":
                    // Help viewer setup names cannot contain periods so we'll replace them with underscores
                    replaceWith = HttpUtility.HtmlEncode(project.HtmlHelpName.Replace('.', '_'));
                    break;

                case "frameworkcommentlist":
                case "importframeworkcommentlist":
                    replaceWith = this.FrameworkCommentList(fieldName);
                    break;

                case "commentfilelist":
                    replaceWith = commentsFiles.CommentFileList(workingFolder, false);
                    break;

                case "inheritedcommentfilelist":
                    replaceWith = commentsFiles.CommentFileList(workingFolder, true);
                    break;

                case "helptitle":
                    replaceWith = project.HelpTitle;
                    break;

                case "htmlenchelptitle":
                    replaceWith = HttpUtility.HtmlEncode(project.HelpTitle);
                    break;

                case "scripthelptitle":
                    // This is used when the title is passed as a parameter
                    // to a JavaScript function.
                    replaceWith = HttpUtility.HtmlEncode(project.HelpTitle).Replace("'", @"\'");
                    break;

                case "urlenchelptitle": // Just replace &, <, >, and " for  now
                    replaceWith = project.HelpTitle.Replace("&", "%26").Replace(
                        "<", "%3C").Replace(">", "%3E").Replace("\"", "%22");
                    break;

                case "rootnamespacetitle":
                    replaceWith = project.RootNamespaceTitle;

                    if(replaceWith.Length == 0)
                        replaceWith = "<include item=\"rootTopicTitleLocalized\"/>";
                    break;

                case "namespacegrouping":
                    if(project.NamespaceGrouping && presentationStyle.SupportsNamespaceGrouping)
                        replaceWith = "true";
                    else
                    {
                        replaceWith = "false";

                        if(project.NamespaceGrouping)
                            this.ReportWarning("BE0027", "Namespace grouping was requested but the selected " +
                                "presentation style does not support it.  Option ignored.");
                    }
                    break;

                case "codesnippetgrouping":
                    replaceWith = presentationStyle.SupportsCodeSnippetGrouping.ToString().ToLowerInvariant();
                    break;

                case "maximumgroupparts":
                    replaceWith = project.MaximumGroupParts.ToString(CultureInfo.InvariantCulture);
                    break;

                case "binarytoc":
                    replaceWith = project.BinaryTOC ? "Yes" : "No";
                    break;

                case "windowoptions":
                    // Currently, we use a default set of options and only
                    // allow showing or hiding the Favorites tab.
                    replaceWith = (project.IncludeFavorites) ? "0x63520" : "0x62520";
                    break;

                case "langid":
                    replaceWith = language.LCID.ToString(CultureInfo.InvariantCulture);
                    break;

                case "language":
                    replaceWith = String.Format(CultureInfo.InvariantCulture, "0x{0:X} {1}", language.LCID,
                        language.NativeName);
                    break;

                case "resourceitemsfolder":
                    replaceWith = FolderPath.TerminatePath(Path.Combine(
                        presentationStyle.ResolvePath(presentationStyle.ResourceItemsPath), languageFolder));
                    break;

                case "locale":
                    replaceWith = language.Name.ToLowerInvariant();
                    break;

                case "localemixedcase":
                    replaceWith = language.Name;
                    break;

                case "copyright":
                    // Include copyright info if there is a copyright HREF or copyright text
                    if(project.CopyrightHref.Length != 0 || project.CopyrightText.Length != 0)
                        replaceWith = "<include item=\"copyright\"/>";
                    else
                        replaceWith = String.Empty;
                    break;

                case "copyrightinfo":
                    if(project.CopyrightHref.Length == 0 && project.CopyrightText.Length == 0)
                        replaceWith = String.Empty;
                    else
                        if(project.CopyrightHref.Length == 0)
                            replaceWith = project.DecodedCopyrightText;
                        else
                            if(project.CopyrightText.Length == 0)
                                replaceWith = project.CopyrightHref;
                            else
                                replaceWith = String.Format(CultureInfo.CurrentCulture, "{0} ({1})",
                                    project.DecodedCopyrightText, project.CopyrightHref);
                    break;

                case "htmlenccopyrightinfo":
                    if(project.CopyrightHref.Length == 0 && project.CopyrightText.Length == 0)
                        replaceWith = String.Empty;
                    else
                        if(project.CopyrightHref.Length == 0)
                            replaceWith = "<p>" + HttpUtility.HtmlEncode(project.DecodedCopyrightText) + "</p>";
                        else
                            if(project.CopyrightText.Length == 0)
                                replaceWith = String.Format(CultureInfo.CurrentCulture,
                                    "<p><a href='{0}' target='_blank'>{0}</a></p>",
                                    HttpUtility.HtmlEncode(project.CopyrightHref));
                            else
                                replaceWith = String.Format(CultureInfo.CurrentCulture,
                                    "<p><a href='{0}' target='_blank'>{1}</a></p>",
                                    HttpUtility.HtmlEncode(project.CopyrightHref),
                                    HttpUtility.HtmlEncode(project.DecodedCopyrightText));
                    break;

                case "copyrighthref":
                    replaceWith = project.CopyrightHref;
                    break;

                case "htmlenccopyrighthref":
                    if(project.CopyrightHref.Length == 0)
                        replaceWith = String.Empty;
                    else
                        replaceWith = String.Format(CultureInfo.CurrentCulture,
                            "<a href='{0}' target='_blank'>{0}</a>",
                            HttpUtility.HtmlEncode(project.CopyrightHref));
                    break;

                case "copyrighttext":
                    if(project.CopyrightText.Length == 0)
                        replaceWith = String.Empty;
                    else
                        replaceWith = project.DecodedCopyrightText;
                    break;

                case "htmlenccopyrighttext":
                    if(project.CopyrightText.Length == 0)
                        replaceWith = String.Empty;
                    else
                        replaceWith = HttpUtility.HtmlEncode(project.DecodedCopyrightText);
                    break;

                case "comments":
                    // Include "send comments" line if feedback e-mail address
                    // is specified.
                    if(project.FeedbackEMailAddress.Length != 0)
                        replaceWith = "<include item=\"comments\"/>";
                    else
                        replaceWith = String.Empty;
                    break;

                case "feedbackemailaddress":
                    replaceWith = project.FeedbackEMailAddress;
                    break;

                case "feedbackemaillinktext":
                    replaceWith = project.FeedbackEMailLinkText;
                    break;

                case "urlencfeedbackemailaddress":
                    if(project.FeedbackEMailAddress.Length == 0)
                        replaceWith = String.Empty;
                    else
                        replaceWith = HttpUtility.UrlEncode(project.FeedbackEMailAddress);
                    break;

                case "htmlencfeedbackemailaddress":
                    // If link text is specified, it will be used instead
                    if(project.FeedbackEMailAddress.Length == 0)
                        replaceWith = String.Empty;
                    else
                        if(project.FeedbackEMailLinkText.Length == 0)
                            replaceWith = HttpUtility.HtmlEncode(project.FeedbackEMailAddress);
                        else
                            replaceWith = HttpUtility.HtmlEncode(project.FeedbackEMailLinkText);
                    break;

                case "headertext":
                    replaceWith = project.HeaderText;
                    break;

                case "footertext":
                    replaceWith = project.FooterText;
                    break;

                case "indenthtml":
                    replaceWith = project.IndentHtml.ToString().ToLowerInvariant();
                    break;

                case "preliminary":
                    // Include the "preliminary" warning in the header text if wanted
                    if(project.Preliminary)
                        replaceWith = "<include item=\"preliminary\"/>";
                    else
                        replaceWith = String.Empty;
                    break;

                case "defaulttopic":
                    replaceWith = defaultTopic;
                    break;

                case "webdefaulttopic":
                    replaceWith = defaultTopic.Replace('\\', '/');
                    break;

                case "targetframeworkidentifier":
                    replaceWith = frameworkSettings.Platform;
                    break;

                case "frameworkversion":
                    replaceWith = frameworkSettings.Version.ToString();
                    break;

                case "frameworkversionshort":
                    replaceWith = frameworkSettings.Version.ToString(2);
                    break;

                case "platformversion":     // MRefBuilder legacy platform element support
                    // This specifies the framework version for use by CCI in MRefBuilder.  It is related to
                    // the .NET Framework and is currently capped at 2.0.  Using a higher value causes
                    // MRefBuilder to throw an exception.
                    if(frameworkSettings.Platform != PlatformType.DotNetFramework ||
                      frameworkSettings.Version.Major > 2)
                        replaceWith = "2.0";
                    else
                        replaceWith = frameworkSettings.Version.ToString(2);

                    break;

                case "coreframeworkpath":   // MRefBuilder legacy platform element support
                    replaceWith = frameworkSettings.AssemblyLocations.First(l => l.IsCoreLocation).Path;
                    break;

                case "help1xprojectfiles":
                    replaceWith = this.HelpProjectFileList(String.Format(CultureInfo.InvariantCulture,
                        @"{0}Output\{1}", workingFolder, HelpFileFormats.HtmlHelp1), HelpFileFormats.HtmlHelp1);
                    break;

                case "htmlsdklinktype":
                    replaceWith = project.HtmlSdkLinkType.ToString().ToLowerInvariant();
                    break;

                case "mshelpviewersdklinktype":
                    replaceWith = project.MSHelpViewerSdkLinkType.ToString().ToLowerInvariant();
                    break;

                case "websitesdklinktype":
                    replaceWith = project.WebsiteSdkLinkType.ToString().ToLowerInvariant();
                    break;

                case "sdklinktarget":
                    replaceWith = "_" + project.SdkLinkTarget.ToString().ToLowerInvariant();
                    break;

                case "htmltoc":
                    replaceWith = this.GenerateHtmlToc();
                    break;

                case "syntaxfilters":
                    replaceWith = ComponentUtilities.SyntaxFilterGeneratorsFrom(syntaxGenerators,
                        project.SyntaxFilters);
                    break;

                case "syntaxfiltersdropdown":
                    // Note that we can't remove the dropdown box if only a single language is selected as
                    // script still depends on it.
                    replaceWith = ComponentUtilities.SyntaxFilterLanguagesFrom(syntaxGenerators,
                        project.SyntaxFilters);
                    break;

                case "autodocumentconstructors":
                    replaceWith = project.AutoDocumentConstructors.ToString().ToLowerInvariant();
                    break;

                case "autodocumentdisposemethods":
                    replaceWith = project.AutoDocumentDisposeMethods.ToString().ToLowerInvariant();
                    break;

                case "showmissingparams":
                    replaceWith = project.ShowMissingParams.ToString().ToLowerInvariant();
                    break;

                case "showmissingremarks":
                    replaceWith = project.ShowMissingRemarks.ToString().ToLowerInvariant();
                    break;

                case "showmissingreturns":
                    replaceWith = project.ShowMissingReturns.ToString().ToLowerInvariant();
                    break;

                case "showmissingsummaries":
                    replaceWith = project.ShowMissingSummaries.ToString().ToLowerInvariant();
                    break;

                case "showmissingtypeparams":
                    replaceWith = project.ShowMissingTypeParams.ToString().ToLowerInvariant();
                    break;

                case "showmissingvalues":
                    replaceWith = project.ShowMissingValues.ToString().ToLowerInvariant();
                    break;

                case "showmissingnamespaces":
                    replaceWith = project.ShowMissingNamespaces.ToString().ToLowerInvariant();
                    break;

                case "showmissingincludetargets":
                    replaceWith = project.ShowMissingIncludeTargets.ToString().ToLowerInvariant();
                    break;

                case "documentattributes":
                    replaceWith = project.DocumentAttributes.ToString().ToLowerInvariant();
                    break;

                case "documentexplicitinterfaceimplementations":
                    replaceWith = project.DocumentExplicitInterfaceImplementations.ToString().ToLowerInvariant();
                    break;

                case "documentinheritedmembers":
                    replaceWith = project.DocumentInheritedMembers.ToString().ToLowerInvariant();
                    break;

                case "documentinheritedframeworkmembers":
                    replaceWith = project.DocumentInheritedFrameworkMembers.ToString().ToLowerInvariant();
                    break;

                case "documentinheritedframeworkinternalmembers":
                    replaceWith = project.DocumentInheritedFrameworkInternalMembers.ToString().ToLowerInvariant();
                    break;

                case "documentinheritedframeworkprivatemembers":
                    replaceWith = project.DocumentInheritedFrameworkPrivateMembers.ToString().ToLowerInvariant();
                    break;

                case "documentinternals":
                    replaceWith = project.DocumentInternals.ToString().ToLowerInvariant();
                    break;

                case "documentprivates":
                    replaceWith = project.DocumentPrivates.ToString().ToLowerInvariant();
                    break;

                case "documentprivatefields":
                    replaceWith = project.DocumentPrivateFields.ToString().ToLowerInvariant();
                    break;

                case "documentprotected":
                    replaceWith = project.DocumentProtected.ToString().ToLowerInvariant();
                    break;

                case "documentsealedprotected":
                    replaceWith = project.DocumentSealedProtected.ToString().ToLowerInvariant();
                    break;

                case "documentprotectedinternalasprotected":
                    replaceWith = project.DocumentProtectedInternalAsProtected.ToString().ToLowerInvariant();
                    break;

                case "documentnopiatypes":
                    replaceWith = project.DocumentNoPIATypes.ToString().ToLowerInvariant();
                    break;

                case "apifilter":
                    // In a partial build used to get API info for the API filter designer, we won't apply the
                    // filter.
                    if(!this.SuppressApiFilter && project.ApiFilter.Count != 0)
                        replaceWith = project.ApiFilter.ToString();
                    else
                        replaceWith = String.Empty;
                    break;

                case "builddate":
                    // Apply a format specifier?
                    if(match.Groups["Format"].Value.Length != 0)
                        replaceWith = String.Format(CultureInfo.CurrentCulture,
                            "{0:" + match.Groups["Format"].Value + "}", DateTime.Now);
                    else
                        replaceWith = DateTime.Now.ToString(CultureInfo.CurrentCulture);
                    break;

                case "projectnodename":
                    replaceWith = "R:Project_" + project.HtmlHelpName.Replace(" ", "_");
                    break;

                case "rootnamespacecontainer":
                    replaceWith = project.RootNamespaceContainer.ToString().ToLowerInvariant();
                    break;

                case "projectnodeidoptional":
                    if(project.RootNamespaceContainer)
                        replaceWith = "Project_" + project.HtmlHelpName.Replace(" ", "_").Replace("&", "_");
                    else
                        replaceWith = String.Empty;
                    break;

                case "projectnodeidrequired":
                    replaceWith = "Project_" + project.HtmlHelpName.Replace(" ", "_").Replace("&", "_");
                    break;

                case "help1folder":
                    if((project.HelpFileFormat & HelpFileFormats.HtmlHelp1) != 0)
                        replaceWith = @"Output\" + HelpFileFormats.HtmlHelp1.ToString();
                    else
                        replaceWith = String.Empty;
                    break;

                case "websitefolder":
                    if((project.HelpFileFormat & HelpFileFormats.Website) != 0)
                        replaceWith = @"Output\" + HelpFileFormats.Website.ToString();
                    else
                        replaceWith = String.Empty;
                    break;

                case "helpfileversion":
                    replaceWith = project.HelpFileVersion;
                    break;

                case "helpattributes":
                    replaceWith = project.HelpAttributes.ToConfigurationString();
                    break;

                case "tokenfiles":
                    sb = new StringBuilder(1024);

                    foreach(var file in this.ConceptualContent.TokenFiles)
                        sb.AppendFormat("<content file=\"{0}\" />\r\n", Path.GetFileName(file.FullPath));

                    replaceWith = sb.ToString();
                    break;

                case "codesnippetsfiles":
                    sb = new StringBuilder(1024);

                    foreach(var file in this.ConceptualContent.CodeSnippetFiles)
                        sb.AppendFormat("<examples file=\"{0}\" />\r\n", file.FullPath);

                    replaceWith = sb.ToString();
                    break;

                case "resourceitemfiles":
                    sb = new StringBuilder(1024);

                    // Add syntax generator resource item files.  All languages are included regardless of the
                    // project filter settings since code examples can be in any language.  Files are copied and
                    // transformed as they may contain substitution tags
                    foreach(string itemFile in ComponentUtilities.SyntaxGeneratorResourceItemFiles(
                      componentContainer, project.Language))
                    {
                        sb.AppendFormat("<content file=\"{0}\" />\r\n", Path.GetFileName(itemFile));

                        this.TransformTemplate(Path.GetFileName(itemFile), Path.GetDirectoryName(itemFile),
                            workingFolder);
                    }

                    // Add project resource item files last so that they override all other files
                    foreach(var file in project.ContentFiles(BuildAction.ResourceItems).OrderBy(f => f.LinkPath))
                    {
                        sb.AppendFormat("<content file=\"{0}\" />\r\n", Path.GetFileName(file.FullPath));

                        this.TransformTemplate(Path.GetFileName(file.FullPath),
                            Path.GetDirectoryName(file.FullPath), workingFolder);
                    }

                    replaceWith = sb.ToString();
                    break;

                case "xamlconfigfiles":
                    sb = new StringBuilder(1024);

                    foreach(var file in project.ContentFiles(BuildAction.XamlConfiguration))
                        sb.AppendFormat("<filter files=\"{0}\" />\r\n", file.FullPath);

                    replaceWith = sb.ToString();
                    break;

                case "buildassemblerverbosity":
                    replaceWith = (project.BuildAssemblerVerbosity == BuildAssemblerVerbosity.AllMessages) ?
                        "Info" :
                        (project.BuildAssemblerVerbosity == BuildAssemblerVerbosity.OnlyWarningsAndErrors) ?
                        "Warn" : "Error";
                    break;

                case "componentlocations":
                    if(String.IsNullOrWhiteSpace(project.ComponentPath))
                        replaceWith = String.Empty;
                    else
                        replaceWith = String.Format(CultureInfo.InvariantCulture, "<location folder=\"{0}\" />\r\n",
                            HttpUtility.HtmlEncode(project.ComponentPath));

                    replaceWith += String.Format(CultureInfo.InvariantCulture, "<location folder=\"{0}\" />",
                        HttpUtility.HtmlEncode(Path.GetDirectoryName(project.Filename)));
                    break;

                case "helpfileformat":
                    replaceWith = project.HelpFileFormat.ToString();
                    break;

                case "helpformatoutputpaths":
                    sb = new StringBuilder(1024);

                    // Add one entry for each help file format being generated
                    foreach(string baseFolder in this.HelpFormatOutputFolders)
                        sb.AppendFormat("<path value=\"{0}\" />", baseFolder.Substring(workingFolder.Length));

                    replaceWith = sb.ToString();
                    break;

                case "catalogname":
                    if(!String.IsNullOrWhiteSpace(project.CatalogName))
                        replaceWith = "/catalogName \"" + project.CatalogName + "\"";
                    else
                        replaceWith = String.Empty;
                    break;

                case "catalogproductid":
                    replaceWith = project.CatalogProductId;
                    break;

                case "catalogversion":
                    replaceWith = project.CatalogVersion;
                    break;

                case "vendorname":
                    replaceWith = !String.IsNullOrEmpty(project.VendorName) ? project.VendorName : "Vendor Name";
                    break;

                case "htmlencvendorname":
                    replaceWith = !String.IsNullOrEmpty(project.VendorName) ?
                        HttpUtility.HtmlEncode(project.VendorName) : "Vendor Name";
                    break;

                case "producttitle":
                    replaceWith = !String.IsNullOrEmpty(project.ProductTitle) ?
                        project.ProductTitle : project.HelpTitle;
                    break;

                case "htmlencproducttitle":
                    replaceWith = !String.IsNullOrEmpty(project.ProductTitle) ?
                        HttpUtility.HtmlEncode(project.ProductTitle) : HttpUtility.HtmlEncode(project.HelpTitle);
                    break;

                case "topicversion":
                    replaceWith = HttpUtility.HtmlEncode(project.TopicVersion);
                    break;

                case "tocparentid":
                    replaceWith = HttpUtility.HtmlEncode(project.TocParentId);
                    break;

                case "tocparentversion":
                    replaceWith = HttpUtility.HtmlEncode(project.TocParentVersion);
                    break;

                case "apitocparentid":
                    // If null, empty or it starts with '*', it's parented to the root node
                    if(!String.IsNullOrEmpty(this.ApiTocParentId) && this.ApiTocParentId[0] != '*')
                    {
                        // Ensure that the ID is valid and visible in the TOC
                        if(!this.ConceptualContent.Topics.Any(t => t[this.ApiTocParentId] != null &&
                          t[this.ApiTocParentId].Visible))
                            throw new BuilderException("BE0022", String.Format(CultureInfo.CurrentCulture,
                                "The project's ApiTocParent property value '{0}' must be associated with a topic in " +
                                "your project's conceptual content and must have its Visible property set to True in " +
                                "the content layout file.", this.ApiTocParentId));

                        replaceWith = HttpUtility.HtmlEncode(this.ApiTocParentId);
                    }
                    else
                        if(!String.IsNullOrEmpty(this.RootContentContainerId))
                            replaceWith = HttpUtility.HtmlEncode(this.RootContentContainerId);
                        else
                            replaceWith = HttpUtility.HtmlEncode(project.TocParentId);
                    break;

                case "addxamlsyntaxdata":
                    // If the XAML syntax generator is present, add XAML syntax data to the reflection file
                    if(ComponentUtilities.SyntaxFiltersFrom(syntaxGenerators, project.SyntaxFilters).Any(
                      s => s.Id == "XAML Usage"))
                        replaceWith = @";~\ProductionTransforms\AddXamlSyntaxData.xsl";
                    else
                        replaceWith = String.Empty;
                    break;

                case "transformcomponentarguments":
                    sb = new StringBuilder(1024);

                    foreach(var arg in project.TransformComponentArguments)
                        if(arg.Value != null)
                            sb.AppendFormat("<argument key=\"{0}\" value=\"{1}\" />\r\n", arg.Key, arg.Value);
                        else
                            sb.AppendFormat("<argument key=\"{0}\">{1}</argument>\r\n", arg.Key, arg.Content);

                    replaceWith = sb.ToString();
                    break;

                case "referencelinknamespacefiles":
                    sb = new StringBuilder(1024);

                    foreach(string s in this.ReferencedNamespaces)
                        sb.AppendFormat("<namespace file=\"{0}.xml\" />\r\n", s);

                    replaceWith = sb.ToString();
                    break;

                case "uniqueid":
                    // Get a unique ID for the project and current user
                    replaceWith = Environment.GetEnvironmentVariable("USERNAME");

                    if(String.IsNullOrWhiteSpace(replaceWith))
                        replaceWith = "DefaultUser";

                    replaceWith = (project.Filename + "_" + replaceWith).GetHashCode().ToString("X",
                        CultureInfo.InvariantCulture);
                    break;

                case "hrefformat":
                    if((project.HelpFileFormat & HelpFileFormats.Markdown) != 0)
                        replaceWith = "<hrefFormat value=\"{0}\" />";
                    else
                        replaceWith = String.Empty;
                    break;

                case "sandcastlepath":
                    // This is obsolete but will still appear in the older component and plug-in configurations.
                    // Throw an exception that describes what to do to fix it.
                    throw new BuilderException("BE0065", "One or more component or plug-in configurations in " +
                        "this project contains an obsolete path setting.  Please remove the custom components " +
                        "and plug-ins and add them again so that their configurations are updated.  See the " +
                        "version release notes for information on breaking changes that require this update.");

                default:
                    // TODO: This will go away once all tags are handled
                    replaceWith = substitutionTags.OnFieldMatch(match);
                    break;
            }

            return replaceWith;
        }

        /// <summary>
        /// This is used to generate an appropriate list of entries that represent .NET Framework comments file
        /// locations for the various configuration files.
        /// </summary>
        /// <param name="listType">The type of list to generate (frameworkcommentlist or importframeworkcommentlist)</param>
        /// <returns>The list of framework comments file sources in the appropriate format.</returns>
        private string FrameworkCommentList(string listType)
        {
            FrameworkSettings settings = FrameworkDictionary.AllFrameworks.GetFrameworkWithRedirect(
                project.FrameworkVersion);
            StringBuilder sb = new StringBuilder(1024);
            string folder, wildcard;

            // Build the list based on the type and what actually exists
            foreach(var location in settings.CommentsFileLocations(project.Language))
            {
                folder = Path.GetDirectoryName(location);
                wildcard = Path.GetFileName(location);

                if(listType == "frameworkcommentlist")
                {
                    // Files are cached by platform, version, and location.  The groupId attribute can be
                    // used by caching components to identify the cache and its location.
                    sb.AppendFormat(CultureInfo.InvariantCulture, "<data base=\"{0}\" files=\"{1}\" " +
                        "recurse=\"false\" duplicateWarning=\"false\" groupId=\"{2}_{3}_{4:X}\" />\r\n",
                        folder, wildcard, settings.Platform, settings.Version,
                        location.GetHashCode());
                }
                else    // importframeworkcommentlist
                    sb.AppendFormat(CultureInfo.InvariantCulture, "<import path=\"{0}\" file=\"{1}\" " +
                        "recurse=\"false\" />\r\n", folder, wildcard);
            }

            return sb.ToString();
        }

        /// <summary>
        /// This is used to merge the component configurations from the project with the <b>sandcastle.config</b>
        /// file.
        /// </summary>
        private void MergeComponentConfigurations()
        {
            XmlDocument config;
            XmlNode configNode;
            string configName;

            this.ReportProgress(BuildStep.MergeCustomConfigs, "Merging custom build component configurations");

            // Reset the adjusted instance values to match the configuration instance values
            foreach(var component in buildComponents.Values)
            {
                component.ReferenceBuildPlacement.AdjustedInstance = component.ReferenceBuildPlacement.Instance;
                component.ConceptualBuildPlacement.AdjustedInstance = component.ConceptualBuildPlacement.Instance;
            }

            if(this.ExecutePlugIns(ExecutionBehaviors.InsteadOf))
                return;

            this.ExecutePlugIns(ExecutionBehaviors.Before);

            configName = workingFolder + "sandcastle.config";
            this.ReportProgress(configName);

            config = new XmlDocument();
            config.Load(configName);

            this.MergeConfigurations(config, false);
            config.Save(configName);

            // Do the same for conceptual.config if necessary
            if(this.ConceptualContent.ContentLayoutFiles.Count != 0)
            {
                configName = workingFolder + "conceptual.config";
                this.ReportProgress(configName);

                config = new XmlDocument();
                config.Load(configName);

                this.MergeConfigurations(config, true);

                // Remove the example component if there are no snippets file
                if(this.ConceptualContent.CodeSnippetFiles.Count == 0)
                {
                    this.ReportProgress("    Removing unused ExampleComponent.");

                    var rootNode = config.SelectSingleNode("configuration/dduetools/builder/components");
                    configNode = rootNode.SelectSingleNode("component[@id='Example Component']");

                    if(configNode != null)
                        configNode.ParentNode.RemoveChild(configNode);
                }

                config.Save(configName);
            }

            this.ExecutePlugIns(ExecutionBehaviors.After);
        }

        /// <summary>
        /// This handles merging the build component configurations into the given configuration file
        /// </summary>
        /// <param name="config">The configuration file into which the configurations are merged</param>
        /// <param name="isConceptualConfig">True for a conceptual configuration file, false for a reference
        /// configuration file.</param>
        private void MergeConfigurations(XmlDocument config, bool isConceptualConfig)
        {
            Dictionary<string, XmlNode> outputNodes = new Dictionary<string, XmlNode>();
            BuildComponentFactory factory;
            BuildComponentConfiguration projectComp;
            XmlNode rootNode, configNode, clone;
            XmlNodeList outputFormats;
            PlacementAction placement;
            string compConfig, configType;

            rootNode = config.SelectSingleNode("configuration/dduetools/builder/components");

            foreach(string id in project.ComponentConfigurations.Keys)
            {
                projectComp = project.ComponentConfigurations[id];

                if(!buildComponents.TryGetValue(id, out factory))
                    throw new BuilderException("BE0021", String.Format(CultureInfo.CurrentCulture,
                        "The project contains a reference to a custom build component '{0}' that could not " +
                        "be found.", id));

                if(isConceptualConfig)
                {
                    placement = factory.ConceptualBuildPlacement.Placement;
                    configType = "conceptual";
                }
                else
                {
                    placement = factory.ReferenceBuildPlacement.Placement;
                    configType = "reference";
                }

                if(placement == PlacementAction.None)
                    this.ReportProgress("    Skipping component '{0}', not used in {1} build", id, configType);
                else
                    if(projectComp.Enabled)
                    {
                        compConfig = projectComp.Configuration;

                        // Replace template tags.  They may be nested.
                        while(reField.IsMatch(compConfig))
                            compConfig = reField.Replace(compConfig, fieldMatchEval);

                        configNode = config.CreateDocumentFragment();
                        configNode.InnerXml = compConfig;
                        outputNodes.Clear();

                        foreach(XmlNode match in configNode.SelectNodes("//helpOutput"))
                        {
                            outputNodes.Add(match.Attributes["format"].Value, match);
                            match.ParentNode.RemoveChild(match);
                        }

                        // Is it output format specific?
                        if(outputNodes.Count == 0)
                        {
                            // Replace the component in the file
                            this.MergeComponent(id, factory, rootNode, configNode, isConceptualConfig, null);
                        }
                        else
                        {
                            // Replace the component in each output format node
                            outputFormats = rootNode.SelectNodes(
                                "component[@id='Multi-format Output Component']/helpOutput");

                            if(outputFormats.Count != 0)
                            {
                                foreach(XmlNode format in outputFormats)
                                {
                                    clone = configNode.Clone();
                                    clone.FirstChild.InnerXml += outputNodes[format.Attributes["format"].Value].InnerXml;
                                    this.MergeComponent(id, factory, format, clone, isConceptualConfig, null);
                                }
                            }
                            else
                            {
                                // Some presentation styles only support one help format.  In those cases, get
                                // the configuration for that format and use it alone.
                                XmlNode format;

                                if(outputNodes.TryGetValue(project.HelpFileFormat.ToString(), out format))
                                {
                                    configNode.FirstChild.InnerXml += format.InnerXml;
                                    this.MergeComponent(id, factory, rootNode, configNode, isConceptualConfig, null);
                                }
                                else
                                    this.ReportProgress("    Skipping component '{0}', configuration for help " +
                                        "format '{1}' not found", id, project.HelpFileFormat);
                            }
                        }
                    }
                    else
                        this.ReportProgress("    The configuration for '{0}' is disabled and will not be used.", id);
            }
        }

        /// <summary>
        /// This handles merging of the custom component configurations into the configuration file including
        /// dependencies.
        /// </summary>
        /// <param name="id">The ID of the component to merge</param>
        /// <param name="factory">The build component factory</param>
        /// <param name="rootNode">The root container node</param>
        /// <param name="configNode">The configuration node to merge</param>
        /// <param name="isConceptualConfig">True if this is a conceptual content configuration file or false if
        /// it is a reference build configuration file.</param>
        /// <param name="mergeStack">A stack used to check for circular build component dependencies.  Pass null
        /// on the first non-recursive call.</param>
        private void MergeComponent(string id, BuildComponentFactory factory, XmlNode rootNode, XmlNode configNode,
          bool isConceptualConfig, Stack<string> mergeStack)
        {
            BuildComponentFactory dependencyFactory;
            ComponentPlacement position;
            XmlNodeList matchingNodes;
            XmlNode node;
            string replaceId;

            // Merge dependent component configurations first
            if(factory.Dependencies.Any())
            {
                if(mergeStack == null)
                    mergeStack = new Stack<string>();

                foreach(string dependency in factory.Dependencies)
                {
                    node = rootNode.SelectSingleNode("component[@id='" + dependency + "']");

                    // If it's already there or would create a circular dependency, ignore it
                    if(node != null || mergeStack.Contains(dependency))
                        continue;

                    // Add the dependency with a default configuration
                    if(!buildComponents.TryGetValue(dependency, out dependencyFactory))
                        throw new BuilderException("BE0023", String.Format(CultureInfo.CurrentCulture,
                            "The project contains a reference to a custom build component '{0}' that has a " +
                            "dependency '{1}' that could not be found.", id, dependency));

                    node = rootNode.OwnerDocument.CreateDocumentFragment();
                    node.InnerXml = reField.Replace(dependencyFactory.DefaultConfiguration, fieldMatchEval);

                    this.ReportProgress("    Merging '{0}' dependency for '{1}'", dependency, id);

                    mergeStack.Push(dependency);
                    this.MergeComponent(dependency, dependencyFactory, rootNode, node, isConceptualConfig, mergeStack);
                    mergeStack.Pop();
                }
            }

            position = (!isConceptualConfig) ? factory.ReferenceBuildPlacement : factory.ConceptualBuildPlacement;

            // Find all matching components by ID
            replaceId = position.Id;
            matchingNodes = rootNode.SelectNodes("component[@id='" + replaceId + "']");

            // If replacing another component, search for that by ID and replace it if found
            if(position.Placement == PlacementAction.Replace)
            {
                if(matchingNodes.Count < position.AdjustedInstance)
                {
                    this.ReportProgress("    Could not find configuration '{0}' (instance {1}) to replace with " +
                        "configuration for '{2}' so it will be omitted.", replaceId, position.AdjustedInstance, id);

                    // If it's a dependency, that's a problem
                    if(mergeStack.Count != 0)
                        throw new BuilderException("BE0024", "Unable to add dependent configuration: " + id);

                    return;
                }

                rootNode.ReplaceChild(configNode, matchingNodes[position.AdjustedInstance - 1]);

                this.ReportProgress("    Replaced configuration for '{0}' (instance {1}) with configuration " +
                    "for '{2}'", replaceId, position.AdjustedInstance, id);

                // Adjust instance values on matching components
                foreach(var component in buildComponents.Values)
                    if(!isConceptualConfig)
                    {
                        if(component.ReferenceBuildPlacement.Id == replaceId &&
                          component.ReferenceBuildPlacement.AdjustedInstance > position.AdjustedInstance)
                        {
                            component.ReferenceBuildPlacement.AdjustedInstance--;
                        }
                    }
                    else
                        if(component.ConceptualBuildPlacement.Id == replaceId &&
                          component.ConceptualBuildPlacement.AdjustedInstance > position.AdjustedInstance)
                        {
                            component.ConceptualBuildPlacement.AdjustedInstance--;
                        }

                return;
            }

            // See if the configuration already exists.  If so, replace it.
            // We'll assume it's already in the correct location.
            node = rootNode.SelectSingleNode("component[@id='" + id + "']");

            if(node != null)
            {
                this.ReportProgress("    Replacing default configuration for '{0}' with the custom configuration", id);
                rootNode.ReplaceChild(configNode, node);
                return;
            }

            // Create the node and add it in the correct location
            switch(position.Placement)
            {
                case PlacementAction.Start:
                    rootNode.InsertBefore(configNode, rootNode.ChildNodes[0]);
                    this.ReportProgress("    Added configuration for '{0}' to the start of the configuration file", id);
                    break;

                case PlacementAction.End:
                    rootNode.InsertAfter(configNode,
                        rootNode.ChildNodes[rootNode.ChildNodes.Count - 1]);
                    this.ReportProgress("    Added configuration for '{0}' to the end of the configuration file", id);
                    break;

                case PlacementAction.Before:
                    if(matchingNodes.Count < position.AdjustedInstance)
                        this.ReportProgress("    Could not find configuration '{0}' (instance {1}) to add " +
                            "configuration for '{2}' so it will be omitted.", replaceId, position.AdjustedInstance, id);
                    else
                    {
                        rootNode.InsertBefore(configNode, matchingNodes[position.AdjustedInstance - 1]);
                        this.ReportProgress("    Added configuration for '{0}' before '{1}' (instance {2})",
                            id, replaceId, position.AdjustedInstance);
                    }
                    break;

                default:    // After
                    if(matchingNodes.Count < position.AdjustedInstance)
                        this.ReportProgress("    Could not find configuration '{0}' (instance {1}) to add " +
                            "configuration for '{2}' so it will be omitted.", replaceId, position.AdjustedInstance, id);
                    else
                    {
                        rootNode.InsertAfter(configNode, matchingNodes[position.AdjustedInstance - 1]);
                        this.ReportProgress("    Added configuration for '{0}' after '{1}' (instance {2})",
                            id, replaceId, position.AdjustedInstance);
                    }
                    break;
            }
        }

        /// <summary>
        /// This is used to get an enumerable list of unique namespaces from the given reflection data file
        /// </summary>
        /// <param name="reflectionInfoFile">The reflection data file to search for namespaces</param>
        /// <param name="validNamespaces">An enumerable list of valid namespaces</param>
        /// <returns>An enumerable list of unique namespaces</returns>
        public IEnumerable<string> GetReferencedNamespaces(string reflectionInfoFile,
          IEnumerable<string> validNamespaces)
        {
            XPathDocument doc = new XPathDocument(reflectionInfoFile);
            XPathNavigator nav = doc.CreateNavigator();
            HashSet<string> seenNamespaces = new HashSet<string>();
            string ns;

            // Find all type references and extract the namespace from them.  This is a rather brute force way
            // of doing it but the type element can appear in various places.  This way we find them all.
            // Examples: //ancestors/type/@api, //returns/type/@api, //parameter/type/@api,
            // //parameter/referenceTo/type/@api, //attributes/attribute/argument/type/@api,
            // //returns/type/specialization/type/@api, //containers/type/@api, //overrides/member/type/@api
            var nodes = nav.Select("//type/@api");

            foreach(XPathNavigator n in nodes)
                if(n.Value.Length > 2 && n.Value.IndexOf('.') != -1)
                {
                    ns = n.Value.Substring(2, n.Value.LastIndexOf('.') - 2);

                    if(validNamespaces.Contains(ns) && !seenNamespaces.Contains(ns))
                    {
                        seenNamespaces.Add(ns);
                        yield return ns;
                    }
                }
        }
    }
}
