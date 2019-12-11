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

import * as React  from 'react';
import { clone, isEqual, each, findLast} from "lodash";
import { utc } from "moment";
import Legend, { iLegendData } from './../Graphs/Legend';
import { StandardAnalyticServiceFunction } from '../../../TS/Services/OpenSEE';

export type LegendClickCallback = (event?: React.MouseEvent<HTMLDivElement>, row?: iLegendData, key?: string, getData?: boolean) => void;
export type GetDataFunction = (props: LineChartAnaltyicalBaseProps, ctrl: LineChartAnalyticBase) => void;
export type GetColorFunction = (key: {ChartLabel: string}, index: number) => string;

export interface LineChartAnaltyicalBaseProps {
    eventId: number, startDate: string, endDate: string, pixels: number, stateSetter: Function, height: number, hover: number, tableData: Map<string, { data: number, color: string }>,
    pointsTable: any[], postedData: iPostedData, tableSetter: Function, fftStartTime?: string, fftEndTime?: string, analytic?: string, tooltipWithDeltaTable: Map<string, Map<string, { data: number, color: string }>>
};

interface LineChartAnalyticBassClassProps extends LineChartAnaltyicalBaseProps{
    legendKey: string, openSEEServiceFunction: StandardAnalyticServiceFunction, legendEnable: Function, legendDisplay: Function,
    getColor?: GetColorFunction, highlightCycle?: boolean, getData?: GetDataFunction, harmonic?: number, order?: number, Trc?: number

}

interface iDataSet {
    CalculationEnd: number, CalculationTime: number, Data: Array<iDataSeries>, EndDate: string, StartDate: string
}
interface iDataSeries {
    ChannelID: number, ChannelName: string, ChannelDescription: string, MeasurementType: string, MeasurementCharacteristic: string, Phase: string, SeriesType: string, ChartLabel: string, DataPoints: Array<[number, number]>
}


export default class LineChartAnalyticBase extends React.Component<LineChartAnalyticBassClassProps, any>{
    plot: any;
    options: object;
    clickHandled: boolean;
    panCenter: number;
    state: { legendRows: Map<string, iLegendData>, dataSet: iDataSet, dataHandle: JQuery.jqXHR, harmonic: number }
    constructor(props, context) {
        super(props, context);
        var ctrl = this;

        ctrl.state = {
            legendRows: new Map<string, iLegendData>(),
            dataSet: {
                CalculationEnd: null, CalculationTime: null, Data: null, EndDate: null, StartDate: null
            } , 
            dataHandle: undefined,
            harmonic: 1
        };
        ctrl.options = {
            canvas: true,
            legend: { show: false },
            crosshair: { mode: "x" },
            selection: { mode: "x" },
            grid: {
                autoHighlight: false,
                clickable: true,
                hoverable: true,
                markings: [],
            },
            xaxis: {
                mode: "time",
                tickLength: 10,
                reserveSpace: false,
                ticks: function (axis) {
                    var ticks = [],
                        delta = (axis.max - axis.min) / 11,
                        i = 0;

                    for (var i = 1; i < 11; ++i)
                    {
                        ticks.push(axis.min + i * delta);
                    }

                    return ticks;
                },
                tickFormatter: function (value, axis) {
                    if (axis.delta < 12) {
                        var trunc = value - ctrl.floorInBase(value, 1000);
                        return ctrl.defaultTickFormatter(trunc, axis) + " ms";
                    }

                    if (axis.delta < 1000) {
                        return utc(value).format("mm:ss.SS");
                    }
                    else {
                        return utc(value).format("HH:mm:ss.S");
                    }
                }
            },
            yaxis: {
                labelWidth: 50,
                panRange: false,
                //ticks: 1,
                tickLength: 10,
                tickFormatter: function (val, axis) {
                    if (axis.delta > 1000000 && (val > 1000000 || val < -1000000))
                        return ((val / 1000000) | 0) + "M";
                    else if (axis.delta > 1000 && (val > 1000 || val < -1000))
                        return ((val / 1000) | 0) + "K";
                    else
                        return val.toFixed(axis.tickDecimals);
                }
            }
        }

        ctrl.panCenter = null;
        ctrl.clickHandled = false;

        if (ctrl.props.getColor != undefined) ctrl.getColor = (label, i) => ctrl.props.getColor(label, i);
        if (ctrl.props.getData != undefined) ctrl.getData = (props) => ctrl.props.getData(props, ctrl);
        
    }

    componentDidMount() {
        this.getData(this.props);
    }

    componentWillUnmount() {
        if (this.state.dataHandle !== undefined && this.state.dataHandle.abort !== undefined) {
            this.state.dataHandle.abort();
            this.setState({ dataHandle: undefined });
        }

        var placeholder = $(this.refs.graphWindow);
        var overlay = $(this.refs.graphWindow).find(" .flot-overlay");
        placeholder.off("plotselected");
        placeholder.off("plothover");
        placeholder.off("plotclick");

        overlay.off(".plotZoom");
        overlay.off(".plotPan");
    }

    getColor(label, index) {
        if (label.ChartLabel.indexOf('VAN') >= 0) return '#A30000';
        if (label.ChartLabel.indexOf('VBN') >= 0) return '#0029A3';
        if (label.ChartLabel.indexOf('VCN') >= 0) return '#007A29';
        if (label.ChartLabel.indexOf('IAN') >= 0) return '#FF0000';
        if (label.ChartLabel.indexOf('IBN') >= 0) return '#0066CC';
        if (label.ChartLabel.indexOf('ICN') >= 0) return '#33CC33';
        if (label.ChartLabel.indexOf('VAB') >= 0) return '#A30000';
        if (label.ChartLabel.indexOf('VBC') >= 0) return '#0029A3';
        if (label.ChartLabel.indexOf('VCA') >= 0) return '#007A29';
        if (label.ChartLabel.indexOf('NG') >= 0) return '#d3d3d3';
        if (label.ChartLabel.indexOf('ING') >= 0) return '#ffd900';
        if (label.ChartLabel.indexOf('IRES') >= 0) return '#D3D3D3';    
        else {
            var ranNumOne = Math.floor(Math.random() * 256).toString(16);
            var ranNumTwo = Math.floor(Math.random() * 256).toString(16);
            var ranNumThree = Math.floor(Math.random() * 256).toString(16);

            return `#${(ranNumOne.length > 1 ? ranNumOne : "0" + ranNumOne)}${(ranNumTwo.length > 1 ? ranNumTwo : "0" + ranNumTwo)}${(ranNumThree.length > 1 ? ranNumThree : "0" + ranNumThree)}`;
        }
    }

    getData(props: LineChartAnaltyicalBaseProps) {
        var handle = this.props.openSEEServiceFunction(props.eventId, props.pixels, props.startDate, props.endDate).then((data: iDataSet) => {
            if (data == null) {
                return;
            }

            var hightlightFunction = this.props.highlightCycle == undefined || this.props.highlightCycle ? this.highlightCycle : this.highlightSample
            this.options['grid'].markings = [];

            var highlight = hightlightFunction(data);
            if(highlight != undefined )
                this.options['grid'].markings.push(highlight);


            var legend = this.createLegendRows(data.Data);

            var dataSet = this.state.dataSet;
            if (dataSet.Data != undefined)
                dataSet.Data = dataSet.Data.concat(data.Data);
            else
                dataSet = data;

            this.createDataRows(data, legend);
            this.setState({ dataSet: data });
        });
        this.setState({ dataHandle: handle });

    }

    componentWillReceiveProps(nextProps: LineChartAnaltyicalBaseProps) {
        var props = clone(this.props) as any;
        var nextPropsClone = clone(nextProps);

        delete props.hover;
        delete nextPropsClone.hover;
        delete props.stateSetter;
        delete nextPropsClone.stateSetter;
        delete props.tableSetter;
        delete nextPropsClone.tableSetter;
        delete props.pointsTable;
        delete nextPropsClone.pointsTable;
        delete props.tableData;
        delete nextPropsClone.tableData;
        delete props.postedData;
        delete nextPropsClone.postedData;
        delete props.legendDisplay;
        delete nextPropsClone.legendDisplay;
        delete props.openSEEServiceFunction;
        delete nextPropsClone.openSEEServiceFunction;
        delete props.legendEnable;
        delete nextPropsClone.legendEnable;
        delete props.getColor;
        delete nextPropsClone.getColor;
        delete props.getData;
        delete nextPropsClone.getData;
        delete props.analytic;
        delete nextPropsClone.analytic;

        if (nextProps.startDate && nextProps.endDate) {
            if (this.plot != null && (this.props.startDate != nextProps.startDate || this.props.endDate != nextProps.endDate)) {
                var xaxis = this.plot.getAxes().xaxis;
                var xmin = this.getMillisecondTime(nextProps.startDate);
                var xmax = this.getMillisecondTime(nextProps.endDate);
                xaxis.options.min = xmin;
                xaxis.options.max = xmax;
                this.plot.setupGrid();
                this.plot.draw();
            }
        }

        if (!(isEqual(props, nextPropsClone))) {
            this.getData(nextProps);
        }
        else if (this.props.hover != nextProps.hover) {
            if (this.plot)
                this.plot.setCrosshair({ x: nextProps.hover });

            //var table = new Map([...Array.from(this.props.tableData)]);
            var table = this.props.tableData;
            each(this.state.dataSet.Data, (data, i) => {
                var vector = findLast(data.DataPoints, (x) => x[0] <= nextProps.hover);
                if (vector)
                    table.set(data.ChartLabel, { data: vector[1], color: this.state.legendRows.get(data.ChartLabel).color });
            });

            this.props.tableSetter(table);
        }
    }

    createLegendRows(data) {
        var ctrl = this;

        var legend = (this.state.legendRows != undefined ? this.state.legendRows : new Map<string, iLegendData>());

        $.each(data, function (i, key) {
            var record = legend.get(key.ChartLabel);
            if (record == undefined)
                legend.set(key.ChartLabel, { color: ctrl.getColor(key, i), display: ctrl.props.legendDisplay(key.ChartLabel), enabled: ctrl.props.legendEnable(key.ChartLabel), data: key.DataPoints, markerdata: (key.DataMarker ? key.DataMarker : []) });
            else {
                legend.get(key.ChartLabel).data = key.DataPoints;
                legend.get(key.ChartLabel).markerdata = (key.DataMarker ? key.DataMarker : []);
            }

        });

        legend = new Map(Array.from(legend).sort((a, b) => {
            return natural_compare(a[0], b[0]);
        }));
        this.setState({ legendRows: legend });
        return legend;

        function pad(n) { return ("00000000" + n).substr(-8); }
        function natural_expand(a) { return a.replace(/\d+/g, pad) };
        function natural_compare(a, b) {
            return natural_expand(a).localeCompare(natural_expand(b));
        }

    }

    createDataRows(data, legend) {
        // if start and end date are not provided calculate them from the data set
        var ctrl = this;
        var startString = this.props.startDate;
        var endString = this.props.endDate;

        var newVessel = [];

        legend.forEach((row, key, map) => {
            if (row.enabled) {
                newVessel.push({ label: key, data: row.data, color: row.color })
                if (row.markerdata.length > 0) {
                    let visiblemarker = [];
                    let mx = Math.max(...row.data.map(item=>item[0]));
                    let mn = Math.min(...row.data.map(item => item[0]));
                   
                    row.markerdata.forEach(pt => {
                        if ((pt[0] > mn) && (pt[0] < mx)) {
                            visiblemarker.push(pt);
                        }
                            
                    });

                    if (visiblemarker.length > 0) {
                        newVessel.push({
                            label: key, data: visiblemarker, color: row.color, lines: { show: false }, points: {
                                show: true,
                                symbol: "circle",
                                radius: 4,
                                fill: true,
                                fillColor: row.color
                            }
                        })
                    }
                }
            }
                
        });

        newVessel.push([[this.getMillisecondTime(startString), null], [this.getMillisecondTime(endString), null]]);

        if ($('#tooltipwithdelta').css('display') != 'none') {
            this.props.tooltipWithDeltaTable.forEach((value, key, map) => {
                var marking = this.highlightCycleForDelta(this.getMillisecondTime(key));

                if(marking != undefined)
                    this.options['grid'].markings.push(marking);
            });
            
        }

        
        var options = clone(this.options);
        options.yaxis.axisLabel = this.getPlotUnits(this.props.legendKey, legend);
        options.axisLabels = { show: true };

        this.plot = $.plot($(ctrl.refs.graphWindow), newVessel, options);
        this.plot.lockCrosshair();
        this.plotSelected();
        this.plotZoom();
        this.plotPan();
        this.plotHover();
        this.plotClick();
    }

    plotZoom() {
        var ctrl = this;
        var overlay = $(ctrl.refs.graphWindow).find(".flot-overlay");

        overlay.off("mousewheel.plotZoom");
        overlay.bind("mousewheel.plotZoom", function (event) {
            var minDelta = null;
            var maxDelta = 5;
            var xaxis = ctrl.plot.getAxes().xaxis;
            var xcenter = ctrl.props.hover;
            var startDate = ctrl.props.startDate;
            var endDate = ctrl.props.endDate;

            var xmin;
            var xmax;
            var deltaMagnitude;
            var delta;
            var factor;

            if (startDate == null || endDate == null) {
                xmin = xaxis.min || xaxis.datamin;
                xmax = xaxis.max || xaxis.datamax;
            } else {
                xmin = ctrl.getMillisecondTime(startDate);
                xmax = ctrl.getMillisecondTime(endDate);
            }

            xcenter = Math.max(xcenter, xmin);
            xcenter = Math.min(xcenter, xmax);

            if ((event.originalEvent as any).wheelDelta != undefined)
                delta = (event.originalEvent as any).wheelDelta;
            else
                delta = -(event.originalEvent as any).detail;

            deltaMagnitude = Math.abs(delta);

            if (minDelta == null || deltaMagnitude < minDelta)
                minDelta = deltaMagnitude;

            deltaMagnitude /= minDelta;
            deltaMagnitude = Math.min(deltaMagnitude, maxDelta);
            factor = deltaMagnitude / 10;

            if (delta > 0) {
                xmin = xmin * (1 - factor) + xcenter * factor;
                xmax = xmax * (1 - factor) + xcenter * factor;
            } else {
                xmin = (xmin - xcenter * factor) / (1 - factor);
                xmax = (xmax - xcenter * factor) / (1 - factor);
            }

            if (xmin == xaxis.options.xmin && xmax == xaxis.options.xmax)
                return;

            ctrl.props.stateSetter({ StartDate: ctrl.getDateString(xmin), EndDate: ctrl.getDateString(xmax) });
        });
    }

    plotPan() {
        var ctrl = this;
        var overlay = $(ctrl.refs.graphWindow).find(".flot-overlay");

        overlay.off("mousedown.plotPan");
        overlay.on("mousedown.plotPan", function (e) {
            if (e.which !== 1) {
                ctrl.clickHandled = true;
                return;
            }

            if (e.shiftKey) {
                ctrl.panCenter = e.pageX;
                ctrl.plot.suspendSelection();

                $(document).one("mouseup", function (e) {
                    if (e.which === 1 && ctrl.panCenter !== null) {
                        ctrl.panCenter = null;
                        ctrl.plot.resumeSelection();
                    }
                });
            }

            ctrl.clickHandled = false;
        });

        overlay.off("mousemove.plotPan");
        overlay.on("mousemove.plotPan", function (e) {
            if (ctrl.panCenter !== null) {
                var panDistance = ctrl.panCenter - e.pageX;

                var xaxis = ctrl.plot.getAxes().xaxis;
                var xaxisDistance = panDistance / xaxis.scale;
                var xmin = xaxis.min || xaxis.datamin;
                var xmax = xaxis.max || xaxis.datamax;

                var startDate = ctrl.props.startDate;
                var endDate = ctrl.props.endDate;

                if (startDate != null && endDate != null) {
                    xmin = ctrl.getMillisecondTime(startDate);
                    xmax = ctrl.getMillisecondTime(endDate);
                }

                xmin += xaxisDistance;
                xmax += xaxisDistance;
                ctrl.props.stateSetter({ StartDate: ctrl.getDateString(xmin), EndDate: ctrl.getDateString(xmax) });

                ctrl.panCenter -= panDistance;
                ctrl.clickHandled = true;
            }
        });
    }

    plotSelected() {
        var ctrl = this;
        $(ctrl.refs.graphWindow).off("plotselected");
        $(ctrl.refs.graphWindow).bind("plotselected", function (event, ranges) {
            ctrl.clickHandled = true;
            ctrl.props.stateSetter({ StartDate: ctrl.getDateString(ranges.xaxis.from), EndDate: ctrl.getDateString(ranges.xaxis.to) });
        });
    }

    plotHover() {
        var ctrl = this;
        $(ctrl.refs.graphWindow).off("plothover");
        $(ctrl.refs.graphWindow).bind("plothover", function (event, pos, item) {
            var data = ctrl.plot.getData();
            if (data.length > 0 && data[0].data != undefined) {
                var point = ctrl.plot.getData()[0].data.map(x => x[0]).sort((a, b) => Math.abs(a - pos.x) - Math.abs(b - pos.x))[0];
                
                ctrl.props.stateSetter({ Hover: point });

            }
        });
    }

    plotClick() {
        var ctrl = this;
        $(ctrl.refs.graphWindow).off("plotclick");
        $(ctrl.refs.graphWindow).bind("plotclick", function (event, pos, item) {
            var data = ctrl.plot.getData();

            if (data.length == 0 || data[0].data == undefined) return;
            var point = ctrl.plot.getData()[0].data.map(x => x[0]).sort((a, b) => Math.abs(a - pos.x) - Math.abs(b - pos.x))[0];
            var timeString = ctrl.getDateString(point);

            var time;
            var deltatime;
            var deltavalue;

            if (ctrl.clickHandled || !item) {
                ctrl.clickHandled = false;
                return;

            }

            if ($('#accumulatedpoints').css('display') != "none") {
                var pointsTable = clone(ctrl.props.pointsTable);

                time = (item.datapoint[0] - Number(ctrl.props.postedData.postedEventMilliseconds)) / 1000.0;
                deltatime = 0.0;
                deltavalue = 0.0;

                if (pointsTable.length > 0) {
                    deltatime = time - pointsTable[pointsTable.length - 1].thetime;
                    deltavalue = item.datapoint[1] - pointsTable[pointsTable.length - 1].thevalue;
                }

                pointsTable.push({
                    theseries: item.series.label,
                    thetime: time,
                    thevalue: item.datapoint[1].toFixed(3),
                    deltatime: deltatime,
                    deltavalue: deltavalue.toFixed(3),
                    arrayIndex: ctrl.props.pointsTable.length
                });
                ctrl.props.stateSetter({ PointsTable: pointsTable });


            }
            if ($('#tooltipwithdelta').css('display') != "none") {
                var map = new Map(ctrl.props.tooltipWithDeltaTable);
                if (map.size > 1)
                    map.clear();
                map.set(timeString, ctrl.props.tableData);

                ctrl.props.stateSetter({ TooltipWithDeltaTable: map });

            }
        });
    }

    defaultTickFormatter(value, axis) {

        var factor = axis.tickDecimals ? Math.pow(10, axis.tickDecimals) : 1;
        var formatted = "" + Math.round(value * factor) / factor;

        // If tickDecimals was specified, ensure that we have exactly that
        // much precision; otherwise default to the value's own precision.

        if (axis.tickDecimals != null) {
            var decimal = formatted.indexOf(".");
            var precision = decimal == -1 ? 0 : formatted.length - decimal - 1;
            if (precision < axis.tickDecimals) {
                return (precision ? formatted : formatted + ".") + ("" + factor).substr(1, axis.tickDecimals - precision);
            }
        }

        return formatted;
    };
    // round to nearby lower multiple of base
    floorInBase(n, base) {
        return base * Math.floor(n / base);
    }

    handleSeriesLegendClick(event: React.MouseEvent<HTMLDivElement>, row: iLegendData, key: string, getData?: boolean): void {
        if(row != undefined)
            row.enabled = !row.enabled;

        this.setState({ legendRows: this.state.legendRows });       

        this.createDataRows(this.state.dataSet, this.state.legendRows);

        if (getData == true)
            this.getData(this.props);
    }


    render() {
        return (
            <div>
                <div ref="graphWindow" style={{ height: this.props.height, float: 'left', width: 'calc(100% - 220px)'/*this.props.pixels - 222 margn: '0x', padding: '0px'*/}}></div>
                <Legend data={this.state.legendRows} callback={this.handleSeriesLegendClick.bind(this)} type={this.props.legendKey} height={this.props.height} harmonicSetter={(harmonic: any) => this.setState({ harmonic: harmonic }, () => this.getData(this.props))} harmonic={this.props.harmonic}/>
            </div>
        );
    }

    getMillisecondTime(date) {
        var milliseconds = utc(date).valueOf();
        var millisecondsFractionFloat = parseFloat((date.toString().indexOf('.') >= 0 ? '.' + date.toString().split('.')[1] : '0')) * 1000;

        return milliseconds + millisecondsFractionFloat - Math.floor(millisecondsFractionFloat);
    }


    highlightSample(series) {
        if (series.CalculationTime > 0)
            return {
                color: "#EB0",
                xaxis: {
                    from: series.CalculationTime,
                    to: series.CalculationTime
                }
            };
    }

    highlightCycle(series) {
        if (series != null && series.CalculationTime > 0 && series.CalculationEnd > 0)
            return {
                color: "#FFA",
                xaxis: {
                    from: series.CalculationTime,
                    to: series.CalculationEnd
                }
            };
    }

    highlightCycleForDelta(time) {
        return {
            color: "#0062cc",
            xaxis: {
                from: time,
                to: time
            }
        };
    }

    highlightFFTCycle() {
        return {
            color: "#ADD8E6",
            xaxis: {
                from: this.props.fftStartTime,
                to: this.props.fftEndTime
            }
        };

    }

    getDateString(float) {
        var date = utc(float).format('YYYY-MM-DDTHH:mm:ss.SSS');
        var millisecondFraction = parseInt((float.toString().indexOf('.') >= 0 ? float.toString().split('.')[1] : '0'))

        return date + millisecondFraction.toString();
    }

    getPlotUnits(type: string, data: any) {
        const distinct = (value, index, self) => {
            return self.indexOf(value) === index;
        }

        var labels = [];

        data.forEach((row, key, map) => {
            if (row.enabled) {

                //Generate Voltage Plot Legend
                if (type == "Voltage") {
                    if (key.indexOf('Phase') >= 0) {
                        labels.push("deg");
                    }
                    else {
                        labels.push("Volt");
                    }

                }
                else if (type == "Current") {
                    if (key.indexOf('Phase') >= 0) {
                        labels.push("deg");
                    }
                    else {
                        labels.push("Amps");
                    }
                }
                else if (type.toLowerCase() == "power") {
                    if (key.indexOf('Active') >= 0) {
                        labels.push("W");
                    }
                    else if (key.indexOf('Reactive') >= 0) {
                        labels.push("VAR");
                    }
                    else if (key.indexOf('Apparent') >= 0) {
                        labels.push("VA");
                    }
                }
                else if (type.toLowerCase() == "impedance") {
                    if (key.indexOf('Resistance') >= 0) {
                        labels.push("Ohm (R)");
                    }
                    else if (key.indexOf('Reactance') >= 0) {
                        labels.push("Ohm (X)");
                    }
                    else if (key.indexOf('Impedance') >= 0) {
                        labels.push("Ohm (Z)");
                    }
                }
                else if (type.toLowerCase() == "rapidvoltagechange") {
                    labels.push("Volt");
                }
                else if (type.toLowerCase() == "firstderivative") {
                    if (key.indexOf('V') >= 0) {
                        labels.push("Volt/sec");
                    }
                    else {
                        labels.push("Amps/sec");
                    }

                }
                else if (type.toLowerCase() == "lowpassfilter") {
                    if (key.indexOf('V') >= 0) {
                        labels.push("Volt");
                    }
                    else {
                        labels.push("Amps");
                    }
                }
                else if (type.toLowerCase() == "highpassfilter") {
                    if (key.indexOf('V') >= 0) {
                        labels.push("Volt");
                    }
                    else {
                        labels.push("Amps");
                    }
                }
                else if (type.toLowerCase() == "symmetricalcomponents") {
                    // This is an issue and needs some additonal attention
                }
                else if (type.toLowerCase() == "rectifier") {
                    if (key.indexOf('Voltage') >= 0) {
                        labels.push("Volt");
                    }
                    else {
                        labels.push("Amps");
                    }
                }
                else if (type.toLowerCase() == "unbalance") {
                    labels.push("Percent");
                }
                else if (type.toLowerCase() == "clippedwaveforms") {
                    if (key.indexOf('V') >= 0) {
                        labels.push("Volt");
                    }
                    else {
                        labels.push("Amps");
                    }
                }
                else if (type.toLowerCase() == "thd") {
                    labels.push("Percent");
                }
                else if (type.toLowerCase() == "overlappingwaveform") {
                    if (key.indexOf('V') >= 0) {
                        labels.push("Volt");
                    }
                    else {
                        labels.push("Amps");
                    }
                }
                else if (type.toLowerCase() == "removecurrent") {
                    labels.push("Amps");
                }
                else if (type.toLowerCase() == "missingvoltage") {
                    labels.push("Volt");
                }
                else if (type.toLowerCase() == "harmonicspectrum") {

                }
                else if (type.toLowerCase() == "fft") {

                }
                else if (type.toLowerCase() == "frequency") {
                    labels.push("Hz");
                }
                else if (type.toLowerCase() == "digital") {
                    labels.push("Digital");
                }
                else if (type.toLowerCase() == "trip coil current") {
                    labels.push("Amps");
                }
                else if (type.toLowerCase() == "faultlocation") {
                    labels.push("miles");
                }
            }
        });       
        labels = labels.filter(distinct);
        return labels.join('/');
    }
}