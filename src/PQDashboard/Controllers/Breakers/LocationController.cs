﻿//******************************************************************************************************
//  LocationController.cs - Gbtc
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
//  03/27/2020 - Billy Ernest
//       Generated original version of source code.
//
//******************************************************************************************************

using System;
using System.Collections.Generic;
using System.Data;
using System.Web.Http;
using GSF;
using GSF.Data;
using GSF.Data.Model;
using GSF.Identity;
using GSF.Security;
using GSF.Web.Model;
using openXDA.Model;
using PQDashboard.Model;

namespace PQDashboard.Controllers
{
    [RoutePrefix("api/Breakers/Location")]
    public class BreakersLocationController : LocationController<Breaker>
    {

        #region [ constructor ]
        public BreakersLocationController()
        {
            Query = @"
                        DECLARE @EventDateFrom DATETIME = {0}
                        DECLARE @EventDateTo DATETIME = {1}
                        DECLARE @meterIds AS varchar(max) = {2}
                        DECLARE @context as nvarchar(20) = {3}

                        DECLARE @startDate DATETIME = @EventDateFrom
                        DECLARE @endDate DATETIME = DATEADD(DAY, 1, CAST(@EventDateTo AS DATE))


                        IF @context = 'day'
                        BEGIN
                            SET @endDate = DATEADD(DAY, 1, @startDate)
                        END

                        if @context = 'hour'
                        BEGIN
                            SET @endDate = DATEADD(HOUR, 1, @startDate)
                        END

                        if @context = 'minute'
                        BEGIN
                            SET @endDate = DATEADD(MINUTE, 1, @startDate)
                        END

                        if @context = 'second'
                        BEGIN
                            SET @endDate = DATEADD(SECOND, 1, @startDate)
                        END

                        DECLARE @PivotColumns NVARCHAR(MAX) = N''
                        DECLARE @CountColumns NVARCHAR(MAX) = N''
                        DECLARE @ReturnColumns NVARCHAR(MAX) = N''
                        DECLARE @SQLStatement NVARCHAR(MAX) = N''

                        create table #TEMP (Name varchar(max))
                        insert into #TEMP SELECT Name FROM (Select Distinct Name FROM BreakerOperationType) as t

                        SELECT @PivotColumns = @PivotColumns + '[' + COALESCE(CAST(Name as varchar(max)), '') + '],'
                        FROM #TEMP ORDER BY Name desc

                        SELECT @CountColumns = @CountColumns + 'COALESCE([' + COALESCE(CAST(Name as varchar(20)), '') + '], 0) + '
                        FROM #TEMP ORDER BY Name desc

                        SELECT @ReturnColumns = @ReturnColumns + ' COALESCE([' + COALESCE(CAST(Name as varchar(max)), '') + '], 0) AS [' + COALESCE(CAST(Name as varchar(max)), '') + '],'
                        FROM #TEMP ORDER BY Name desc

                        DROP TABLE #TEMP


                        SET @SQLStatement =
                    '   SELECT *
                        INTO #selectedMeters
                        FROM String_To_Int_Table(@MeterIds, '','')

                        SELECT Meter.ID,
                                 Meter.Name,
                                 Location.Longitude,
                                 Location.Latitude,
                                 ' + SUBSTRING(@CountColumns,0, LEN(@CountColumns)) +' as Count,
                                 ' + SUBSTRING(@ReturnColumns,0, LEN(@ReturnColumns)) + '
                        FROM
                            Meter JOIN
                            Location ON Meter.LocationID = Location.ID LEFT OUTER JOIN
                            (
                                SELECT MeterID,
                                       COUNT(*) AS EventCount,
                                       BreakerOperationType.Name as OperationType
                                FROM BreakerOperation JOIN
                                     Event ON BreakerOperation.EventID = Event.ID JOIN
                                     EventType ON Event.EventTypeID = EventType.ID JOIN
                                     BreakerOperationType ON BreakerOperation.BreakerOperationTypeID = BreakerOperationType.ID
                                WHERE
                                     TripCoilEnergized >= @startDate AND TripCoilEnergized < @endDate
                                GROUP BY Event.MeterID, BreakerOperationType.Name
                               ) as ed
                               PIVOT(
                                     SUM(ed.EventCount)
                                     FOR ed.OperationType IN(' + SUBSTRING(@PivotColumns,0, LEN(@PivotColumns)) + ')
                               ) as pvt On pvt.MeterID = meter.ID
                        WHERE
                            Meter.ID IN (SELECT * FROM #selectedMeters)

                        ORDER BY Meter.Name'
                        exec sp_executesql @SQLStatement, N'@MeterIds nvarchar(MAX), @startDate DATETIME, @endDate DATETIME, @EventDateFrom DATETIME ', @MeterIds = @MeterIds, @startDate = @startDate, @endDate = @endDate, @EventDateFrom = @EventDateFrom
                ";
            Tab = "Breakers";
        }
        #endregion
    }
}