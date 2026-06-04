/*! Rappid v3.3.0 - HTML5 Diagramming Framework - TRIAL VERSION

Copyright (c) 2021 client IO

 2021-06-28 


This Source Code Form is subject to the terms of the Rappid Trial License
, v. 2.0. If a copy of the Rappid License was not distributed with this
file, You can obtain one at http://jointjs.com/license/rappid_v2.txt
 or from the Rappid archive as was distributed by client IO. See the LICENSE file.*/


var App = App || {};
App.config = App.config || {};

(function () {

    'use strict';

    App.config.stencil = {};

    App.config.stencil.groups = {
        standard: { index: 1, label: 'Track' },
        fsa: { index: 2, label: 'Point machine' },
        pn: { index: 3, label: 'Signal' },
        erd: { index: 4, label: 'Shunt' },
        uml: { index: 5, label: 'Gate' },
        org: { index: 6, label: 'ORG' },
        busbar: { index: 6, label: 'Bus Bar' }
    };

    App.config.stencil.shapes = {};

    App.config.stencil.shapes.standard = [
        {
            type: 'examples.Track5',
            size: { width: 100, height: 100 },
            position: { x: 200, y: 500 },
            attrs: {
                root: {
                    dataTooltip: 'Track',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'bold',
                    fontSize: 20,
                    strokeWidth: 0
                }
            }
        }
        ,
        {
            type: 'examples.Track6',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Track',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'bold',
                    fontSize: 20,
                    strokeWidth: 0
                }
            }
        },
        {
            type: 'examples.Track',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Track',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'bold',
                    fontSize: 20,
                    strokeWidth: 0
                }
            }
        },
        {
            type: 'examples.Track1',
            size: { width: 200, height: 0 },
           
            attrs: {
                root: {
                    dataTooltip: 'Track',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'bold',
                    fontSize: 20,
                    strokeWidth: 0
                }
            }
        }
        ,
        {
            type: 'examples.Track2',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Track',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'bold',
                    fontSize: 20,
                    strokeWidth: 0
                }
            }
        }
        
       
    ];

    App.config.stencil.shapes.fsa = [
        {
            type: 'examples.PointMachine',
            size: { width: 200, height: 200 },

        },
        {
            type: 'examples.PointMachine1',
            size: { width: 200, height: 200 },
        },
        {
            type: 'examples.RightArrow',
            size: { width: 200, height: 200 },
        },
        {
            type: 'examples.LeftArrow',
            size: { width: 200, height: 200 },
        },
        {
            type: 'examples.Track3',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Track',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'bold',
                    fontSize: 20,
                    strokeWidth: 0
                }
            }
        },
        {
            type: 'examples.Track4',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Track',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'bold',
                    fontSize: 20,
                    strokeWidth: 0
                }
            }
        }
    ];

    App.config.stencil.shapes.pn = [

        
         {
             type: 'examples.Signal45',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Signal',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'Normal',
                    fontSize: 11,
                    strokeWidth: 0
                }
            }
        },
        {
            type: 'examples.Signal90',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Signal',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'Normal',
                    fontSize: 11,
                    strokeWidth: 0
                }
            }
        },
        {
            type: 'examples.Signald45',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Signal',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'Normal',
                    fontSize: 11,
                    strokeWidth: 0
                }
            }
        },
        {
            type: 'examples.Signald90',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Signal',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'Normal',
                    fontSize: 11,
                    strokeWidth: 0
                }
            }
        },
        {
            type: 'examples.Post',
            size: { width: 100, height: 100 },
        },
        {
            type: 'examples.Post1',
            size: { width: 100, height: 100 },
        },
        {
            type: 'examples.Post2',
            size: { width: 100, height: 100 },
        },
        {
            type: 'examples.Post3',
            size: { width: 300, height: 100 },
        },
        {
            type: 'examples.Route',
            size: { width: 100, height: 100 },
        }
    ];

    App.config.stencil.shapes.erd = [
        {
            type: 'examples.Shaunt',
            size: { width: 100, height: 100 },
        },
        {
            type: 'examples.Shaunt2',
            size: { width: 100, height: 100 },
        }, {
            type: 'examples.AxleCounter',
            size: { width: 100, height: 100 },
        }
        
    ];

    App.config.stencil.shapes.uml = [
        {
            type: 'examples.Gate',
            size: { width: 100, height: 100 },
        }
    ];

    App.config.stencil.shapes.busbar = [
        {
            type: 'examples.BusBar',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'BusBar',
                    dataTooltipPosition: 'left',
                    dataTooltipPositionSelector: '.joint-stencil'
                },
                label: {
                    text: '',
                    fill: '#6a7596',
                    fontFamily: 'Roboto Condensed',
                    fontWeight: 'bold',
                    fontSize: 20,
                    strokeWidth: 0
                }
            }
        }
    ];

    App.config.stencil.shapes.org = [

       
    ];


})();