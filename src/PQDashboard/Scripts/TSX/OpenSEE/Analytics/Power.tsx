﻿//******************************************************************************************************
//  Power.tsx - Gbtc
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

import * as React from 'react';
import OpenSEEService from './../../../TS/Services/OpenSEE';
import LineChartAnalyticBase, { LineChartAnaltyicalBaseProps } from './../Graphs/LineChartAnalyticBase';

export default class Power extends React.Component<any, any>{
    openSEEService: OpenSEEService;
    props: LineChartAnaltyicalBaseProps
    constructor(props) {
        super(props);
        this.openSEEService = new OpenSEEService();
    }


    getColor(label, index) {
        if (label.ChartLabel.indexOf('AN') >= 0) return '#A30000';
        if (label.ChartLabel.indexOf('BN') >= 0) return '#0029A3';
        if (label.ChartLabel.indexOf('CN') >= 0) return '#007A29';
        if (label.ChartLabel.indexOf('Total') >= 0) return '#000000';

        else {
            var ranNumOne = Math.floor(Math.random() * 256).toString(16);
            var ranNumTwo = Math.floor(Math.random() * 256).toString(16);
            var ranNumThree = Math.floor(Math.random() * 256).toString(16);

            return `#${(ranNumOne.length > 1 ? ranNumOne : "0" + ranNumOne)}${(ranNumTwo.length > 1 ? ranNumTwo : "0" + ranNumTwo)}${(ranNumThree.length > 1 ? ranNumThree : "0" + ranNumThree)}`;
        }
    }
    render() {
        return <LineChartAnalyticBase
            legendDisplay={(key) => key.indexOf("Active") >= 0}
            legendEnable={(key) => key.indexOf("Active") >= 0}
            legendKey="Power"
            openSEEServiceFunction={this.openSEEService.getPowerData}

            getColor={this.getColor}
            endDate={this.props.endDate}
            eventId={this.props.eventId}
            height={this.props.height}
            hover={this.props.hover}
            pixels={this.props.pixels}
            pointsTable={this.props.pointsTable}
            postedData={this.props.postedData}
            startDate={this.props.startDate}
            stateSetter={this.props.stateSetter}
            tableData={this.props.tableData}
            tableSetter={this.props.tableSetter}
            tooltipWithDeltaTable={this.props.tooltipWithDeltaTable}
        />
    }


}