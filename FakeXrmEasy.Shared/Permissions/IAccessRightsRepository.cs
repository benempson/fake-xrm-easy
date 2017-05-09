﻿using System;
using System.Collections.Generic;
using System.Text;

using Microsoft.Xrm.Sdk;
using Microsoft.Crm.Sdk.Messages;

namespace FakeXrmEasy.Permissions
{
    public interface IAccessRightsRepository
    {
        /// <summary>
        /// Grants the specified rights to the security principal (user or team) for the specified record
        /// </summary>
        /// <param name="er"></param>
        /// <param name="pa"></param>
        void GrantAccessTo(EntityReference er, PrincipalAccess pa);

        /// <summary>
        /// Revokes the specified rights to the security principal (user or team) for the specified record
        /// </summary>
        /// <param name="er"></param>
        /// <param name="pa"></param>
        void RevokeAccessTo(EntityReference er, EntityReference principal);

        /// <summary>
        /// Revokes any access to any record for the specified security principal (kind of 'Clear All')
        /// </summary>
        /// <param name="er"></param>
        /// <param name="pa"></param>
        void RevokeAccessToAllRecordsTo(PrincipalAccess pa);

        /// <summary>
        /// Retrieves the RetrievePrincipalAccessResponse for the specified security principal (user or team) and record
        /// </summary>
        /// <param name="er"></param>
        /// <param name="principal"></param>
        /// <returns></returns>
        RetrievePrincipalAccessResponse RetrievePrincipalAccess(EntityReference er, EntityReference principal);

        /// <summary>
        /// Retrieves the list of permitted security principals (user or team) that have access to the given record
        /// </summary>
        /// <param name="er"></param>
        /// <returns></returns>
        RetrieveSharedPrincipalsAndAccessResponse RetrieveSharedPrincipalsAndAccess(EntityReference er);

        /// <summary>
        /// Retrieves all principals (security principals) who have any access to the specified record
        /// </summary>
        /// <param name="er"></param>
        void GetAllPrincipalAccessFor(EntityReference er);
    }
}
