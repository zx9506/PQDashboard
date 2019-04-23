﻿//******************************************************************************************************
//  HarmonicSpectrum.tsx - Gbtc
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
import { each } from 'lodash';
import OpenSEEService from './../../../TS/Services/OpenSEE';
import BarChartAnalyticBase, { BarChartAnaltyicalBaseProps } from './../Graphs/BarChartAnalyticBase';
import { iLegendData } from '../Graphs/Legend';

export default class HarmonicSpectrum extends React.Component<any, any>{
    openSEEService: OpenSEEService;
    props: BarChartAnaltyicalBaseProps
    constructor(props) {
        super(props);
        this.openSEEService = new OpenSEEService();

        this.getData = this.getData.bind(this);

    }

    componentWillUnmount() {
        this.props.stateSetter({fftStartTime: undefined, fftEndTime: undefined});
    }

    createLegendRows(data, ctrl) {
        var legend = ctrl.state.legendRows as Map<string, iLegendData>;

        $.each(data, function (i, key) {
            var record = legend.get(key.ChartLabel);
            if (record == undefined)
                legend.set(key.ChartLabel, { color: ctrl.getColor(key, i), display: ctrl.props.legendDisplay(key.ChartLabel), enabled: ctrl.props.legendEnable(key.ChartLabel), data: key.DataPoints, harmonic: 10 });
            else
                legend.get(key.ChartLabel).data = key.DataPoints
        });

        legend = new Map(Array.from(legend).sort((a, b) => {
            return natural_compare(a[0], b[0]);
        }));
        ctrl.setState({ legendRows: legend });
        return legend;

        function pad(n) { return ("00000000" + n).substr(-8); }
        function natural_expand(a) { return a.replace(/\d+/g, pad) };
        function natural_compare(a, b) {
            return natural_expand(a).localeCompare(natural_expand(b));
        }

    }


    getData(props, ctrl: BarChartAnalyticBase) {
        var legendRow = ctrl.state.legendRows.entries().next().value;
        var harmonic = 10;
        if (legendRow != undefined)
            harmonic = legendRow[1].harmonic;

        var handle = this.openSEEService.getHarmonicSpectrumData(props.eventId, harmonic, props.fftStartTime).then(data => {
            if (data == null) {
                return;
            }

            this.props.stateSetter({ fftStartTime: data.CalculationTime, fftEndTime: data.CalculationEnd });


            each(data.Data, (record) => {
                var theData = {};

                each(Object.keys(record.DataPoints).map(a => parseInt(a)), (key, index) => {
                    var newKey = key / 60;
                    theData[newKey] = record.DataPoints[key];
                });

                record.DataPoints = theData;

            });

            var legend = this.createLegendRows(data.Data, ctrl);

            ctrl.createDataRows(data, legend);
            ctrl.setState({ dataSet: data });
        });
        ctrl.setState({ dataHandle: handle });


    }


    render() {
        return <BarChartAnalyticBase
            openSEEServiceFunction={(eventID, startDate, endDate) => this.openSEEService.getHarmonicSpectrumData(eventID, 1, startDate, endDate)}
            legendEnable={(key) => key.indexOf("VAN") == 0 && key.indexOf("Mag") >= 0}
            legendDisplay={(key) => key.indexOf("V") == 0 && key.indexOf("Mag") >= 0}
            legendKey="HarmonicSpectrum"
            fftStartTime={this.props.fftStartTime}
            fftEndTime={this.props.fftEndTime}
            getData={this.getData}

            eventId={this.props.eventId}
            height={this.props.height}
            pixels={this.props.pixels}
            pointsTable={this.props.pointsTable}
            postedData={this.props.postedData}
            stateSetter={this.props.stateSetter}
            tableData={this.props.tableData}
            tableSetter={this.props.tableSetter}

        />
    }

}