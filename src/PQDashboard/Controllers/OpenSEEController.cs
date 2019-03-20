﻿//******************************************************************************************************
//  OpenSEEController.cs - Gbtc
//
//  Copyright © 2018, Grid Protection Alliance.  All Rights Reserved.
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
//  04/17/2018 - Billy Ernest
//       Generated original version of source code.
//
//******************************************************************************************************
using FaultData.DataAnalysis;
using GSF;
using GSF.Data;
using GSF.Data.Model;
using GSF.Security;
using GSF.Web;
using GSF.Web.Model;
using openXDA.Model;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Runtime.Caching;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace OpenSEE.Controller
{
    public class OpenSEEController : ApiController
    {
        #region [ Members ]

        // Fields
        private DateTime m_epoch = new DateTime(1970, 1, 1);
        private bool m_disposed;

        public class JsonReturn
        {
            public DateTime StartDate;
            public DateTime EndDate;
            public double CalculationTime;
            public double CalculationEnd;
            public List<FlotSeries> Data;
        }

        public class FlotSeries
        {
            public int ChannelID;
            public string ChannelName;
            public string ChannelDescription;
            public string MeasurementType;
            public string MeasurementCharacteristic;
            public string Phase;
            public string SeriesType;
            public string ChartLabel;
            public List<double[]> DataPoints = new List<double[]>();
        }

        #endregion

        #region [ Static ]
        private static MemoryCache s_memoryCache;

        static OpenSEEController()
        {
            s_memoryCache = new MemoryCache("openSEE");
        }
        #endregion

        #region [ Methods ]

        #region [ Waveform Data ]
        [HttpGet]
        public JsonReturn GetData()
        {
            using(AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                Dictionary<string, string> query = Request.QueryParameters();
                int eventId = int.Parse(query["eventId"]);
                Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                meter.ConnectionFactory = () => new AdoDataConnection(connection.Connection, typeof(SqlDataAdapter), false);
                int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;

                string type = query["type"];
                string dataType = query["dataType"];

                DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                int pixels = int.Parse(query["pixels"]);
                DataTable table;

                Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                foreach (DataRow row in table.Rows)
                {
                    int eventID = row.ConvertField<int>("ID");
                    DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                    Dictionary<string, FlotSeries> temp;

                    if (dataType == "Time")
                        temp = QueryEventData(eventID, eventStartTime, meter, type);
                    else
                        temp = QueryFrequencyData(eventID, eventStartTime, meter, type);

                    foreach (string key in temp.Keys)
                    {
                        if (temp[key].MeasurementType == type)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                }
                if (dict.Count == 0) return null;

                double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                List<FlotSeries> returnList = new List<FlotSeries>();
                foreach (string key in dict.Keys)
                {
                    FlotSeries series = new FlotSeries();
                    series = dict[key];
                    series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                    returnList.Add(series);
                }
                JsonReturn returnDict = new JsonReturn();
                returnDict.StartDate = evt.StartTime;
                returnDict.EndDate = evt.EndTime;
                returnDict.Data = returnList;
                returnDict.CalculationTime = calcTime;
                returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                return returnDict;


            }
        }

        private Dictionary<string, FlotSeries> QueryEventData(int eventID, DateTime startTime, Meter meter, string type)
        {
            string target = $"DataGroup-{eventID}-{startTime.Ticks}-{type}";
            DataGroup dataGroup = (DataGroup)s_memoryCache.Get(target);

            if (dataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings")) {
                    byte[] timeDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    dataGroup = ToDataGroup(meter, timeDomainData);
                    s_memoryCache.Add(target, dataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetDataLookup(dataGroup, type);
        }

        private Dictionary<string, FlotSeries> QueryFrequencyData(int eventID, DateTime startTime, Meter meter, string type)
        {
            string target = $"VICycleDataGroup-{eventID}-{startTime.Ticks}-{type}";
            VICycleDataGroup vICycleDataGroup = (VICycleDataGroup)s_memoryCache.Get(target);

            if (vICycleDataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings")) {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    DataGroup dataGroup = ToDataGroup(meter, frequencyDomainData);
                    double freq = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0D;
                    vICycleDataGroup = Transform.ToVICycleDataGroup(new VIDataGroup(dataGroup), 60);
                    s_memoryCache.Add(target, vICycleDataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetFrequencyDataLookup(vICycleDataGroup, type);
        }

        private Dictionary<string, FlotSeries> GetDataLookup(DataGroup dataGroup, string type)
        {
            Dictionary<string, FlotSeries> dataLookup = dataGroup.DataSeries.Where(ds => ds.SeriesInfo.Channel.MeasurementType.Name == type).ToDictionary(ds => GetChartLabel(ds.SeriesInfo.Channel), ds => new FlotSeries()
            {
                ChannelID = ds.SeriesInfo.Channel.ID,
                ChannelName = ds.SeriesInfo.Channel.Name,
                ChannelDescription = ds.SeriesInfo.Channel.Description,
                MeasurementCharacteristic = ds.SeriesInfo.Channel.MeasurementCharacteristic.Name,
                MeasurementType = ds.SeriesInfo.Channel.MeasurementType.Name,
                Phase = ds.SeriesInfo.Channel.Phase.Name,
                SeriesType = ds.SeriesInfo.Channel.MeasurementType.Name,
                DataPoints = ds.DataPoints.Select(dataPoint => new double[] { dataPoint.Time.Subtract(m_epoch).TotalMilliseconds, dataPoint.Value }).ToList(),
                ChartLabel = GetChartLabel(ds.SeriesInfo.Channel)

            });

            return dataLookup;
        }

        private Dictionary<string, FlotSeries> GetFrequencyDataLookup(VICycleDataGroup vICycleDataGroup, string type)
        {
            IEnumerable<string> names = vICycleDataGroup.CycleDataGroups.Where(ds => ds.RMS.SeriesInfo.Channel.MeasurementType.Name == type).Select(ds => ds.RMS.SeriesInfo.Channel.Phase.Name);
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();
            foreach (CycleDataGroup cdg in vICycleDataGroup.CycleDataGroups.Where(ds => ds.RMS.SeriesInfo.Channel.MeasurementType.Name == type))
            {

                FlotSeries flotSeriesRMS = new FlotSeries
                {
                    ChannelID = cdg.RMS.SeriesInfo.Channel.ID,
                    ChannelName = cdg.RMS.SeriesInfo.Channel.Name,
                    ChannelDescription = cdg.RMS.SeriesInfo.Channel.Description,
                    MeasurementCharacteristic = cdg.RMS.SeriesInfo.Channel.MeasurementCharacteristic.Name,
                    MeasurementType = cdg.RMS.SeriesInfo.Channel.MeasurementType.Name,
                    Phase = cdg.RMS.SeriesInfo.Channel.Phase.Name,
                    SeriesType = cdg.RMS.SeriesInfo.Channel.MeasurementType.Name,
                    DataPoints = cdg.RMS.DataPoints.Select(dataPoint => new double[] { dataPoint.Time.Subtract(m_epoch).TotalMilliseconds, dataPoint.Value }).ToList(),
                    ChartLabel = GetChartLabel(cdg.RMS.SeriesInfo.Channel, "RMS")
                };
                dataLookup.Add(flotSeriesRMS.ChartLabel, flotSeriesRMS);

                FlotSeries flotSeriesWaveAmp = new FlotSeries
                {
                    ChannelID = cdg.Peak.SeriesInfo.Channel.ID,
                    ChannelName = cdg.Peak.SeriesInfo.Channel.Name,
                    ChannelDescription = cdg.Peak.SeriesInfo.Channel.Description,
                    MeasurementCharacteristic = cdg.Peak.SeriesInfo.Channel.MeasurementCharacteristic.Name,
                    MeasurementType = cdg.Peak.SeriesInfo.Channel.MeasurementType.Name,
                    Phase = cdg.Peak.SeriesInfo.Channel.Phase.Name,
                    SeriesType = cdg.Peak.SeriesInfo.Channel.MeasurementType.Name,
                    DataPoints = cdg.Peak.DataPoints.Select(dataPoint => new double[] { dataPoint.Time.Subtract(m_epoch).TotalMilliseconds, dataPoint.Value }).ToList(),
                    ChartLabel = GetChartLabel(cdg.Peak.SeriesInfo.Channel, "Amplitude")
                };
                dataLookup.Add(flotSeriesWaveAmp.ChartLabel, flotSeriesWaveAmp);

                FlotSeries flotSeriesPolarAngle = new FlotSeries
                {
                    ChannelID = cdg.Phase.SeriesInfo.Channel.ID,
                    ChannelName = cdg.Phase.SeriesInfo.Channel.Name,
                    ChannelDescription = cdg.Phase.SeriesInfo.Channel.Description,
                    MeasurementCharacteristic = cdg.Phase.SeriesInfo.Channel.MeasurementCharacteristic.Name,
                    MeasurementType = cdg.Phase.SeriesInfo.Channel.MeasurementType.Name,
                    Phase = cdg.Phase.SeriesInfo.Channel.Phase.Name,
                    SeriesType = cdg.Phase.SeriesInfo.Channel.MeasurementType.Name,
                    DataPoints = cdg.Phase.Multiply(180.0D / Math.PI).DataPoints.Select(dataPoint => new double[] { dataPoint.Time.Subtract(m_epoch).TotalMilliseconds, dataPoint.Value }).ToList(),
                    ChartLabel = GetChartLabel(cdg.Phase.SeriesInfo.Channel, "Phase")
                };
                dataLookup.Add(flotSeriesPolarAngle.ChartLabel, flotSeriesPolarAngle);

            }

            return dataLookup;
        }

        private string GetChartLabel(openXDA.Model.Channel channel, string type = null)
        {

            if (channel.MeasurementType.Name == "Voltage" && type == null)
                return "V" + channel.Phase.Name;
            else if (channel.MeasurementType.Name == "Current" && type == null)
                return "I" + channel.Phase.Name;
            else if (channel.MeasurementType.Name == "Voltage")
                return "V" + channel.Phase.Name + " " + type;
            else if (channel.MeasurementType.Name == "Current")
                return "I" + channel.Phase.Name + " " + type;

            return null;
        }

        #endregion

        #region [ Digitals Data ]
        [HttpGet]
        public JsonReturn GetBreakerData()
        {
            Dictionary<string, string> query = Request.QueryParameters();

            using (AdoDataConnection connection = new AdoDataConnection("systemSettings")) {
                int eventId = int.Parse(query["eventId"]);
                Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                meter.ConnectionFactory = () => new AdoDataConnection(connection.Connection, typeof(SqlDataAdapter), false);

                DateTime epoch = new DateTime(1970, 1, 1);
                DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                int pixels = int.Parse(query["pixels"]);
                DataTable table;

                int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                table = connection.RetrieveData("select ID from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                foreach (DataRow row in table.Rows)
                {
                    Dictionary<string, FlotSeries> temp = QueryBreakerData(int.Parse(row["ID"].ToString()), meter, startTime);
                    foreach (string key in temp.Keys)
                    {
                        if (dict.ContainsKey(key))
                            dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                        else
                            dict.Add(key, temp[key]);
                    }
                }

                if (dict.Count == 0) return null;
                double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                List<FlotSeries> returnList = new List<FlotSeries>();
                foreach (string key in dict.Keys)
                {
                    FlotSeries series = new FlotSeries();
                    series = dict[key];
                    series.DataPoints = Downsample(dict[key].DataPoints.Where(x => !double.IsNaN(x[1])).OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                    returnList.Add(series);
                }
                JsonReturn returnDict = new JsonReturn();
                returnDict.StartDate = evt.StartTime;
                returnDict.EndDate = evt.EndTime;
                returnDict.Data = returnList;
                returnDict.CalculationTime = calcTime;

                return returnDict;


            }
        }

        private Dictionary<string, FlotSeries> QueryBreakerData(int eventID, Meter meter, DateTime startTime)
        {
            string target = $"Digitals-{eventID}-{startTime.Ticks}";
            DataGroup dataGroup = (DataGroup)s_memoryCache.Get(target);

            if (dataGroup == null)
            {

                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] timeDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    dataGroup = ToDataGroup(meter, timeDomainData);
                    s_memoryCache.Add(target, dataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }
            return GetBreakerLookup(dataGroup);

        }
        private Dictionary<string, FlotSeries> GetBreakerLookup(DataGroup dataGroup)
        {
            //Dictionary<string, FlotSeries> dataLookup = dataGroup.DataSeries.Where(ds => ds.SeriesInfo.Channel.MeasurementType.Name == "Digital" && (ds.SeriesInfo.Channel.MeasurementCharacteristic.Name == "BreakerStatus" || ds.SeriesInfo.Channel.MeasurementCharacteristic.Name == "TCE")).ToDictionary(ds => ds.SeriesInfo.Channel.Name, ds => new FlotSeries()
            Dictionary<string, FlotSeries> dataLookup = dataGroup.DataSeries.Where(ds => ds.SeriesInfo.Channel.MeasurementType.Name == "Digital").ToDictionary(ds => ds.SeriesInfo.Channel.Name, ds => new FlotSeries()
            {
                ChannelID = ds.SeriesInfo.Channel.ID,
                ChannelName = ds.SeriesInfo.Channel.Name,
                ChannelDescription = ds.SeriesInfo.Channel.Description,
                MeasurementCharacteristic = ds.SeriesInfo.Channel.MeasurementCharacteristic.Name,
                MeasurementType = ds.SeriesInfo.Channel.MeasurementType.Name,
                Phase = ds.SeriesInfo.Channel.Phase.Name,
                SeriesType = ds.SeriesInfo.Channel.MeasurementType.Name,
                DataPoints = ds.DataPoints.Select(dataPoint => new double[] { dataPoint.Time.Subtract(m_epoch).TotalMilliseconds, dataPoint.Value }).ToList(),
                ChartLabel = ds.SeriesInfo.Channel.Description

            });

            return dataLookup;
        }

        #endregion

        #region [ Fault Location Data ]
        [HttpGet]
        public JsonReturn GetFaultDistanceData()
        {
            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                Dictionary<string, string> query = Request.QueryParameters();

                int eventId = int.Parse(query["eventId"]);
                Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                meter.ConnectionFactory = () => new AdoDataConnection(connection.Connection, typeof(SqlDataAdapter), false);

                DateTime epoch = new DateTime(1970, 1, 1);
                DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                int pixels = int.Parse(query["pixels"]);
                DataTable table;

                int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                table = connection.RetrieveData("SELECT ID FROM FaultCurve WHERE EventID IN (SELECT ID FROM Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3})", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                foreach (DataRow row in table.Rows)
                {
                    KeyValuePair<string, FlotSeries> temp = QueryFaultDistanceData(int.Parse(row["ID"].ToString()), meter);
                    if (dict.ContainsKey(temp.Key))
                        dict[temp.Key].DataPoints = dict[temp.Key].DataPoints.Concat(temp.Value.DataPoints).ToList();
                    else
                        dict.Add(temp.Key, temp.Value);
                }

                if (dict.Count == 0) return null;
                double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                List<FlotSeries> returnList = new List<FlotSeries>();
                foreach (string key in dict.Keys)
                {
                    FlotSeries series = new FlotSeries();
                    series = dict[key];
                    series.DataPoints = Downsample(dict[key].DataPoints.Where(x => !double.IsNaN(x[1])).OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                    returnList.Add(series);
                }
                JsonReturn returnDict = new JsonReturn();
                returnDict.StartDate = evt.StartTime;
                returnDict.EndDate = evt.EndTime;
                returnDict.Data = returnList;
                returnDict.CalculationTime = calcTime;

                return returnDict;
            }


        }

        private KeyValuePair<string, FlotSeries> QueryFaultDistanceData(int faultCurveID, Meter meter)
        {
            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                FaultCurve faultCurve = new TableOperations<FaultCurve>(connection).QueryRecordWhere("ID = {0}", faultCurveID);
                DataGroup dataGroup = ToDataGroup(meter, faultCurve.Data);
                FlotSeries flotSeries = new FlotSeries()
                {
                    ChannelID = 0,
                    ChannelName = faultCurve.Algorithm,
                    ChannelDescription = faultCurve.Algorithm,
                    MeasurementCharacteristic = "FaultCurve",
                    MeasurementType = "FaultCurve",
                    Phase = "None",
                    SeriesType = "None",
                    DataPoints = dataGroup.DataSeries[0].DataPoints.Select(dataPoint => new double[] { dataPoint.Time.Subtract(m_epoch).TotalMilliseconds, dataPoint.Value }).ToList(),
                    ChartLabel = faultCurve.Algorithm

                };
                return new KeyValuePair<string, FlotSeries>(faultCurve.Algorithm, flotSeries);

            }

        }

        #endregion

        #region [ Shared Functions ]
        private DataGroup ToDataGroup(Meter meter, byte[] data)
        {
            DataGroup dataGroup = new DataGroup();
            dataGroup.FromData(meter, data);
            return dataGroup;
        }

        private List<double[]> Downsample(List<double[]> series, int maxSampleCount, Range<DateTime> range)
        {
            List<double[]> data = new List<double[]>();
            DateTime epoch = new DateTime(1970, 1, 1);
            double startTime = range.Start.Subtract(epoch).TotalMilliseconds;
            double endTime = range.End.Subtract(epoch).TotalMilliseconds;
            int step = (int)(endTime * 1000 - startTime * 1000) / maxSampleCount;
            if (step < 1)
                step = 1;

            series = series.Where(x => x[0] >= startTime && x[0] <= endTime).ToList();

            int index = 0;

            for (double n = startTime * 1000; n <= endTime * 1000; n += 2 * step)
            {
                double[] min = null;
                double[] max = null;

                while (index < series.Count && series[index][0] * 1000 < n + 2 * step)
                {
                    if (min == null || min[1] > series[index][1])
                        min = series[index];

                    if (max == null || max[1] <= series[index][1])
                        max = series[index];

                    ++index;
                }

                if (min != null)
                {
                    if (min[0] < max[0])
                    {
                        data.Add(min);
                        data.Add(max);
                    }
                    else if (min[0] > max[0])
                    {
                        data.Add(max);
                        data.Add(min);
                    }
                    else
                    {
                        data.Add(min);
                    }
                }
            }

            return data;

        }

        private IDbDataParameter ToDateTime2(AdoDataConnection connection, DateTime dateTime)
        {
            using (IDbCommand command = connection.Connection.CreateCommand())
            {
                IDbDataParameter parameter = command.CreateParameter();
                parameter.DbType = DbType.DateTime2;
                parameter.Value = dateTime;
                return parameter;
            }
        }

        #endregion

        #region [ Info ]
        [HttpGet]
        public Dictionary<string, dynamic> GetHeaderData()
        {
            Dictionary<string, string> query = Request.QueryParameters();
            int eventId = int.Parse(query["eventId"]);
            string breakerOperationID = (query.ContainsKey("breakeroperation") ? query["breakeroperation"] : "-1");

            Func<string, string> func = inputString => {
                switch (inputString)
                {
                    case "System":
                        return "GetPreviousAndNextEventIdsForSystem";
                    case "Station":
                        return "GetPreviousAndNextEventIdsForMeterLocation";
                    case "Meter":
                        return "GetPreviousAndNextEventIdsForMeter";
                    default:
                        return "GetPreviousAndNextEventIdsForLine";
                }

            };

            Dictionary<string, Tuple<EventView, EventView>> nextBackLookup = new Dictionary<string, Tuple<EventView, EventView>>()
            {
                { "System", Tuple.Create((EventView)null, (EventView)null) },
                { "Station", Tuple.Create((EventView)null, (EventView)null) },
                { "Meter", Tuple.Create((EventView)null, (EventView)null) },
                { "Line", Tuple.Create((EventView)null, (EventView)null) }
            };

            Dictionary<string, dynamic> returnDict = new Dictionary<string, dynamic>();


            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                EventView theEvent = new TableOperations<EventView>(connection).QueryRecordWhere("ID = {0}", eventId);

                returnDict.Add("postedSystemFrequency", connection.ExecuteScalar<string>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? "60.0");
                returnDict.Add("postedStationName", theEvent.StationName);
                returnDict.Add("postedMeterId", theEvent.MeterID.ToString());
                returnDict.Add("postedMeterName", theEvent.MeterName);
                returnDict.Add("postedLineName", theEvent.LineName);
                returnDict.Add("postedLineLength", theEvent.Length.ToString());

                returnDict.Add("postedEventName", theEvent.EventTypeName);
                returnDict.Add("postedEventDate", theEvent.StartTime.ToString("yyyy-MM-dd HH:mm:ss.fffffff"));
                returnDict.Add("postedDate", theEvent.StartTime.ToShortDateString());
                returnDict.Add("postedEventMilliseconds", theEvent.StartTime.Subtract(new DateTime(1970, 1, 1)).TotalMilliseconds.ToString());

                returnDict.Add("xdaInstance", connection.ExecuteScalar<string>("SELECT Value FROM DashSettings WHERE Name = 'System.XDAInstance'"));

                using (IDbCommand cmd = connection.Connection.CreateCommand())
                {
                    cmd.CommandType = CommandType.StoredProcedure;
                    cmd.Parameters.Add(new SqlParameter("@EventID", eventId));
                    cmd.CommandTimeout = 300;

                    foreach (string procedure in nextBackLookup.Keys.ToList())
                    {
                        EventView back = null;
                        EventView next = null;
                        int backID = -1;
                        int nextID = -1;

                        cmd.CommandText = func(procedure);

                        using (IDataReader rdr = cmd.ExecuteReader())
                        {
                            rdr.Read();

                            if (!rdr.IsDBNull(0))
                            {
                                backID = rdr.GetInt32(0);
                            }

                            if (!rdr.IsDBNull(1))
                            {
                                nextID = rdr.GetInt32(1);
                            }
                        }

                        back = new TableOperations<EventView>(connection).QueryRecordWhere("ID = {0}", backID);
                        next = new TableOperations<EventView>(connection).QueryRecordWhere("ID = {0}", nextID);
                        nextBackLookup[procedure] = Tuple.Create(back, next);
                    }
                }

                returnDict.Add("nextBackLookup", nextBackLookup);

                if (new List<string>() { "Fault", "RecloseIntoFault" }.Contains(returnDict["postedEventName"]))
                {
                    const string SagDepthQuery =
                        "SELECT TOP 1 " +
                        "    (1 - PerUnitMagnitude) * 100 " +
                        "FROM " +
                        "    FaultSummary JOIN " +
                        "    Disturbance ON " +
                        "         Disturbance.EventID = FaultSummary.EventID AND " +
                        "         Disturbance.StartTime <= dbo.AdjustDateTime2(FaultSummary.Inception, FaultSummary.DurationSeconds) AND " +
                        "         Disturbance.EndTime >= FaultSummary.Inception JOIN " +
                        "    EventType ON " +
                        "        Disturbance.EventTypeID = EventType.ID AND " +
                        "        EventType.Name = 'Sag' JOIN " +
                        "    Phase ON " +
                        "        Disturbance.PhaseID = Phase.ID AND " +
                        "        Phase.Name = 'Worst' " +
                        "WHERE FaultSummary.ID = {0} " +
                        "ORDER BY PerUnitMagnitude";

                    FaultSummary thesummary = new TableOperations<FaultSummary>(connection).QueryRecordsWhere("EventID = {0} AND IsSelectedAlgorithm = 1", theEvent.ID).OrderBy(row => row.IsSuppressed).ThenBy(row => row.Inception).FirstOrDefault();
                    double sagDepth = connection.ExecuteScalar<double>(SagDepthQuery, thesummary.ID);

                    if ((object)thesummary != null)
                    {
                        returnDict.Add("postedStartTime", thesummary.Inception.TimeOfDay.ToString());
                        returnDict.Add("postedPhase", thesummary.FaultType);
                        returnDict.Add("postedDurationPeriod", thesummary.DurationCycles.ToString("##.##", CultureInfo.InvariantCulture) + " cycles");
                        returnDict.Add("postedMagnitude", thesummary.CurrentMagnitude.ToString("####.#", CultureInfo.InvariantCulture) + " Amps (RMS)");
                        returnDict.Add("postedSagDepth", sagDepth.ToString("####.#", CultureInfo.InvariantCulture) + "%");
                        returnDict.Add("postedCalculationCycle", thesummary.CalculationCycle.ToString());
                    }
                }
                else if (new List<string>() { "Sag", "Swell" }.Contains(returnDict["postedEventName"]))
                {
                    openXDA.Model.Disturbance disturbance = new TableOperations<openXDA.Model.Disturbance>(connection).QueryRecordsWhere("EventID = {0}", theEvent.ID).Where(row => row.EventTypeID == theEvent.EventTypeID).OrderBy(row => row.StartTime).FirstOrDefault();

                    if ((object)disturbance != null)
                    {
                        returnDict.Add("postedStartTime", disturbance.StartTime.TimeOfDay.ToString());
                        returnDict.Add("postedPhase", new TableOperations<Phase>(connection).QueryRecordWhere("ID = {0}", disturbance.PhaseID).Name);
                        returnDict.Add("postedDurationPeriod", disturbance.DurationCycles.ToString("##.##", CultureInfo.InvariantCulture) + " cycles");

                        if (disturbance.PerUnitMagnitude != -1.0e308)
                        {
                            returnDict.Add("postedMagnitude", disturbance.PerUnitMagnitude.ToString("N3", CultureInfo.InvariantCulture) + " pu (RMS)");
                        }
                    }
                }

                if (breakerOperationID != "")
                {
                    int id;

                    if (int.TryParse(breakerOperationID, out id))
                    {
                        BreakerOperation breakerRow = new TableOperations<BreakerOperation>(connection).QueryRecordWhere("ID = {0}", id);

                        if ((object)breakerRow != null)
                        {
                            returnDict.Add("postedBreakerNumber", breakerRow.BreakerNumber);
                            returnDict.Add("postedBreakerPhase", new TableOperations<Phase>(connection).QueryRecordWhere("ID = {0}", breakerRow.PhaseID).Name);
                            returnDict.Add("postedBreakerTiming", breakerRow.BreakerTiming.ToString());
                            returnDict.Add("postedBreakerSpeed", breakerRow.BreakerSpeed.ToString());
                            returnDict.Add("postedBreakerOperation", connection.ExecuteScalar("SELECT Name FROM BreakerOperationType WHERE ID = {0}", breakerRow.BreakerOperationTypeID).ToString());
                        }
                    }
                }


                return returnDict;

            }

        }


        #endregion

        #region [ Compare ]
        [HttpGet]
        public DataTable GetOverlappingEvents()
        {
            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                Dictionary<string, string> query = Request.QueryParameters();
                int eventId = int.Parse(query["eventId"]);

                Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);


                DataTable dataTable = connection.RetrieveData(@"
                SELECT
	                DISTINCT
	                Meter.Name as MeterName,
	                MeterLine.LineName,
	                Event.ID as EventID
                FROM
	                Event JOIN
	                Meter ON Meter.ID = Event.MeterID JOIN
	                Line ON Line.ID = Event.LineID JOIN
	                MeterLine ON MeterLine.MeterID = Meter.ID AND MeterLine.LineID = Line.ID
                WHERE
	                Event.ID != {0} AND ( 
	                Event.StartTime BETWEEN {1} AND {2} OR
	                Event.EndTime BETWEEN {1} AND {2} )
                ", eventId, ToDateTime2(connection, startTime), ToDateTime2(connection, endTime));
                return dataTable;

            }
        }


        #endregion

        #region [ Analysis ]

        #region [ First Derivative ]
        [HttpGet]
        public Task<JsonReturn> GetFirstDerivativeData(CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    Dictionary<string, string> query = Request.QueryParameters();
                    int eventId = int.Parse(query["eventId"]);
                    Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                    Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                    meter.ConnectionFactory = () => new AdoDataConnection("systemSettings");
                    int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                    double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;


                    DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                    DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                    int pixels = int.Parse(query["pixels"]);
                    DataTable table;

                    Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                    table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                    foreach (DataRow row in table.Rows)
                    {
                        int eventID = row.ConvertField<int>("ID");
                        DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                        Dictionary<string, FlotSeries> temp = QueryFirstDerivativeData(eventID, eventStartTime, meter);

                        foreach (string key in temp.Keys)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                    if (dict.Count == 0) return null;

                    double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                    List<FlotSeries> returnList = new List<FlotSeries>();
                    foreach (string key in dict.Keys)
                    {
                        FlotSeries series = new FlotSeries();
                        series = dict[key];
                        series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                        returnList.Add(series);
                    }
                    JsonReturn returnDict = new JsonReturn();
                    returnDict.StartDate = evt.StartTime;
                    returnDict.EndDate = evt.EndTime;
                    returnDict.Data = returnList;
                    returnDict.CalculationTime = calcTime;
                    returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                    return returnDict;
                }

            }, cancellationToken);
        }

        private Dictionary<string, FlotSeries> QueryFirstDerivativeData(int eventID, DateTime startTime, Meter meter)
        {
            string target = $"DataGroup-{eventID}-{startTime.Ticks}";
            DataGroup dataGroup = (DataGroup)s_memoryCache.Get(target);

            if (dataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    dataGroup = ToDataGroup(meter, frequencyDomainData);
                    s_memoryCache.Add(target, dataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetFirstDerivativeLookup(dataGroup);
        }

        private Dictionary<string, FlotSeries> GetFirstDerivativeLookup(DataGroup dataGroup)
        {
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();

            DataSeries vAN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "AN");
            DataSeries iAN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "AN");
            DataSeries vBN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "BN");
            DataSeries iBN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "BN");
            DataSeries vCN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "CN");
            DataSeries iCN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "CN");

            if (vAN != null) {
                double lastX = 0;
                double lastY = 0;

                dataLookup.Add("First Derivative VAN", new FlotSeries() { ChartLabel = "VAN First Derivative", DataPoints = vAN.DataPoints.Select((point, index) => {
                    double x = point.Time.Subtract(m_epoch).TotalMilliseconds;
                    double y = point.Value;

                    if (index == 0) {
                        lastX = x;
                        lastY = y;
                    }

                    return new double[] { x, (y - lastY)/(x - lastX) };
                }).ToList() });
            }

            if (iAN != null)
            {
                double lastX = 0;
                double lastY = 0;

                dataLookup.Add("First Derivative IAN", new FlotSeries()
                {
                    ChartLabel = "IAN First Derivative",
                    DataPoints = iAN.DataPoints.Select((point, index) => {
                        double x = point.Time.Subtract(m_epoch).TotalMilliseconds;
                        double y = point.Value;

                        if (index == 0)
                        {
                            lastX = x;
                            lastY = y;
                        }

                        return new double[] { x, (y - lastY) / (x - lastX) };
                    }).ToList()
                });
            }

            if (vBN != null)
            {
                double lastX = 0;
                double lastY = 0;

                dataLookup.Add("First Derivative VBN", new FlotSeries()
                {
                    ChartLabel = "VBN First Derivative",
                    DataPoints = vBN.DataPoints.Select((point, index) => {
                        double x = point.Time.Subtract(m_epoch).TotalMilliseconds;
                        double y = point.Value;

                        if (index == 0)
                        {
                            lastX = x;
                            lastY = y;
                        }

                        return new double[] { x, (y - lastY) / (x - lastX) };
                    }).ToList()
                });
            }

            if (iBN != null)
            {
                double lastX = 0;
                double lastY = 0;

                dataLookup.Add("First Derivative IBN", new FlotSeries()
                {
                    ChartLabel = "IBN First Derivative",
                    DataPoints = iBN.DataPoints.Select((point, index) => {
                        double x = point.Time.Subtract(m_epoch).TotalMilliseconds;
                        double y = point.Value;

                        if (index == 0)
                        {
                            lastX = x;
                            lastY = y;
                        }

                        return new double[] { x, (y - lastY) / (x - lastX) };
                    }).ToList()
                });
            }

            if (vCN != null)
            {
                double lastX = 0;
                double lastY = 0;

                dataLookup.Add("First Derivative VCN", new FlotSeries()
                {
                    ChartLabel = "VCN First Derivative",
                    DataPoints = vCN.DataPoints.Select((point, index) => {
                        double x = point.Time.Subtract(m_epoch).TotalMilliseconds;
                        double y = point.Value;

                        if (index == 0)
                        {
                            lastX = x;
                            lastY = y;
                        }

                        return new double[] { x, (y - lastY) / (x - lastX) };
                    }).ToList()
                });
            }

            if (iCN != null)
            {
                double lastX = 0;
                double lastY = 0;

                dataLookup.Add("First Derivative ICN", new FlotSeries()
                {
                    ChartLabel = "ICN First Derivative",
                    DataPoints = iCN.DataPoints.Select((point, index) => {
                        double x = point.Time.Subtract(m_epoch).TotalMilliseconds;
                        double y = point.Value;

                        if (index == 0)
                        {
                            lastX = x;
                            lastY = y;
                        }

                        return new double[] { x, (y - lastY) / (x - lastX) };
                    }).ToList()
                });
            }



            return dataLookup;
        }
        #endregion

        #region [ Impedance ]
        [HttpGet]
        public Task<JsonReturn> GetImpedanceData(CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    Dictionary<string, string> query = Request.QueryParameters();
                    int eventId = int.Parse(query["eventId"]);
                    Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                    Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                    meter.ConnectionFactory = () => new AdoDataConnection("systemSettings");
                    int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                    double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;


                    DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                    DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                    int pixels = int.Parse(query["pixels"]);
                    DataTable table;

                    Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                    table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                    foreach (DataRow row in table.Rows)
                    {
                        int eventID = row.ConvertField<int>("ID");
                        DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                        Dictionary<string, FlotSeries> temp = QueryImpedanceData(eventID, eventStartTime, meter);

                        foreach (string key in temp.Keys)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                    if (dict.Count == 0) return null;

                    double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                    List<FlotSeries> returnList = new List<FlotSeries>();
                    foreach (string key in dict.Keys)
                    {
                        FlotSeries series = new FlotSeries();
                        series = dict[key];
                        series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                        returnList.Add(series);
                    }
                    JsonReturn returnDict = new JsonReturn();
                    returnDict.StartDate = evt.StartTime;
                    returnDict.EndDate = evt.EndTime;
                    returnDict.Data = returnList;
                    returnDict.CalculationTime = calcTime;
                    returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                    return returnDict;


                }

            }, cancellationToken);
        }

        private Dictionary<string, FlotSeries> QueryImpedanceData(int eventID, DateTime startTime, Meter meter)
        {
            string target = $"VICycleDataGroup-{eventID}-{startTime.Ticks}";
            VICycleDataGroup vICycleDataGroup = (VICycleDataGroup)s_memoryCache.Get(target);

            if (vICycleDataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    DataGroup dataGroup = ToDataGroup(meter, frequencyDomainData);
                    double freq = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0D;
                    vICycleDataGroup = Transform.ToVICycleDataGroup(new VIDataGroup(dataGroup), freq);
                    s_memoryCache.Add(target, vICycleDataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetImpedanceLookup(vICycleDataGroup);
        }

        private Dictionary<string, FlotSeries> GetImpedanceLookup(VICycleDataGroup vICycleDataGroup)
        {
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();

            if (vICycleDataGroup.IA != null && vICycleDataGroup.VA != null) {
                List<DataPoint> voltagePointsMag = vICycleDataGroup.VA.RMS.DataPoints;
                List<DataPoint> voltagePointsAng = vICycleDataGroup.VA.Phase.DataPoints;
                List<Complex> voltagePoints = voltagePointsMag.Select((vMagPoint, index) => Complex.FromPolarCoordinates(vMagPoint.Value, voltagePointsAng[index].Value)).ToList();

                List<DataPoint> currentPointsMag = vICycleDataGroup.IA.RMS.DataPoints;
                List<DataPoint> currentPointsAng = vICycleDataGroup.IA.Phase.DataPoints;
                List<Complex> currentPoints = currentPointsMag.Select((iMagPoint, index) => Complex.FromPolarCoordinates(iMagPoint.Value, currentPointsAng[index].Value)).ToList();

                IEnumerable<Complex> impedancePoints = voltagePoints.Select((vPoint, index) => currentPoints[index] / vPoint);
                dataLookup.Add("Reactance AN", new FlotSeries() { ChartLabel = "AN Reactance", DataPoints = impedancePoints.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Imaginary}).ToList() });
                dataLookup.Add("Resistance AN", new FlotSeries() { ChartLabel = "AN Resistance", DataPoints = impedancePoints.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Real }).ToList() });
                dataLookup.Add("Impedance AN", new FlotSeries() { ChartLabel = "AN Impedance", DataPoints = impedancePoints.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude }).ToList() });

            }

            if (vICycleDataGroup.IB != null && vICycleDataGroup.VB != null)
            {
                List<DataPoint> voltagePointsMag = vICycleDataGroup.VB.RMS.DataPoints;
                List<DataPoint> voltagePointsAng = vICycleDataGroup.VB.Phase.DataPoints;
                List<Complex> voltagePoints = voltagePointsMag.Select((vMagPoint, index) => Complex.FromPolarCoordinates(vMagPoint.Value, voltagePointsAng[index].Value)).ToList();

                List<DataPoint> currentPointsMag = vICycleDataGroup.IB.RMS.DataPoints;
                List<DataPoint> currentPointsAng = vICycleDataGroup.IB.Phase.DataPoints;
                List<Complex> currentPoints = currentPointsMag.Select((iMagPoint, index) => Complex.FromPolarCoordinates(iMagPoint.Value, currentPointsAng[index].Value)).ToList();

                IEnumerable<Complex> impedancePoints = voltagePoints.Select((vPoint, index) => currentPoints[index] / vPoint);
                dataLookup.Add("Reactance BN", new FlotSeries() { ChartLabel = "BN Reactance", DataPoints = impedancePoints.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Imaginary }).ToList() });
                dataLookup.Add("Resistance BN", new FlotSeries() { ChartLabel = "BN Resistance", DataPoints = impedancePoints.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Real }).ToList() });
                dataLookup.Add("Impedance BN", new FlotSeries() { ChartLabel = "BN Impedance", DataPoints = impedancePoints.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude }).ToList() });

            }

            if (vICycleDataGroup.IC != null && vICycleDataGroup.VC != null)
            {
                List<DataPoint> voltagePointsMag = vICycleDataGroup.VC.RMS.DataPoints;
                List<DataPoint> voltagePointsAng = vICycleDataGroup.VC.Phase.DataPoints;
                List<Complex> voltagePoints = voltagePointsMag.Select((vMagPoint, index) => Complex.FromPolarCoordinates(vMagPoint.Value, voltagePointsAng[index].Value)).ToList();

                List<DataPoint> currentPointsMag = vICycleDataGroup.IC.RMS.DataPoints;
                List<DataPoint> currentPointsAng = vICycleDataGroup.IC.Phase.DataPoints;
                List<Complex> currentPoints = currentPointsMag.Select((iMagPoint, index) => Complex.FromPolarCoordinates(iMagPoint.Value, currentPointsAng[index].Value)).ToList();

                IEnumerable<Complex> impedancePoints = voltagePoints.Select((vPoint, index) => currentPoints[index] / vPoint);
                dataLookup.Add("Reactance CN", new FlotSeries() { ChartLabel = "CN Reactance", DataPoints = impedancePoints.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Imaginary }).ToList() });
                dataLookup.Add("Resistance CN", new FlotSeries() { ChartLabel = "CN Resistance", DataPoints = impedancePoints.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Real }).ToList() });
                dataLookup.Add("Impedance CN", new FlotSeries() { ChartLabel = "CN Impedance", DataPoints = impedancePoints.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude }).ToList() });
            }

            return dataLookup;
        }
        #endregion

        #region [ Remove Current ]
        [HttpGet]
        public Task<JsonReturn> GetRemoveCurrentData(CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    Dictionary<string, string> query = Request.QueryParameters();
                    int eventId = int.Parse(query["eventId"]);
                    Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                    Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                    meter.ConnectionFactory = () => new AdoDataConnection("systemSettings");
                    int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                    double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;


                    DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                    DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                    int pixels = int.Parse(query["pixels"]);
                    DataTable table;

                    Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                    table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                    foreach (DataRow row in table.Rows)
                    {
                        int eventID = row.ConvertField<int>("ID");
                        DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                        Dictionary<string, FlotSeries> temp = QueryRemoveCurrentData(eventID, eventStartTime, meter);

                        foreach (string key in temp.Keys)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                    if (dict.Count == 0) return null;

                    double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                    List<FlotSeries> returnList = new List<FlotSeries>();
                    foreach (string key in dict.Keys)
                    {
                        FlotSeries series = new FlotSeries();
                        series = dict[key];
                        series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                        returnList.Add(series);
                    }
                    JsonReturn returnDict = new JsonReturn();
                    returnDict.StartDate = evt.StartTime;
                    returnDict.EndDate = evt.EndTime;
                    returnDict.Data = returnList;
                    returnDict.CalculationTime = calcTime;
                    returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                    return returnDict;


                }

            }, cancellationToken);

        }

        private Dictionary<string, FlotSeries> QueryRemoveCurrentData(int eventID, DateTime startTime, Meter meter)
        {
            string target = $"DataGroup-{eventID}-{startTime.Ticks}";
            DataGroup dataGroup = (DataGroup)s_memoryCache.Get(target);

            if (dataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    dataGroup = ToDataGroup(meter, frequencyDomainData);
                    s_memoryCache.Add(target, dataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetRemoveCurrentLookup(dataGroup);
        }

        private Dictionary<string, FlotSeries> GetRemoveCurrentLookup(DataGroup dataGroup)
        {
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();
            double systemFrequency;
            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;
            }
            DataSeries iAN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "AN");
            DataSeries iBN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "BN");
            DataSeries iCN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "CN");



            if (iAN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(iAN.SampleRate, systemFrequency);

                List<DataPoint> firstCycle = iAN.DataPoints.Take(samplesPerCycle).ToList();
                List<DataPoint> lastCycle = iAN.DataPoints.OrderByDescending(x => x.Time).Take(samplesPerCycle).ToList();

                List<DataPoint> fullWaveFormPre = iAN.DataPoints.Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - firstCycle[index % samplesPerCycle].Value }).ToList();
                List<DataPoint> fullWaveFormPost = iAN.DataPoints.OrderByDescending(x => x.Time).Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - lastCycle[index % samplesPerCycle].Value }).OrderBy(x => x.Time).ToList();

                dataLookup.Add("Pre Fault IAN", new FlotSeries() { ChartLabel = "IAN Pre Fault", DataPoints = fullWaveFormPre.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });
                dataLookup.Add("Post Fault IAN", new FlotSeries() { ChartLabel = "IAN Post Fault", DataPoints = fullWaveFormPost.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });

            }


            if (iBN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(iBN.SampleRate, systemFrequency);

                List<DataPoint> firstCycle = iBN.DataPoints.Take(samplesPerCycle).ToList();
                List<DataPoint> lastCycle = iBN.DataPoints.OrderByDescending(x => x.Time).Take(samplesPerCycle).ToList();

                List<DataPoint> fullWaveFormPre = iBN.DataPoints.Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - firstCycle[index % samplesPerCycle].Value }).ToList();
                List<DataPoint> fullWaveFormPost = iBN.DataPoints.OrderByDescending(x => x.Time).Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - lastCycle[index % samplesPerCycle].Value }).OrderBy(x => x.Time).ToList();

                dataLookup.Add("Pre Fault IBN", new FlotSeries() { ChartLabel = "IBN Pre Fault", DataPoints = fullWaveFormPre.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });
                dataLookup.Add("Post Fault IBN", new FlotSeries() { ChartLabel = "IBN Post Fault", DataPoints = fullWaveFormPost.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });
            }

            if (iCN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(iCN.SampleRate, systemFrequency);

                List<DataPoint> firstCycle = iCN.DataPoints.Take(samplesPerCycle).ToList();
                List<DataPoint> lastCycle = iCN.DataPoints.OrderByDescending(x => x.Time).Take(samplesPerCycle).ToList();

                List<DataPoint> fullWaveFormPre = iCN.DataPoints.Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - firstCycle[index % samplesPerCycle].Value }).ToList();
                List<DataPoint> fullWaveFormPost = iCN.DataPoints.OrderByDescending(x => x.Time).Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - lastCycle[index % samplesPerCycle].Value }).OrderBy(x => x.Time).ToList();

                dataLookup.Add("Pre Fault ICN", new FlotSeries() { ChartLabel = "ICN Pre Fault", DataPoints = fullWaveFormPre.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });
                dataLookup.Add("Post Fault ICN", new FlotSeries() { ChartLabel = "ICN Post Fault", DataPoints = fullWaveFormPost.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });
            }



            return dataLookup;
        }
        #endregion

        #region [ Power ]
        [HttpGet]
        public Task<JsonReturn> GetPowerData(CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    Dictionary<string, string> query = Request.QueryParameters();
                    int eventId = int.Parse(query["eventId"]);
                    Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                    Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                    meter.ConnectionFactory = () => new AdoDataConnection("systemSettings");
                    int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                    double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;


                    DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                    DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                    int pixels = int.Parse(query["pixels"]);
                    DataTable table;

                    Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                    table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                    foreach (DataRow row in table.Rows)
                    {
                        int eventID = row.ConvertField<int>("ID");
                        DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                        Dictionary<string, FlotSeries> temp = QueryPowerData(eventID, eventStartTime, meter);

                        foreach (string key in temp.Keys)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                    if (dict.Count == 0) return null;

                    double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                    List<FlotSeries> returnList = new List<FlotSeries>();
                    foreach (string key in dict.Keys)
                    {
                        FlotSeries series = new FlotSeries();
                        series = dict[key];
                        series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                        returnList.Add(series);
                    }
                    JsonReturn returnDict = new JsonReturn();
                    returnDict.StartDate = evt.StartTime;
                    returnDict.EndDate = evt.EndTime;
                    returnDict.Data = returnList;
                    returnDict.CalculationTime = calcTime;
                    returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                    return returnDict;


                }

            }, cancellationToken);
        }

        private Dictionary<string, FlotSeries> QueryPowerData(int eventID, DateTime startTime, Meter meter)
        {
            string target = $"VICycleDataGroup-{eventID}-{startTime.Ticks}";
            VICycleDataGroup vICycleDataGroup = (VICycleDataGroup)s_memoryCache.Get(target);

            if (vICycleDataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    DataGroup dataGroup = ToDataGroup(meter, frequencyDomainData);
                    double freq = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0D;
                    vICycleDataGroup = Transform.ToVICycleDataGroup(new VIDataGroup(dataGroup), freq);
                    s_memoryCache.Add(target, vICycleDataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetPowerLookup(vICycleDataGroup);
        }

        private Dictionary<string, FlotSeries> GetPowerLookup(VICycleDataGroup vICycleDataGroup)
        {
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();
            List<Complex> powerPointsAN = null;
            List<Complex> powerPointsBN = null;
            List<Complex> powerPointsCN = null;

            if (vICycleDataGroup.IA != null && vICycleDataGroup.VA != null)
            {
                List<DataPoint> voltagePointsMag = vICycleDataGroup.VA.RMS.DataPoints;
                List<DataPoint> voltagePointsAng = vICycleDataGroup.VA.Phase.DataPoints;
                List<Complex> voltagePoints = voltagePointsMag.Select((vMagPoint, index) => Complex.FromPolarCoordinates(vMagPoint.Value, voltagePointsAng[index].Value)).ToList();

                List<DataPoint> currentPointsMag = vICycleDataGroup.IA.RMS.DataPoints;
                List<DataPoint> currentPointsAng = vICycleDataGroup.IA.Phase.DataPoints;
                List<Complex> currentPoints = currentPointsMag.Select((iMagPoint, index) => Complex.Conjugate(Complex.FromPolarCoordinates(iMagPoint.Value, currentPointsAng[index].Value))).ToList();

                powerPointsAN = voltagePoints.Select((vPoint, index) => currentPoints[index] * vPoint).ToList();
                dataLookup.Add("Reactive Power AN", new FlotSeries() { ChartLabel = "AN Reactive Power", DataPoints = powerPointsAN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Imaginary }).ToList() });
                dataLookup.Add("Active Power AN", new FlotSeries() { ChartLabel = "AN Active Power", DataPoints = powerPointsAN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Real }).ToList() });
                dataLookup.Add("Apparent Power AN", new FlotSeries() { ChartLabel = "AN Apparent Power", DataPoints = powerPointsAN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude }).ToList() });
                dataLookup.Add("Power Factor AN", new FlotSeries() { ChartLabel = "AN Power Factor", DataPoints = powerPointsAN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude/iPoint.Real }).ToList() });

            }

            if (vICycleDataGroup.IB != null && vICycleDataGroup.VB != null)
            {
                List<DataPoint> voltagePointsMag = vICycleDataGroup.VB.RMS.DataPoints;
                List<DataPoint> voltagePointsAng = vICycleDataGroup.VB.Phase.DataPoints;
                List<Complex> voltagePoints = voltagePointsMag.Select((vMagPoint, index) => Complex.FromPolarCoordinates(vMagPoint.Value, voltagePointsAng[index].Value)).ToList();

                List<DataPoint> currentPointsMag = vICycleDataGroup.IB.RMS.DataPoints;
                List<DataPoint> currentPointsAng = vICycleDataGroup.IB.Phase.DataPoints;
                List<Complex> currentPoints = currentPointsMag.Select((iMagPoint, index) => Complex.Conjugate(Complex.FromPolarCoordinates(iMagPoint.Value, currentPointsAng[index].Value))).ToList();

                powerPointsBN = voltagePoints.Select((vPoint, index) => currentPoints[index] * vPoint).ToList();
                dataLookup.Add("Reactive Power BN", new FlotSeries() { ChartLabel = "BN Reactive Power", DataPoints = powerPointsBN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Imaginary }).ToList() });
                dataLookup.Add("Active Power BN", new FlotSeries() { ChartLabel = "BN Active Power", DataPoints = powerPointsBN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Real }).ToList() });
                dataLookup.Add("Apparent Power BN", new FlotSeries() { ChartLabel = "BN Apparent Power", DataPoints = powerPointsBN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude }).ToList() });
                dataLookup.Add("Power Factor BN", new FlotSeries() { ChartLabel = "BN Power Factor", DataPoints = powerPointsBN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude / iPoint.Real }).ToList() });

            }

            if (vICycleDataGroup.IC != null && vICycleDataGroup.VC != null)
            {
                List<DataPoint> voltagePointsMag = vICycleDataGroup.VC.RMS.DataPoints;
                List<DataPoint> voltagePointsAng = vICycleDataGroup.VC.Phase.DataPoints;
                List<Complex> voltagePoints = voltagePointsMag.Select((vMagPoint, index) => Complex.FromPolarCoordinates(vMagPoint.Value, voltagePointsAng[index].Value)).ToList();

                List<DataPoint> currentPointsMag = vICycleDataGroup.IC.RMS.DataPoints;
                List<DataPoint> currentPointsAng = vICycleDataGroup.IC.Phase.DataPoints;
                List<Complex> currentPoints = currentPointsMag.Select((iMagPoint, index) => Complex.Conjugate(Complex.FromPolarCoordinates(iMagPoint.Value, currentPointsAng[index].Value))).ToList();

                powerPointsCN = voltagePoints.Select((vPoint, index) => currentPoints[index] * vPoint).ToList();
                dataLookup.Add("Reactive Power CN", new FlotSeries() { ChartLabel = "CN Reactive Power", DataPoints = powerPointsCN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Imaginary }).ToList() });
                dataLookup.Add("Active Power CN", new FlotSeries() { ChartLabel = "CN Active Power", DataPoints = powerPointsCN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Real }).ToList() });
                dataLookup.Add("Apparent Power CN", new FlotSeries() { ChartLabel = "CN Apparent Power", DataPoints = powerPointsCN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude }).ToList() });
                dataLookup.Add("Power Factor CN", new FlotSeries() { ChartLabel = "CN Power Factor", DataPoints = powerPointsCN.Select((iPoint, index) => new double[] { voltagePointsMag[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude / iPoint.Real }).ToList() });

            }

            if (powerPointsAN != null && powerPointsAN.Any() && powerPointsBN != null && powerPointsBN.Any() && powerPointsCN != null && powerPointsCN.Any())
            {
                IEnumerable<Complex> powerPoints = powerPointsAN.Select((pPoint, index) => pPoint + powerPointsBN[index] + powerPointsCN[index]).ToList();
                dataLookup.Add("Reactive Power Total", new FlotSeries() { ChartLabel = "Total Reactive Power", DataPoints = powerPoints.Select((iPoint, index) => new double[] { vICycleDataGroup.VC.RMS.DataPoints[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Imaginary }).ToList() });
                dataLookup.Add("Active Power Total", new FlotSeries() { ChartLabel = "Total Active Power", DataPoints = powerPoints.Select((iPoint, index) => new double[] { vICycleDataGroup.VC.RMS.DataPoints[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Real }).ToList() });
                dataLookup.Add("Apparent Power Total", new FlotSeries() { ChartLabel = "Total Apparent Power", DataPoints = powerPoints.Select((iPoint, index) => new double[] { vICycleDataGroup.VC.RMS.DataPoints[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude }).ToList() });
                dataLookup.Add("Power Factor Total", new FlotSeries() { ChartLabel = "Total Power Factor", DataPoints = powerPoints.Select((iPoint, index) => new double[] { vICycleDataGroup.VC.RMS.DataPoints[index].Time.Subtract(m_epoch).TotalMilliseconds, iPoint.Magnitude / iPoint.Real }).ToList() });

            }

            return dataLookup;
        }
        #endregion

        #region [ Missing Voltage ]
        [HttpGet]
        public Task<JsonReturn> GetMissingVoltageData(CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    Dictionary<string, string> query = Request.QueryParameters();
                    int eventId = int.Parse(query["eventId"]);
                    Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                    Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                    meter.ConnectionFactory = () => new AdoDataConnection("systemSettings");
                    int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                    double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;


                    DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                    DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                    int pixels = int.Parse(query["pixels"]);
                    DataTable table;

                    Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                    table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                    foreach (DataRow row in table.Rows)
                    {
                        int eventID = row.ConvertField<int>("ID");
                        DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                        Dictionary<string, FlotSeries> temp = QueryMissingVoltageData(eventID, eventStartTime, meter);

                        foreach (string key in temp.Keys)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                    if (dict.Count == 0) return null;

                    double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                    List<FlotSeries> returnList = new List<FlotSeries>();
                    foreach (string key in dict.Keys)
                    {
                        FlotSeries series = new FlotSeries();
                        series = dict[key];
                        series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                        returnList.Add(series);
                    }
                    JsonReturn returnDict = new JsonReturn();
                    returnDict.StartDate = evt.StartTime;
                    returnDict.EndDate = evt.EndTime;
                    returnDict.Data = returnList;
                    returnDict.CalculationTime = calcTime;
                    returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                    return returnDict;


                }

            }, cancellationToken);
        }

        private Dictionary<string, FlotSeries> QueryMissingVoltageData(int eventID, DateTime startTime, Meter meter)
        {
            string target = $"DataGroup-{eventID}-{startTime.Ticks}";
            DataGroup dataGroup = (DataGroup)s_memoryCache.Get(target);

            if (dataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    dataGroup = ToDataGroup(meter, frequencyDomainData);
                    s_memoryCache.Add(target, dataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetMissingVoltageLookup(dataGroup);
        }

        private Dictionary<string, FlotSeries> GetMissingVoltageLookup(DataGroup dataGroup)
        {
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();
            double systemFrequency;
            DataSeries vAN;
            DataSeries vBN;
            DataSeries vCN;
            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;
                vAN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "AN");
                vBN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "BN");
                vCN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "CN");
            }


            if (vAN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(vAN.SampleRate, systemFrequency);

                List<DataPoint> firstCycle = vAN.DataPoints.Take(samplesPerCycle).ToList();
                List<DataPoint> lastCycle = vAN.DataPoints.OrderByDescending(x => x.Time).Take(samplesPerCycle).ToList();

                List<DataPoint> fullWaveFormPre = vAN.DataPoints.Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - firstCycle[index % samplesPerCycle].Value }).ToList();
                List<DataPoint> fullWaveFormPost = vAN.DataPoints.OrderByDescending(x => x.Time).Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - lastCycle[index % samplesPerCycle].Value }).OrderBy(x => x.Time).ToList();

                dataLookup.Add("Pre Fault VAN", new FlotSeries() { ChartLabel = "VAN Pre Fault", DataPoints = fullWaveFormPre.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });
                dataLookup.Add("Post Fault VAN", new FlotSeries() { ChartLabel = "VAN Post Fault", DataPoints = fullWaveFormPost.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });

            }


            if (vBN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(vBN.SampleRate, systemFrequency);

                List<DataPoint> firstCycle = vBN.DataPoints.Take(samplesPerCycle).ToList();
                List<DataPoint> lastCycle = vBN.DataPoints.OrderByDescending(x => x.Time).Take(samplesPerCycle).ToList();

                List<DataPoint> fullWaveFormPre = vBN.DataPoints.Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - firstCycle[index % samplesPerCycle].Value }).ToList();
                List<DataPoint> fullWaveFormPost = vBN.DataPoints.OrderByDescending(x => x.Time).Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - lastCycle[index % samplesPerCycle].Value }).OrderBy(x => x.Time).ToList();

                dataLookup.Add("Pre Fault VBN", new FlotSeries() { ChartLabel = "VBN Pre Fault", DataPoints = fullWaveFormPre.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });
                dataLookup.Add("Post Fault VBN", new FlotSeries() { ChartLabel = "VBN Post Fault", DataPoints = fullWaveFormPost.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });
            }

            if (vCN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(vCN.SampleRate, systemFrequency);

                List<DataPoint> firstCycle = vCN.DataPoints.Take(samplesPerCycle).ToList();
                List<DataPoint> lastCycle = vCN.DataPoints.OrderByDescending(x => x.Time).Take(samplesPerCycle).ToList();

                List<DataPoint> fullWaveFormPre = vCN.DataPoints.Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - firstCycle[index % samplesPerCycle].Value }).ToList();
                List<DataPoint> fullWaveFormPost = vCN.DataPoints.OrderByDescending(x => x.Time).Select((dataPoint, index) => new DataPoint() { Time = dataPoint.Time, Value = dataPoint.Value - lastCycle[index % samplesPerCycle].Value }).OrderBy(x => x.Time).ToList();

                dataLookup.Add("Pre Fault VCN", new FlotSeries() { ChartLabel = "VCN Pre Fault", DataPoints = fullWaveFormPre.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });
                dataLookup.Add("Post Fault VCN", new FlotSeries() { ChartLabel = "VCN Post Fault", DataPoints = fullWaveFormPost.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, point.Value }).ToList() });
            }



            return dataLookup;
        }
        #endregion

        #region [ LowPassFilter ]
        [HttpGet]
        public Task<JsonReturn> GetLowPassFilterData(CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    Dictionary<string, string> query = Request.QueryParameters();
                    int eventId = int.Parse(query["eventId"]);
                    Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                    Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                    meter.ConnectionFactory = () => new AdoDataConnection("systemSettings");
                    int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                    double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;


                    DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                    DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                    int pixels = int.Parse(query["pixels"]);
                    DataTable table;

                    Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                    table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                    foreach (DataRow row in table.Rows)
                    {
                        int eventID = row.ConvertField<int>("ID");
                        DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                        Dictionary<string, FlotSeries> temp = QueryLowPassFilterData(eventID, eventStartTime, meter);

                        foreach (string key in temp.Keys)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                    if (dict.Count == 0) return null;

                    double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                    List<FlotSeries> returnList = new List<FlotSeries>();
                    foreach (string key in dict.Keys)
                    {
                        FlotSeries series = new FlotSeries();
                        series = dict[key];
                        series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                        returnList.Add(series);
                    }
                    JsonReturn returnDict = new JsonReturn();
                    returnDict.StartDate = evt.StartTime;
                    returnDict.EndDate = evt.EndTime;
                    returnDict.Data = returnList;
                    returnDict.CalculationTime = calcTime;
                    returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                    return returnDict;


                }

            }, cancellationToken);
        }

        private Dictionary<string, FlotSeries> QueryLowPassFilterData(int eventID, DateTime startTime, Meter meter)
        {
            string target = $"DataGroup-{eventID}-{startTime.Ticks}";
            DataGroup dataGroup = (DataGroup)s_memoryCache.Get(target);

            if (dataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    dataGroup = ToDataGroup(meter, frequencyDomainData);
                    s_memoryCache.Add(target, dataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetLowPassFilterLookup(dataGroup);
        }

        private Dictionary<string, FlotSeries> GetLowPassFilterLookup(DataGroup dataGroup)
        {
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();
            double systemFrequency;
            DataSeries vAN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "AN");
            DataSeries vBN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "BN");
            DataSeries vCN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "CN");
            DataSeries iAN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "AN");
            DataSeries iBN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "BN");
            DataSeries iCN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "CN");

            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;
            }


            if (vAN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(vAN.SampleRate, systemFrequency);
                List<DataPoint> points = vAN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.LowPass(samplesPerCycle*systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("Low Pass Filter VAN", new FlotSeries() { ChartLabel = "VAN Low Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }


            if (vBN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(vBN.SampleRate, systemFrequency);
                List<DataPoint> points = vBN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.LowPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("Low Pass Filter VBN", new FlotSeries() { ChartLabel = "VBN Low Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }

            if (vCN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(vCN.SampleRate, systemFrequency);
                List<DataPoint> points = vCN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.LowPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("Low Pass Filter VCN", new FlotSeries() { ChartLabel = "VCN Low Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }

            if (iAN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(iAN.SampleRate, systemFrequency);
                List<DataPoint> points = iAN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.LowPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("Low Pass Filter IAN", new FlotSeries() { ChartLabel = "IAN Low Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }


            if (iBN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(iBN.SampleRate, systemFrequency);
                List<DataPoint> points = iBN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.LowPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("Low Pass Filter IBN", new FlotSeries() { ChartLabel = "IBN Low Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }

            if (iCN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(iCN.SampleRate, systemFrequency);
                List<DataPoint> points = iCN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.LowPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("Low Pass Filter ICN", new FlotSeries() { ChartLabel = "ICN Low Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }



            return dataLookup;
        }
        #endregion

        #region [ High Pass Filter ]
        [HttpGet]
        public Task<JsonReturn> GetHighPassFilterData(CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    Dictionary<string, string> query = Request.QueryParameters();
                    int eventId = int.Parse(query["eventId"]);
                    Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                    Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                    meter.ConnectionFactory = () => new AdoDataConnection("systemSettings");
                    int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                    double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;


                    DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                    DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                    int pixels = int.Parse(query["pixels"]);
                    DataTable table;

                    Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                    table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                    foreach (DataRow row in table.Rows)
                    {
                        int eventID = row.ConvertField<int>("ID");
                        DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                        Dictionary<string, FlotSeries> temp = QueryHighPassFilterData(eventID, eventStartTime, meter);

                        foreach (string key in temp.Keys)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                    if (dict.Count == 0) return null;

                    double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                    List<FlotSeries> returnList = new List<FlotSeries>();
                    foreach (string key in dict.Keys)
                    {
                        FlotSeries series = new FlotSeries();
                        series = dict[key];
                        series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                        returnList.Add(series);
                    }
                    JsonReturn returnDict = new JsonReturn();
                    returnDict.StartDate = evt.StartTime;
                    returnDict.EndDate = evt.EndTime;
                    returnDict.Data = returnList;
                    returnDict.CalculationTime = calcTime;
                    returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                    return returnDict;


                }

            }, cancellationToken);
        }

        private Dictionary<string, FlotSeries> QueryHighPassFilterData(int eventID, DateTime startTime, Meter meter)
        {
            string target = $"DataGroup-{eventID}-{startTime.Ticks}";
            DataGroup dataGroup = (DataGroup)s_memoryCache.Get(target);

            if (dataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    dataGroup = ToDataGroup(meter, frequencyDomainData);
                    s_memoryCache.Add(target, dataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetHighPassFilterLookup(dataGroup);
        }

        private Dictionary<string, FlotSeries> GetHighPassFilterLookup(DataGroup dataGroup)
        {
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();
            double systemFrequency;
            DataSeries vAN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "AN");
            DataSeries vBN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "BN");
            DataSeries vCN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "CN");
            DataSeries iAN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "AN");
            DataSeries iBN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "BN");
            DataSeries iCN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "CN");

            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;
            }


            if (vAN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(vAN.SampleRate, systemFrequency);
                List<DataPoint> points = vAN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.HighPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("High Pass Filter VAN", new FlotSeries() { ChartLabel = "VAN High Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }


            if (vBN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(vBN.SampleRate, systemFrequency);
                List<DataPoint> points = vBN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.HighPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("High Pass Filter VBN", new FlotSeries() { ChartLabel = "VBN High Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }

            if (vCN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(vCN.SampleRate, systemFrequency);
                List<DataPoint> points = vCN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.HighPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("High Pass Filter VCN", new FlotSeries() { ChartLabel = "VCN High Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }

            if (iAN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(iAN.SampleRate, systemFrequency);
                List<DataPoint> points = iAN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.HighPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("High Pass Filter IAN", new FlotSeries() { ChartLabel = "IAN High Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }


            if (iBN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(iBN.SampleRate, systemFrequency);
                List<DataPoint> points = iBN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.HighPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("High Pass Filter IBN", new FlotSeries() { ChartLabel = "IBN High Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }

            if (iCN != null)
            {
                int samplesPerCycle = Transform.CalculateSamplesPerCycle(iCN.SampleRate, systemFrequency);
                List<DataPoint> points = iCN.DataPoints;

                double[] lowPass = MathNet.Filtering.FIR.FirCoefficients.HighPass(samplesPerCycle * systemFrequency, systemFrequency);

                MathNet.Filtering.FIR.OnlineFirFilter filter = new MathNet.Filtering.FIR.OnlineFirFilter(lowPass);

                double[] results = filter.ProcessSamples(points.Select(x => x.Value).ToArray());

                dataLookup.Add("High Pass Filter ICN", new FlotSeries() { ChartLabel = "ICN High Pass Filter", DataPoints = results.Select((point, index) => new double[] { points[index].Time.Subtract(m_epoch).TotalMilliseconds, point }).ToList() });
            }



            return dataLookup;
        }
        #endregion

        #region [ Symmetrical Components  ]
        [HttpGet]
        public Task<JsonReturn> GetSymmetricalComponentsData(CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    Dictionary<string, string> query = Request.QueryParameters();
                    int eventId = int.Parse(query["eventId"]);
                    Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                    Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                    meter.ConnectionFactory = () => new AdoDataConnection("systemSettings");
                    int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                    double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;


                    DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                    DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                    int pixels = int.Parse(query["pixels"]);
                    DataTable table;

                    Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                    table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                    foreach (DataRow row in table.Rows)
                    {
                        int eventID = row.ConvertField<int>("ID");
                        DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                        Dictionary<string, FlotSeries> temp = QuerySymmetricalComponentsData(eventID, eventStartTime, meter);

                        foreach (string key in temp.Keys)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                    if (dict.Count == 0) return null;

                    double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                    List<FlotSeries> returnList = new List<FlotSeries>();
                    foreach (string key in dict.Keys)
                    {
                        FlotSeries series = new FlotSeries();
                        series = dict[key];
                        series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                        returnList.Add(series);
                    }
                    JsonReturn returnDict = new JsonReturn();
                    returnDict.StartDate = evt.StartTime;
                    returnDict.EndDate = evt.EndTime;
                    returnDict.Data = returnList;
                    returnDict.CalculationTime = calcTime;
                    returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                    return returnDict;


                }

            }, cancellationToken);
        }

        private Dictionary<string, FlotSeries> QuerySymmetricalComponentsData(int eventID, DateTime startTime, Meter meter)
        {
            string target = $"VICycleDataGroup-{eventID}-{startTime.Ticks}";
            VICycleDataGroup vICycleDataGroup = (VICycleDataGroup)s_memoryCache.Get(target);

            if (vICycleDataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    DataGroup dataGroup = ToDataGroup(meter, frequencyDomainData);
                    double freq = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0D;
                    vICycleDataGroup = Transform.ToVICycleDataGroup(new VIDataGroup(dataGroup), freq);
                    s_memoryCache.Add(target, vICycleDataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetSymmetricalComponentsLookup(vICycleDataGroup);
        }

        private Dictionary<string, FlotSeries> GetSymmetricalComponentsLookup(VICycleDataGroup vICycleDataGroup)
        {
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();



            if (vICycleDataGroup.VA != null && vICycleDataGroup.VB != null && vICycleDataGroup.VC != null)
            {
                var va = vICycleDataGroup.VA.RMS.DataPoints;
                var vaPhase = vICycleDataGroup.VA.Phase.DataPoints;
                var vb = vICycleDataGroup.VB.RMS.DataPoints;
                var vbPhase = vICycleDataGroup.VB.Phase.DataPoints;
                var vc = vICycleDataGroup.VC.RMS.DataPoints;
                var vcPhase = vICycleDataGroup.VC.Phase.DataPoints;

                IEnumerable<SequenceComponents> sequencComponents = va.Select((point, index) => {
                    DataPoint vaPoint = point;
                    DataPoint vaPhasePoint = vaPhase[index];
                    Complex vaComplex = Complex.FromPolarCoordinates(vaPoint.Value, vaPhasePoint.Value);

                    DataPoint vbPoint = vb[index];
                    DataPoint vbPhasePoint = vbPhase[index];
                    Complex vbComplex = Complex.FromPolarCoordinates(vbPoint.Value, vbPhasePoint.Value);

                    DataPoint vcPoint = vc[index];
                    DataPoint vcPhasePoint = vcPhase[index];
                    Complex vcComplex = Complex.FromPolarCoordinates(vcPoint.Value, vcPhasePoint.Value);

                    SequenceComponents sequenceComponents = CalculateSequenceComponents(vaComplex, vbComplex, vcComplex);

                    return sequenceComponents;
                });

                dataLookup.Add("S0 Voltage", new FlotSeries() { ChartLabel = "Voltage S0", DataPoints = sequencComponents.Select((point, index) => new double[] { va[index].Time.Subtract(m_epoch).TotalMilliseconds, point.S0.Magnitude }).ToList() });
                dataLookup.Add("S1 Voltage", new FlotSeries() { ChartLabel = "Voltage S1", DataPoints = sequencComponents.Select((point, index) => new double[] { va[index].Time.Subtract(m_epoch).TotalMilliseconds, point.S1.Magnitude }).ToList() });
                dataLookup.Add("S2 Voltage", new FlotSeries() { ChartLabel = "Voltage S2", DataPoints = sequencComponents.Select((point, index) => new double[] { va[index].Time.Subtract(m_epoch).TotalMilliseconds, point.S2.Magnitude }).ToList() });

            }


            if (vICycleDataGroup.IA != null && vICycleDataGroup.IB != null && vICycleDataGroup.IC != null)
            {

                var ia = vICycleDataGroup.IA.RMS.DataPoints;
                var iaPhase = vICycleDataGroup.IA.Phase.DataPoints;
                var ib = vICycleDataGroup.IB.RMS.DataPoints;
                var ibPhase = vICycleDataGroup.IB.Phase.DataPoints;
                var ic = vICycleDataGroup.IC.RMS.DataPoints;
                var icPhase = vICycleDataGroup.IC.Phase.DataPoints;

                IEnumerable<SequenceComponents> sequencComponents = ia.Select((point, index) => {
                    DataPoint iaPoint = point;
                    DataPoint iaPhasePoint = iaPhase[index];
                    Complex iaComplex = Complex.FromPolarCoordinates(iaPoint.Value, iaPhasePoint.Value);

                    DataPoint ibPoint = ib[index];
                    DataPoint ibPhasePoint = ibPhase[index];
                    Complex ibComplex = Complex.FromPolarCoordinates(ibPoint.Value, ibPhasePoint.Value);

                    DataPoint icPoint = ic[index];
                    DataPoint icPhasePoint = icPhase[index];
                    Complex icComplex = Complex.FromPolarCoordinates(icPoint.Value, icPhasePoint.Value);

                    SequenceComponents sequenceComponents = CalculateSequenceComponents(iaComplex, ibComplex, icComplex);

                    return sequenceComponents;
                });

                dataLookup.Add("S0 Current", new FlotSeries() { ChartLabel = "Current S0", DataPoints = sequencComponents.Select((point, index) => new double[] { ia[index].Time.Subtract(m_epoch).TotalMilliseconds, point.S0.Magnitude }).ToList() });
                dataLookup.Add("S1 Current", new FlotSeries() { ChartLabel = "Current S1", DataPoints = sequencComponents.Select((point, index) => new double[] { ia[index].Time.Subtract(m_epoch).TotalMilliseconds, point.S1.Magnitude }).ToList() });
                dataLookup.Add("S2 Current", new FlotSeries() { ChartLabel = "Current S2", DataPoints = sequencComponents.Select((point, index) => new double[] { ia[index].Time.Subtract(m_epoch).TotalMilliseconds, point.S2.Magnitude }).ToList() });

            }

            return dataLookup;
        }


        private class SequenceComponents {
            public Complex S0 { get; set; }
            public Complex S2 { get; set; }
            public Complex S1 { get; set; }

        }

        private SequenceComponents CalculateSequenceComponents(Complex an, Complex bn, Complex cn)
        {
            double TwoPI = 2.0D * Math.PI;
            double Rad120 = TwoPI / 3.0D;
            Complex a = new Complex(Math.Cos(Rad120), Math.Sin(Rad120));
            Complex aSq = a * a;

            SequenceComponents sequenceComponents = new SequenceComponents();

            sequenceComponents.S0 = (an + bn + cn) / 3.0D;
            sequenceComponents.S1 = (an + a * bn + aSq * cn) / 3.0D;
            sequenceComponents.S2 = (an + aSq * bn + a * cn) / 3.0D;

            return sequenceComponents;
        }


        #endregion

        #region [ Unbalance ]
        [HttpGet]
        public Task<JsonReturn> GetUnbalanceData(CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    Dictionary<string, string> query = Request.QueryParameters();
                    int eventId = int.Parse(query["eventId"]);
                    Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                    Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                    meter.ConnectionFactory = () => new AdoDataConnection("systemSettings");
                    int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                    double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;


                    DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                    DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                    int pixels = int.Parse(query["pixels"]);
                    DataTable table;

                    Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                    table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                    foreach (DataRow row in table.Rows)
                    {
                        int eventID = row.ConvertField<int>("ID");
                        DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                        Dictionary<string, FlotSeries> temp = QueryUnbalanceData(eventID, eventStartTime, meter);

                        foreach (string key in temp.Keys)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                    if (dict.Count == 0) return null;

                    double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                    List<FlotSeries> returnList = new List<FlotSeries>();
                    foreach (string key in dict.Keys)
                    {
                        FlotSeries series = new FlotSeries();
                        series = dict[key];
                        series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                        returnList.Add(series);
                    }
                    JsonReturn returnDict = new JsonReturn();
                    returnDict.StartDate = evt.StartTime;
                    returnDict.EndDate = evt.EndTime;
                    returnDict.Data = returnList;
                    returnDict.CalculationTime = calcTime;
                    returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                    return returnDict;


                }

            }, cancellationToken);
        }

        private Dictionary<string, FlotSeries> QueryUnbalanceData(int eventID, DateTime startTime, Meter meter)
        {
            string target = $"VICycleDataGroup-{eventID}-{startTime.Ticks}";
            VICycleDataGroup vICycleDataGroup = (VICycleDataGroup)s_memoryCache.Get(target);

            if (vICycleDataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    DataGroup dataGroup = ToDataGroup(meter, frequencyDomainData);
                    double freq = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0D;
                    vICycleDataGroup = Transform.ToVICycleDataGroup(new VIDataGroup(dataGroup), freq);
                    s_memoryCache.Add(target, vICycleDataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetUnbalanceLookup(vICycleDataGroup);
        }

        private Dictionary<string, FlotSeries> GetUnbalanceLookup(VICycleDataGroup vICycleDataGroup)
        {
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();



            if (vICycleDataGroup.VA != null && vICycleDataGroup.VB != null && vICycleDataGroup.VC != null)
            {
                var va = vICycleDataGroup.VA.RMS.DataPoints;
                var vaPhase = vICycleDataGroup.VA.Phase.DataPoints;
                var vb = vICycleDataGroup.VB.RMS.DataPoints;
                var vbPhase = vICycleDataGroup.VB.Phase.DataPoints;
                var vc = vICycleDataGroup.VC.RMS.DataPoints;
                var vcPhase = vICycleDataGroup.VC.Phase.DataPoints;

                IEnumerable<SequenceComponents> sequencComponents = va.Select((point, index) => {
                    DataPoint vaPoint = point;
                    DataPoint vaPhasePoint = vaPhase[index];
                    Complex vaComplex = Complex.FromPolarCoordinates(vaPoint.Value, vaPhasePoint.Value);

                    DataPoint vbPoint = vb[index];
                    DataPoint vbPhasePoint = vbPhase[index];
                    Complex vbComplex = Complex.FromPolarCoordinates(vbPoint.Value, vbPhasePoint.Value);

                    DataPoint vcPoint = vc[index];
                    DataPoint vcPhasePoint = vcPhase[index];
                    Complex vcComplex = Complex.FromPolarCoordinates(vcPoint.Value, vcPhasePoint.Value);

                    SequenceComponents sequenceComponents = CalculateSequenceComponents(vaComplex, vbComplex, vcComplex);

                    return sequenceComponents;
                });

                dataLookup.Add("S0/S1 Voltage", new FlotSeries() { ChartLabel = "Voltage S0/S1", DataPoints = sequencComponents.Select((point, index) => new double[] { va[index].Time.Subtract(m_epoch).TotalMilliseconds, point.S0.Magnitude/point.S1.Magnitude }).ToList() });
                dataLookup.Add("S2/S1 Voltage", new FlotSeries() { ChartLabel = "Voltage S2/S1", DataPoints = sequencComponents.Select((point, index) => new double[] { va[index].Time.Subtract(m_epoch).TotalMilliseconds, point.S2.Magnitude/point.S1.Magnitude }).ToList() });

            }


            if (vICycleDataGroup.IA != null && vICycleDataGroup.IB != null && vICycleDataGroup.IC != null)
            {

                var ia = vICycleDataGroup.IA.RMS.DataPoints;
                var iaPhase = vICycleDataGroup.IA.Phase.DataPoints;
                var ib = vICycleDataGroup.IB.RMS.DataPoints;
                var ibPhase = vICycleDataGroup.IB.Phase.DataPoints;
                var ic = vICycleDataGroup.IC.RMS.DataPoints;
                var icPhase = vICycleDataGroup.IC.Phase.DataPoints;

                IEnumerable<SequenceComponents> sequencComponents = ia.Select((point, index) => {
                    DataPoint iaPoint = point;
                    DataPoint iaPhasePoint = iaPhase[index];
                    Complex iaComplex = Complex.FromPolarCoordinates(iaPoint.Value, iaPhasePoint.Value);

                    DataPoint ibPoint = ib[index];
                    DataPoint ibPhasePoint = ibPhase[index];
                    Complex ibComplex = Complex.FromPolarCoordinates(ibPoint.Value, ibPhasePoint.Value);

                    DataPoint icPoint = ic[index];
                    DataPoint icPhasePoint = icPhase[index];
                    Complex icComplex = Complex.FromPolarCoordinates(icPoint.Value, icPhasePoint.Value);

                    SequenceComponents sequenceComponents = CalculateSequenceComponents(iaComplex, ibComplex, icComplex);

                    return sequenceComponents;
                });

                dataLookup.Add("S0/S1 Current", new FlotSeries() { ChartLabel = "Current S0/S1", DataPoints = sequencComponents.Select((point, index) => new double[] { ia[index].Time.Subtract(m_epoch).TotalMilliseconds, point.S0.Magnitude / point.S1.Magnitude }).ToList() });
                dataLookup.Add("S2/S1 Current", new FlotSeries() { ChartLabel = "Current S2/S1", DataPoints = sequencComponents.Select((point, index) => new double[] { ia[index].Time.Subtract(m_epoch).TotalMilliseconds, point.S2.Magnitude / point.S1.Magnitude }).ToList() });

            }

            return dataLookup;
        }


        #endregion

        #region [ Rectifier ]
        [HttpGet]
        public Task<JsonReturn> GetRectifierData(CancellationToken cancellationToken)
        {
            return Task.Run(() => {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    Dictionary<string, string> query = Request.QueryParameters();
                    int eventId = int.Parse(query["eventId"]);
                    Event evt = new TableOperations<Event>(connection).QueryRecordWhere("ID = {0}", eventId);
                    Meter meter = new TableOperations<Meter>(connection).QueryRecordWhere("ID = {0}", evt.MeterID);
                    meter.ConnectionFactory = () => new AdoDataConnection("systemSettings");
                    int calcCycle = connection.ExecuteScalar<int?>("SELECT CalculationCycle FROM FaultSummary WHERE EventID = {0} AND IsSelectedAlgorithm = 1", evt.ID) ?? -1;
                    double systemFrequency = connection.ExecuteScalar<double?>("SELECT Value FROM Setting WHERE Name = 'SystemFrequency'") ?? 60.0;


                    DateTime startTime = (query.ContainsKey("startDate") ? DateTime.Parse(query["startDate"]) : evt.StartTime);
                    DateTime endTime = (query.ContainsKey("endDate") ? DateTime.Parse(query["endDate"]) : evt.EndTime);
                    int pixels = int.Parse(query["pixels"]);
                    DataTable table;

                    Dictionary<string, FlotSeries> dict = new Dictionary<string, FlotSeries>();
                    table = connection.RetrieveData("select ID, StartTime from Event WHERE StartTime <= {0} AND EndTime >= {1} and MeterID = {2} AND LineID = {3}", ToDateTime2(connection, endTime), ToDateTime2(connection, startTime), evt.MeterID, evt.LineID);
                    foreach (DataRow row in table.Rows)
                    {
                        int eventID = row.ConvertField<int>("ID");
                        DateTime eventStartTime = row.ConvertField<DateTime>("StartTime");
                        Dictionary<string, FlotSeries> temp = QueryRectifierData(eventID, eventStartTime, meter);

                        foreach (string key in temp.Keys)
                        {
                            if (dict.ContainsKey(key))
                                dict[key].DataPoints = dict[key].DataPoints.Concat(temp[key].DataPoints).ToList();
                            else
                                dict.Add(key, temp[key]);
                        }
                    }
                    if (dict.Count == 0) return null;

                    double calcTime = (calcCycle >= 0 ? dict.First().Value.DataPoints[calcCycle][0] : 0);

                    List<FlotSeries> returnList = new List<FlotSeries>();
                    foreach (string key in dict.Keys)
                    {
                        FlotSeries series = new FlotSeries();
                        series = dict[key];
                        series.DataPoints = Downsample(dict[key].DataPoints.OrderBy(x => x[0]).ToList(), pixels, new Range<DateTime>(startTime, endTime));
                        returnList.Add(series);
                    }
                    JsonReturn returnDict = new JsonReturn();
                    returnDict.StartDate = evt.StartTime;
                    returnDict.EndDate = evt.EndTime;
                    returnDict.Data = returnList;
                    returnDict.CalculationTime = calcTime;
                    returnDict.CalculationEnd = calcTime + 1000 / systemFrequency;

                    return returnDict;


                }

            }, cancellationToken);
        }

        private Dictionary<string, FlotSeries> QueryRectifierData(int eventID, DateTime startTime, Meter meter)
        {
            string target = $"DataGroup-{eventID}-{startTime.Ticks}";
            DataGroup dataGroup = (DataGroup)s_memoryCache.Get(target);

            if (dataGroup == null)
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    byte[] frequencyDomainData = connection.ExecuteScalar<byte[]>("SELECT TimeDomainData FROM EventData WHERE ID = (SELECT EventDataID FROM Event WHERE ID = {0})", eventID);
                    dataGroup = ToDataGroup(meter, frequencyDomainData);
                    s_memoryCache.Add(target, dataGroup, new CacheItemPolicy { SlidingExpiration = TimeSpan.FromMinutes(10.0D) });
                }
            }

            return GetRectifierLookup(dataGroup);
        }

        private Dictionary<string, FlotSeries> GetRectifierLookup(DataGroup dataGroup)
        {
            Dictionary<string, FlotSeries> dataLookup = new Dictionary<string, FlotSeries>();

            List<DataPoint> vAN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "AN").DataPoints;
            List<DataPoint> vBN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "BN").DataPoints;
            List<DataPoint> vCN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Voltage" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "CN").DataPoints;
            List<DataPoint> iAN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "AN").DataPoints;
            List<DataPoint> iBN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "BN").DataPoints;
            List<DataPoint> iCN = dataGroup.DataSeries.ToList().Find(x => x.SeriesInfo.Channel.MeasurementType.Name == "Current" && x.SeriesInfo.Channel.MeasurementCharacteristic.Name == "Instantaneous" && x.SeriesInfo.Channel.Phase.Name == "CN").DataPoints;
                           


            if (vAN != null && vBN != null && vCN != null)
            {

                dataLookup.Add("Rectifier Voltage", new FlotSeries() { ChartLabel = "Voltage Rectifier", DataPoints = vAN.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, new List<double>() { Math.Abs(point.Value), Math.Abs(vBN[index].Value), Math.Abs(vCN[index].Value)}.Max() }).ToList() });

            }


            if (iAN != null && iBN != null && iCN != null)
            {

                dataLookup.Add("Rectifier Current", new FlotSeries() { ChartLabel = "Current Rectifier", DataPoints = iAN.Select((point, index) => new double[] { point.Time.Subtract(m_epoch).TotalMilliseconds, new List<double>() { Math.Abs(point.Value), Math.Abs(iBN[index].Value), Math.Abs(iCN[index].Value) }.Max() }).ToList() });

            }



            return dataLookup;
        }


        #endregion


        #endregion

        #region [ UI Widgets ]
        [HttpGet]
        public Dictionary<string, string> GetScalarStats()
        {
            Dictionary<string, string> query = Request.QueryParameters();
            int eventId = int.Parse(query["eventId"]);

            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                DataTable dataTable = connection.RetrieveData("SELECT * FROM OpenSEEScalarStatView WHERE EventID = {0}", eventId);
                if (dataTable.Rows.Count == 0) return new Dictionary<string, string>();

                DataRow row = dataTable.AsEnumerable().First();
                return row.Table.Columns.Cast<DataColumn>().ToDictionary(c => c.ColumnName, c => row[c].ToString());

            }
        }

        [HttpGet]
        public DataTable GetHarmonics()
        {
            Dictionary<string, string> query = Request.QueryParameters();
            int eventId = int.Parse(query["eventId"]);

            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                DataTable dataTable = connection.RetrieveData(@"
                    SELECT 
                        MeasurementType.Name + ' ' + Phase.Name as Channel, 
                        SpectralData 
                    FROM 
                        SnapshotHarmonics JOIN 
                        Channel ON Channel.ID = SnapshotHarmonics.ChannelID JOIN
                        MeasurementType ON Channel.MeasurementTypeID = MeasurementType.ID JOIN
                        Phase ON Channel.PhaseID = Phase.ID
                        WHERE EventID = {0}", eventId);

                return dataTable;

            }
        }
        public DataTable GetTimeCorrelatedSags()
        {
            Dictionary<string, string> query = Request.QueryParameters();
            int eventID = int.Parse(query["eventId"]);

            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                const string SQL =
                    "SELECT DISTINCT " +
                    "    Event.ID AS EventID, " +
                    "    EventType.Name AS EventType, " +
                    "    Event.StartTime, " +
                    "    Meter.Name AS MeterName, " +
                    "    MeterLine.LineName " +
                    "FROM " +
                    "    Disturbance JOIN " +
                    "    EventType DisturbanceType ON Disturbance.EventTypeID = DisturbanceType.ID JOIN " +
                    "    Event ON Disturbance.EventID = Event.ID JOIN " +
                    "    EventType ON Event.EventTypeID = EventType.ID JOIN " +
                    "    Meter ON Event.MeterID = Meter.ID JOIN " +
                    "    MeterLine ON " +
                    "        Event.MeterID = MeterLine.MeterID AND " +
                    "        Event.LineID = MeterLine.LineID " +
                    "WHERE " +
                    "    DisturbanceType.Name = 'Sag' AND " +
                    "    Disturbance.StartTime <= {1} AND " +
                    "    Disturbance.EndTime >= {0} " +
                    "ORDER BY " +
                    "    Event.StartTime, " +
                    "    Meter.Name, " +
                    "    MeterLine.LineName";

                double timeTolerance = connection.ExecuteScalar<double>("SELECT Value FROM Setting WHERE Name = 'TimeTolerance'");
                DateTime startTime = connection.ExecuteScalar<DateTime>("SELECT StartTime FROM Event WHERE ID = {0}", eventID);
                DateTime endTime = connection.ExecuteScalar<DateTime>("SELECT EndTime FROM Event WHERE ID = {0}", eventID);
                DateTime adjustedStartTime = startTime.AddSeconds(-timeTolerance);
                DateTime adjustedEndTime = endTime.AddSeconds(timeTolerance);
                DataTable dataTable = connection.RetrieveData(SQL, adjustedStartTime, adjustedEndTime);
                return dataTable;
            }
        }


        #endregion

        #region [ Note Management ]
        [HttpGet]
        public DataTable GetNotes()
        {
            Dictionary<string, string> query = Request.QueryParameters();
            int eventID = int.Parse(query["eventId"]);
            using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
            {
                const string SQL = "SELECT * FROM EventNote WHERE EventID = {0}";

                DataTable dataTable = connection.RetrieveData(SQL, eventID);
                return dataTable;
            }


        }

        public class FormData
        {
            public int? ID { get; set; }
            public int EventID { get; set; }
            public string Note { get; set; }
        }

        [HttpPost]
        public IHttpActionResult AddNote(FormData note)
        {
            IHttpActionResult result = ValidateAdminRequest();
            if (result != null) return result;

            try
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    EventNote record = new EventNote()
                    {
                        EventID = note.EventID,
                        Note = note.Note,
                        UserAccount = User.Identity.Name,
                        Timestamp = DateTime.Now
                    };

                    new TableOperations<EventNote>(connection).AddNewRecord(record);

                    result = Ok(record);

                }
            }
            catch (Exception ex)
            {
                result = InternalServerError(ex);
            }

            return result;
        }

        [HttpDelete]
        public IHttpActionResult DeleteNote(FormData note)
        {
            IHttpActionResult result = ValidateAdminRequest();
            if (result != null) return result;
            try
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    EventNote record = new TableOperations<EventNote>(connection).QueryRecordWhere("ID = {0}", note.ID);
                    new TableOperations<EventNote>(connection).DeleteRecord(record);
                    result = Ok(record);

                }
            }
            catch (Exception ex)
            {
                result = InternalServerError(ex);
            }

            return result;

        }

        [HttpPatch]
        public IHttpActionResult UpdateNote(FormData note)
        {
            IHttpActionResult result = ValidateAdminRequest();
            if (result != null) return result;
            try
            {
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    EventNote record = new TableOperations<EventNote>(connection).QueryRecordWhere("ID = {0}", note.ID);

                    record.Note = note.Note;
                    record.UserAccount = User.Identity.Name;
                    record.Timestamp = DateTime.Now;


                    new TableOperations<EventNote>(connection).UpdateRecord(record);

                    result = Ok(record);

                }
            }
            catch (Exception ex)
            {
                result = InternalServerError(ex);
            }

            return result;
        }

        #endregion

        #region [ Security ]

        private IHttpActionResult ValidateAdminRequest()
        {
            string username = User.Identity.Name;
            ISecurityProvider securityProvider = SecurityProviderUtility.CreateProvider(username);
            securityProvider.PassthroughPrincipal = User;

            if (!securityProvider.Authenticate())
                return StatusCode(HttpStatusCode.Forbidden);

            SecurityIdentity approverIdentity = new SecurityIdentity(securityProvider);
            SecurityPrincipal approverPrincipal = new SecurityPrincipal(approverIdentity);

            if (!approverPrincipal.IsInRole("Administrator"))
                return StatusCode(HttpStatusCode.Forbidden);

            return null;
        }
        #endregion

        #endregion



    }
}