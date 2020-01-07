﻿//******************************************************************************************************
//  D3Legend.tsx - Gbtc
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
//  01/06/2020 - C Lackner
//       Generated original version of source code.
//
//******************************************************************************************************
import * as React from 'react';
import * as _ from 'lodash';
import { LegendClickCallback } from './D3LineChartBase';

export interface iD3LegendData {
    color: string,
    display: boolean,
    enabled: boolean,
    channelID: number,
    chartLabel: string,
    legendClass: string,
    legendGroup: string,
    secondaryLegendClass: string
}

export interface iD3LegendProps {
    type: string,
    data: Map<string, iD3LegendData>,
    callback: LegendClickCallback,
    height: number,
}

declare var samplesPerCycle: number;
declare var cycles: number;

export default class Legend extends React.Component<any, any>{
    props: iD3LegendProps;
    samplesPerCycleOptions: any[];

    constructor(props) {
        super(props);
    }

    componentWillReceiveProps(nextProps: iD3LegendProps) {

    }

    render() {
        if (this.props.data == null || this.props.data.size == 0) return null;

        let rows: Array<JSX.Element> = [];
        this.props.data.forEach((row, key, map) => {
            if (row.display)
                rows.push(<Row key={key} label={key} color={row.color} enabled={row.enabled} callback={(e) => {

                    if (row.enabled) {
                        var legend = $(this.refs.legend);

                        $(legend.find('label').toArray().find(x => $(x).text() === row.secondaryLegendClass)).removeClass('active');

                    }
                    this.props.callback(e, row, key)
                }} />)
        });

        let secondaryHeader: Array<string> = Array.from(new Set(Array.from(this.props.data.values()).map(item => item.secondaryLegendClass)));
        let primaryHeader: Array<string> = Array.from(new Set(Array.from(this.props.data.values()).map(item => item.legendClass)));

        let secondaryBtns: Array<any> = [];
        let primaryBtns: Array<any> = [];

        let TableHeight: number = this.props.height - 38 - (secondaryHeader.length > 1 ? 35 : 0) - (primaryHeader.length > 1 ? 35 : 0);

        primaryHeader.forEach(item => primaryBtns.push({ label: item, value: item, active: true}));
        secondaryHeader.forEach(item => secondaryBtns.push({ label: item, value: item, active: true}));

        return (
            <div ref="legend" id={this.props.type + '-legend'} className='legend' style={{ float: 'right', width: '200px', height: this.props.height - 38, marginTop: '6px', borderStyle: 'solid', borderWidth: '2px', overflowY: 'hidden' }}>
                {(primaryHeader.length > 1 ?
                    <ToggleButtonGroup type="radio" defaultValue={primaryHeader[0]} buttons={primaryBtns} onChange={this.toggleAll.bind(this)} />
                    : null)}

                {(secondaryHeader.length > 1 ?
                    <ToggleButtonGroup type="radio" defaultValue={secondaryHeader[0]} buttons={secondaryBtns} onChange={this.toggleAll.bind(this)} />
                    : null)}

                <table ref="table" style={{ maxHeight: TableHeight, overflowY: 'auto', display: 'block' }}>
                    <tbody >
                        {rows}
                    </tbody>
                </table>                
            </div>
        );
    }

   
    toggleAll(active: Array<string>, value: string, type: string) {
        this.props.data.forEach((row, key, map) => {
            var enabled = row.enabled && row.secondaryLegendClass != value;

            //If type is Radio we hide all that are not in this one
            if (type == "radio") {
                row.display = row.legendClass == value;
                enabled = false;
            }
            
            if (type == "radio") {
                if (row.display && $(this.refs.legend).find('label.active').toArray().some(x => $(x).text() === row.legendClass)) {
                    enabled = true;
                }
            }
            else {
                if (row.display && $(this.refs.legend).find('label.active').toArray().some(x => $(x).text() === row.secondaryLegendClass)) {
                    enabled = true;
                }
            }

            row.enabled = enabled;
            $('[name="' + key + '"]').prop('checked', row.enabled);

        });

        this.props.callback();

    }

}

const Row = (props: {label: string, enabled: boolean, color: string, callback: LegendClickCallback}) => {
    return (
        <tr>
            <td>
                <input name={props.label} className='legendCheckbox' type="checkbox" style={{ display: 'none' }} defaultChecked={props.enabled}/>
            </td>
            <td>
                <div style={{ border: '1px solid #ccc', padding: '1px' }}>
                    <div style={{ width: ' 4px', height: 0, border: '5px solid', borderColor: (props.enabled ? convertHex(props.color, 100) : convertHex(props.color, 50)), overflow: 'hidden' }} onClick={props.callback}>
                    </div>
                </div>
            </td>
            <td>
                <span style={{color: props.color, fontSize: 'smaller', fontWeight: 'bold', whiteSpace: 'nowrap'}}>{props.label}</span>
            </td>
        </tr>
    );
}

function convertHex(hex, opacity) {
    hex = hex.replace('#', '');
    var r = parseInt(hex.substring(0, 2), 16);
    var g = parseInt(hex.substring(2, 4), 16);
    var b = parseInt(hex.substring(4, 6), 16);

    var result = 'rgba(' + r + ',' + g + ',' + b + ',' + opacity / 100 + ')';
    return result;
}

class ToggleButtonGroup extends React.Component {
    props: { type: "radio" | "checkbox", buttons: { label: string, value: string, active: boolean }[], onChange: (active: Array<string>, value: string, type: string) => void, defaultValue: string }
    state: { buttons: { label: string, value: string, active: boolean }[]}
    constructor(props, context) {
        super(props, context);

        this.state = {
            buttons: this.props.buttons
        }

    }

    handleToggle(value: string): void {
        if (this.props.type == "checkbox") {
            var buttons = JSON.parse(JSON.stringify(this.state.buttons)) as { label: string, value: string, active: boolean }[];
            var button = buttons.find(x => x.value == value);
            button.active = !button.active;
            this.setState({buttons: buttons}, () => this.props.onChange(this.state.buttons.filter(x=> x.active).map(x=> x.value), value, this.props.type));
        }
        else {
            var buttons = JSON.parse(JSON.stringify(this.state.buttons)) as { label: string, value: string, active: boolean }[];
            buttons.forEach(x => x.active = false);
            var button = buttons.find(x => x.value == value);
            button.active = true;
            this.setState({ buttons: buttons }, () => this.props.onChange(this.state.buttons.filter(x => x.active).map(x => x.value), value, this.props.type));
        }
    }

    render() {
        let rows = this.state.buttons.map(x => <ToggleButton key={x.value} active={x.active} value={x.value} style={{ width: 100 / this.props.buttons.length + '%', height: 35 }} label={x.label} onChange={(value) => this.handleToggle(value)}/>);
        return (
            <div className="btn-group btn-group-toggle" style={{ width: '100%' }}>{rows}</div>
        );
    }

}

class ToggleButton extends React.Component {
    props: { active: boolean, value: string, style: React.CSSProperties, label: string, onChange: (value: string) => void}
    constructor(props, context) {
        super(props, context);

    }

    render() {
        return <label className={"btn btn-primary" + (this.props.active ? ' active' : '')} style={this.props.style}><input className="toggleButton" type="checkbox" name="checkbox" value={this.props.value}  onChange={(e: React.ChangeEvent<HTMLInputElement>) => this.props.onChange(this.props.value)} />{this.props.label}</label>;
    }
}
