/*! Rappid v3.3.0 - HTML5 Diagramming Framework - TRIAL VERSION

Copyright (c) 2021 client IO

 2021-06-28 


This Source Code Form is subject to the terms of the Rappid Trial License
, v. 2.0. If a copy of the Rappid License was not distributed with this
file, You can obtain one at http://jointjs.com/license/rappid_v2.txt
 or from the Rappid archive as was distributed by client IO. See the LICENSE file.*/


var App = App || {};
App.config = App.config || {};

(function() {

    'use strict';

    App.config.toolbar = {
        groups: {
            'undo-redo': { index: 1 },
            'clear': { index: 2 },
            'export': { index: 3 },
            'print': { index: 4 },
            'fullscreen': { index: 5 },
            'order': { index: 6 },
            'layout': { index: 7 },
            'zoom': { index: 8 },
            'grid': { index: 9 },
            'snapline': { index: 10 }
        },
        tools: [       
            {
                type: 'zoom-out',
                name: 'zoom-out',
                group: 'zoom',
                attrs: {
                    button: {
                        'data-tooltip': 'Zoom Out',
                        'data-tooltip-position': 'top',
                        'data-tooltip-position-selector': '.toolbar-container'
                    }
                }
            },
            {
                type: 'label',
                name: 'zoom-slider-label',
                group: 'zoom',
                text: 'Zoom:'
            },
            {
                type: 'zoom-slider',
                name: 'zoom-slider',
                group: 'zoom'
            },
            {
                type: 'zoom-in',
                name: 'zoom-in',
                group: 'zoom',
                attrs: {
                    button: {
                        'data-tooltip': 'Zoom In',
                        'data-tooltip-position': 'top',
                        'data-tooltip-position-selector': '.toolbar-container'
                    }
                }
            },
            {
                type: 'separator',
                group: 'grid'
            }
        ]
    };
})();
