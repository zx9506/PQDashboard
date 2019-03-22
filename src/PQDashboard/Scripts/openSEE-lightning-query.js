﻿window.LightningQuery = (function () {
    if (window.LightningQuery)
        return window.LightningQuery;

    var $this = {};

    var _query = null;
    var _queryTask = null;
    var _timeExtent = null;
    var _geometryService = null;
    var _bufferParameters = null;
    var _unwrapFunctions = [];

    function init(esriConfig, query, queryTask, timeExtent, geometryService, bufferParameters) {
        esriConfig.defaults.io.proxyUrl = "http://pq/arcgisproxynew/proxy.ashx";
        esriConfig.defaults.io.alwaysUseProxy = false;

        _query = query;
        _queryTask = queryTask;
        _timeExtent = timeExtent;
        _geometryService = geometryService;
        _bufferParameters = bufferParameters;

        for (var i = 0; i < _unwrapFunctions.length; i++)
            _unwrapFunctions[i]();
    }

    require(["esri/config", "esri/tasks/query", "esri/tasks/QueryTask", "esri/TimeExtent", "esri/tasks/GeometryService", "esri/tasks/BufferParameters"], init);

    function exportFunction(key, apiFunction) {
        var pendingCalls = [];

        $this[key] = function () {
            pendingCalls.push(arguments);
        };

        _unwrapFunctions.push(function () {
            $this[key] = apiFunction;

            for (var i = 0; i < pendingCalls.length; i++)
                apiFunction(pendingCalls[i]);
        });
    }

    exportFunction("queryLineGeometry", function (lineKey, callback) {
        var lineQueryTask = new _queryTask("https://gis.tva.gov/arcgis/rest/services/EGIS_Transmission/Transmission_Grid_Restricted_2/MapServer/6");

        var lineQuery = new _query();
        lineQuery.returnGeometry = true;
        lineQuery.outFields = ["LINENAME"];
        lineQuery.where = "UPPER(LINENAME) like '%" + lineKey.toUpperCase() + "%'";

        lineQueryTask.execute(query, function (results) {
            var totalLine = results.features.map(function (feature) { return feature.geometry; });
            callback(totalLine);
        });
    });

    exportFunction("queryLineBufferGeometry", function (lineGeometry, callback) {
        var geometryService = new _geometryService("https://gis.tva.gov/arcgis/rest/services/Utilities/Geometry/GeometryServer");

        var bufferParameters = new _bufferParameters();
        bufferParameters.geometries = lineGeometry;
        bufferParameters.unionResults = true;
        bufferParameters.distances = [0.5];
        bufferParameters.unit = _geometryService.UNIT_STATUTE_MILE;

        geometryService.buffer(bufferParameters, function (geometries) {
            callback(geometries[0]);
        });
    });

    exportFunction("queryLightningData", function (lineBufferGeometry, startTime, endTime, callback) {
        var lightningQueryTask = new _queryTask("https://gis.tva.gov/arcgis/rest/services/EGIS/LightningQuery/MapServer/0");
        var timeExtent = new _timeExtent(startTime, endTime);

        var lightningQuery = new _query();
        //lightningQuery.returnGeometry = true;
        lightningQuery.outFields = ["DISPLAYTIME", "LATITUDE", "LONGITUDE", "AMPLITUDE"];
        lightningQuery.timeExtent = timeExtent;
        lightningQuery.geometry = lineBufferGeometry;
        lightningQuery.spatialrelationship = _query.SPATIAL_REL_INTERSECTS;

        lightningQueryTask.execute(lightningQuery, function (results) {
            var lightningData = results.features.map(function (feature) { return feature.attributes; });
            callback(lightningData);
        });
    });

    return $this;
})();