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
        standard: { index: 1, label: 'Line' },
        track: { index: 2, label: 'Track' },
        division: { index: 3, label: 'Division' },
        site: { index: 4, label: 'Site' },
        image: { index: 5, label: 'Image' },
    };

    App.config.stencil.shapes = {};

    App.config.stencil.shapes.standard = [
        {
            type: 'examples.Line1',
            size: { width: 100, height: 100 },
            position: { x: 200, y: 500 },
            attrs: {
                root: {
                    dataTooltip: 'Line1',
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
            type: 'examples.Line2',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Line2',
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
            type: 'examples.Line3',
            size: { width: 100, height: 100 },
            position: { x: 200, y: 700 },
            attrs: {
                root: {
                    dataTooltip: 'Line3',
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

    App.config.stencil.shapes.division = [
        {
            type: 'examples.division',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Division',
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

    App.config.stencil.shapes.site = [
        {
            type: 'examples.site',
            size: { width: 100, height: 100 },
            attrs: {
                root: {
                    dataTooltip: 'Site',
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

    App.config.stencil.shapes.track = [
        {
            type: 'examples.Track1',
            size: { width: 100, height: 100 },
            position: { x: 200, y: 200 },
            attrs: {
                root: {
                    dataTooltip: 'Track1',
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

    App.config.stencil.shapes.image = [
        {
            type: 'examples.Image1',
            size: { width: 100, height: 100 },
            position: { x: 200, y: 200 },
            attrs: {
                root: {
                    dataTooltip: 'Image1',
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
})();