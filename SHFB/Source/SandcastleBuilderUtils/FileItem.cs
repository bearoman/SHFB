//===============================================================================================================
// System  : Sandcastle Help File Builder Utilities
// File    : FileItem.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 05/11/2015
// Note    : Copyright 2008-2015, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class representing a file that is part of the project (MAML/additional content, site
// map, style sheet, etc.).
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://GitHub.com/EWSoftware/SHFB.  This
// notice, the author's name, and all copyright notices must remain intact in all applications, documentation,
// and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 07/24/2008  EFW  Created the code
// 12/04/2009  EFW  Added support for resource item files
// 07/09/2010  EFW  Updated for use with .NET 4.0 and MSBuild 4.0.
// 04/08/2012  EFW  Added support for XAML configuration files
// 05/08/2015  EFW  Removed support for ExcludeFromToc metadata item
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

using Microsoft.Build.Evaluation;

using SandcastleBuilder.Utils.Design;

namespace SandcastleBuilder.Utils
{
    /// <summary>
    /// This class represents a file that is part of the project (MAML/additional content, site map, style
    /// sheet, etc.).
    /// </summary>
    public class FileItem : ICustomTypeDescriptor
    {
        #region Private data members
        //=====================================================================

        private BuildAction buildAction;
        private FilePath includePath, linkPath;
        private string imageId, altText;
        private bool copyToMedia;
        private int sortOrder;

        private static Regex reInsertSpaces = new Regex(@"((?<=[a-z0-9])[A-Z](?=[a-z0-9]))|((?<=[A-Za-z])\d+)");
        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This returns the project element associated with the instance
        /// </summary>
        [Browsable(false)]
        public ProjectElement ProjectElement { get; private set; }

        /// <summary>
        /// This is used to set or get the build action of the item
        /// </summary>
        /// <value>If set to <c>Image</c>, <see cref="ImageId"/> and <see cref="AlternateText" /> will be set to
        /// the filename if not set already.</value>
        [Category("Build Action"), Description("The build action for this item"),
          RefreshProperties(RefreshProperties.All), TypeConverter(typeof(BuildActionEnumConverter))]
        public BuildAction BuildAction
        {
            get { return buildAction; }
            set
            {
                string baseName;

                this.ProjectElement.ItemType = value.ToString();
                buildAction = value;

                // Set default ID and description if set to Image
                if(buildAction == BuildAction.Image)
                {
                    baseName = Path.GetFileNameWithoutExtension(includePath);

                    if(String.IsNullOrEmpty(this.ImageId))
                        this.ImageId = baseName;

                    if(String.IsNullOrEmpty(this.AlternateText))
                    {
                        baseName = baseName.Replace("_", " ");
                        this.AlternateText = reInsertSpaces.Replace(baseName, " $&").Trim();
                    }
                }
            }
        }

        /// <summary>
        /// This is used to set or get the filename (include path)
        /// </summary>
        [Browsable(false)]
        public FilePath Include
        {
            get { return includePath; }
            set
            {
                if(value == null || value.Path.Length == 0 || value.Path.IndexOfAny(new char[] { '*', '?' }) != -1)
                    throw new ArgumentException("A file path must be specified and cannot contain wildcards " +
                        "(* or ?)", "value");

                // Do this first in case the project isn't editable
                this.ProjectElement.Include = value.PersistablePath;

                includePath = value;
                includePath.PersistablePathChanging += includePath_PersistablePathChanging;
            }
        }

        /// <summary>
        /// This is used to set or get the link path
        /// </summary>
        /// <value>If the item has no link path, this returns the <see cref="Include" /> path</value>
        [Browsable(false)]
        public FilePath Link
        {
            get { return (linkPath == null) ? includePath : linkPath; }
            set
            {
                if(value != null && value.Path.Length != 0 && value.Path.IndexOfAny(new char[] { '*', '?' }) != -1)
                    throw new ArgumentException("A file path must be specified and cannot contain wildcards " +
                        "(* or ?)", "value");

                if(value != null && value.Path.Length != 0)
                {
                    // Do this first in case the project isn't editable
                    this.ProjectElement.SetMetadata(BuildItemMetadata.LinkPath, value.PersistablePath);
                    linkPath = value;
                    linkPath.PersistablePathChanging += linkPath_PersistablePathChanging;
                }
                else
                {
                    this.ProjectElement.SetMetadata(BuildItemMetadata.LinkPath, null);
                    linkPath = null;
                }
            }
        }

        /// <summary>
        /// This is used to get the full path to the item
        /// </summary>
        [Category("File"), Description("The full path to the file")]
        public string FullPath
        {
            get { return includePath; }
        }

        /// <summary>
        /// This is used to set or get the name of the item
        /// </summary>
        [Category("File"), Description("The name of the item"), RefreshProperties(RefreshProperties.All)]
        public string Name
        {
            get
            {
                string path = includePath;

                if(buildAction == BuildAction.Folder && path.EndsWith(@"\", StringComparison.Ordinal))
                    return Path.GetFileName(path.Substring(0, path.Length - 1));

                return Path.GetFileName(path);
            }
            set
            {
                string tempPath, newPath, path = includePath;

                if(String.IsNullOrEmpty(value) || value.IndexOfAny(new char[] { ':', '\\', '*', '?' }) != -1)
                    throw new ArgumentException("New name cannot be blank and cannot contain ':', '\\', '*', or '?'");

                if(buildAction != BuildAction.Folder)
                {
                    // If it's a link, copy the file to the project folder and remove the link metadata
                    if(this.ProjectElement.HasMetadata(BuildItemMetadata.LinkPath))
                    {
                        newPath = linkPath;
                        File.Copy(path, newPath, true);
                        File.SetAttributes(newPath, FileAttributes.Normal);
                        path = newPath;
                        this.ProjectElement.SetMetadata(BuildItemMetadata.LinkPath, null);
                    }

                    newPath = Path.Combine(Path.GetDirectoryName(path), value);

                    if(path != newPath)
                    {
                        // If the file exists and it isn't just a case change, disallow it
                        if(File.Exists(newPath) && String.Compare(path, newPath,
                          StringComparison.OrdinalIgnoreCase) != 0)
                            throw new ArgumentException("A file with that name already exists in the project folder");

                        File.Move(path, newPath);
                        this.Include = new FilePath(newPath, this.ProjectElement.Project);
                    }

                    return;
                }

                // Rename the folder and all items starting with the folder name
                if(path.EndsWith(@"\", StringComparison.Ordinal))
                    path = path.Substring(0, path.Length - 1);

                newPath = Path.Combine(Path.GetDirectoryName(path), value);

                if(Directory.Exists(newPath) && String.Compare(path, newPath,
                  StringComparison.OrdinalIgnoreCase) != 0)
                    throw new ArgumentException("A folder with that name already exists in the project folder");

                // To allow renaming a folder by changing its case, move it to a temporary name first and then
                // the new name.
                if(String.Compare(path, newPath, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    tempPath = Guid.NewGuid().ToString();
                    Directory.Move(path, tempPath);
                    path = tempPath;
                }

                Directory.Move(path, newPath);
                path = this.ProjectElement.Include;
                newPath = Path.Combine(Path.GetDirectoryName(path.Substring(0, path.Length - 1)), value) + "\\";
                this.Include = new FilePath(newPath, this.ProjectElement.Project);

                foreach(ProjectItem item in this.ProjectElement.Project.MSBuildProject.AllEvaluatedItems)
                    if(item.EvaluatedInclude.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                        item.UnevaluatedInclude = newPath + item.UnevaluatedInclude.Substring(path.Length);
            }
        }

        /// <summary>
        /// This is used to get or set an ID for a conceptual content image
        /// </summary>
        /// <remarks>This is used to indicate that an image file is part of the conceptual content.  Image items
        /// without an ID are not valid and will be ignored.</remarks>
        [Category("Metadata"), Description("The ID for a conceptual content image"), DefaultValue(null)]
        public string ImageId
        {
            get { return imageId; }
            set
            {
                if(value != null)
                    value = value.Trim();

                this.ProjectElement.SetMetadata(BuildItemMetadata.ImageId, value);
                imageId = value;
            }
        }

        /// <summary>
        /// This is used to get or set alternate text for an image
        /// </summary>
        [Category("Metadata"), Description("Image alternate text"), DefaultValue(null)]
        public string AlternateText
        {
            get { return altText; }
            set
            {
                if(value != null)
                    value = value.Trim();

                this.ProjectElement.SetMetadata(BuildItemMetadata.AlternateText, value);
                altText = value;
            }
        }

        /// <summary>
        /// This is used to get or set whether an item is copied to the output folder during a build
        /// </summary>
        /// <remarks>If this is set to true, the image will always be copied to the build's media folder.  If
        /// false, it is only copied if referenced in a topic.</remarks>
        [Category("Metadata"), Description("If set to true, the image will always be copied to the build's " +
          "media folder.  If false, it is only copied if referenced in a topic."), DefaultValue(false)]
        public bool CopyToMedia
        {
            get { return copyToMedia; }
            set
            {
                this.ProjectElement.SetMetadata(BuildItemMetadata.CopyToMedia, value.ToString(CultureInfo.InvariantCulture));
                copyToMedia = value;
            }
        }

        /// <summary>
        /// This is used to get or set the sort order for content layout and site map files
        /// </summary>
        [Category("Metadata"), Description("For content layout and site map files, this defines the sort " +
          "order for merging them into the table of contents."), DefaultValue(0)]
        public int SortOrder
        {
            get { return sortOrder; }
            set
            {
                this.ProjectElement.SetMetadata(BuildItemMetadata.SortOrder, value.ToString(CultureInfo.InvariantCulture));
                sortOrder = value;
            }
        }
        #endregion

        #region Private helper methods
        //=====================================================================

        /// <summary>
        /// This is used to handle changes in the <see cref="Include" /> properties such that the path gets
        /// stored in the project file.
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void includePath_PersistablePathChanging(object sender, EventArgs e)
        {
            this.ProjectElement.Include = includePath.PersistablePath;
        }

        /// <summary>
        /// This is used to handle changes in the <see cref="Link" /> properties such that the path gets stored
        /// in the project file.
        /// </summary>
        /// <param name="sender">The sender of the event</param>
        /// <param name="e">The event arguments</param>
        private void linkPath_PersistablePathChanging(object sender, EventArgs e)
        {
            this.ProjectElement.SetMetadata(BuildItemMetadata.LinkPath, includePath.PersistablePath);
        }
        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Internal Constructor
        /// </summary>
        /// <param name="element">The project element</param>
        internal FileItem(ProjectElement element)
        {
            this.ProjectElement = element;

            buildAction = (BuildAction)Enum.Parse(typeof(BuildAction), element.ItemType, true);
            includePath = new FilePath(element.Include, element.Project);
            includePath.PersistablePathChanging += includePath_PersistablePathChanging;

            element.Include = includePath.PersistablePath;

            if(element.HasMetadata(BuildItemMetadata.LinkPath))
            {
                linkPath = new FilePath(element.GetMetadata(BuildItemMetadata.LinkPath), element.Project);
                linkPath.PersistablePathChanging += linkPath_PersistablePathChanging;
            }

            if(element.HasMetadata(BuildItemMetadata.ImageId))
                imageId = element.GetMetadata(BuildItemMetadata.ImageId);

            if(element.HasMetadata(BuildItemMetadata.AlternateText))
                altText = element.GetMetadata(BuildItemMetadata.AlternateText);

            if(element.HasMetadata(BuildItemMetadata.CopyToMedia))
                if(!Boolean.TryParse(ProjectElement.GetMetadata(BuildItemMetadata.CopyToMedia), out copyToMedia))
                    copyToMedia = false;

            if(element.HasMetadata(BuildItemMetadata.SortOrder))
                if(!Int32.TryParse(ProjectElement.GetMetadata(BuildItemMetadata.SortOrder), out sortOrder))
                    sortOrder = 0;
        }
        #endregion

        #region Refresh path properties
        //=====================================================================

        /// <summary>
        /// Refresh the paths due to a parent path being renamed
        /// </summary>
        public void RefreshPaths()
        {
            this.includePath = new FilePath(this.ProjectElement.Include, this.ProjectElement.Project);

            if(this.ProjectElement.HasMetadata(BuildItemMetadata.LinkPath))
                this.Link = new FilePath(this.ProjectElement.GetMetadata(BuildItemMetadata.LinkPath),
                    this.ProjectElement.Project);
        }
        #endregion

        #region ICustomTypeDescriptor Members
        //=====================================================================

        /// <inheritdoc />
        public AttributeCollection GetAttributes()
        {
            return TypeDescriptor.GetAttributes(this, true);
        }

        /// <inheritdoc />
        public string GetClassName()
        {
            return TypeDescriptor.GetClassName(this, true);
        }

        /// <inheritdoc />
        public string GetComponentName()
        {
            return TypeDescriptor.GetComponentName(this, true);
        }

        /// <inheritdoc />
        public TypeConverter GetConverter()
        {
            return TypeDescriptor.GetConverter(this, true);
        }

        /// <inheritdoc />
        public EventDescriptor GetDefaultEvent()
        {
            return TypeDescriptor.GetDefaultEvent(this, true);
        }

        /// <inheritdoc />
        public PropertyDescriptor GetDefaultProperty()
        {
            return TypeDescriptor.GetDefaultProperty(this, true);
        }

        /// <inheritdoc />
        public object GetEditor(Type editorBaseType)
        {
            return TypeDescriptor.GetEditor(this, editorBaseType, true);
        }

        /// <inheritdoc />
        public EventDescriptorCollection GetEvents(Attribute[] attributes)
        {
            return TypeDescriptor.GetEvents(this, attributes, true);
        }

        /// <inheritdoc />
        public EventDescriptorCollection GetEvents()
        {
            return TypeDescriptor.GetEvents(this, true);
        }

        /// <inheritdoc />
        public PropertyDescriptorCollection GetProperties(Attribute[] attributes)
        {
            PropertyDescriptorCollection pdc = TypeDescriptor.GetProperties(this, attributes, true);

            return this.FilterProperties(pdc);
        }

        /// <inheritdoc />
        public PropertyDescriptorCollection GetProperties()
        {
            PropertyDescriptorCollection pdc = TypeDescriptor.GetProperties(this, true);

            return this.FilterProperties(pdc);
        }

        /// <inheritdoc />
        public object GetPropertyOwner(PropertyDescriptor pd)
        {
            return this;
        }
        #endregion

        #region Property filter method
        //=====================================================================

        /// <summary>
        /// This is used to filter the properties based on the
        /// <see cref="BuildAction" />.
        /// </summary>
        /// <param name="pdc">The property descriptor collection to filter</param>
        /// <returns>The filtered property descriptor collection</returns>
        private PropertyDescriptorCollection FilterProperties(PropertyDescriptorCollection pdc)
        {
            List<string> removeProps = new List<string>();

            switch(buildAction)
            {
                case BuildAction.None:
                case BuildAction.CodeSnippets:
                case BuildAction.ResourceItems:
                case BuildAction.Tokens:
                case BuildAction.XamlConfiguration:
                    removeProps.AddRange(new string[] { "ImageId", "AlternateText", "CopyToMedia", "SortOrder" });
                    break;

                case BuildAction.Content:
                    removeProps.AddRange(new string[] { "ImageId", "AlternateText", "CopyToMedia", "SortOrder" });
                    break;

                case BuildAction.Folder:
                    removeProps.AddRange(new string[] { "BuildAction", "ImageId", "AlternateText", "CopyToMedia",
                        "SortOrder" });
                    break;

                case BuildAction.Image:
                    removeProps.AddRange(new string[] { "SortOrder" });
                    break;

                case BuildAction.ContentLayout:
                case BuildAction.SiteMap:
                    removeProps.AddRange(new string[] { "ImageId", "AlternateText", "CopyToMedia" });
                    break;

                default:    // Leave them all in
                    break;
            }

            PropertyDescriptorCollection adjustedProps = new PropertyDescriptorCollection(new PropertyDescriptor[] { });

            foreach(PropertyDescriptor pd in pdc)
                if(removeProps.IndexOf(pd.Name) == -1)
                    adjustedProps.Add(pd);

            return adjustedProps;
        }
        #endregion
    }
}
