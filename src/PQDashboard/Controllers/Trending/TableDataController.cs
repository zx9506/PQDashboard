﻿//******************************************************************************************************
//  TableDataController.cs - Gbtc
//
//  Copyright © 2020, Grid Protection Alliance.  All Rights Reserved.
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
//  03/30/2020 - Billy Ernest
//       Generated original version of source code.
//
//******************************************************************************************************


using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Web.Http;
using GSF.Data;
using GSF.Data.Model;
using GSF.Collections;
using openXDA.Model;

namespace PQDashboard.Controllers.Trending
{
    public class DetailtsForSitesForm
    {
        public string siteId { get; set; }
        public string targetDate { get; set; }
        public string colorScale { get; set; }
        public string context { get; set; }
    }

    [RoutePrefix("api/Trending/TableData")]
    public class TrendingTableDataController : ApiController
    {
        [Route(""), HttpPost]
        public IHttpActionResult Post(DetailtsForSitesForm form)
        {
            try
            {
                string tab = "Trending";
                using (AdoDataConnection connection = new AdoDataConnection("systemSettings"))
                {
                    IEnumerable<DashSettings> dashSettings = new TableOperations<DashSettings>(connection).QueryRecords(restriction: new RecordRestriction("Name = '" + tab + "Chart'"));
                    DateTime date = DateTime.Parse(form.targetDate).ToUniversalTime();
                    Dictionary<string, bool> disabledFileds = new Dictionary<string, bool>();
                    foreach (DashSettings setting in dashSettings)
                    {
                        if (!disabledFileds.ContainsKey(setting.Value))
                            disabledFileds.Add(setting.Value, setting.Enabled);
                    }

                    DataTable table = connection.RetrieveData(@"
                        DECLARE @EventDate DATETIME = {0}
                        DECLARE @MeterID AS varchar(max) = {1}
                        DECLARE @context as nvarchar(20) = {2}
                        declare @theDate as Date
                        declare  @MeterIDs TABLE (ID int);

                        set @theDate = CAST(@EventDate as Date)

                        INSERT INTO @MeterIDs(ID) SELECT Value FROM dbo.String_to_int_table(@MeterID, ',');

                            Select
                            Meter.ID as meterid,
                            Channel.ID as channelid,
                            Meter.Name as sitename,
                            [dbo].[AlarmType].[Name] as eventtype,
                            [dbo].[MeasurementCharacteristic].[Name] as characteristic,
                            [dbo].[MeasurementType].[Name] as measurementtype,
                            [dbo].[Phase].[Name] as phasename,
                            Channel.HarmonicGroup,
                            SUM (ChannelAlarmSummary.AlarmPoints) as eventcount,
                            @theDate as date

                            from Channel

                            join ChannelAlarmSummary on ChannelAlarmSummary.ChannelID = Channel.ID and Date = @theDate
                            join Meter on Channel.MeterID = Meter.ID and [MeterID] in ( Select * from @MeterIDs)
                            join [dbo].[AlarmType] on
                                [dbo].[AlarmType].[ID] = ChannelAlarmSummary.AlarmTypeID and
                                ([dbo].[AlarmType].[Name] = 'OffNormal' or [dbo].[AlarmType].[Name] = 'Alarm')

                            join [dbo].[MeasurementCharacteristic] on Channel.MeasurementCharacteristicID = [dbo].[MeasurementCharacteristic].[ID]
                            join [dbo].[MeasurementType] on Channel.MeasurementTypeID =  [dbo].[MeasurementType].ID
                            join [dbo].[Phase] on Channel.PhaseID = [dbo].[Phase].ID

                            Group By Meter.ID , Channel.ID , Meter.Name , [dbo].[AlarmType].[Name], [MeasurementCharacteristic].[Name] , [MeasurementType].[Name] , [dbo].[Phase].[Name], Channel.HarmonicGroup
                            Order By Meter.ID

                    ", date, form.siteId, form.context, form.colorScale);


                    List<string> skipColumns;
                    if (tab == "Events" || tab == "Disturbances") skipColumns = new List<string>() { "EventID", "MeterID", "Site" };
                    else skipColumns = table.Columns.Cast<DataColumn>().Select(x => x.ColumnName).ToList();


                    List<string> columnsToRemove = new List<string>();
                    foreach (DataColumn column in table.Columns)
                    {
                        if (!skipColumns.Contains(column.ColumnName) && !disabledFileds.ContainsKey(column.ColumnName))
                        {
                            disabledFileds.Add(column.ColumnName, true);
                            new TableOperations<DashSettings>(connection).GetOrAdd(tab + "Chart", column.ColumnName, true);
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


                    return Ok(table);

                }
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}