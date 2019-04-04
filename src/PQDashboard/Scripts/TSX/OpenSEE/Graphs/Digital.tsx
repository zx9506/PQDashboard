﻿//******************************************************************************************************
//  Power.ts - Gbtc
//
//  Copyright © 2019, Grid Protection Alliance.  All Rights Reserved.
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
//  03/18/2019 - Billy Ernest
//       Generated original version of source code.
//
//******************************************************************************************************

import { createElement } from 'react';
import OpenSEEService from './../../../TS/Services/OpenSEE';
import LineChartAnalyticBase, { LineChartAnaltyicalBaseProps } from './../Graphs/LineChartAnalyticBase';

export default function Digital(props: LineChartAnaltyicalBaseProps): JSX.Element {
    function getColor(key, index) {
        if (index == 0) return '#edc240';
        else if (index == 1) return '#afd8f8';
        else if (index == 2) return '#cb4b4b';
        else if (index == 3) return '#4da74d';
        else if (index == 4) return '#9440ed';
        else if (index == 5) return '#bd9b33';
        else if (index == 6) return '#3498db';
        else if (index == 7) return '#1d5987';
        else {
            var ranNumOne = Math.floor(Math.random() * 256).toString(16);
            var ranNumTwo = Math.floor(Math.random() * 256).toString(16);
            var ranNumThree = Math.floor(Math.random() * 256).toString(16);

            return `#${(ranNumOne.length > 1 ? ranNumOne : "0" + ranNumOne)}${(ranNumTwo.length > 1 ? ranNumTwo : "0" + ranNumTwo)}${(ranNumThree.length > 1 ? ranNumThree : "0" + ranNumThree)}`;
        }
    }

    var openSEEService = new OpenSEEService();
    return createElement(LineChartAnalyticBase, {
        legendDisplay: (key) => true,
        legendEnable: (key) => true,
        legendKey: "Digital",
        openSEEServiceFunction: (eventid, pixels, startDate, endDate) => openSEEService.getDigitalsData(eventid, pixels, startDate, endDate),
        endDate: props.endDate,
        eventId: props.eventId,
        getColor: (key,index) => getColor(key,index),
        height: props.height,
        hover: props.hover,
        pixels: props.pixels,
        pointsTable: props.pointsTable,
        postedData: props.postedData,
        startDate: props.startDate,
        stateSetter: props.stateSetter,
        tableData: props.tableData,
        tableSetter: props.tableSetter,
        tooltipWithDeltaTable: props.tooltipWithDeltaTable,
    }, null);

}