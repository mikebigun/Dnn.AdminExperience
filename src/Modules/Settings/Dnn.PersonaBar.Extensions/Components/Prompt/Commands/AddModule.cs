﻿using System;
using System.Collections.Generic;
using System.Text;
using Dnn.PersonaBar.Extensions.Components.Prompt.Models;
using Dnn.PersonaBar.Library.Prompt;
using Dnn.PersonaBar.Library.Prompt.Attributes;
using Dnn.PersonaBar.Library.Prompt.Models;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Entities.Modules;
using DotNetNuke.Entities.Modules.Definitions;
using DotNetNuke.Entities.Portals;
using DotNetNuke.Entities.Users;
using DotNetNuke.Services.Exceptions;

namespace Dnn.PersonaBar.Extensions.Components.Prompt.Commands
{
    [ConsoleCommand("add-module", "Adds a new module instance to a page", new[] { "id" })]
    public class AddModule : ConsoleCommandBase
    {

        private const string FlagName = "name";
        private const string FlagPageid = "pageid";
        private const string FlagPane = "pane";
        private const string FlagTitle = "title";

        public string ModuleName { get; private set; }
        public int? PageId { get; private set; }    // the page on which to add the module
        public string Pane { get; private set; }
        public string Title { get; private set; }   // title for the new module. defaults to friendly name

        public override void Init(string[] args, PortalSettings portalSettings, UserInfo userInfo, int activeTabId)
        {
            base.Init(args, portalSettings, userInfo, activeTabId);
            var sbErrors = new StringBuilder();

            if (HasFlag(FlagName))
            {
                ModuleName = Flag(FlagName);
            }
            else if (args.Length >= 2 && !IsFlag(args[1]))
            {
                // assume first argument is the module name
                ModuleName = args[1];
            }
            if (string.IsNullOrEmpty(ModuleName))
            {
                sbErrors.AppendFormat("You must supply the ModuleName for the module you wish to add. This can be passed using the --{0} flag or as the first argument after the command name; ", FlagName);
            }

            if (HasFlag(FlagPageid))
            {
                var tmpId = 0;
                if (int.TryParse(Flag(FlagPageid), out tmpId))
                {
                    PageId = tmpId;
                }
                else
                {
                    sbErrors.AppendFormat("--{0} must be an integer; ", FlagPageid);
                }
            }
            else
            {
                // Page ID is required
                sbErrors.AppendFormat("--{0} is required; ", FlagPageid);
            }

            if (HasFlag(FlagTitle))
                Title = Flag(FlagTitle);

            if (HasFlag(FlagPane))
                Pane = Flag(FlagPane);
            if (string.IsNullOrEmpty(Pane))
                Pane = "ContentPane";

            if (PageId.HasValue && PageId <= 0)
            {
                sbErrors.Append("The target Page ID must be greater than 0; ");
            }

            ValidationMessage = sbErrors.ToString();
        }

        public override ConsoleResultModel Run()
        {

            // get the desktop module id from the module name
            try
            {
                var desktopModule = DesktopModuleController.GetDesktopModuleByModuleName(ModuleName, PortalId);
                if (desktopModule == null)
                {
                    return new ConsoleErrorResultModel(
                        $"Unable to find a desktop module with the name '{ModuleName}' for this portal");
                }
                try
                {
                    var addedModules = AddNewModule(Title, desktopModule.DesktopModuleID, Pane, 0, 0, null);
                    if (addedModules.Count == 0)
                    {
                        return new ConsoleResultModel("No modules were added");
                    }
                    var lst = new List<ModuleInstanceModel>();
                    foreach (var newModule in addedModules)
                    {
                        lst.Add(ModuleInstanceModel.FromDnnModuleInfo(ModuleController.Instance.GetTabModule(newModule.TabModuleID)));
                    }
                    return new ConsoleResultModel(
                        $"Successfully added {lst.Count} new module{(lst.Count == 1 ? string.Empty : "s")}") { Data = lst };
                }
                catch (Exception ex)
                {
                    Exceptions.LogException(ex);
                    return new ConsoleErrorResultModel("An error occurred while attempting to add the module. Please see the DNN Event Viewer for details.");
                }
            }
            catch (Exception ex)
            {
                Exceptions.LogException(ex);
                return new ConsoleErrorResultModel("An error occurred while attempting to add the module. Please see the DNN Event Viewer for details.");
            }

        }

        private List<ModuleInfo> AddNewModule(string title, int desktopModuleId, string pane, int position, int permissionType, string align)
        {

            var lstOut = new List<ModuleInfo>();

            foreach (var modDef in ModuleDefinitionController.GetModuleDefinitionsByDesktopModuleID(desktopModuleId).Values)
            {
                var mi = new ModuleInfo();
                mi.Initialize(PortalId);
                mi.PortalID = PortalId;
                mi.TabID = TabId;
                mi.ModuleOrder = position;
                mi.ModuleTitle = string.IsNullOrEmpty(title) ? modDef.FriendlyName : title;
                mi.PaneName = pane;
                mi.ModuleDefID = modDef.ModuleDefID;
                if (modDef.DefaultCacheTime > 0)
                {
                    mi.CacheTime = modDef.DefaultCacheTime;
                    if (PortalSettings.Current.DefaultModuleId > Null.NullInteger && PortalSettings.Current.DefaultTabId > Null.NullInteger)
                    {
                        // get the default module so we can access its cachetime
                        var defaultModule = ModuleController.Instance.GetModule(PortalSettings.Current.DefaultModuleId, PortalSettings.Current.DefaultTabId, true);
                        if (defaultModule != null)
                        {
                            mi.CacheTime = defaultModule.CacheTime;
                        }
                    }
                }

                // Set initial permissions on Module
                ModuleController.Instance.InitialModulePermission(mi, mi.TabID, permissionType);

                // Set initial localization of module if needed
                if (PortalSettings.Current.ContentLocalizationEnabled)
                {
                    var defaultLocale = DotNetNuke.Services.Localization.LocaleController.Instance.GetDefaultLocale(PortalId);
                    var tabInfo = DotNetNuke.Entities.Tabs.TabController.Instance.GetTab(mi.TabID, PortalId, false);
                    mi.CultureCode = tabInfo != null ? tabInfo.CultureCode : defaultLocale.Code;
                }
                else
                {
                    mi.CultureCode = Null.NullString;
                }

                mi.AllTabs = false;
                mi.Alignment = align;


                // Add the new module to the Page
                ModuleController.Instance.AddModule(mi);

                lstOut.Add(mi);

                // Set position so future additions to page can operate correctly
                position = ModuleController.Instance.GetTabModule(mi.TabModuleID).ModuleOrder + 1;
            }

            return lstOut;
        }


    }
}