﻿//******************************************************************************************************
//  MainController.cs - Gbtc
//
//  Copyright © 2016, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may
//  not use this file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  08/31/2016 - Billy Ernest
//       Generated original version of source code.
//
//******************************************************************************************************


using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Caching;
using System.Web.Mvc;
using FaultData.DataAnalysis;
using GSF;
using GSF.Data;
using GSF.Identity;
using GSF.Security;
using GSF.Web.Model;
using GSF.Web.Security;
using Newtonsoft.Json.Linq;
using openXDA.Model;
using PQDashboard.Model;

namespace PQDashboard.Controllers
{
    /// <summary>
    /// Represents a MVC controller for the site's main pages.
    /// </summary>
    //[AuthorizeControllerRole]
    public class MainController : Controller
    {
        #region [ Members ]

        // Fields
        private readonly DataContext m_dataContext;
        private readonly AppModel m_appModel;
        private bool m_disposed;

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="MainController"/>.
        /// </summary>
        public MainController()
        {
            // Establish data context for the view
            m_dataContext = new DataContext(exceptionHandler: MvcApplication.LogException);
            ViewData.Add("DataContext", m_dataContext);

            // Set default model for pages used by layout
            m_appModel = new AppModel(m_dataContext);
            ViewData.Model = m_appModel;
        }

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="MainController"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    if (disposing)
                        m_dataContext?.Dispose();
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        public ActionResult Home()
        {
            m_appModel.ConfigureView(Url.RequestContext, "Home", ViewBag);

            try
            {
                ViewBag.username = System.Web.HttpContext.Current.User.Identity.Name;
                ViewBag.usersid = UserInfo.UserNameToSID(ViewBag.username);

                if (m_dataContext.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM UserAccount WHERE Name = {0}", ViewBag.usersid) == 0)
                {
                    ViewBag.username = "External";
                    ViewBag.usersid = "External";
                }
            }
            catch (Exception ex)
            {
                ViewBag.username = "";
            }

            return View();
        }

        public ActionResult Help()
        {
            m_appModel.ConfigureView(Url.RequestContext, "Help", ViewBag);
            return View();
        }

        public ActionResult GraphMeasurements()
        {
            m_appModel.ConfigureView(Url.RequestContext, "GraphMeasurements", ViewBag);
            return View();
        }

        public ActionResult Contact()
        {
            m_appModel.ConfigureView(Url.RequestContext, "Contact", ViewBag);
            ViewBag.Message = "Contacting the Grid Protection Alliance";
            return View();
        }

        public ActionResult DisplayPDF()
        {
            // Using route ID, i.e., /Main/DisplayPDF/{id}, as page name of PDF load
            string routeID = Url.RequestContext.RouteData.Values["id"] as string ?? "UndefinedPageName";
            m_appModel.ConfigureView(Url.RequestContext, routeID, ViewBag);

            return View();
        }

        public ActionResult OpenSEE()
        {
            ViewBag.EnableLightningQuery = m_dataContext.Connection.ExecuteScalar<bool>("SELECT Value FROM Setting WHERE Name = 'OpenSEE.EnableLightningQuery'");

            ViewBag.IsAdmin = ValidateAdminRequest();
            int eventID = int.Parse(Request.QueryString["eventid"]);
            Event evt = m_dataContext.Table<Event>().QueryRecordWhere("ID = {0}", eventID);
            ViewBag.EventID = eventID;
            ViewBag.EventStartTime = evt.StartTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
            ViewBag.EventEndTime = evt.EndTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");


            ViewBag.SamplesPerCycle = m_dataContext.Connection.ExecuteScalar<double>("select SamplesPerCycle from event join meter on event.meterid = meter.id where event.id = {0}", eventID);
            return View();
        }

        public ActionResult OpenSTE()
        {
            //m_appModel.ConfigureView(Url.RequestContext, "OpenSEE", ViewBag);
            return View();
        }

        public ActionResult MeterEventsByLine()
        {
            m_appModel.ConfigureView(Url.RequestContext, "MeterEventsByLine", ViewBag);

            try
            {
                ViewBag.username = System.Web.HttpContext.Current.User.Identity.Name;
                ViewBag.usersid = UserInfo.UserNameToSID(ViewBag.username);

                if (m_dataContext.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM UserAccount WHERE Name = {0}", ViewBag.usersid) == 0)
                {
                    ViewBag.username = "External";
                    ViewBag.usersid = "External";
                }
            }
            catch (Exception ex)
            {
                ViewBag.username = "";
            }

            return View();
        }

        public ActionResult MeterExtensionsByLine()
        {
            m_appModel.ConfigureView(Url.RequestContext, "MeterEventsByLine", ViewBag);

            try
            {
                ViewBag.username = System.Web.HttpContext.Current.User.Identity.Name;
                ViewBag.usersid = UserInfo.UserNameToSID(ViewBag.username);

                if (m_dataContext.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM UserAccount WHERE Name = {0}", ViewBag.usersid) == 0)
                {
                    ViewBag.username = "External";
                    ViewBag.usersid = "External";
                }
            }
            catch (Exception ex)
            {
                ViewBag.username = "";
            }

            return View();
        }

        public ActionResult MeterDisturbancesByLine()
        {
            try
            {
                ViewBag.username = System.Web.HttpContext.Current.User.Identity.Name;
                ViewBag.usersid = UserInfo.UserNameToSID(ViewBag.username);

                if (m_dataContext.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM UserAccount WHERE Name = {0}", ViewBag.usersid) == 0)
                {
                    ViewBag.username = "External";
                    ViewBag.usersid = "External";
                }
            }
            catch (Exception ex)
            {
                ViewBag.username = "";
            }

            m_appModel.ConfigureView(Url.RequestContext, "MeterDisturbancesByLine", ViewBag);
            return View();
        }

        public ActionResult QuickSearch()
        {
            try
            {
                ViewBag.username = System.Web.HttpContext.Current.User.Identity.Name;
                ViewBag.usersid = UserInfo.UserNameToSID(ViewBag.username);

                if (m_dataContext.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM UserAccount WHERE Name = {0}", ViewBag.usersid) == 0)
                {
                    ViewBag.username = "External";
                    ViewBag.usersid = "External";
                }
            }
            catch (Exception ex)
            {
                ViewBag.username = "";
            }

            m_appModel.ConfigureView(Url.RequestContext, "QuickSearch", ViewBag);
            return View();
        }
        #endregion

        private bool ValidateAdminRequest()
        {
            string username = User.Identity.Name;
            ISecurityProvider securityProvider = SecurityProviderUtility.CreateProvider(username);
            securityProvider.PassthroughPrincipal = User;

            if (!securityProvider.Authenticate())
                return false;

            SecurityIdentity approverIdentity = new SecurityIdentity(securityProvider);
            SecurityPrincipal approverPrincipal = new SecurityPrincipal(approverIdentity);

            if (!approverPrincipal.IsInRole("Administrator"))
                return false;

            return true;
        }

    }
}