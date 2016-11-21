﻿#region Copyright
// DotNetNuke® - http://www.dotnetnuke.com
// Copyright (c) 2002-2016
// by DotNetNuke Corporation
// All Rights Reserved
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security;
using System.Web.Http;
using Dnn.PersonaBar.Library;
using Dnn.PersonaBar.Library.Attributes;
using Dnn.PersonaBar.Roles.Services.DTO;
using DotNetNuke.Common;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Entities.Portals;
using DotNetNuke.Entities.Users;
using DotNetNuke.Instrumentation;
using DotNetNuke.Security.Roles;
using DotNetNuke.Web.Api;

namespace Dnn.PersonaBar.Roles.Services
{
    [MenuPermission(MenuName = "Roles")]
    public class RolesController : PersonaBarApiController
    {
        private static readonly ILog Logger = LoggerSource.Instance.GetLogger(typeof(RolesController));

        #region Role API

        [HttpGet]
        public HttpResponseMessage GetRoles(int groupId, string keyword, int startIndex, int pageSize)
        {
            try
            {
                var isAdmin = IsAdmin();

                var roles = (groupId < Null.NullInteger
                                    ? RoleController.Instance.GetRoles(PortalId)
                                    : RoleController.Instance.GetRoles(PortalId, r => r.RoleGroupID == groupId))
                                    .Where(r => isAdmin || r.RoleID != PortalSettings.AdministratorRoleId)
                                    .Select(RoleDto.FromRoleInfo);

                if (!string.IsNullOrEmpty(keyword))
                {
                    roles =
                        roles.Where(
                            r => r.Name.IndexOf(keyword, StringComparison.InvariantCultureIgnoreCase) > Null.NullInteger);
                }

                var loadMore = roles.Count() > startIndex + pageSize;
                roles = roles.Skip(startIndex).Take(pageSize);

                return Request.CreateResponse(HttpStatusCode.OK, new {roles = roles, loadMore = loadMore});
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, new {Error = ex.Message});
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public HttpResponseMessage SaveRole(RoleDto roleDto, [FromUri] bool assignExistUsers)
        {
            try
            {
                Validate(roleDto);

                var role = roleDto.ToRoleInfo();
                role.PortalID = PortalId;
                var rolename = role.RoleName.ToUpperInvariant();

                if (roleDto.Id == Null.NullInteger)
                {
                    
                    if (RoleController.Instance.GetRole(PortalId,
                        r => rolename.Equals(r.RoleName, StringComparison.InvariantCultureIgnoreCase)) == null)
                    {
                        RoleController.Instance.AddRole(role, assignExistUsers);
                        roleDto.Id = role.RoleID;
                    }
                    else
                    {
                        return Request.CreateResponse(HttpStatusCode.BadRequest, new { Error = "DuplicateRole" });
                    }
                }
                else
                {
                    if (RoleController.Instance.GetRole(PortalId,
                        r => rolename.Equals(r.RoleName, StringComparison.InvariantCultureIgnoreCase) && r.RoleID != roleDto.Id) == null)
                    {
                        RoleController.Instance.UpdateRole(role, assignExistUsers);
                    }
                    else
                    {
                        return Request.CreateResponse(HttpStatusCode.BadRequest, new { Error = "DuplicateRole" });
                    }
                }

                return Request.CreateResponse(HttpStatusCode.OK, GetRole(roleDto.Id));
            }
            catch (ArgumentException ex)
            {
                return Request.CreateResponse(HttpStatusCode.BadRequest, new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, new { Error = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public HttpResponseMessage DeleteRole(RoleDto roleDto)
        {
            var role = RoleController.Instance.GetRoleById(PortalId, roleDto.Id);
            if (role == null)
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, "RoleNotFound");
            }

            if (role.RoleID == PortalSettings.AdministratorRoleId && !IsAdmin())
            {
                return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "InvalidRequest");
            }

            RoleController.Instance.DeleteRole(role);
            DataCache.RemoveCache("GetRoles");

            return Request.CreateResponse(HttpStatusCode.OK, new {roleId = roleDto.Id});
        }

        #endregion

        #region Role Group API

        [HttpGet]
        public HttpResponseMessage GetRoleGroups(bool reload = false)
        {
            try
            {
                if (reload)
                {
                    DataCache.RemoveCache(string.Format(DataCache.RoleGroupsCacheKey, PortalId));
                }
                var groups = RoleController.GetRoleGroups(PortalId)
                                .Cast<RoleGroupInfo>()
                                .Select(RoleGroupDto.FromRoleGroupInfo);

                return Request.CreateResponse(HttpStatusCode.OK, groups);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, new { Error = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public HttpResponseMessage SaveRoleGroup(RoleGroupDto roleGroupDto)
        {
            try
            {
                Validate(roleGroupDto);

                var roleGroup = roleGroupDto.ToRoleGroupInfo();
                roleGroup.PortalID = PortalId;

                if (roleGroup.RoleGroupID < Null.NullInteger)
                {
                    try
                    {
                        RoleController.AddRoleGroup(roleGroup);
                    }
                    catch
                    {
                        return Request.CreateResponse(HttpStatusCode.BadRequest, new { Error = "DuplicateRoleGroup" });
                    }
                }
                else
                {
                    try
                    {
                        RoleController.UpdateRoleGroup(roleGroup);
                    }
                    catch
                    {
                        return Request.CreateResponse(HttpStatusCode.BadRequest, new { Error = "DuplicateRoleGroup" });
                    }
                }

                roleGroup = RoleController.GetRoleGroups(PortalId).Cast<RoleGroupInfo>()
                    .FirstOrDefault(r => r.RoleGroupName == roleGroupDto.Name?.Trim());

                return Request.CreateResponse(HttpStatusCode.OK, RoleGroupDto.FromRoleGroupInfo(roleGroup));
            }
            catch (ArgumentException ex)
            {
                return Request.CreateResponse(HttpStatusCode.BadRequest, new { Error = ex.Message });
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, new { Error = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public HttpResponseMessage DeleteRoleGroup(RoleGroupDto roleGroupDto)
        {
            var roleGroup = RoleController.GetRoleGroup(PortalId, roleGroupDto.Id);
            if (roleGroup == null)
            {
                return Request.CreateErrorResponse(HttpStatusCode.NotFound, "RoleGroupNotFound");
            }

            RoleController.DeleteRoleGroup(roleGroup);

            return Request.CreateResponse(HttpStatusCode.OK, new {groupId = roleGroupDto.Id});
        }

        #endregion

        #region Role Users API

        [HttpGet]
        public HttpResponseMessage GetSuggestUsers(string keyword, int roleId, int count)
        {
            try
            {
                if (string.IsNullOrEmpty(keyword))
                {
                    return Request.CreateResponse(HttpStatusCode.OK, new List<UserRoleDto>());
                }

                var displayMatch = keyword + "%";
                var totalRecords = 0;
                var isAdmin = IsAdmin();

                var matchedUsers = UserController.GetUsersByDisplayName(PortalId, displayMatch, 0, count,
                    ref totalRecords, false, false)
                    .Cast<UserInfo>()
                    .Where(x => isAdmin || !x.Roles.Contains(PortalSettings.AdministratorRoleName))
                    .Select(u => new UserRoleDto()
                    {
                        UserId = u.UserID,
                        DisplayName = $"{u.DisplayName} ({u.Username})"
                    });

                return Request.CreateResponse(HttpStatusCode.OK, matchedUsers);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, new { Error = ex.Message });
            }
            
        }

        [HttpGet]
        public HttpResponseMessage GetRoleUsers(string keyword, int roleId, int pageIndex, int pageSize)
        {
            try
            {
                var role = RoleController.Instance.GetRoleById(PortalId, roleId);
                if (role == null)
                {
                    return Request.CreateResponse(HttpStatusCode.NotFound, new { });
                }

                if (role.RoleID == PortalSettings.AdministratorRoleId && !IsAdmin())
                {
                    return Request.CreateErrorResponse(HttpStatusCode.BadRequest, "InvalidRequest");
                }

                var users = RoleController.Instance.GetUserRoles(PortalId, Null.NullString, role.RoleName);
                if (!string.IsNullOrEmpty(keyword))
                {
                    users = users.Where(u => u.FullName.StartsWith(keyword, StringComparison.InvariantCultureIgnoreCase))
                        .ToList();
                }

                var totalRecords = users.Count;
                var startIndex = pageIndex*pageSize;
                var portal = PortalController.Instance.GetPortal(PortalId);
                var pagedData = users.Skip(startIndex).Take(pageSize).Select(u => new UserRoleDto()
                    {
                        UserId = u.UserID,
                        RoleId = u.RoleID,
                        DisplayName = u.FullName,
                        StartTime = u.EffectiveDate,
                        ExpiresTime = u.ExpiryDate,
                        AllowExpired = AllowExpired(u.UserID, u.RoleID),
                        AllowDelete = RoleController.CanRemoveUserFromRole(portal, u.UserID, u.RoleID)
                    });

                return Request.CreateResponse(HttpStatusCode.OK, new {users = pagedData, totalRecords = totalRecords});
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, new { Error = ex.Message });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public HttpResponseMessage AddUserToRole(UserRoleDto userRoleDto, bool notifyUser, bool isOwner)
        {
            try
            {
                Validate(userRoleDto);

                if (!AllowExpired(userRoleDto.UserId, userRoleDto.RoleId))
                {
                    userRoleDto.StartTime = userRoleDto.ExpiresTime = Null.NullDate;
                }
                var user = UserController.Instance.GetUserById(PortalId, userRoleDto.UserId);
                var role = RoleController.Instance.GetRoleById(PortalId, userRoleDto.RoleId);
                if (role.SecurityMode != SecurityMode.SocialGroup && role.SecurityMode != SecurityMode.Both)
                    isOwner = false;

                RoleController.AddUserRole(user, role, PortalSettings, RoleStatus.Approved, userRoleDto.StartTime,
                    userRoleDto.ExpiresTime, notifyUser, isOwner);

                var addedUser = RoleController.Instance.GetUserRole(PortalId, userRoleDto.UserId, userRoleDto.RoleId);
                var portal = PortalController.Instance.GetPortal(PortalId);

                return Request.CreateResponse(HttpStatusCode.OK,
                    new UserRoleDto
                    {
                        UserId = addedUser.UserID,
                        RoleId = addedUser.RoleID,
                        DisplayName = addedUser.FullName,
                        StartTime = addedUser.EffectiveDate,
                        ExpiresTime = addedUser.ExpiryDate,
                        AllowExpired = AllowExpired(addedUser.UserID, addedUser.RoleID),
                        AllowDelete = RoleController.CanRemoveUserFromRole(portal, addedUser.UserID, addedUser.RoleID)
                    });
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, new {Error = ex.Message});
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public HttpResponseMessage RemoveUserFromRole(UserRoleDto userRoleDto)
        {
            try
            {
                Validate(userRoleDto);

                RoleController.Instance.UpdateUserRole(PortalId, userRoleDto.UserId, userRoleDto.RoleId,
                    RoleStatus.Approved, false, true);

                return Request.CreateResponse(HttpStatusCode.OK, new {userRoleDto.UserId, userRoleDto.RoleId});
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                return Request.CreateResponse(HttpStatusCode.InternalServerError, new {Error = ex.Message});
            }
        }

        #endregion

        #region Private Methods

        private void Validate(RoleDto role)
        {
            Requires.NotNullOrEmpty("Name", role.Name);

            if (!IsAdmin() && role.Id == PortalSettings.AdministratorRoleId)
            {
                throw new SecurityException("InvalidRequest");
            }
        }

        private void Validate(RoleGroupDto role)
        {
            Requires.NotNullOrEmpty("Name", role.Name);
        }

        private void Validate(UserRoleDto userRoleDto)
        {
            Requires.NotNegative("UserId", userRoleDto.UserId);
            Requires.NotNegative("RoleId", userRoleDto.RoleId);

            if (!IsAdmin() && userRoleDto.RoleId == PortalSettings.AdministratorRoleId)
            {
                throw new SecurityException("InvalidRequest");
            }
        }

        private bool AllowExpired(int userId, int roleId)
        {
            return userId != PortalSettings.AdministratorId || roleId != PortalSettings.AdministratorRoleId;
        }

        private RoleDto GetRole(int roleId)
        {
            return RoleDto.FromRoleInfo(RoleController.Instance.GetRoleById(PortalId, roleId));
        }

        private bool IsAdmin()
        {
            var user = UserController.Instance.GetCurrentUserInfo();
            return user.IsSuperUser || user.IsInRole(PortalSettings.AdministratorRoleName);
        }

        #endregion
    }
}