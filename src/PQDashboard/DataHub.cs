﻿//******************************************************************************************************
//  DataHub.cs - Gbtc
//
//  Copyright © 2016, Grid Protection Alliance.  All Rights Reserved.
//
//  Licensed to the Grid Protection Alliance (GPA) under one or more contributor license agreements. See
//  the NOTICE file distributed with this work for additional information regarding copyright ownership.
//  The GPA licenses this file to you under the MIT License (MIT), the "License"; you may not use this
//  file except in compliance with the License. You may obtain a copy of the License at:
//
//      http://opensource.org/licenses/MIT
//
//  Unless agreed to in writing, the subject software distributed under the License is distributed on an
//  "AS-IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. Refer to the
//  License for the specific language governing permissions and limitations.
//
//  Code Modification History:
//  ----------------------------------------------------------------------------------------------------
//  01/14/2016 - Ritchie Carroll
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Script.Serialization;
using System.Web.Services;
using GSF.Configuration;
using GSF.Collections;
using GSF.Data;
using GSF.Data.Model;
using GSF.Identity;
using GSF.Web.Hubs;
using GSF.Web.Model;
using GSF.Web.Security;
using PQDashboard.Model;
using openHistorian.XDALink;
using Newtonsoft.Json;
using EventView = openXDA.Model.EventView;
using AlarmRangeLimit = openXDA.Model.AlarmRangeLimit;
using Meter = openXDA.Model.Meter;
using DefaultAlarmRangeLimit = openXDA.Model.DefaultAlarmRangeLimit;
using Event = openXDA.Model.Event;
using openXDA.Model;

namespace PQDashboard
{
    public class DataHub : RecordOperationsHub<DataHub>
    {
        #region [ Members ]

        // Fields
        private readonly DataContext m_coreContext;
        private bool m_disposed;
        enum TimeUnits { Millisecond, Second, Minute, Hour, Day, Week, Month, Year };


        #endregion

        #region [ Constructors ]

        public DataHub() : base(MvcApplication.LogStatusMessage, MvcApplication.LogException)
        {
            m_coreContext = new DataContext("securityProvider",exceptionHandler: MvcApplication.LogException);

        }

        #endregion

        #region [ Properties ]

        /// <summary>
        /// Gets <see cref="IRecordOperationsHub.RecordOperationsCache"/> for SignalR hub.
        /// </summary>
        public RecordOperationsCache RecordOperationsCache => s_recordOperationsCache;

        #endregion

        #region [ Methods ]

        /// <summary>
        /// Releases the unmanaged resources used by the <see cref="DataHub"/> object and optionally releases the managed resources.
        /// </summary>
        /// <param name="disposing">true to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected override void Dispose(bool disposing)
        {
            if (!m_disposed)
            {
                try
                {
                    if (disposing)
                    {
                        m_coreContext?.Dispose();
                    }
                }
                finally
                {
                    m_disposed = true;          // Prevent duplicate dispose.
                    base.Dispose(disposing);    // Call base class Dispose().
                }
            }
        }

        public override Task OnConnected()
        {
            // Store the current connection ID for this thread
            s_connectionID.Value = Context.ConnectionId;
            s_connectCount++;

            //MvcApplication.LogStatusMessage($"DataHub connect by {Context.User?.Identity?.Name ?? "Undefined User"} [{Context.ConnectionId}] - count = {s_connectCount}");
            return base.OnConnected();
        }

        public override Task OnDisconnected(bool stopCalled)
        {
            if (stopCalled)
            {
                s_connectCount--;
                //MvcApplication.LogStatusMessage($"DataHub disconnect by {Context.User?.Identity?.Name ?? "Undefined User"} [{Context.ConnectionId}] - count = {s_connectCount}");
            }

            return base.OnDisconnected(stopCalled);
        }

        #endregion

        #region [ Static ]

        // Static Properties

        /// <summary>
        /// Gets the hub connection ID for the current thread.
        /// </summary>
        public static string CurrentConnectionID => s_connectionID.Value;

        // Static Fields
        private static volatile int s_connectCount;
        private static readonly ThreadLocal<string> s_connectionID = new ThreadLocal<string>();
        private static readonly RecordOperationsCache s_recordOperationsCache;
        private static string connectionstring = ConfigurationFile.Current.Settings["systemSettings"]["ConnectionString"].Value;


        // Static Methods

        /// <summary>
        /// Gets statically cached instance of <see cref="RecordOperationsCache"/> for <see cref="DataHub"/> instances.
        /// </summary>
        /// <returns>Statically cached instance of <see cref="RecordOperationsCache"/> for <see cref="DataHub"/> instances.</returns>
        public static RecordOperationsCache GetRecordOperationsCache() => s_recordOperationsCache;

        // Static Constructor
        static DataHub()
        {
            // Analyze and cache record operations of security hub
            s_recordOperationsCache = new RecordOperationsCache(typeof(DataHub));
        }

        #endregion

        // Client-side script functionality

        #region [ Graph Data Operations]
        public class EventSet
        {
            public DateTime StartDate;
            public DateTime EndDate;
            public class EventDetail
            {
                public string Name;
                public List<Tuple<DateTime, int>> Data;
                public string Color;
                public EventDetail()
                {
                    Data = new List<Tuple<DateTime, int>>();
                } 
            }
            public List<EventDetail> Types; 

            public EventSet()
            {
                Types = new List<EventDetail>();
            }
        }

        public class TrendingData
        {
            public string Date;
            public double Minimum;
            public double Maximum;
            public double Average;
        }

        public EventSet GetDataForPeriod(string siteID, string targetDateFrom, string targetDateTo, string userName, string tab, string context)
        {
            EventSet eventSet = new EventSet();
            string contextWord = "";
            if(context == "day")
            {
                eventSet.StartDate = DateTime.Parse(targetDateFrom).ToUniversalTime();
                eventSet.EndDate = eventSet.StartDate.AddDays(1).AddSeconds(-1);
                contextWord = "Hour";
            }
            else if (context == "hour")
            {
                eventSet.StartDate = DateTime.Parse(targetDateFrom).ToUniversalTime();
                eventSet.EndDate = eventSet.StartDate.AddHours(1).AddSeconds(-1);
                contextWord = "Minute";
            }
            else if (context == "minute" || context =="second")
            {
                eventSet.StartDate = DateTime.Parse(targetDateFrom).ToUniversalTime();
                eventSet.EndDate = eventSet.StartDate.AddMinutes(1).AddSeconds(-1);
                contextWord = "Seconds";
            }
            else
            {
                eventSet.StartDate = DateTime.Parse(targetDateFrom).ToUniversalTime();
                eventSet.EndDate = DateTime.Parse(targetDateTo).ToUniversalTime();
                contextWord = "DateRange";
            }
            Dictionary<string, string> colors = new Dictionary<string, string>();
            Random r = new Random(DateTime.UtcNow.Millisecond);

            IEnumerable<DashSettings> dashSettings = DataContext.Table<DashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "Chart'"));
            List<UserDashSettings> userDashSettings = DataContext.Table<UserDashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "Chart' AND UserAccountID IN (SELECT ID FROM UserAccount WHERE Name = {0})", userName)).ToList();

            Dictionary<string, bool> disabledFileds = new Dictionary<string, bool>();
            foreach (DashSettings setting in dashSettings)
            {
                var index = userDashSettings.IndexOf(x => x.Name == setting.Name && x.Value == setting.Value);
                if (index >= 0)
                {
                    setting.Enabled = userDashSettings[index].Enabled;
                }

                if (!disabledFileds.ContainsKey(setting.Value))
                    disabledFileds.Add(setting.Value, setting.Enabled);

            }

            IEnumerable<DashSettings> colorSettings = DataContext.Table<DashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "ChartColors' AND Enabled = 1"));
            List<UserDashSettings> userColorSettings = DataContext.Table<UserDashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "ChartColors' AND UserAccountID IN (SELECT ID FROM UserAccount WHERE Name = {0})", userName)).ToList();

            foreach (var color in colorSettings)
            {
                var index = userColorSettings.IndexOf(x => x.Name == color.Name && x.Value.Split(',')?[0] == color.Value.Split(',')?[0]);
                if (index >= 0)
                {
                    color.Value = userColorSettings[index].Value;
                }

                if (colors.ContainsKey(color.Value.Split(',')[0]))
                    colors[color.Value.Split(',')[0]] = color.Value.Split(',')[1];
                else
                    colors.Add(color.Value.Split(',')[0], color.Value.Split(',')[1]);
            }

            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = "dbo.select" + tab + "ForMeterIDbyDateRange" ;
                sc.CommandType = CommandType.StoredProcedure;
                IDbDataParameter eventDateFrom = sc.CreateParameter();
                eventDateFrom.ParameterName = "@EventDateFrom";
                eventDateFrom.Value = eventSet.StartDate;
                IDbDataParameter eventDateTo = sc.CreateParameter();
                eventDateTo.ParameterName = "@EventDateTo";
                eventDateTo.Value = eventSet.EndDate;
                sc.Parameters.Add(eventDateFrom);
                sc.Parameters.Add(eventDateTo);
                IDbDataParameter param3 = sc.CreateParameter();
                param3.ParameterName = "@MeterID";
                param3.Value = siteID;
                IDbDataParameter param4 = sc.CreateParameter();
                param4.ParameterName = "@username";
                param4.Value = userName;
                sc.Parameters.Add(param3);
                sc.Parameters.Add(param4);

                // Use next two fields only on Event based tabs that allow context picking.
                List<string> tabList = new List<string> { "Events", "Disturbances", "Faults", "Breakers", "Extensions" };
                if (tabList.Contains(tab))
                {
                    IDbDataParameter param5 = sc.CreateParameter();
                    param5.ParameterName = "@context";
                    param5.Value = context;
                    sc.Parameters.Add(param5);
                }

                IDataReader rdr = sc.ExecuteReader();
                DataTable table = new DataTable();
                table.Load(rdr);

                try
                {
                    foreach (DataRow row in table.Rows)
                    {
                        foreach (DataColumn column in table.Columns)
                        {
                            if (column.ColumnName != "thedate" && !disabledFileds.ContainsKey(column.ColumnName))
                            {
                                disabledFileds.Add(column.ColumnName, true);
                                DataContext.Table<DashSettings>().GetOrAdd(tab + "Chart", column.ColumnName, true);
                            }

                            if (column.ColumnName != "thedate" && disabledFileds[column.ColumnName])
                            {
                                if (eventSet.Types.All(x => x.Name != column.ColumnName))
                                {
                                    eventSet.Types.Add(new EventSet.EventDetail());
                                    eventSet.Types[eventSet.Types.Count - 1].Name = column.ColumnName;
                                    if (colors.ContainsKey(column.ColumnName))
                                        eventSet.Types[eventSet.Types.Count - 1].Color = colors[column.ColumnName];
                                    else
                                    {
                                        eventSet.Types[eventSet.Types.Count - 1].Color = "#" + r.Next(256).ToString("X2") + r.Next(256).ToString("X2") + r.Next(256).ToString("X2");
                                        DataContext.Table<DashSettings>().GetOrAdd(tab + "ChartColors",column.ColumnName + "," + eventSet.Types[eventSet.Types.Count - 1].Color, true);
                                    }
                                }
                                eventSet.Types[eventSet.Types.IndexOf(x => x.Name == column.ColumnName)].Data.Add(Tuple.Create(Convert.ToDateTime(row["thedate"]), Convert.ToInt32(row[column.ColumnName])));
                            }
                        }
                    }

                    if (!eventSet.Types.Any())
                    {
                        foreach (DataColumn column in table.Columns)
                        {
                            if (column.ColumnName != "thedate" && !disabledFileds.ContainsKey(column.ColumnName))
                            {
                                disabledFileds.Add(column.ColumnName, true);
                                DataContext.Table<DashSettings>().GetOrAdd(tab + "Chart", column.ColumnName, true);
                            }

                            if (column.ColumnName != "thedate" && disabledFileds[column.ColumnName])
                            {
                                if (eventSet.Types.All(x => x.Name != column.ColumnName))
                                {
                                    eventSet.Types.Add(new EventSet.EventDetail());
                                    eventSet.Types[eventSet.Types.Count - 1].Name = column.ColumnName;
                                    if (colors.ContainsKey(column.ColumnName))
                                        eventSet.Types[eventSet.Types.Count - 1].Color = colors[column.ColumnName];
                                    else
                                    {
                                        eventSet.Types[eventSet.Types.Count - 1].Color = "#" + r.Next(256).ToString("X2") + r.Next(256).ToString("X2") + r.Next(256).ToString("X2");
                                        DataContext.Table<DashSettings>().GetOrAdd(tab + "ChartColors", column.ColumnName + "," + eventSet.Types[eventSet.Types.Count - 1].Color, true);
                                    }
                                }
                            }
                        }

                    }

                }
                finally
                {
                    if (!rdr.IsClosed)
                    {
                        rdr.Close();
                    }
                }
            }
            return eventSet;
        }

        public EventSet GetDataForPeriodTrending(string siteID, string targetDateFrom, string targetDateTo, string userName, string tab, string context)
        {
            EventSet eventSet = new EventSet();
            eventSet.Types.Add(new EventSet.EventDetail() { Name = "Normal", Color = "" , Data = new List<Tuple<DateTime, int>>() });
            eventSet.Types.Add(new EventSet.EventDetail() { Name = "OffNormal", Color = "", Data = new List<Tuple<DateTime, int>>() });

            string contextWord = "";
            if (context == "day")
            {
                eventSet.StartDate = DateTime.Parse(targetDateFrom).ToUniversalTime();
                eventSet.EndDate = eventSet.StartDate.AddDays(1).AddSeconds(-1);
                for(int i = 0; i < 24; ++i)
                {
                    eventSet.Types[0].Data.Add(Tuple.Create( eventSet.StartDate.AddHours(i), 0));
                    eventSet.Types[1].Data.Add(Tuple.Create(eventSet.StartDate.AddHours(i), 0));
                }
            }
            else
            {
                return GetDataForPeriod(siteID, targetDateFrom, targetDateTo, userName, tab, context);
            }

            string historianServer = DataContext.Connection.ExecuteScalar<string>("SELECT Value FROM Setting WHERE Name = 'Historian.Server'") ?? "127.0.0.1";
            string historianInstance = DataContext.Connection.ExecuteScalar<string>("SELECT Value FROM Setting WHERE Name = 'Historian.InstanceName'") ?? "XDA";
            IEnumerable<Channel> channelIds = DataContext.Table<Channel>().QueryRecordsWhere("MeterID IN (SELECT * FROM String_To_Int_Table({0}, ','))", siteID);

            DateTime epoch = new DateTime(1970, 1, 1);
            Dictionary<string, string> colors = new Dictionary<string, string>();
            Random r = new Random(DateTime.UtcNow.Millisecond);

            IEnumerable<DashSettings> dashSettings = DataContext.Table<DashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "Chart'"));
            List<UserDashSettings> userDashSettings = DataContext.Table<UserDashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "Chart' AND UserAccountID IN (SELECT ID FROM UserAccount WHERE Name = {0})", userName)).ToList();

            Dictionary<string, bool> disabledFileds = new Dictionary<string, bool>();
            foreach (DashSettings setting in dashSettings)
            {
                var index = userDashSettings.IndexOf(x => x.Name == setting.Name && x.Value == setting.Value);
                if (index >= 0)
                {
                    setting.Enabled = userDashSettings[index].Enabled;
                }

                if (!disabledFileds.ContainsKey(setting.Value))
                    disabledFileds.Add(setting.Value, setting.Enabled);

            }

            IEnumerable<DashSettings> colorSettings = DataContext.Table<DashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "ChartColors' AND Enabled = 1"));
            List<UserDashSettings> userColorSettings = DataContext.Table<UserDashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "ChartColors' AND UserAccountID IN (SELECT ID FROM UserAccount WHERE Name = {0})", userName)).ToList();

            foreach (var color in colorSettings)
            {
                var index = userColorSettings.IndexOf(x => x.Name == color.Name && x.Value.Split(',')?[0] == color.Value.Split(',')?[0]);
                if (index >= 0)
                {
                    color.Value = userColorSettings[index].Value;
                }

                if (colors.ContainsKey(color.Value.Split(',')[0]))
                    colors[color.Value.Split(',')[0]] = color.Value.Split(',')[1];
                else
                    colors.Add(color.Value.Split(',')[0], color.Value.Split(',')[1]);
            }

            foreach (EventSet.EventDetail column in eventSet.Types)
            {
                if (!disabledFileds.ContainsKey(column.Name))
                {
                    disabledFileds.Add(column.Name, true);
                    DataContext.Table<DashSettings>().GetOrAdd(tab + "Chart", column.Name, true);
                }

                if (disabledFileds[column.Name])
                {
                    if (colors.ContainsKey(column.Name))
                        eventSet.Types[eventSet.Types.IndexOf(x => x.Name == column.Name)].Color = colors[column.Name];
                    else
                    {
                        eventSet.Types[eventSet.Types.Count - 1].Color = "#" + r.Next(256).ToString("X2") + r.Next(256).ToString("X2") + r.Next(256).ToString("X2");
                        DataContext.Table<DashSettings>().GetOrAdd(tab + "ChartColors", column.Name + "," + eventSet.Types[eventSet.Types.Count - 1].Color, true);
                    }

                }
            }
            IEnumerable<AlarmRangeLimit> alarmLimits = DataContext.Table<AlarmRangeLimit>().QueryRecordsWhere("ChannelID IN ({0})", string.Join(",", channelIds.Select(x => x.ToString())));
            IEnumerable<DefaultAlarmRangeLimit> defaultAlarmLimits = DataContext.Table<DefaultAlarmRangeLimit>().QueryRecords();

            using (Historian historian = new Historian(historianServer, historianInstance))
            {
                foreach (openHistorian.XDALink.TrendingDataPoint point in historian.Read(channelIds.Select(x => x.ID), eventSet.StartDate, eventSet.EndDate))
                {
                    Channel channel = channelIds.First(x => x.ID == point.ChannelID);

                    if (point.SeriesID == SeriesID.Average) {
                        if (alarmLimits.Where(x => x.ChannelID == point.ChannelID).Any())
                        {
                            AlarmRangeLimit limit = alarmLimits.Where(x => x.ChannelID == point.ChannelID).First();
                            if (CheckAlarm(channel, point, limit))
                            {
                                //eventSet.Types[eventSet.Types.IndexOf(x => x.Name == "Alarm")].Data.First(x => x.Item1 >= point.Timestamp && x.Item1 < point)
                            }
                        }
                        else
                        {
                            int measurmentTypeID = channel.MeasurementTypeID;
                            int measurementCharacteristicID = channel.MeasurementCharacteristicID;

                            if (defaultAlarmLimits.Where(x => x.MeasurementTypeID == measurmentTypeID && x.MeasurementCharacteristicID == measurementCharacteristicID).Any())
                            {
                                DefaultAlarmRangeLimit limit = defaultAlarmLimits.Where(x => x.MeasurementTypeID == measurmentTypeID && x.MeasurementCharacteristicID == measurementCharacteristicID).First();

                            }
                        }
                    }
                }
            }

            return eventSet;
        }
        public List<TrendingData> GetTrendingDataForPeriod(string siteID, string colorScale, string targetDateFrom, string targetDateTo, string userName)
        {
            List<TrendingData> eventSet = new List<TrendingData>();
            DateTime thedatefrom = DateTime.Parse(targetDateFrom);
            DateTime thedateto = DateTime.Parse(targetDateTo);

            int duration = thedateto.Subtract(thedatefrom).Days + 1;
            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = "dbo.selectTrendingDataByChannelByDate";
                sc.CommandType = CommandType.StoredProcedure;
                IDbDataParameter param1 = sc.CreateParameter();
                param1.ParameterName = "@StartDate";
                param1.Value = targetDateFrom;
                IDbDataParameter param2 = sc.CreateParameter();
                param2.ParameterName = "@EndDate";
                param2.Value = targetDateTo;
                IDbDataParameter param3 = sc.CreateParameter();
                param3.ParameterName = "@MeterID";
                param3.Value = siteID;
                IDbDataParameter param4 = sc.CreateParameter();
                param4.ParameterName = "@username";
                param4.Value = userName;
                IDbDataParameter param5 = sc.CreateParameter();
                param5.ParameterName = "@colorScale";
                param5.Value = colorScale;
    

                sc.Parameters.Add(param1);
                sc.Parameters.Add(param2);
                sc.Parameters.Add(param3);
                sc.Parameters.Add(param4);
                sc.Parameters.Add(param5);

                IDataReader rdr = sc.ExecuteReader();
                try
                {
                    while (rdr.Read())
                    {
                        TrendingData td = new TrendingData();
                        td.Date = Convert.ToString(rdr["Date"]);
                        td.Maximum = Convert.ToDouble(rdr["Maximum"]);
                        td.Minimum = Convert.ToDouble(rdr["Minimum"]);
                        td.Average = Convert.ToDouble(rdr["Average"]);

                        eventSet.Add(td);
                    }
                }
                finally
                {
                    if (!rdr.IsClosed)
                    {
                        rdr.Close();
                    }
                }
            }

            return (eventSet);
        }

        public class MagDurData
        {
            public int EventID { get; set; }
            public double DurationSeconds { get; set; }
            public double PerUnitMagnitude { get; set; }
        }

        public IEnumerable<MagDurData> GetVoltageMagnitudeData(string meterIds,string startDate, string endDate, string context)
        {

            DateTime beginDate;
            DateTime finishDate;
            if (context == "day")
            {
                beginDate = DateTime.Parse(startDate).ToUniversalTime();
                finishDate = beginDate.AddDays(1).AddSeconds(-1);
            }
            else if (context == "hour")
            {
                beginDate = DateTime.Parse(startDate).ToUniversalTime();
                finishDate = beginDate.AddHours(1).AddSeconds(-1);
            }
            else if (context == "minute")
            {
                beginDate = DateTime.Parse(startDate).ToUniversalTime();
                finishDate = beginDate.AddMinutes(1).AddSeconds(-1);
            }
            else
            {
                beginDate = DateTime.Parse(startDate).ToUniversalTime();
                finishDate = DateTime.Parse(endDate).ToUniversalTime();
            }

            DataTable table = DataContext.Connection.RetrieveData(
                @" SELECT Disturbance.EventID, 
                          Disturbance.DurationSeconds,
                          Disturbance.PerUnitMagnitude
                  FROM Disturbance JOIN 
                       Event ON Event.ID = Disturbance.EventID JOIN
			           DisturbanceSeverity ON Disturbance.ID = DisturbanceSeverity.DisturbanceID JOIN
			           Phase ON Phase.ID = Disturbance.PhaseID JOIN
			           VoltageEnvelope ON VoltageEnvelope.ID = DisturbanceSeverity.VoltageEnvelopeID	               
                 WHERE PhaseID IN (SELECT ID FROM Phase WHERE Name = 'Worst') AND
			           VoltageEnvelope.Name = COALESCE((SELECT TOP 1 Value FROM Setting WHERE Name = 'DefaultVoltageEnvelope'), 'ITIC') AND 
                       (Event.MeterID IN (Select * FROM String_To_Int_Table({0},','))) AND
                       Event.StartTime >= {1} AND Event.StartTime <= {2}  ", meterIds, beginDate, finishDate);
            return table.Select().Select(row => new MagDurData() {
                EventID = int.Parse(row["EventID"].ToString()),
                DurationSeconds = double.Parse(row["DurationSeconds"].ToString()),
                PerUnitMagnitude = double.Parse(row["PerUnitMagnitude"].ToString())
            });
        }

        public IEnumerable<WorkbenchVoltageCurveView> GetCurves()
        {
            return DataContext.Table<WorkbenchVoltageCurveView>().QueryRecords("ID, LoadOrder");
        }

        #endregion

        #region [ Map/Grip Data Operations]

        public class MeterLocations
        {
            public string Data;
            public Dictionary<string, string> Colors; 
        }

        /// <summary>
        /// getLocationsEvents 
        /// </summary>
        /// <param name="targetDateFrom">Start Date</param>
        /// <param name="targetDateTo">End Date</param>
        /// <param name="meterIds">comma separated list of meterIds</param>
        /// <param name="tab">Current PQDashboard Tab</param>
        /// <param name="userName">Current PQDashboard user</param>
        /// <returns>Object with list of meters and counts for tab.</returns> 
        public MeterLocations GetMeterLocations(string targetDateFrom, string targetDateTo, string meterIds, string tab, string userName, string context)
        {
            MeterLocations meters = new MeterLocations();
            DataTable table = new DataTable();

            Dictionary<string, string> colors = new Dictionary<string, string>();

            IEnumerable<DashSettings> dashSettings = DataContext.Table<DashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "Chart'"));
            List<UserDashSettings> userDashSettings = DataContext.Table<UserDashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "Chart' AND UserAccountID IN (SELECT ID FROM UserAccount WHERE Name = {0})", userName)).ToList();

            Dictionary<string, bool> disabledFileds = new Dictionary<string, bool>();
            foreach (DashSettings setting in dashSettings)
            {
                var index = userDashSettings.IndexOf(x => x.Name == setting.Name && x.Value == setting.Value);
                if (index >= 0)
                {
                    setting.Enabled = userDashSettings[index].Enabled;
                }

                if (!disabledFileds.ContainsKey(setting.Value))
                    disabledFileds.Add(setting.Value, setting.Enabled);

            }

            IEnumerable<DashSettings> colorSettings = DataContext.Table<DashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "ChartColors' AND Enabled = 1"));
            List<UserDashSettings> userColorSettings = DataContext.Table<UserDashSettings>().QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "ChartColors' AND UserAccountID IN (SELECT ID FROM UserAccount WHERE Name = {0})", userName)).ToList();

            foreach (var color in colorSettings)
            {
                var index = userColorSettings.IndexOf(x => x.Name == color.Name && x.Value.Split(',')?[0] == color.Value.Split(',')?[0]);
                if (index >= 0)
                {
                    color.Value = userColorSettings[index].Value;
                }

                if (colors.ContainsKey(color.Value.Split(',')[0]))
                    colors[color.Value.Split(',')[0]] = color.Value.Split(',')[1];
                else
                    colors.Add(color.Value.Split(',')[0], color.Value.Split(',')[1]);
            }
            DateTime startDate;
            DateTime endDate;

            if (context == "day")
            {
                startDate = DateTime.Parse(targetDateFrom).ToUniversalTime();
                endDate = startDate.AddDays(1).AddSeconds(-1);
            }
            else if (context == "hour")
            {
                startDate = DateTime.Parse(targetDateFrom).ToUniversalTime();
                endDate = startDate.AddHours(1).AddSeconds(-1);
            }
            else if (context == "minute")
            {
                startDate = DateTime.Parse(targetDateFrom).ToUniversalTime();
                endDate = startDate.AddMinutes(1).AddSeconds(-1);
            }
            else if (context == "second")
            {
                startDate = DateTime.Parse(targetDateFrom).ToUniversalTime();
                endDate = startDate.AddSeconds(1).AddMilliseconds(-1);
            }
            else
            {
                startDate = DateTime.Parse(targetDateFrom).ToUniversalTime();
                endDate = DateTime.Parse(targetDateTo).ToUniversalTime();
            }

            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = "dbo.selectMeterLocations" + tab;
                sc.CommandType = CommandType.StoredProcedure;
                IDbDataParameter eventDateFrom = sc.CreateParameter();
                eventDateFrom.ParameterName = "@EventDateFrom";
                eventDateFrom.Value = startDate;
                IDbDataParameter eventDateTo = sc.CreateParameter();
                eventDateTo.ParameterName = "@EventDateTo";
                eventDateTo.Value = endDate;
                sc.Parameters.Add(eventDateFrom);
                sc.Parameters.Add(eventDateTo);
                IDbDataParameter param3 = sc.CreateParameter();
                param3.ParameterName = "@meterIds";
                param3.Value = meterIds;
                sc.Parameters.Add(param3);
                sc.CommandTimeout = 60;

                // Use next two fields only on Event based tabs that allow context picking.
                List<string> tabList = new List<string> { "Events", "Disturbances", "Faults", "Breakers", "Extensions" };
                if (tabList.Contains(tab))
                {
                    IDbDataParameter param4 = sc.CreateParameter();
                    param4.ParameterName = "@username";
                    param4.Value = userName;
                    IDbDataParameter param5 = sc.CreateParameter();
                    param5.ParameterName = "@context";
                    param5.Value = context;
                    sc.Parameters.Add(param4);
                    sc.Parameters.Add(param5);
                }

                IDataReader rdr = sc.ExecuteReader();
                table.Load(rdr);
            }

            List<string> skipColumns = new List<string>() { "ID", "Name", "Longitude", "Latitude", "Count", "ExpectedPoints", "GoodPoints", "LatchedPoints", "UnreasonablePoints", "NoncongruentPoints", "DuplicatePoints" };
            List<string> columnsToRemove = new List<string>();
            foreach (DataColumn column in table.Columns)
            {
                if (!skipColumns.Contains(column.ColumnName) && !disabledFileds.ContainsKey(column.ColumnName))
                {
                    disabledFileds.Add(column.ColumnName, true);
                    DataContext.Table<DashSettings>().GetOrAdd(tab + "Chart", column.ColumnName, true);
                }

                if (!skipColumns.Contains(column.ColumnName) && !colors.ContainsKey(column.ColumnName))
                {
                    Random r = new Random(DateTime.UtcNow.Millisecond);
                    string color = r.Next(256).ToString("X2") + r.Next(256).ToString("X2") + r.Next(256).ToString("X2");
                    colors.Add(column.ColumnName, color);
                    DataContext.Table<DashSettings>().GetOrAdd(tab + "ChartColors", column.ColumnName + "," + color, true);
                }


                if (!skipColumns.Contains(column.ColumnName) && !disabledFileds[column.ColumnName])
                {
                    columnsToRemove.Add(column.ColumnName);
                }

            }
            foreach (string columnName in columnsToRemove)
            {
                table.Columns.Remove(columnName);
            }

            meters.Colors = colors;
            meters.Data = DataTable2JSON(table);
            return meters;
        }

        /// <summary>
        /// getLocationsHeatmapSags 
        /// </summary>
        /// <param name="targetDateFrom"></param>
        /// <param name="targetDateTo"></param>
        /// <param name="userName"></param>
        /// <returns></returns>
        public MeterLocations GetLocationsHeatmap(string targetDateFrom, string targetDateTo, string meterIds, string type)
        {
            SqlConnection conn = null;
            MeterLocations meters = new MeterLocations();
            DataTable table = new DataTable();

            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = "dbo.selectMeterLocations" + type;
                sc.CommandType = CommandType.StoredProcedure;
                IDbDataParameter eventDateFrom = sc.CreateParameter();
                eventDateFrom.ParameterName = "@EventDateFrom";
                eventDateFrom.Value = targetDateFrom;
                IDbDataParameter eventDateTo = sc.CreateParameter();
                eventDateTo.ParameterName = "@EventDateTo";
                eventDateTo.Value = targetDateTo;
                sc.Parameters.Add(eventDateFrom);
                sc.Parameters.Add(eventDateTo);
                IDbDataParameter param3 = sc.CreateParameter();
                param3.ParameterName = "@meterIds";
                param3.Value = meterIds;
                sc.Parameters.Add(param3);

                IDataReader rdr = sc.ExecuteReader();
                table.Load(rdr);
            }
            meters.Colors = null;
            meters.Data = DataTable2JSON(table);

            return meters;
        }

        /// <summary>
        /// getLocationsTrendingData 
        /// </summary>
        /// <param name="targetDateFrom"></param>
        /// <param name="measurementType"></param>
        /// <param name="targetDateTo"></param>
        /// <param name="userName"></param>
        /// <returns></returns>
        public ContourInfo GetLocationsTrendingData(ContourQuery contourQuery)
        {
            List<TrendingDataLocation> locations = new List<TrendingDataLocation>();
            DataTable colorScale;

            using (AdoDataConnection conn = new AdoDataConnection(connectionstring, typeof(SqlConnection), typeof(SqlDataAdapter)))
            using (IDbCommand cmd = conn.Connection.CreateCommand())
            {

                cmd.Parameters.Add(new SqlParameter("@EventDateFrom", contourQuery.GetStartDate()));
                cmd.Parameters.Add(new SqlParameter("@EventDateTo", contourQuery.GetEndDate()));
                cmd.Parameters.Add(new SqlParameter("@colorScaleName", contourQuery.ColorScaleName));
                cmd.Parameters.Add(new SqlParameter("@meterIds", contourQuery.MeterIds));
                cmd.CommandText = "dbo.selectMeterLocationsTrendingData";
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.CommandTimeout = 300;

                using (IDataReader rdr = cmd.ExecuteReader())
                {
                    while (rdr.Read())
                    {
                        TrendingDataLocation ourStatus = new TrendingDataLocation();
                        ourStatus.Latitude = (double)rdr["Latitude"];
                        ourStatus.Longitude = (double)rdr["Longitude"];
                        ourStatus.Name = (string)rdr["Name"];
                        ourStatus.Average = (rdr.IsDBNull(rdr.GetOrdinal("Average")) ? (double?)null : (double)rdr["Average"]);
                        ourStatus.Maximum = (rdr.IsDBNull(rdr.GetOrdinal("Maximum")) ? (double?)null : (double)rdr["Maximum"]);
                        ourStatus.Minimum = (rdr.IsDBNull(rdr.GetOrdinal("Minimum")) ? (double?)null : (double)rdr["Minimum"]);
                        ourStatus.ID = (int)rdr["id"];
                        ourStatus.Data.Add(ourStatus.Average);
                        ourStatus.Data.Add(ourStatus.Maximum);
                        ourStatus.Data.Add(ourStatus.Minimum);
                        locations.Add(ourStatus);
                    }
                }

                string query =
                    "SELECT " +
                    "    ContourColorScalePoint.Value, " +
                    "    ContourColorScalePoint.Color " +
                    "FROM " +
                    "    ContourColorScale JOIN " +
                    "    ContourColorScalePoint ON ContourColorScalePoint.ContourColorScaleID = ContourColorScale.ID " +
                    "WHERE ContourColorScale.Name = {0} " +
                    "ORDER BY ContourColorScalePoint.OrderID";

                colorScale = conn.RetrieveData(query, contourQuery.ColorScaleName);
            }

            double[] colorDomain = colorScale
                .Select()
                .Select(row => row.ConvertField<double>("Value"))
                .ToArray();

            double[] colorRange = colorScale
                .Select()
                .Select(row => (double)(uint)row.ConvertField<int>("Color"))
                .ToArray();

            return new ContourInfo()
            {
                Locations = locations,
                ColorDomain = colorDomain,
                ColorRange = colorRange,
                DateTo = contourQuery.EndDate,
                DateFrom = contourQuery.StartDate
            };
        }

        #endregion

        #region [ Fault Notes ]

        public IEnumerable<FaultNote> GetNotesForFault(int id)
        {
            return DataContext.Table<FaultNote>().QueryRecords(restriction: new RecordRestriction("FaultSummaryID = {0}", id));
        }

        public void SaveNoteForFault(int id, string note, string userId)
        {
            DataContext.Table<FaultNote>().AddNewRecord(new FaultNote()
            {
                FaultSummaryID = id,
                Note = note,
                UserAccountID = DataContext.Connection.ExecuteScalar<Guid>("SELECT ID FROM UserAccount WHERE Name = {0}", userId),
                Timestamp = DateTime.UtcNow
            });
        }

        public void RemoveNote(int id)
        {
            DataContext.Table<FaultNote>().DeleteRecord(restriction: new RecordRestriction("ID = {0}", id));
        }

        #endregion

        #region [ Event Notes ]

        public IEnumerable<EventNote> GetNotesForEvent(int id)
        {
            return DataContext.Table<EventNote>().QueryRecords(restriction: new RecordRestriction("EventID = {0}", id));
        }

        public void SaveNoteForEvent(int id, string note, string userId)
        {
            if (note.Trim().Length > 0)
            {
                DataContext.Table<EventNote>().AddNewRecord(new EventNote()
                {
                    EventID = id,
                    Note = note,
                    UserAccount = userId,
                    Timestamp = DateTime.UtcNow
                });
            }
        }

        public void SaveMultiNoteForEvent(List<int> ids, string note, string userId)
        {
            if (note.Trim().Length > 0)
            {

                foreach (int evt in ids) {
                    DataContext.Table<EventNote>().AddNewRecord(new EventNote()
                    {
                        EventID = evt,
                        Note = note,
                        UserAccount = userId,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }

        public void SaveMultiNoteForAllEvents(List<int> ids, string note, string userId)
        {
            if (note.Trim().Length > 0)
            {
                foreach (int id in ids)
                {
                    DataContext.Table<EventNote>().AddNewRecord(new EventNote()
                    {
                        EventID = id,
                        Note = note,
                        UserAccount = userId,
                        Timestamp = DateTime.UtcNow
                    });
                }
            }
        }


        public void RemoveEventNote(int id)
        {
            DataContext.Table<EventNote>().DeleteRecord(restriction: new RecordRestriction("ID = {0}", id));
        }


        #endregion

        #region [ Disturbance Notes ]

        public IEnumerable<EventNote> GetNotesForDisturbance(int id)
        {
            return DataContext.Table<EventNote>().QueryRecords(restriction: new RecordRestriction("EventID = (SELECT EventID FROM Disturbance WHERE ID = {0})", id));
        }
        #endregion

        #region [ MeterEventsByLine Operations ]

        public DataTable GetSiteLinesDetailsByDate(string siteID, string targetDate, string context, string tab = "")
        {
            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            using (AdoDataConnection XDAconnection = new AdoDataConnection("dbOpenXDA"))
            {
                int timeWindow = connection.ExecuteScalar<int>("SELECT AltText1 FROM ValueList WHERE Text = 'TimeWindow' AND GroupID = (SELECT ID FROM ValueListGroup WHERE Name = 'System')");

                DataTable table = XDAconnection.RetrieveData(@"
					DECLARE @EventDate DATETIME2 = {0}
                    DECLARE @context as nvarchar(20) = {1}
					DECLARE @MeterID INT = {2}

                    DECLARE @startDate DATETIME = @EventDate
                    DECLARE @endDate DATETIME


                    IF @context = '180d'
                    BEGIN
                        SET @startDate = DATEADD(HOUR, -180, @EventDate)
                        SET @endDate = @EventDate
                    END

                    IF @context = '90d'
                    BEGIN
                        SET @startDate = DATEADD(DAY, -90, @EventDate)
                        SET @endDate = @EventDate
                    END

                    IF @context = '30d'
                    BEGIN
                        SET @startDate = DATEADD(DAY, -30, @EventDate)
                        SET @endDate = @EventDate
                    END

                    IF @context = '7d'
                    BEGIN
                        SET @startDate = DATEADD(DAY, -7, @EventDate)
                        SET @endDate = @EventDate
                    END

                    IF @context = '24h'
                    BEGIN
                        SET @startDate = DATEADD(HOUR, -24, @EventDate)
                        SET @endDate = @EventDate
                    END

                    IF @context = 'day'
                    BEGIN
                        SET @startDate = DATEADD(DAY, DATEDIFF(DAY, 0, @EventDate), 0)
                        SET @endDate = DATEADD(DAY, 1, @startDate)
                    END

                    if @context = 'hour'
                    BEGIN
                        SET @startDate = DATEADD(HOUR, DATEDIFF(HOUR, 0, @EventDate), 0)
                        SET @endDate = DATEADD(HOUR, 1, @startDate)
                    END

                    if @context = 'minute'
                    BEGIN
                        SET @startDate = DATEADD(MINUTE, DATEDIFF(MINUTE, 0, @EventDate), 0)
                        SET @endDate = DATEADD(MINUTE, 1, @startDate)
                    END

                    if @context = 'second'
                    BEGIN
                        DECLARE @tempDate DATETIME = DATEADD(DAY, DATEDIFF(DAY, 0, @EventDate), 0)
                        SET @startDate = DATEADD(SECOND, DATEDIFF(SECOND, @tempDate, @EventDate), @tempDate)
                        SET @endDate = DATEADD(SECOND, 1, @startDate)
                    END


                DECLARE @simStartDate DATETIME = DATEADD(SECOND, -5, @startDate)
                    DECLARE @simEndDate DATETIME = DATEADD(SECOND, 5, @endDate)
                    print @simStartDate
                    print @simEndDate
                    DECLARE @localEventDate DATE = CAST(@EventDate AS DATE)
                    DECLARE @localMeterID INT = CAST(@MeterID AS INT)
                    DECLARE @timeWindow int = {3}

	                SELECT
		                Event.ID,
		                Event.AssetID,
		                EventType.Name AS EventType,
		                Event.StartTime,
		                Asset.AssetName as LineName,
		                Asset.AssetKey AS LineKey,
		                Asset.VoltageKV AS LineVoltage,
		                FaultSummary.FaultType,
		                Disturbance.Type AS DisturbanceType,
		                FaultSummary.Distance AS FaultDistance,
		                Event.UpdatedBy
	                INTO #event
                    FROM
                        Event JOIN
                        EventType ON Event.EventTypeID = EventType.ID OUTER APPLY
		                (
			                SELECT TOP 1
				                Disturbance.*,
				                Phase.Name AS Type
			                FROM
				                Disturbance JOIN
				                Phase ON Disturbance.PhaseID = Phase.ID
			                WHERE
				                EventID = Event.ID AND
				                Phase.Name <> 'Worst'
			                ORDER BY
				                CASE EventType.Name
					                WHEN 'Sag' THEN PerUnitMagnitude
					                WHEN 'Swell' THEN -PerUnitMagnitude
					                WHEN 'Interruption' THEN PerUnitMagnitude
					                WHEN 'Transient' THEN -PerUnitMagnitude
				                END,
				                StartTime
		                ) Disturbance OUTER APPLY
		                (
			                SELECT TOP 1 *
			                FROM FaultSummary
			                WHERE EventID = Event.ID
			                ORDER BY IsSelectedAlgorithm DESC, IsSuppressed, IsValid DESC, Inception
		                ) FaultSummary JOIN
                        Meter ON Meter.ID = @MeterID JOIN
                        Asset ON Event.AssetID = Asset.ID JOIN
                        MeterAsset ON MeterAsset.MeterID = @MeterID AND MeterAsset.AssetID = Asset.ID
                    WHERE
                        Event.StartTime >= @startDate AND Event.StartTime < @endDate AND
                        Event.MeterID = @localMeterID

	                SELECT
                        AssetID AS thelineid,
                        ID AS theeventid,
                        EventType AS theeventtype,
                        CAST(StartTime AS VARCHAR(26)) AS theinceptiontime,
                        LineName + ' ' + LineKey AS thelinename,
                        LineVoltage AS voltage,
                        COALESCE(FaultType, DisturbanceType, '') AS thefaulttype,
                        CASE WHEN FaultDistance = '-1E308' THEN 'NaN' ELSE COALESCE(CAST(CAST(FaultDistance AS DECIMAL(16, 4)) AS NVARCHAR(19)), '') END AS thecurrentdistance,
                        dbo.EventHasImpactedComponents(ID) AS pqiexists,
                        StartTime,
                        (SELECT COUNT(*) FROM Event as EventCount WHERE EventCount.StartTime BETWEEN DateAdd(SECOND, -5, Event.StartTime) and  DateAdd(SECOND, 5, Event.StartTime)) as SimultaneousCount,
                        (SELECT COUNT(*) FROM Event as EventCount WHERE EventTypeID IN (SELECT ID FROM EventType WHERE Name = 'Sag' OR Name = 'Fault') AND EventCount.StartTime BETWEEN DateAdd(SECOND, -@timeWindow, Event.StartTime) and  DateAdd(SECOND, @timeWindow, Event.StartTime)) as SimultaneousFAndSCount,
                        (SELECT COUNT(*) FROM Event as EventCount WHERE EventCount.AssetID = Event.AssetID AND EventCount.StartTime BETWEEN DateAdd(Day, -60, Event.StartTime) and  Event.StartTime) as SixtyDayCount,
                        UpdatedBy,
                        (SELECT COUNT(*) FROM EventNote WHERE EventID = Event.ID) as Note
	                INTO #temp
	                FROM #event Event

                    DECLARE @sql NVARCHAR(MAX)
                    SELECT @sql = COALESCE(@sql + ',dbo.' + HasResultFunction + '(theeventid) AS ' + ServiceName, 'dbo.' + HasResultFunction + '(theeventid) AS ' + ServiceName)
                    FROM EASExtension

                    DECLARE @serviceList NVARCHAR(MAX)
                    SELECT @serviceList = COALESCE(@serviceList + ',' + ServiceName, ServiceName)
                    FROM EASExtension
                    Set @serviceList = '''' + @serviceList + ''''


                    SET @sql = COALESCE('SELECT *,' + @sql + ', '+ @ServiceList +'as ServiceList FROM #temp', 'SELECT *, '''' AS ServiceList FROM #temp')
                    print @sql
                    EXEC sp_executesql @sql

                    DROP TABLE #temp
	                DROP TABLE #event
                ", targetDate, context, siteID, timeWindow);

                return table;
            }

        }
        public IEnumerable<EventView> GetSimultaneousEvents(int eventId, double window, int timeUnit)
        {
            DateTime time = DataContext.Connection.ExecuteScalar<DateTime>("SELECT StartTime From Event WHERE ID = {0}", eventId);
            return DataContext.Table<EventView>().QueryRecordsWhere("StartTime BETWEEN DateAdd(" + ((TimeUnits)timeUnit).ToString() + ", -{0}, {1}) and  DateAdd(" + ((TimeUnits)timeUnit).ToString() + ", {0}, {1})", window, time);
        }

        public IEnumerable<EventView> GetSimultaneousFaultsAndSags(int eventId)
        {
            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            using (AdoDataConnection XDAconnection = new AdoDataConnection("systemSettings"))
            {
                int timeWindow = connection.ExecuteScalar<int>("SELECT AltText1 FROM ValueList WHERE Text = 'TimeWindow' AND GroupID = (SELECT ID FROM ValueListGroup WHERE Name = 'System')");

                DateTime time = XDAconnection.ExecuteScalar<DateTime>("SELECT StartTime From Event WHERE ID = {0}", eventId);
                return new TableOperations<EventView>(XDAconnection).QueryRecordsWhere("EventTypeID IN (SELECT ID FROM EventType WHERE Name = 'Sag' OR Name = 'Fault') AND StartTime BETWEEN DateAdd(SECOND, -1*{2}, {0}) and  DateAdd(SECOND, {2}, {0}) AND ID != {1}", time, eventId, timeWindow);

            }
        }

        public IEnumerable<EventView> GetEventsForLineLastSixtyDays(int eventId)
        {
            Event record = DataContext.Table<Event>().QueryRecordWhere("ID = {0}", eventId);
            return DataContext.Table<EventView>().QueryRecordsWhere("StartTime BETWEEN DateAdd(Day, -60, {0}) and  {0} AND AssetID = {1}", record.StartTime, record.AssetID);
        }

        #endregion

        #region [ QuickSearch Operations ]

        public DataTable GetEvents(DateTime date, int minuteWindow, int timeUnit)
        {
            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            using (AdoDataConnection XDAconnection = new AdoDataConnection("systemSettings"))
            {
                int timeWindow = connection.ExecuteScalar<int>("SELECT AltText1 FROM ValueList WHERE Text = 'TimeWindow' AND GroupID = (SELECT ID FROM ValueListGroup WHERE Name = 'System')");

                string query = @"
                    DECLARE @minuteWindow int = {0}
	                DECLARE @startDate DATETIME = DATEADD(" + ((TimeUnits)timeUnit).ToString() + @", -1*@minuteWindow, {1}) 
                    DECLARE @endDate DATETIME = DATEADD(" + ((TimeUnits)timeUnit).ToString() + @", @minuteWindow, {1}) 

                    DECLARE @localEventDate DATE = CAST({1} AS DATE)
	                DECLARE @timeWindow int = {2}
                    
                    ; WITH cte AS
                    (
                    SELECT
                        Event.AssetID AS thelineid, 
                        Event.ID AS theeventid, 
                        EventType.Name AS theeventtype,
                        CAST(Event.StartTime AS VARCHAR(26)) AS theinceptiontime,
                        Asset.AssetName AS thelinename,
                        Asset.VoltageKV AS voltage,
                        COALESCE(FaultSummary.FaultType, Phase.Name, '') AS thefaulttype,
                        CASE WHEN FaultSummary.Distance = '-1E308' THEN 'NaN' ELSE COALESCE(CAST(CAST(FaultSummary.Distance AS DECIMAL(16, 4)) AS NVARCHAR(19)), '') END AS thecurrentdistance,
                        Event.StartTime,
                        CASE EventType.Name
                            WHEN 'Sag' THEN ROW_NUMBER() OVER(PARTITION BY Event.ID ORDER BY Magnitude, Disturbance.StartTime, IsSelectedAlgorithm DESC, IsSuppressed, Inception)
                            WHEN 'Interruption' THEN ROW_NUMBER() OVER(PARTITION BY Event.ID ORDER BY Magnitude, Disturbance.StartTime, IsSelectedAlgorithm DESC, IsSuppressed, Inception)
                            WHEN 'Swell' THEN ROW_NUMBER() OVER(PARTITION BY Event.ID ORDER BY Magnitude DESC, Disturbance.StartTime, IsSelectedAlgorithm DESC, IsSuppressed, Inception)
                            WHEN 'Fault' THEN ROW_NUMBER() OVER(PARTITION BY Event.ID ORDER BY IsSelectedAlgorithm DESC, IsSuppressed, IsValid DESC, Inception)
                            ELSE ROW_NUMBER() OVER(PARTITION BY Event.ID ORDER BY Event.ID)
                        END AS RowPriority,
			            (SELECT COUNT(*) FROM EventNote WHERE EventID = Event.ID) as Note
                    FROM
                        Event JOIN
                        EventType ON Event.EventTypeID = EventType.ID LEFT OUTER JOIN
                        Disturbance ON Disturbance.EventID = Event.ID LEFT OUTER JOIN
                        FaultSummary ON FaultSummary.EventID = Event.ID  LEFT OUTER JOIN
                        Phase ON Disturbance.PhaseID = Phase.ID JOIN
                        Meter ON Meter.ID = Event.MeterID JOIN
                        Asset ON Event.AssetID = Asset.ID JOIN
                        MeterAsset ON MeterAsset.MeterID = Meter.ID AND MeterAsset.AssetID = Asset.ID
                    WHERE
                        Event.StartTime >= @startDate AND Event.StartTime < @endDate AND (Phase.ID IS NULL OR Phase.Name <> 'Worst')
                    )
                    SELECT * FROM cte WHERE RowPriority = 1
                    ORDER BY StartTime

            ";
                return XDAconnection.RetrieveData(query, minuteWindow, date, timeWindow);
            }
        }

        public DataTable GetDisturbances(DateTime date, int minuteWindow, int timeUnit)
        {
            string query = @"
                    DECLARE @minuteWindow int = {0}
	                DECLARE @startDate DATETIME = DATEADD(" + ((TimeUnits)timeUnit).ToString() + @", -1*@minuteWindow, {1}) 
                    DECLARE @endDate DATETIME = DATEADD(" + ((TimeUnits)timeUnit).ToString() + @", @minuteWindow, {1}) 
                    DECLARE @worstPhaseID INT = (SELECT ID FROM Phase WHERE Name = 'Worst')
                    DECLARE @voltageEnvelope varchar(max) = (SELECT TOP 1 Value FROM Setting WHERE Name = 'DefaultVoltageEnvelope')

                    SELECT 
	                    Event.AssetID AS thelineid, 
	                    Event.ID AS theeventid, 
	                    Disturbance.ID as disturbanceid,
	                    EventType.Name AS disturbancetype,
	                    Phase.Name AS phase,
                        CASE Disturbance.PerUnitMagnitude
                            WHEN -1E308 THEN 'NaN'
                            ELSE CAST(Disturbance.PerUnitMagnitude AS VARCHAR(20))
                        END AS magnitude,
                        CASE Disturbance.DurationSeconds
                            WHEN -1E308 THEN 'NaN'
                            ELSE CAST(CONVERT(DECIMAL(10,3), Disturbance.DurationSeconds) AS VARCHAR(40))
                        END AS duration,
	                    CAST(Disturbance.StartTime AS VARCHAR(26)) AS theinceptiontime,
                        dbo.DateDiffTicks('1970-01-01', Disturbance.StartTime) / 10000.0 AS startmillis,
                        dbo.DateDiffTicks('1970-01-01', Disturbance.EndTime) / 10000.0 AS endmillis,
	                    DisturbanceSeverity.SeverityCode,
	                    Asset.AssetName as thelinename,
	                    Asset.VoltageKV AS voltage,
		                (SELECT COUNT(*) FROM EventNote WHERE EventNote.EventID = Event.ID) as Note
                    FROM
	                    Event JOIN
	                    Disturbance ON Disturbance.EventID = Event.ID JOIN
                        Disturbance WorstDisturbance ON
                            Disturbance.EventID = WorstDisturbance.EventID AND
                            Disturbance.PerUnitMagnitude = WorstDisturbance.PerUnitMagnitude AND
                            Disturbance.DurationSeconds = WorstDisturbance.DurationSeconds JOIN
	                    EventType ON Disturbance.EventTypeID = EventType.ID JOIN
	                    Phase ON Disturbance.PhaseID = Phase.ID JOIN
	                    DisturbanceSeverity ON Disturbance.ID = DisturbanceSeverity.DisturbanceID JOIN
	                    Meter ON Meter.ID = Event.MeterID JOIN
	                    Asset ON Event.AssetID = Asset.ID JOIN
	                    MeterAsset ON MeterAsset.MeterID = Meter.ID AND MeterAsset.AssetID = Asset.ID JOIN
	                    VoltageEnvelope ON VoltageEnvelope.ID = DisturbanceSeverity.VoltageEnvelopeID	
                    WHERE
	                    Event.StartTime >= @startDate AND Event.StartTime < @endDate AND 
                        WorstDisturbance.PhaseID = @worstPhaseID AND
                        Disturbance.PhaseID <> @worstPhaseID AND
	                    VoltageEnvelope.Name = COALESCE(@voltageEnvelope, 'ITIC')
                    ORDER BY
	                    Event.StartTime ASC
            ";
            return DataContext.Connection.RetrieveData(query, minuteWindow, date);
        }

        public DataTable GetFaults(DateTime date, int minuteWindow, int timeUnit)
        {
            string query = @"
                    DECLARE @minuteWindow int = {0}
	                DECLARE @startDate DATETIME = DATEADD(" + ((TimeUnits)timeUnit).ToString() + @", -1*@minuteWindow, {1}) 
                    DECLARE @endDate DATETIME = DATEADD(" + ((TimeUnits)timeUnit).ToString() + @", @minuteWindow, {1}) 

                	; WITH FaultDetail AS
                    (
                        SELECT
                            FaultSummary.ID AS thefaultid,
                            Meter.Name AS thesite,
                            Meter.ShortName AS theshortsite,
                            MeterLocation.ShortName AS locationname,
                            Meter.ID AS themeterid,
                            Line.ID AS thelineid,
                            Event.ID AS theeventid,
                            MeterLine.LineName AS thelinename,
                            Line.VoltageKV AS voltage,
                            CAST(CAST(Event.StartTime AS TIME) AS NVARCHAR(100)) AS theinceptiontime,
                            FaultSummary.FaultType AS thefaulttype,
                            CASE WHEN FaultSummary.Distance = '-1E308' THEN 'NaN' ELSE CAST(CAST(FaultSummary.Distance AS DECIMAL(16,2)) AS NVARCHAR(19)) END AS thecurrentdistance,
		                    (SELECT COUNT(*) FROM EventNote WHERE EventNote.EventID = Event.ID) as Note,
                            ROW_NUMBER() OVER(PARTITION BY Event.ID ORDER BY FaultSummary.IsSuppressed, FaultSummary.IsSelectedAlgorithm DESC, FaultSummary.Inception) AS rk
                        FROM
                            FaultSummary JOIN
                            Event ON FaultSummary.EventID = Event.ID JOIN
                            EventType ON Event.EventTypeID = EventType.ID JOIN
                            Meter ON Event.MeterID = Meter.ID JOIN
                            MeterLocation ON Meter.MeterLocationID = MeterLocation.ID JOIN
                            Line ON Event.LineID = Line.ID JOIN
                            MeterLine ON MeterLine.MeterID = Meter.ID AND MeterLine.LineID = Line.ID
                        WHERE
                            EventType.Name = 'Fault' AND
                            Event.StartTime >= @startDate AND Event.StartTime < @endDate
                    )
                    SELECT *
                    FROM FaultDetail
                    WHERE rk = 1
            ";
            return DataContext.Connection.RetrieveData(query, minuteWindow, date);
        }

        public DataTable GetBreakers(DateTime date, int minuteWindow, int timeUnit)
        {
            string query = @"
                    DECLARE @minuteWindow int = {0}
	                DECLARE @startDate DATETIME = DATEADD(" + ((TimeUnits)timeUnit).ToString() + @", -1*@minuteWindow, {1}) 
                    DECLARE @endDate DATETIME = DATEADD(" + ((TimeUnits)timeUnit).ToString() + @", @minuteWindow, {1}) 

                    SELECT
                        Meter.ID AS meterid,
                        Event.ID AS theeventid,
                        EventType.Name AS eventtype,
                        BreakerOperation.ID AS breakeroperationid,
                        CAST(CAST(BreakerOperation.TripCoilEnergized AS TIME) AS NVARCHAR(100)) AS energized,
                        BreakerOperation.BreakerNumber AS breakernumber,
                        MeterLine.LineName AS linename,
                        Phase.Name AS phasename,
                        CAST(BreakerOperation.BreakerTiming AS DECIMAL(16,5)) AS timing,
                        CAST(BreakerOperation.StatusTiming AS DECIMAL(16,5)) AS statustiming,
                        BreakerOperation.BreakerSpeed AS speed,
                        BreakerOperation.StatusBitChatter AS chatter,
                        BreakerOperation.DcOffsetDetected AS dcoffset,
                        BreakerOperationType.Name AS operationtype,
		                (SELECT COUNT(*) FROM EventNote WHERE EventNote.EventID = Event.ID) as Note
                    FROM
                        BreakerOperation JOIN
                        Event ON BreakerOperation.EventID = Event.ID JOIN
                        EventType ON EventType.ID = Event.EventTypeID JOIN
                        Meter ON Meter.ID = Event.MeterID JOIN
                        Line ON Line.ID = Event.LineID JOIN
                        MeterLine ON MeterLine.LineID = Event.LineID AND MeterLine.MeterID = Meter.ID JOIN
                        BreakerOperationType ON BreakerOperation.BreakerOperationTypeID = BreakerOperationType.ID JOIN
                        Phase ON BreakerOperation.PhaseID = Phase.ID
                    WHERE
                        TripCoilEnergized >= @startDate AND TripCoilEnergized < @endDate
                        ";
            return DataContext.Connection.RetrieveData(query, minuteWindow, date);
        }

        #endregion

        #region [ PQI Operations ]

        public int GetPQICount(int eventId)
        {
            SqlConnection conn = null;
            SqlDataReader rdr = null;
            DataTable dt = new DataTable();

            try
            {
                conn = new SqlConnection(connectionstring);
                conn.Open();
                SqlCommand cmd = new SqlCommand("dbo.GetAllImpactedComponents", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.Add(new SqlParameter("@eventID", eventId));
                cmd.CommandTimeout = 300;

                rdr = cmd.ExecuteReader();
                dt.Load(rdr);
            }
            finally
            {
                if (conn != null)
                {
                    conn.Close();
                }
                if (rdr != null)
                {
                    rdr.Close();
                }
            }
            return dt.Rows.Count;

        }

        #endregion

        #region [ FileGroup - Event - FaultSummary - Disturbance - DisturbanceSeverity ]
        
        private bool ValidatePassedTimeSpanUnit(string timeSpanUnit)
        {
            // The Validation of the date range unit in string timeSpanUnit; where,
            // the date range unit indicates the spanning of whole days ('d','dd') or months ('m', 'mm') or years ('yy', 'yyyy')
            // used in the SQL Date method DATEADD('time span unit', 'int value of span', 'starting SQL DateTime')
            if (System.Text.RegularExpressions.Regex.IsMatch(timeSpanUnit, @"^(m{1,2}|M{1,2}|d{1,2}|D{1,2}|y{2}|y{4}|Y{2}|Y{4})$"))
            {
                return true;
            }
            return false;
        }

        public DataTable QueryFileGroupsForOverview(DateTime startTime, DateTime endTime)
        {
            string userSID = GetCurrentUserSIDOrExternal();
            DataTable table = new DataTable();

            if (startTime <= endTime)
            {
                using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
                {
                    sc.CommandText = @"SELECT DISTINCT DF.FilePath, FG.*
                        FROM DataFile DF JOIN FileGroup FG on DF.FileGroupID=FG.ID JOIN Event E ON E.FileGroupID=FG.ID
                        WHERE E.MeterID IN
                                (SELECT MeterID
                                FROM UserMeter
                                WHERE UserName=@userSID
                                ) AND
                                ProcessingStartTime BETWEEN @startTime AND @endTime
                        ORDER BY FG.ID, FG.ProcessingStartTime DESC
                        ";

                    sc.CommandType = CommandType.Text;
                    IDbDataParameter param1 = sc.CreateParameter();
                    param1.ParameterName = "@startTime";
                    param1.Value = startTime;

                    IDbDataParameter param2 = sc.CreateParameter();
                    param2.ParameterName = "@endTime";
                    param2.Value = endTime;

                    IDbDataParameter param3 = sc.CreateParameter();
                    param3.ParameterName = "@userSID";
                    param3.Value = userSID;

                    sc.Parameters.Add(param1);
                    sc.Parameters.Add(param2);
                    sc.Parameters.Add(param3);

                    IDataReader rdr = sc.ExecuteReader();
                    table.Load(rdr);

                    return table;
                }
            }
            else
            {
                return null;
            }
        }

        public IEnumerable<EventView> QueryFileGroupEvents(int FileGroupID)
        {
            DataTable table = new DataTable();

            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = "SELECT * " +
                    "FROM EventView " +
                    "WHERE FileGroupID=" + FileGroupID.ToString();

                IDataReader rdr = sc.ExecuteReader();
                table.Load(rdr);

                return table.Select().Select(row => DataContext.Table<EventView>().LoadRecord(row));
            }
        }


        public int QueryFileGroupCount(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {
            int recordCount = -1;
            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                recordCount = DataContext.Table<FileGroup>().QueryRecordCountWhere("[FileGroup].ID IN (SELECT [Event].FileGroupID FROM [Event] LEFT JOIN [FileGroup] ON [FileGroup].ID = [Event].FileGroupID WHERE ([Event].StartTime >= {0} AND [Event].StartTime < DATEADD(" + timeSpanUnit + "," + timeSpanValue + ",{0})))", startTime);
            }

            return recordCount;
        }

        public string GetCurrentUserSIDOrExternal()
        {
            string userSID = UserInfo.UserNameToSID(Context.User.Identity.Name);
            if (DataContext.Connection.ExecuteScalar<int>("SELECT COUNT(*) FROM UserAccount WHERE Name = {0}", userSID) == 0)
                userSID = "External";

            return userSID;
        }

        public DataTable QueryMeterActivity(DateTime startTime, string orderBy, int numberOfResults, bool ascending = false, bool sortByEvents = false)
        {
            string userSID = GetCurrentUserSIDOrExternal();

            string order;
            order = ascending ? "ASC" : "DESC";

            string sortBy, thenBy;
            sortBy = sortByEvents == true ? "Events" : "FileGroups";
            thenBy = sortByEvents == true ? "FileGroups180Days" : "Events180Days";

            if (orderBy == null || orderBy.IndexOf("24h", StringComparison.OrdinalIgnoreCase) >= 0)
                orderBy = sortBy + "24Hours";
            else if (orderBy.IndexOf("7d", StringComparison.OrdinalIgnoreCase) >= 0)
                orderBy = sortBy + "7Days";
            else if (orderBy.IndexOf("30d", StringComparison.OrdinalIgnoreCase) > 0)
                orderBy = sortBy + "30Days";
            else if (orderBy.IndexOf("90d", StringComparison.OrdinalIgnoreCase) > 0)
                orderBy = sortBy + "90Days";
            else if (orderBy.IndexOf("180d", StringComparison.OrdinalIgnoreCase) > 0)
                orderBy = sortBy + "180Days";

            DataTable table = new DataTable();

            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = @" SELECT	TOP " + numberOfResults + @" m.*, 
		                                    (CASE WHEN Summary24H.EventCount IS NULL THEN 0 ELSE Summary24H.EventCount END) AS Events24Hours,
		                                    (CASE WHEN Summary24H.FileGroupCount IS NULL THEN 0 ELSE Summary24H.FileGroupCount END) AS FileGroups24Hours,

		                                    (CASE WHEN Summary7D.EventCount IS NULL THEN 0 ELSE Summary7D.EventCount END) AS Events7Days,
		                                    (CASE WHEN Summary7D.FileGroupCount IS NULL THEN 0 ELSE Summary7D.FileGroupCount END) AS FileGroups7Days,
                                                       
		                                    (CASE WHEN Summary30D.EventCount IS NULL THEN 0 ELSE Summary30D.EventCount END) AS Events30Days,
		                                    (CASE WHEN Summary30D.FileGroupCount IS NULL THEN 0 ELSE Summary30D.FileGroupCount END) AS FileGroups30Days,
                                                       
		                                    (CASE WHEN Summary90D.EventCount IS NULL THEN 0 ELSE Summary90D.EventCount END) AS Events90Days,
		                                    (CASE WHEN Summary90D.FileGroupCount IS NULL THEN 0 ELSE Summary90D.FileGroupCount END) AS FileGroups90Days,
                                                       
		                                    (CASE WHEN Summary180D.EventCount IS NULL THEN 0 ELSE Summary180D.EventCount END) AS Events180Days,
		                                    (CASE WHEN Summary180D.FileGroupCount IS NULL THEN 0 ELSE Summary180D.FileGroupCount END) AS FileGroups180Days,

                                            FirstEvent.EventID AS FirstEventID

                                    FROM	(SELECT me.*
                                            FROM UserMeter u JOIN Meter me ON u.MeterID=me.ID
                                            WHERE u.UserName=@userSID) AS m LEFT OUTER JOIN

		                                    (SELECT MeterID, COUNT(ID) AS EventCount, COUNT(DISTINCT FileGroupID) AS FileGroupCount
		                                    FROM Event
		                                    WHERE StartTime <= @StartTime AND StartTime >= DATEADD(HH,-24,@StartTime)
		                                    GROUP BY MeterID) AS Summary24H ON m.ID=Summary24H.MeterID LEFT OUTER JOIN
	
	                                        (SELECT MeterID, COUNT(ID) AS EventCount, COUNT(DISTINCT FileGroupID) AS FileGroupCount
		                                    FROM Event
		                                    WHERE StartTime <= @StartTime AND StartTime >= DATEADD(DD,-7,@StartTime)
		                                    GROUP BY MeterID) AS Summary7D ON m.ID=Summary7D.MeterID LEFT OUTER JOIN

                                        	(SELECT MeterID, COUNT(ID) AS EventCount, COUNT(DISTINCT FileGroupID) AS FileGroupCount
		                                    FROM Event
		                                    WHERE StartTime <= @StartTime AND StartTime >= DATEADD(DD,-30,@StartTime)
		                                    GROUP BY MeterID) AS Summary30D ON m.ID=Summary30D.MeterID LEFT OUTER JOIN

	                                        (SELECT MeterID, COUNT(ID) AS EventCount, COUNT(DISTINCT FileGroupID) AS FileGroupCount
		                                    FROM Event
		                                    WHERE StartTime <= @StartTime AND StartTime >= DATEADD(DD,-90,@StartTime)
		                                    GROUP BY MeterID) AS Summary90D ON m.ID=Summary90D.MeterID LEFT OUTER JOIN

                                        	(SELECT MeterID, COUNT(ID) AS EventCount, COUNT(DISTINCT FileGroupID) AS FileGroupCount
		                                    FROM Event
		                                    WHERE StartTime <= @StartTime AND StartTime >= DATEADD(DD,-180,@StartTime)
		                                    GROUP BY MeterID) AS Summary180D ON m.ID=Summary180D.MeterID LEFT OUTER JOIN

                                            (SELECT Meter.ID, FirstEvent.EventID
                                            FROM Meter LEFT OUTER JOIN
                                                (SELECT e.MeterID, MIN(e.ID) AS EventID
                                                FROM Event e JOIN
                                                    (SELECT e.MeterID, MAX(e.StartTime) AS MinStartTime
                                                    FROM Event e
                                                    WHERE e.StartTime<=@StartTime 
                                                    GROUP BY e.MeterID) AS MinStartTime ON MinStartTime.MeterID=e.MeterID AND MinStartTime.MinStartTime=e.StartTime
                                                GROUP BY e.MeterID) AS FirstEvent ON FirstEvent.MeterID=Meter.ID) AS FirstEvent ON FirstEvent.ID=m.ID

                                    ORDER BY " + orderBy + " " + order;

                sc.CommandType = CommandType.Text;

                IDbDataParameter param1 = sc.CreateParameter();
                param1.ParameterName = "@StartTime";
                param1.Value = startTime;
                sc.Parameters.Add(param1);

                IDbDataParameter param2 = sc.CreateParameter();
                param2.ParameterName = "@userSID";
                param2.Value = userSID;
                sc.Parameters.Add(param2);

                IDataReader rdr = sc.ExecuteReader();
                table.Load(rdr);

                return table;
            }
        }

        public int QueryMeterCount(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {
            int recordCount = -1;

            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                recordCount = DataContext.Table<Meter>().QueryRecordCountWhere("[Meter].ID IN (SELECT DISTINCT [Event].MeterID FROM [Event] WHERE ([Event].StartTime >= {0} AND [Event].StartTime < DATEADD(" + timeSpanUnit + "," + timeSpanValue + ",{0})))", startTime);
            }

            return recordCount;
        }

        public int QueryTotalMeterCount()
        {
            int recordCount;

            recordCount = DataContext.Table<Meter>().QueryRecordCount();

            return recordCount;
        }

        public IEnumerable<Meter> QueryMeterRecords(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {//**
            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                return DataContext.Table<Meter>().QueryRecords(restriction: new RecordRestriction("[Meter].ID IN (SELECT DISTINCT [Event].MeterID FROM [Event] WHERE ([Event].StartTime >= {0} AND [Event].StartTime < DATEADD(" + timeSpanUnit + "," + timeSpanValue + ",{0})))", startTime)); ;
            }
            else
            {
                return null;
            }
        }

        public int QueryLineCount(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {
            int recordCount = -1;

            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                recordCount = DataContext.Table<Line>().QueryRecordCountWhere("[Line].ID IN (SELECT DISTINCT [Event].LineID FROM [Event] WHERE ([Event].StartTime >= {0} AND [Event].StartTime < DATEADD(" + timeSpanUnit + "," + timeSpanValue + ",{0})))", startTime);
            }

            return recordCount;
        }

        public IEnumerable<Line> QueryLineRecords(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {//**
            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                return DataContext.Table<Line>().QueryRecords(restriction: new RecordRestriction("[Line].ID IN (SELECT DISTINCT [Event].LineID FROM [Event] WHERE ([Event].StartTime >= {0} AND [Event].StartTime < DATEADD(" + timeSpanUnit + "," + timeSpanValue + ",{0})))", startTime)); ;
            }
            else
            {
                return null;
            }
        }

        public int QueryFaultSummaryCount(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {
            int recordCount = -1;

            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                recordCount = DataContext.Table<FaultSummary>().QueryRecordCountWhere("[FaultSummary].EventID IN (SELECT [Event].ID FROM [Event] WHERE ([Event].StartTime >= {0} AND [Event].StartTime < DATEADD(" + timeSpanUnit + ", " + timeSpanValue + ",{0}))) AND ([FaultSummary].IsSelectedAlgorithm <> 0 AND [FaultSummary].IsValid <> 0 AND [FaultSummary].IsSuppressed = 0)", startTime);
            }

            return recordCount;
        }

        public IEnumerable<FaultSummary> QueryFaultSummaryRecords(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {//**
            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                return DataContext.Table<FaultSummary>().QueryRecords(restriction: new RecordRestriction("[FaultSummary].EventID IN " +
                                            "(SELECT [Event].ID FROM [Event] WHERE ([Event].StartTime >= {0} AND [Event].StartTime < DATEADD(" + timeSpanUnit + ", " + timeSpanValue + ",{0})))" +
                                            " AND ([FaultSummary].IsSelectedAlgorithm <> 0 AND [FaultSummary].IsValid <> 0 AND [FaultSummary].IsSuppressed = 0)", startTime));
            }
            else
            {
                return null;
            }
        }

        public DataTable QueryFaultSummarysForOverviewRecords(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {
            DataTable table = new DataTable();

            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
                {
                    sc.CommandText = "SELECT Event.ID AS EventID, " + 
                                            "[Event].StartTime, " + 
                                            "Meter.AssetKey AS MeterName, " + 
                                            "Line.AssetKey AS LineName, " +
                                            "[EventType].[Description]," +
                                            "[FaultSummary].FaultType, " + 
                                            "[FaultSummary].DurationSeconds " +
                    "FROM [FaultSummary] JOIN [Event] ON [FaultSummary].EventID = [Event].ID " +
                                        "JOIN [EventType] ON [EventType].[ID] = [Event].EventTypeID " +
                                        "JOIN Line ON [Event].LineID = [Line].ID " + 
                                        "JOIN Meter ON Event.MeterID = Meter.ID " +
                    "WHERE ([Event].StartTime >= @startDateRange AND [Event].StartTime < DATEADD( " + timeSpanUnit + ", @spanValue, @startDateRange)) " +
                    "AND (FaultSummary.IsSelectedAlgorithm = 1 AND FaultSummary.IsValid = 1 AND FaultSummary.IsSuppressed = 0) " +
                    "ORDER BY[Event].ID";

                    sc.CommandType = CommandType.Text;
                    IDbDataParameter param1 = sc.CreateParameter();
                    param1.ParameterName = "@spanValue";
                    param1.Value = timeSpanValue;
                    IDbDataParameter param2 = sc.CreateParameter();
                    param2.ParameterName = "@startDateRange";
                    param2.Value = startTime;

                    sc.Parameters.Add(param1);
                    sc.Parameters.Add(param2);

                    IDataReader rdr = sc.ExecuteReader();
                    table.Load(rdr);

                    return table;
                }
            } 
            else
            {
                return null;
            }
        }

        public int QueryFaultSummaryGroundFaultCount(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {
            int recordCount = -1;

            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                recordCount = DataContext.Table<FaultSummary>().QueryRecordCountWhere("[FaultSummary].EventID IN (SELECT [Event].ID FROM [Event] WHERE ([Event].StartTime >= {0} AND [Event].StartTime < DATEADD(" + timeSpanUnit + ", " + timeSpanValue + ",{0}))) AND ([FaultSummary].FaultType = 'AN' OR [FaultSummary].FaultType = 'BN' OR [FaultSummary].FaultType = 'CN') AND ([FaultSummary].IsSelectedAlgorithm <> 0 AND [FaultSummary].IsValid <> 0 AND [FaultSummary].IsSuppressed = 0)", startTime);
            }

            return recordCount;
        }

        public int QueryFaultSummaryLineFaultCount(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {
            int recordCount = -1;

            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                recordCount = DataContext.Table<FaultSummary>().QueryRecordCountWhere("[FaultSummary].EventID IN (SELECT [Event].ID FROM [Event] WHERE ([Event].StartTime >= {0} AND [Event].StartTime < DATEADD(" + timeSpanUnit + ", " + timeSpanValue + ",{0}))) AND ([FaultSummary].FaultType = 'AB' OR [FaultSummary].FaultType = 'BC' OR [FaultSummary].FaultType = 'CA') AND (FaultSummary.IsSelectedAlgorithm <> 0 AND FaultSummary.IsValid <> 0 AND FaultSummary.IsSuppressed = 0)", startTime);
            }

            return recordCount;
        }

        public int QueryFaultSummaryAllPhaseFaultCount(DateTime startTime, string timeSpanUnit, int timeSpanValue)
        {
            int recordCount = -1;

            if (ValidatePassedTimeSpanUnit(timeSpanUnit))
            {
                recordCount = DataContext.Table<FaultSummary>().QueryRecordCountWhere("[FaultSummary].EventID IN (SELECT [Event].ID FROM [Event] WHERE ([Event].StartTime >= {0} AND [Event].StartTime < DATEADD(" + timeSpanUnit + ", " + timeSpanValue + ",{0}))) AND ([FaultSummary].FaultType = 'ABC') AND (FaultSummary.IsSelectedAlgorithm <> 0 AND FaultSummary.IsValid <> 0 AND FaultSummary.IsSuppressed = 0)", startTime);
            }

            return recordCount;
        }

        public DataTable GetDisturbanceSeverityByHourOfDay(DateTime CurrrentDaysDate)
        {
            DataTable table = new DataTable();

            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = "dbo.selectDisturbanceSeverityByHourOfDay";
                sc.CommandType = CommandType.StoredProcedure;
                IDbDataParameter startDateRange = sc.CreateParameter();
                startDateRange.ParameterName = "@startDateRange";
                startDateRange.Value = CurrrentDaysDate;
                sc.Parameters.Add(startDateRange);

                IDataReader rdr = sc.ExecuteReader();
                table.Load(rdr);
            }

            return table;
        }

        public DataTable GetAlarmsForLast30Days(DateTime CurrentDate)
        {
            DataTable table = new DataTable();

            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = "dbo.selectAlarmsLast30Days";
                sc.CommandType = CommandType.StoredProcedure;
                IDbDataParameter startDateRange = sc.CreateParameter();
                startDateRange.ParameterName = "@startDate";
                startDateRange.Value = CurrentDate;
                sc.Parameters.Add(startDateRange);

                IDataReader rdr = sc.ExecuteReader();
                table.Load(rdr);
            }

            return table;
        }

        public DataTable GetOffNormalForLast30Days(DateTime CurrentDate)
        {
            DataTable table = new DataTable();

            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = "dbo.selectOffNormalsLast30Days";
                sc.CommandType = CommandType.StoredProcedure;
                IDbDataParameter startDateRange = sc.CreateParameter();
                startDateRange.ParameterName = "@startDate";
                startDateRange.Value = CurrentDate;
                sc.Parameters.Add(startDateRange);

                IDataReader rdr = sc.ExecuteReader();
                table.Load(rdr);
            }

            return table;
        }

        public DataTable GetLevel4_5DisturbancesForLast30Days(DateTime CurrentDate)
        {
            DataTable table = new DataTable();

            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = "dbo.selectAllDistrubanceLevel45Last30DaysByDay";
                sc.CommandType = CommandType.StoredProcedure;
                IDbDataParameter startDateRange = sc.CreateParameter();
                startDateRange.ParameterName = "@startDate";
                startDateRange.Value = CurrentDate;
                sc.Parameters.Add(startDateRange);

                IDataReader rdr = sc.ExecuteReader();
                table.Load(rdr);
            }

            return table;
        }

        public DataTable GetAllFaultsForLast30Days(DateTime CurrentDate)
        {
            DataTable table = new DataTable();

            using (IDbCommand sc = DataContext.Connection.Connection.CreateCommand())
            {
                sc.CommandText = "dbo.selectAllFaultsLast30DaysByDay";
                sc.CommandType = CommandType.StoredProcedure;
                IDbDataParameter startDateRange = sc.CreateParameter();
                startDateRange.ParameterName = "@startDate";
                startDateRange.Value = CurrentDate;
                sc.Parameters.Add(startDateRange);

                IDataReader rdr = sc.ExecuteReader();
                table.Load(rdr);
            }

            return table;
        }

        #endregion

        #region [ Misc Hub Operations ]

        /// <summary>
        /// Gets the absolute path for a virtual path, e.g., ~/Images/Menu
        /// </summary>
        /// <param name="path">Virtual path o convert to absolute path.</param>
        /// <returns>Absolute path for a virtual path.</returns>
        public string GetAbsolutePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "";

            return VirtualPathUtility.ToAbsolute(path);
        }

        public DateTime GetXdaTime()
        {
            string xdaTimeZoneString = DataContext.Connection.ExecuteScalar<string>("Select Value FROM Setting WHERE Name = 'XDATimeZone'") ?? "Eastern Daylight Time";
            TimeZoneInfo xdaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(xdaTimeZoneString);
            DateTime time = DateTime.UtcNow;
            return TimeZoneInfo.ConvertTimeFromUtc(time, xdaTimeZone);
        }

        public string GetXdaTime(string format)
        {
            string xdaTimeZoneString = DataContext.Connection.ExecuteScalar<string>("Select Value FROM Setting WHERE Name = 'XDATimeZone'") ?? "Eastern Daylight Time";
            TimeZoneInfo xdaTimeZone = TimeZoneInfo.FindSystemTimeZoneById(xdaTimeZoneString);
            DateTime time = DateTime.UtcNow;
            string returnString = TimeZoneInfo.ConvertTimeFromUtc(time, xdaTimeZone).ToString(format);
            return returnString;
        }

        /// <summary>
        /// Gets UserAccount table ID for current user.
        /// </summary>
        /// <returns>UserAccount.ID for current user.</returns>
        public static Guid GetCurrentUserID()
        {
            Guid userID;
            AuthorizationCache.UserIDs.TryGetValue(Thread.CurrentPrincipal.Identity.Name, out userID);
            return userID;
        }

        /// <summary>
        /// Gets UserAccount table ID for current user.
        /// </summary>
        /// <returns>UserAccount.ID for current user.</returns>
        public static string GetCurrentUserSID()
        {
            return UserInfo.UserNameToSID(Thread.CurrentPrincipal.Identity.Name);
        }


        /// <summary>
        /// DataTable2JSON
        /// </summary>
        /// <param name="dt"></param>
        /// <returns></returns>
        public string DataTable2JSON(DataTable dt)
        {
            return JsonConvert.SerializeObject(dt);
        }

        private bool CheckAlarm(Channel channel, openHistorian.XDALink.TrendingDataPoint trendingPoint, AlarmRangeLimit rangeLimit)
        {
            double perUnitValue;

            double highLimit = 0.0D;
            double lowLimit = 0.0D;
            bool highValid = true;
            bool lowValid = true;

            perUnitValue = channel.PerUnitValue ?? 1.0D;

            if (rangeLimit.High != null)
            {
                highLimit = Convert.ToBoolean(rangeLimit.PerUnit) ? (rangeLimit.High.GetValueOrDefault() * perUnitValue) : rangeLimit.High.GetValueOrDefault();
                highValid = Convert.ToBoolean(rangeLimit.RangeInclusive) ^ (trendingPoint.Value <= highLimit);
            }

            if (rangeLimit.Low != null)
            {
                lowLimit = Convert.ToBoolean(rangeLimit.PerUnit) ? (rangeLimit.Low.GetValueOrDefault() * perUnitValue) : rangeLimit.Low.GetValueOrDefault();
                lowValid = Convert.ToBoolean(rangeLimit.RangeInclusive) ^ (trendingPoint.Value >= lowLimit);
            }

            return !lowValid || !highValid;
        }


        #endregion
    }
}
