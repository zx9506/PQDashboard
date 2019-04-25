﻿//******************************************************************************************************
//  MeterActivity.tsx - Gbtc
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
//  04/08/2019 - Billy Ernest
//       Generated original version of source code.
//
//******************************************************************************************************

import * as React from 'react';
import * as moment from 'moment';
import { clone, isEqual } from 'lodash';

import createHistory from "history/createBrowserHistory"
import * as queryString from "query-string";
import { History } from 'history';
import EventSearchList from './EventSearchList';
import EventSearchNavbar, { EventSearchNavbarProps } from './EventSearchNavbar';
import EventPreviewPane from './EventSearchPreviewPane';

const momentDateTimeFormat = "MM/DD/YYYY HH:mm:ss.SSS";
const momentDateFormat = "MM/DD/YYYY";
const momentTimeFormat = "HH:mm:ss.SSS";

interface IProps { }
interface IState {
    searchBarProps: EventSearchNavbarProps,
    eventid: number
}

export default class EventSearch extends React.Component<IProps, IState>{
    history: History<any>;
    historyHandle: any;

    constructor(props, context) {
        super(props, context);

        this.history = createHistory();
        var query = queryString.parse(this.history['location'].search);

        this.state = {
            searchBarProps: {
                dfr: (query['dfr'] != undefined ? query['dfr'] == 'true' : true),
                pqMeter: (query['pqMeter'] != undefined ? query['pqMeter'] == 'true': true),
                g500: (query['g500'] != undefined ? query['g500'] == 'true' : true),
                one62to500: (query['one62to500'] != undefined ? query['one62to500'] == 'true' : true),
                seventyTo161: (query['seventyTo161'] != undefined ? query['seventyTo161'] == 'true' : true),
                l70: (query['l70'] != undefined ? query['l70'] == 'true': true),
                faults: (query['faults'] != undefined ? query['faults'] == 'true' : true),
                sags: (query['sags'] != undefined ? query['sags'] == 'true' : true),
                swells: (query['swells'] != undefined ? query['swells'] == 'true' : true),
                interruptions: (query['interruptions'] != undefined ? query['interruptions'] == 'true' : true),
                breakerOps: (query['breakerOps'] != undefined ? query['breakerOps'] == 'true' : true),
                transients: (query['transients'] != undefined ? query['transients'] == 'true' : true),
                others: (query['others'] != undefined ? query['others'] == 'true' : true),
                date: (query['date'] != undefined ? query['date'] : moment.utc().format(momentDateFormat)),
                time: (query['time'] != undefined ? query['time'] : moment.utc().format(momentTimeFormat)),
                windowSize: (query['windowSize'] != undefined ? query['windowSize'] : 10),
                timeWindowUnits: (query['timeWindowUnits'] != undefined ? query['timeWindowUnits'] : 2),
                stateSetter: this.stateSetter.bind(this)

            },
            eventid: (query['eventid'] != undefined ? query['eventid'] : -1),

        };
    }

    componentDidMount() {
    }

    componentWillUnmount() {
    }

    componentWillReceiveProps(nextProps: IProps) {
    }

    render() {
        return (
            <div style={{ width: '100%', height: '100%' }}>
                <EventSearchNavbar {...this.state.searchBarProps}/>
                <div style={{ width: '100%', height: 'calc( 100% - 210px)' }}>
                    <div style={{ width: '50%', height: '100%', maxHeight: '100%', position: 'relative', float: 'left', overflowY: 'scroll' }}>
                        <EventSearchList eventid={this.state.eventid} stateSetter={this.state.searchBarProps.stateSetter} />
                    </div>
                    <div style={{ width: '50%', height: '100%', maxHeight: '100%', position: 'relative', float: 'right', overflowY: 'scroll' }}>
                        <EventPreviewPane eventid={this.state.eventid} />
                    </div>

                </div>
            </div>
        );
    }

    stateSetter(obj) {
        function toQueryString(state: IState) {
            var dataTypes = ["boolean", "number", "string"]
            var stateObject: IState = clone(state.searchBarProps);
            stateObject.eventid = state.eventid;
            $.each(Object.keys(stateObject), (index, key) => {
                if (dataTypes.indexOf(typeof (stateObject[key])) < 0)
                    delete stateObject[key];
            })
            return queryString.stringify(stateObject, { encode: false });
        }

        var oldQueryString = toQueryString(this.state);
        var oldQuery = queryString.parse(oldQueryString);

        this.setState(obj, () => {
            var newQueryString = toQueryString(this.state);
            var newQuery = queryString.parse(newQueryString);

            if (!isEqual(oldQueryString, newQueryString)) {
                clearTimeout(this.historyHandle);
                this.historyHandle = setTimeout(() => this.history['push'](this.history['location'].pathname + '?' + newQueryString), 500);
            }
        });
    }


}