﻿/// <reference path="vendor/angular/angular.js" />
/// <reference path="vendor/lodash/lodash.js" />
/*global angular:false */
'use strict';

var clusterManagerApp = angular.module('ClusterManagerApp', ['ui.bootstrap', 'ui.bootstrap.tpls']);

clusterManagerApp.config(function ($routeProvider, $locationProvider) {
    $routeProvider.when('/servers', {
        templateUrl: '/views/servers.html',
        controller: 'ServersCtrl'
    });

    $routeProvider.when('/', {
        templateUrl: '/views/main.html',
        controller: 'MainCtrl'
    });
    $routeProvider.otherwise({ redirectTo: '/' });
    
    $locationProvider.html5Mode(true);
});