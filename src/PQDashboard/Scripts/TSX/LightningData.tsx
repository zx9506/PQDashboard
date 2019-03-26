﻿//******************************************************************************************************
//  LightningData.tsx - Gbtc
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
//  03/13/2019 - Stephen C. Wills
//       Generated original version of source code.
//
//******************************************************************************************************

import * as React from 'react';
import * as _ from "lodash";
import * as moment from 'moment';
import './../jquery-ui.js';
import OpenSEEService from './../TS/Services/OpenSEE';

declare var window: any

export default class LightningData extends React.Component<any, any>{
    props: { eventId: number, callback: Function }
    state: { rows: Array<Object>, header: Array<Object> }
    openSEEService: OpenSEEService;

    constructor(props) {
        super(props);
        this.openSEEService = new OpenSEEService();

        this.state = {
            rows: [],
            header: []
        };
    }

    componentDidMount() {
        ($("#lightningquery") as any).draggable({ scroll: false, handle: '#lightninghandle' });

        var lightningQuery = window.LightningQuery;

        if (lightningQuery === undefined)
            return;

        this.openSEEService.getLightningParameters(this.props.eventId).done(lightningParameters => {
            var lineKey = lightningParameters.LineKey;
            var startTime = moment.utc(lightningParameters.StartTime).toDate();
            var endTime = moment.utc(lightningParameters.EndTime).toDate();

            lightningQuery.queryLineGeometry(lineKey, lineGeometry => {
                lightningQuery.queryLineBufferGeometry(lineGeometry, lineBufferGeometry => {
                    lightningQuery.queryLightningData(lineBufferGeometry, startTime, endTime, lightningData => {
                        var header = null;

                        if (lightningData.length === 0)
                            header = HeaderRow({ "No Data": "" });
                        else
                            header = HeaderRow(lightningData[0]);

                        var rows = lightningData.map(row => Row(row));
                        this.setState({ header: header, rows: rows });
                        this.props.callback({ enableLightningData: true });
                    });
                });
            });
        });
    }

    render() {
        return (
            <div id="lightningquery" className="ui-widget-content" style={{ position: 'absolute', top: '0', display: 'none'}}>
                <div id="lightninghandle"></div>
                <div id="lightningcontent" style={{ maxWidth: 800 }}>
                    <table className="table" style={{fontSize: 'small', marginBottom: 0}}>
                        <thead style={{ display: 'table', tableLayout: 'fixed', width: 'calc(100% - 1em)'}}>
                            {this.state.header}
                        </thead>
                        <tbody style={{ maxHeight: 500, overflowY: 'auto', display: 'block'}}>
                            {this.state.rows}
                        </tbody>
                    </table>
                </div>
                <button className="CloseButton" onClick={() => {
                    this.props.callback({ lightningDataButtonText: "Show Lightning Data" });
                    $('#lightningquery').hide();
                }}>X</button>
            </div>
        );
    }
}

const Row = (row) => {
    return (
        <tr style={{ display: 'table', tableLayout: 'fixed', width: '100%' }} key={row.label}>
            {Object.keys(row).map(key => <td>{row[key]}</td>)}
        </tr>
    );
}

const HeaderRow = (row) => {
    return (
        <tr key='Header'>
            {Object.keys(row).map(key => <th key={key}>{key}</th>)}
        </tr>
    );
}



