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
            var startTime = lightningParameters.StartTime;
            var endTime = lightningParameters.EndTime;

            var debugGeometry = {
                "rings": [
                    [
                        [-9581997.68, 4209754.1657000035],
                        [-9582362.4501, 4209431.2773],
                        [-9582365.2396, 4209428.819600001],
                        [-9582365.8657, 4209428.270999998],
                        [-9582695.7795, 4209139.547600001],
                        [-9582949.4728, 4208917.1511999965],
                        [-9582950.4982, 4208916.253700003],
                        [-9583310.1471, 4208602.048500001],
                        [-9583312.0016, 4208600.433399998],
                        [-9583639.9527, 4208315.683300003],
                        [-9583705.9026, 4208248.435199998],
                        [-9583711.4211, 4208242.8627],
                        [-9583749.853, 4208206.867299996],
                        [-9583790.5568, 4208173.4624999985],
                        [-9583833.3583, 4208142.7914],
                        [-9583878.0741, 4208114.985299997],
                        [-9583924.5128, 4208090.1633],
                        [-9583972.4755, 4208068.431699999],
                        [-9584021.7568, 4208049.883500002],
                        [-9584072.1457, 4208034.598200001],
                        [-9584123.4264, 4208022.6413],
                        [-9584175.3793, 4208014.0638],
                        [-9584227.782, 4208008.902599998],
                        [-9584280.4101, 4208007.179700002],
                        [-9584333.0382, 4208008.902599998],
                        [-9584377.8588, 4208013.1022000015],
                        [-9584657.1522, 4208047.176399998],
                        [-9584990.2481, 4208079.906300001],
                        [-9584990.5365, 4208079.934699997],
                        [-9585298.4283, 4208110.299999997],
                        [-9585298.6874, 4208110.325599998],
                        [-9585584.4722, 4208138.604000002],
                        [-9585610.2677, 4208141.577500001],
                        [-9585625.8659, 4208143.786600001],
                        [-9585817.1355, 4208172.787699997],
                        [-9585853.4902, 4208179.156000003],
                        [-9585904.7709, 4208191.112999998],
                        [-9585955.1598, 4208206.3983],
                        [-9586004.4412, 4208224.946500003],
                        [-9586052.4038, 4208246.678099997],
                        [-9586098.8425, 4208271.500100002],
                        [-9586143.5583, 4208299.306199998],
                        [-9586186.3598, 4208329.977300003],
                        [-9586227.0636, 4208363.381999999],
                        [-9586265.4955, 4208399.377499998],
                        [-9586301.491, 4208437.8094],
                        [-9586334.8957, 4208478.5132],
                        [-9586365.5668, 4208521.3147],
                        [-9586367.0035, 4208523.4723000005],
                        [-9586560.5807, 4208815.211199999],
                        [-9586569.9556, 4208829.673900001],
                        [-9586659.3448, 4208970.865199998],
                        [-9586672.3053, 4208976.1022000015],
                        [-9586720.268, 4208997.833800003],
                        [-9586766.7067, 4209022.6558],
                        [-9586811.4225, 4209050.461900003],
                        [-9586854.224, 4209081.133000001],
                        [-9586894.9278, 4209114.537799999],
                        [-9586933.3597, 4209150.533200003],
                        [-9586969.3551, 4209188.965099998],
                        [-9587002.7599, 4209229.668899998],
                        [-9587033.431, 4209272.470399998],
                        [-9587061.2371, 4209317.1862],
                        [-9587086.0591, 4209363.624899998],
                        [-9587107.7907, 4209411.5876],
                        [-9587126.3389, 4209460.868900001],
                        [-9587141.6242, 4209511.257799998],
                        [-9587153.5811, 4209562.5385000035],
                        [-9587162.1586, 4209614.491400003],
                        [-9587167.3198, 4209666.894100003],
                        [-9587169.0427, 4209719.522200003],
                        [-9587167.3198, 4209772.150300004],
                        [-9587162.1586, 4209824.553000003],
                        [-9587153.5811, 4209876.505900003],
                        [-9587141.6242, 4209927.786600001],
                        [-9587126.3389, 4209978.175499998],
                        [-9587107.7907, 4210027.456799999],
                        [-9587086.0591, 4210075.419500001],
                        [-9587061.2371, 4210121.858199999],
                        [-9587033.431, 4210166.574000001],
                        [-9587002.7599, 4210209.375500001],
                        [-9586969.3551, 4210250.079300001],
                        [-9586933.3597, 4210288.511200003],
                        [-9586894.9278, 4210324.5066],
                        [-9586854.224, 4210357.911399998],
                        [-9586811.4225, 4210388.582500003],
                        [-9586766.7067, 4210416.388599999],
                        [-9586740.672, 4210430.784699999],
                        [-9586621.7776, 4210493.687100001],
                        [-9586601.3736, 4210504.112899996],
                        [-9586553.4109, 4210525.8445999995],
                        [-9586504.1296, 4210544.392700002],
                        [-9586453.7407, 4210559.678000003],
                        [-9586402.46, 4210571.634999998],
                        [-9586350.5071, 4210580.2124999985],
                        [-9586298.1044, 4210585.3737],
                        [-9586245.4763, 4210587.096600004],
                        [-9586192.8482, 4210585.3737],
                        [-9586140.4455, 4210580.2124999985],
                        [-9586088.4926, 4210571.634999998],
                        [-9586037.2119, 4210559.678000003],
                        [-9585986.823, 4210544.392700002],
                        [-9585937.5417, 4210525.8445999995],
                        [-9585889.579, 4210504.112899996],
                        [-9585843.1403, 4210479.291000001],
                        [-9585798.4245, 4210451.484899998],
                        [-9585755.623, 4210420.8138],
                        [-9585714.9192, 4210387.409000002],
                        [-9585676.4873, 4210351.413599998],
                        [-9585640.4919, 4210312.981700003],
                        [-9585610.7637, 4210277.032399997],
                        [-9585479.0232, 4210107.9745000005],
                        [-9585475.3466, 4210103.219999999],
                        [-9585444.6755, 4210060.418499999],
                        [-9585433.8639, 4210043.798199996],
                        [-9585229.2839, 4209720.661600001],
                        [-9585140.3598, 4209711.862499997],
                        [-9584832.7544, 4209681.5254999995],
                        [-9584553.2104, 4209654.057599999],
                        [-9584368.0662, 4209814.812799998],
                        [-9584009.7964, 4210127.813100003],
                        [-9583756.3407, 4210350.001199998],
                        [-9583755.8315, 4210350.447300002],
                        [-9583427.4349, 4210637.8429000005],
                        [-9583116.663, 4210912.9331],
                        [-9582877.253, 4211213.2080999985],
                        [-9582571.0116, 4211601.681500003],
                        [-9582292.428, 4211958.107500002],
                        [-9582290.7463, 4211960.251599997],
                        [-9582005.155, 4212323.104199998],
                        [-9581764.5066, 4212634.4309],
                        [-9581559.5801, 4212987.993199997],
                        [-9581313.7473, 4213419.0405],
                        [-9581311.9702, 4213422.140600003],
                        [-9581101.2641, 4213787.813100003],
                        [-9580915.5284, 4214114.170599997],
                        [-9580913.5324, 4214117.657399997],
                        [-9580694.9489, 4214497.309900001],
                        [-9580484.421, 4214865.0891999975],
                        [-9580483.3584, 4214866.939800002],
                        [-9580263.02, 4215249.500699997],
                        [-9580036.8413, 4215650.8891],
                        [-9580032.6723, 4215658.197999999],
                        [-9580004.8662, 4215702.913800001],
                        [-9579974.1951, 4215745.715300001],
                        [-9579940.7903, 4215786.419100001],
                        [-9579904.7949, 4215824.8510000035],
                        [-9579866.363, 4215860.8464],
                        [-9579825.6592, 4215894.251199998],
                        [-9579782.8577, 4215924.9223000035],
                        [-9579738.1419, 4215952.728399999],
                        [-9579691.7032, 4215977.550399996],
                        [-9579643.7405, 4215999.281999998],
                        [-9579594.4592, 4216017.830200002],
                        [-9579544.0703, 4216033.115500003],
                        [-9579492.7896, 4216045.072499998],
                        [-9579440.8367, 4216053.649899997],
                        [-9579388.434, 4216058.811099999],
                        [-9579335.8059, 4216060.534000002],
                        [-9579283.1778, 4216058.811099999],
                        [-9579230.7751, 4216053.649899997],
                        [-9579178.8222, 4216045.072499998],
                        [-9579127.5415, 4216033.115500003],
                        [-9579077.1526, 4216017.830200002],
                        [-9579027.8713, 4215999.281999998],
                        [-9578979.9086, 4215977.550399996],
                        [-9578933.4699, 4215952.728399999],
                        [-9578888.7541, 4215924.9223000035],
                        [-9578845.9526, 4215894.251199998],
                        [-9578805.2488, 4215860.8464],
                        [-9578766.8169, 4215824.8510000035],
                        [-9578730.8215, 4215786.419100001],
                        [-9578697.4167, 4215745.715300001],
                        [-9578666.7456, 4215702.913800001],
                        [-9578638.9395, 4215658.197999999],
                        [-9578614.1175, 4215611.759300001],
                        [-9578592.3859, 4215563.796599999],
                        [-9578573.8377, 4215514.515299998],
                        [-9578558.5524, 4215464.126400001],
                        [-9578546.5954, 4215412.845700003],
                        [-9578538.018, 4215360.892800003],
                        [-9578532.8568, 4215308.490099996],
                        [-9578531.1339, 4215255.862000003],
                        [-9578532.8568, 4215203.233900003],
                        [-9578538.018, 4215150.831200004],
                        [-9578546.5954, 4215098.878300004],
                        [-9578558.5524, 4215047.597599998],
                        [-9578573.8377, 4214997.208700001],
                        [-9578592.3859, 4214947.9274],
                        [-9578614.1175, 4214899.9646999985],
                        [-9578634.7705, 4214860.834899999],
                        [-9578862.808, 4214456.1477999985],
                        [-9578866.5565, 4214449.568099998],
                        [-9579088.252, 4214064.651000001],
                        [-9579298.7408, 4213696.939900003],
                        [-9579299.7401, 4213695.199299999],
                        [-9579517.8244, 4213316.413900003],
                        [-9579703.6354, 4212989.924099997],
                        [-9579705.7728, 4212986.191699997],
                        [-9579916.6672, 4212620.192400001],
                        [-9580163.0024, 4212188.264200002],
                        [-9580165.1219, 4212184.570500001],
                        [-9580165.8026, 4212183.393799998],
                        [-9580398.9592, 4211781.125699997],
                        [-9580426.0846, 4211737.586599998],
                        [-9580456.7557, 4211694.785099998],
                        [-9580458.4969, 4211692.5242],
                        [-9580734.0162, 4211336.085000001],
                        [-9580738.3533, 4211330.524599999],
                        [-9581025.2805, 4210965.974699996],
                        [-9581304.0543, 4210609.305399999],
                        [-9581306.1193, 4210606.674699999],
                        [-9581614.772, 4210215.142499998],
                        [-9581617.5292, 4210211.6647000015],
                        [-9581901.8562, 4209855.053400002],
                        [-9581926.0422, 4209826.135300003],
                        [-9581962.0376, 4209787.703400001],
                        [-9581997.68, 4209754.1657000035]
                    ]
                ],
                "spatialReference": {
                    "wkid": 102100,
                    "latestWkid": 3857
                }
            };

            lightningQuery.queryLightningData(debugGeometry, startTime, endTime, lightningData => {
                var header = null;

                if (lightningData.length === 0)
                    header = HeaderRow({ "No Data": "" });
                else
                    header = HeaderRow(lightningData[0]);

                var rows = lightningData.map(row => Row(row));
                this.setState({ header: header, rows: rows });
                this.props.callback({ enableLightningData: true });
            });

            if (debugGeometry)
                return;

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
                <div id="lightningcontent" style={{ maxWidth: 500 }}>
                    <table className="table" style={{fontSize: 'large', marginBottom: 0}}>
                        <thead style={{ display: 'table', tableLayout: 'fixed', width: 'calc(100% - 1em)'}}>
                            {this.state.header}
                        </thead>
                        <tbody style={{ fontSize: 'medium', maxHeight: 500, overflowY: 'auto', display: 'block'}}>
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
            <td>{row.label}</td>
            {row.tds}
        </tr>
    );
}

const HeaderRow = (row) => {
    return (
        <tr key='Header'>
            {row.map(key => <th colSpan={2} scope='colgroup' key={key}>{key}</th>)}
        </tr>
    );
}



