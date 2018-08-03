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
using System.Net;
using System.Net.Http;
using System.Net.Mail;
using System.Runtime.Caching;
using System.Security;
using System.Text;
using System.Threading;
using System.Web.Mvc;
using FaultData.DataAnalysis;
using GSF;
using GSF.Data;
using GSF.Identity;
using GSF.Security;
using GSF.Security.Model;
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
    public class EmailController : Controller
    {
        #region [ Members ]

        // Fields
        private readonly DataContext m_dataContext;
        private readonly AppModel m_appModel;
        private bool m_disposed;

        public class UpdateSettingModel
        {
            public string email { get; set; }
            public string phone { get; set; }
            public string carrier { get; set; }
            public int region { get; set; }
            public int job { get; set; }
            public int sms { get; set; }
            public string submit { get; set; }
            public string sid { get; set; }
            public string username { get; set; }
        }

        public class VerifyCodeModel {
            public string type { get; set; }
            public int code { get; set; }
            public string submit { get; set; }
            public Guid accountid { get; set; }
        }

        #endregion

        #region [ Constructors ]

        /// <summary>
        /// Creates a new <see cref="MainController"/>.
        /// </summary>
        public EmailController()
        {
            // Establish data context for the view
            m_dataContext = new DataContext(exceptionHandler: MvcApplication.LogException);
            ViewData.Add("DataContext", m_dataContext);

            // Set default model for pages used by layout
            m_appModel = new AppModel(m_dataContext);
            ViewData.Model = m_appModel;
        }

        #endregion

        #region [ Static ]
        private static MemoryCache s_memoryCache;

        static EmailController()
        {
            s_memoryCache = new MemoryCache("EmailController");
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

        #region [ View Actions ]
        public ActionResult UpdateSettings()
        {
            m_appModel.ConfigureView(Url.RequestContext, "UpdateSettings", ViewBag);

            try
            {
                ViewBag.username = System.Web.HttpContext.Current.User.Identity.Name;
                ViewBag.usersid = UserInfo.UserNameToSID(ViewBag.username);
                ViewBag.account = m_dataContext.Table<ConfirmableUserAccount>().QueryRecordWhere("Name = {0}", ViewBag.usersid);
                ViewBag.isRegistered = ViewBag.account != null;
            }
            catch (Exception ex)
            {
                ViewBag.username = "External";
            }

            return View();
        }

        public ActionResult Verify(string id)
        {
            m_appModel.ConfigureView(Url.RequestContext, "Verify", ViewBag);

            try
            {
                ViewBag.Type = id;
                ViewBag.username = System.Web.HttpContext.Current.User.Identity.Name;
                ViewBag.usersid = UserInfo.UserNameToSID(ViewBag.username);
                ViewBag.account = m_dataContext.Table<ConfirmableUserAccount>().QueryRecordWhere("Name = {0}", ViewBag.usersid);
                ViewBag.ExpiredCode = TempData["ExpiredCode"];
                ViewBag.BadCode = TempData["BadCode"];
                TempData["ExpiredCode"] = null;
                TempData["BadCode"] = null;
            }
            catch (Exception ex)
            {
                ViewBag.username = "External";
            }

            return View();
        }

        #endregion

        #region [ Post Actions ]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult HandleUpdateSettingForm(UpdateSettingModel formData) {
            if (formData.submit == "Sign Up")
                HandleSignUp(formData);
            else if (formData.submit == "Update")
                HandleUpdate(formData);
            else if (formData.submit == "Unsubscribe")
                HandleUnsubscribe(formData);

            return RedirectToAction("UpdateSettings");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult VerifyCode(VerifyCodeModel formData)
        {
            ConfirmableUserAccount user = m_dataContext.Table<ConfirmableUserAccount>().QueryRecordWhere("ID = {0}", formData.accountid);
            if (formData.submit == "Submit")
                return HandleVerifySubmit(formData, user);
            else if (formData.submit == "Resend Code")
                return HandleVerifyResendCode(formData, user);

            ViewBag.Message = "Bad Command";
            return View("Message");
        }


        #endregion

        #region [ Get Actions ]
        [HttpGet]
        public ActionResult ApproveUser(string id)
        {
            ConfirmableUserAccount confirmableUserAccount = m_dataContext.Table<ConfirmableUserAccount>().QueryRecordWhere("ID = {0}", Guid.Parse(id));
            m_dataContext.Connection.ExecuteNonQuery("UPDATE UserAccount SET Approved = 1 WHERE ID = {0}", confirmableUserAccount.ID);

            string accountName = UserInfo.SIDToAccountName(confirmableUserAccount.Name);
            ViewBag.Message = accountName + " has been approved.";

            SendEmail(confirmableUserAccount.Email, "openXDA Email Service has been approved.", "openXDA Email Service has been approved.");
            return View("Message");
        }

        [HttpGet]
        public ActionResult DenyUser(string id)
        {
            ConfirmableUserAccount confirmableUserAccount = m_dataContext.Table<ConfirmableUserAccount>().QueryRecordWhere("ID = {0}", Guid.Parse(id));
            string accountName = UserInfo.SIDToAccountName(confirmableUserAccount.Name);
            CascadeDelete("UserAccount", $"ID='{id}'");
            ViewBag.Message = accountName + " has been denied.";
            SendEmail(confirmableUserAccount.Email, "openXDA Email Service has been denied.", "openXDA Email Service has been denied.");
            return View("Message");
        }

        #endregion

        #region [ Helper Functions ]
        private void HandleSignUp(UpdateSettingModel formData) {
            //UserInfo userInfo = new UserInfo(System.Web.HttpContext.Current.User.Identity.Name);
            //userInfo.Initialize();
            //// Create new user
            //m_dataContext.Connection.ExecuteNonQuery("INSERT INTO UserAccount (Name, Email, Phone, FirstName, LastName) VALUES ({0}, {1}, {2}, {3}, {4})", formData.sid, formData.email, formData.phone + "@" + formData.carrier, userInfo.FirstName, userInfo.LastName);
            m_dataContext.Connection.ExecuteNonQuery("INSERT INTO UserAccount (Name, Email, Phone) VALUES ({0}, {1}, {2})", formData.sid, formData.email, formData.phone + "@" + formData.carrier);

            ConfirmableUserAccount user = m_dataContext.Table<ConfirmableUserAccount>().QueryRecordWhere("Name = {0}", formData.sid);

            // link new user to correct asset group
            UserAccountAssetGroup userAccountAssetGroup = m_dataContext.Table<UserAccountAssetGroup>().QueryRecordWhere("UserAccountID = {0}", user.ID);

            if (userAccountAssetGroup == null) {

                userAccountAssetGroup.UserAccountID = user.ID;
                userAccountAssetGroup.AssetGroupID = formData.region;
                userAccountAssetGroup.Dashboard = false;
                userAccountAssetGroup.Email = true;
                
                m_dataContext.Table<UserAccountAssetGroup>().AddNewRecord(userAccountAssetGroup);
            }
            else
            {
                userAccountAssetGroup.AssetGroupID = formData.region;
                userAccountAssetGroup.Email = true;

                m_dataContext.Table<UserAccountAssetGroup>().UpdateRecord(userAccountAssetGroup);
            }

            // link new user to email type for the emails
            EmailType emailType = m_dataContext.Table<EmailType>().QueryRecordWhere("EmailCategoryID = (SELECT ID FROM EmailCategory WHERE Name = 'Event') AND XSLTemplateID = {0}", formData.job);
            UserAccountEmailType userAccountEmailType = new UserAccountEmailType();

            if (emailType != null) {
                userAccountEmailType.UserAccountID = user.ID;
                userAccountEmailType.EmailTypeID = emailType.ID;
                m_dataContext.Table<UserAccountEmailType>().AddNewRecord(userAccountEmailType);
            }

            // link new user to email type for the sms
            emailType = m_dataContext.Table<EmailType>().QueryRecordWhere("EmailCategoryID = (SELECT ID FROM EmailCategory WHERE Name = 'Event') AND XSLTemplateID = {0}", formData.sms);
            userAccountEmailType = new UserAccountEmailType();

            if (emailType != null)
            {
                userAccountEmailType.UserAccountID = user.ID;
                userAccountEmailType.EmailTypeID = emailType.ID;
                m_dataContext.Table<UserAccountEmailType>().AddNewRecord(userAccountEmailType);
            }

            string url = m_dataContext.Connection.ExecuteScalar<string>("SELECT Value FROM DashSettings WHERE Name = 'System.URL'");

            // generate code for email confirmation
            Random generator = new Random();
            string code = generator.Next(0, 999999).ToString("D6");
            s_memoryCache.Set("email" + user.ID.ToString(), code, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromDays(1) });
            SendEmail(user.Email, "openXDA Event Email Service requries you to confirm you email.", $"From your workstation, input {code} at {url}/email/verify/email.");

            // generate code for sms confirmation
            if (!string.IsNullOrEmpty(user.Phone))
            {
                generator = new Random();
                code = generator.Next(0, 999999).ToString("D6");
                s_memoryCache.Set("sms" + user.ID.ToString(), code, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromDays(1) });
                SendEmail(user.Phone, "openXDA Event Email Service requries you to confirm you sms number.", $"From your workstation, input {code} at {url}/email/verify/sms.");
            }

            // email system admin for approval 
            string recipient = m_dataContext.Connection.ExecuteScalar<string>("SELECT Value FROM Setting WHERE Name = 'Email.ApprovalAddress'");
            AssetGroup assetGroup = m_dataContext.Table<AssetGroup>().QueryRecordWhere("ID = {0}", userAccountAssetGroup.AssetGroupID);
            XSLTemplate xSLTemplate = m_dataContext.Table<XSLTemplate>().QueryRecordWhere("ID = {0}", formData.job);

            string body = @"
                <html>
                    <p>"+ formData.username + @" requests access to the openXDA Event Email Service. </p>
                    <table>
                        <tr><td>Email:</td><td>" + formData.email + @"</td></tr>
                        <tr><td>Phone:</td><td>" + formData.phone + @"</td></tr>
                        <tr><td>Region:</td><td>" + assetGroup.Name + @"</td></tr>
                        <tr><td>Job:</td><td>" + xSLTemplate.Name + @"</td></tr>
                    </table>
                    <a href='" + url + @"/email/approveuser/" + user.ID + @"'>Approve</a>
                    <a href='" + url + @"/email/denyuser/" + user.ID + @"'>Deny</a>

                </html>
            ";
            SendEmail(recipient, formData.username + " requests access to the openXDA Event Email Service.", body);
        }

        private void HandleUpdate(UpdateSettingModel formData) {
            ConfirmableUserAccount user = m_dataContext.Table<ConfirmableUserAccount>().QueryRecordWhere("Name = {0}", formData.sid);
            string url = m_dataContext.Connection.ExecuteScalar<string>("SELECT Value FROM DashSettings WHERE Name = 'System.URL'");

            // if email changed force reconfirmation
            if (user.Email != formData.email) {
                user.Email = formData.email;
                user.EmailConfirmed = false;

                // generate code for email confirmation
                Random generator = new Random();
                string code = generator.Next(0, 999999).ToString("D6");
                s_memoryCache.Set("email" + user.ID.ToString(), code, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromDays(1) });
                SendEmail(user.Email, "openXDA Event Email Service requries you to confirm you email.", $"From your workstation, input {code} at {url}/email/verify/email");

            }

            // if phone changed force reconfirmation
            if (user.Phone != formData.phone + "@" + formData.carrier) {
                user.Phone = formData.phone + "@" + formData.carrier;
                user.PhoneConfirmed = false;
                Random generator = new Random();
                string code = generator.Next(0, 999999).ToString("D6");
                s_memoryCache.Set("sms" + user.ID.ToString(), code, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromDays(1) });
                SendEmail(user.Phone, "openXDA Event Email Service requries you to confirm you sms number.", $"From your workstation, input {code} at {url}/email/verify/sms");

            }

            m_dataContext.Table<ConfirmableUserAccount>().UpdateRecord(user);

            // update link to asset group
            UserAccountAssetGroup userAccountAssetGroup = m_dataContext.Table<UserAccountAssetGroup>().QueryRecordWhere("UserAccountID = {0}", user.ID);

            if(userAccountAssetGroup.AssetGroupID != formData.region)
            {
                userAccountAssetGroup.AssetGroupID = formData.region;
                m_dataContext.Table<UserAccountAssetGroup>().UpdateRecord(userAccountAssetGroup);
            }


            // update link to email type for the emails
            EmailType emailType = m_dataContext.Table<EmailType>().QueryRecordWhere("EmailCategoryID = (SELECT ID FROM EmailCategory WHERE Name = 'Event') AND XSLTemplateID = {0}", formData.job);
            UserAccountEmailType userAccountEmailType = m_dataContext.Table<UserAccountEmailType>().QueryRecordWhere("EmailTypeID IN (SELECT ID FROM EmailType WHERE SMS = 0) AND UserAccountID = {0}", user.ID);

            if (formData.job == 0 && userAccountEmailType != null) {
                m_dataContext.Table<UserAccountEmailType>().DeleteRecord(userAccountEmailType);
            }
            else if (userAccountEmailType == null)
            {
                userAccountEmailType = new UserAccountEmailType();
                userAccountEmailType.UserAccountID = user.ID;
                userAccountEmailType.EmailTypeID = emailType.ID;
                m_dataContext.Table<UserAccountEmailType>().AddNewRecord(userAccountEmailType);
            }
            else if (emailType.ID != userAccountEmailType.EmailTypeID)
            {
                userAccountEmailType.EmailTypeID = emailType.ID;
                m_dataContext.Table<UserAccountEmailType>().UpdateRecord(userAccountEmailType);
            }

            // update link to email type for the sns
            emailType = m_dataContext.Table<EmailType>().QueryRecordWhere("EmailCategoryID = (SELECT ID FROM EmailCategory WHERE Name = 'Event') AND XSLTemplateID = {0}", formData.job);
            userAccountEmailType = m_dataContext.Table<UserAccountEmailType>().QueryRecordWhere("EmailTypeID IN (SELECT ID FROM EmailType WHERE SMS <> 0) AND UserAccountID = {0}", user.ID);

            if (formData.job == 0 && userAccountEmailType != null)
            {
                m_dataContext.Table<UserAccountEmailType>().DeleteRecord(userAccountEmailType);
            }
            else if (formData.job > 0 && userAccountEmailType == null)
            {
                userAccountEmailType = new UserAccountEmailType();
                userAccountEmailType.UserAccountID = user.ID;
                userAccountEmailType.EmailTypeID = emailType.ID;
                m_dataContext.Table<UserAccountEmailType>().AddNewRecord(userAccountEmailType);
            }
            else if (emailType.ID != userAccountEmailType.EmailTypeID)
            {
                userAccountEmailType.EmailTypeID = emailType.ID;
                m_dataContext.Table<UserAccountEmailType>().UpdateRecord(userAccountEmailType);
            }
        }

        private void HandleUnsubscribe(UpdateSettingModel formData) {
            // cascade delete out all references to user
            CascadeDelete("UserAccount", $"Name = '{formData.sid}'");
        }

        private ActionResult HandleVerifySubmit(VerifyCodeModel formData, ConfirmableUserAccount user) {
            if (s_memoryCache.Contains(formData.type + user.ID.ToString())){
                string code = s_memoryCache.Get(formData.type + user.ID.ToString()).ToString();
                if (code != formData.code.ToString())
                {
                    TempData["BadCode"] = true;
                    return RedirectToAction("Verify", new { id = formData.type });
                }
                m_dataContext.Connection.ExecuteNonQuery($"UPDATE UserAccount Set {(formData.type == "email" ? "EmailConfirmed" : "PhoneConfirmed")} = 1 WHERE ID = '{user.ID}'");
                s_memoryCache.Remove(formData.type + user.ID.ToString());
            }
            else
            {
                TempData["ExpiredCode"] = true;
                return RedirectToAction("Verify", new { id = formData.type });
            }

            return RedirectToAction("UpdateSettings");
        }

        private ActionResult HandleVerifyResendCode(VerifyCodeModel formData, ConfirmableUserAccount user)
        {
            string url = m_dataContext.Connection.ExecuteScalar<string>("SELECT Value FROM DashSettings WHERE Name = 'System.URL'");

            // if email changed force reconfirmation
            if (formData.type == "email")
            {
                // generate code for email confirmation
                Random generator = new Random();
                string code = generator.Next(0, 999999).ToString("D6");
                s_memoryCache.Set("email" + user.ID.ToString(), code, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromDays(1) });
                SendEmail(user.Email, "openXDA Event Email Service requries you to confirm you email.", $"From your workstation, input {code} at {url}/email/verify/email");

            }

            // if phone changed force reconfirmation
            if (formData.type == "sms")
            {
                Random generator = new Random();
                string code = generator.Next(0, 999999).ToString("D6");
                s_memoryCache.Set("sms" + user.ID.ToString(), code, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromDays(1) });
                SendEmail(user.Phone, "openXDA Event Email Service requries you to confirm you sms number.", $"From your workstation, input {code} at {url}/email/verify/sms");
            }

            return RedirectToAction("Verify", new { id = formData.type });
        }


        #endregion

        #region [ Misc ]
        private void CascadeDelete(string tableName, string criterion)
        {

            using (IDbCommand sc = m_dataContext.Connection.Connection.CreateCommand())
            {

                sc.CommandText = "DECLARE @context VARBINARY(128)\n SELECT @context = CONVERT(VARBINARY(128), CONVERT(VARCHAR(128), @userName))\n SET CONTEXT_INFO @context";
                IDbDataParameter param = sc.CreateParameter();
                param.ParameterName = "@userName";
                param.Value = GetCurrentUserName();
                sc.Parameters.Add(param);
                sc.ExecuteNonQuery();
                sc.Parameters.Clear();


                sc.CommandText = "dbo.UniversalCascadeDelete";
                sc.CommandType = CommandType.StoredProcedure;
                IDbDataParameter param1 = sc.CreateParameter();
                param1.ParameterName = "@tableName";
                param1.Value = tableName;
                IDbDataParameter param2 = sc.CreateParameter();
                param2.ParameterName = "@baseCriteria";
                param2.Value = criterion;
                sc.Parameters.Add(param1);
                sc.Parameters.Add(param2);
                sc.ExecuteNonQuery();
            }
        }

        /// <summary>
        /// Gets UserAccount table name for current user.
        /// </summary>
        /// <returns>User name for current user.</returns>
        private string GetCurrentUserName()
        {
            return Thread.CurrentPrincipal.Identity.Name;
        }

        private void SendEmail(string recipient, string subject, string body)
        {
            const int DefaultSMTPPort = 25;

            string smtpServer = m_dataContext.Connection.ExecuteScalar<string>("SELECT Value FROM Setting WHERE Name = 'Email.SMTPServer'");
            string fromAddress = m_dataContext.Connection.ExecuteScalar<string>("SELECT Value FROM Setting WHERE Name = 'Email.FromAddress'");
            string username = m_dataContext.Connection.ExecuteScalar<string>("SELECT Value FROM Setting WHERE Name = 'Email.Username'");
            SecureString password = m_dataContext.Connection.ExecuteScalar<SecureString>("SELECT Value FROM Setting WHERE Name = 'Email.Password'");
            bool enableSSL = m_dataContext.Connection.ExecuteScalar<bool>("SELECT Value FROM Setting WHERE Name = 'Email.EnableSSL'");

            if (string.IsNullOrEmpty(smtpServer))
                return;

            string[] smtpServerParts = smtpServer.Split(':');
            string host = smtpServerParts[0];
            int port;

            if (smtpServerParts.Length <= 1 || !int.TryParse(smtpServerParts[1], out port))
                port = DefaultSMTPPort;

            using (SmtpClient smtpClient = new SmtpClient(host, port))
            using (MailMessage emailMessage = new MailMessage())
            {
                if (!string.IsNullOrEmpty(username) && (object)password != null)
                    smtpClient.Credentials = new NetworkCredential(username, password);

                smtpClient.EnableSsl = enableSSL;

                emailMessage.From = new MailAddress(fromAddress);
                emailMessage.Subject = subject;
                emailMessage.Body = body;
                emailMessage.IsBodyHtml = true;

                // Add the specified To recipients for the email message
                emailMessage.To.Add(recipient.Trim());

                // Send the email
                smtpClient.Send(emailMessage);
            }
        }
        #endregion

        #endregion
    }
}