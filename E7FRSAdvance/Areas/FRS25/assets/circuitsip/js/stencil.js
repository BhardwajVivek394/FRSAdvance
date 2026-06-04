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
        fsa: { index: 2, label: 'Location Box' },
        pn: { index: 3, label: 'Attribute' },
        erd: { index: 4, label: 'Line' },
        sym: { index: 5, label: 'Symbols' },
        tds: { index: 6, label: '3D Symbols' },
    };

    App.config.stencil.shapes = {};

    App.config.stencil.shapes.standard = [       
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
             
       
    ];

    App.config.stencil.shapes.fsa = [
        {
            type: 'examples.LocationBox',
            size: { width: 200, height: 200 },

        },  
        {
            type: 'examples.RectBox',
            size: { width: 100, height: 200 },

        },
        {
            type: 'examples.DrashSquare',
            size: { width: 100, height: 200 },

        },
        {
            type: 'examples.DrashTriangle',
            size: { width: 100, height: 200 },

        },
        {
            type: 'examples.DrashCurved',
            size: { width: 100, height: 200 },

        }
    ];

    App.config.stencil.shapes.pn = [
        {
            type: 'examples.AttributeBox',
            size: { width: 100, height: 100 },    
            
        }, {
            type: 'examples.ChokeDesign',
            size: { width: 50, height: 200 },
        }
    ];

    App.config.stencil.shapes.erd = [
   
        {
            type: 'examples.HoriLine',
            size: { width: 100, height: 100 },
        }
        ,
        {
            type: 'examples.ArrowLine',
            size: { width: 300, height: 0 },
        }, {
            type: 'examples.RoundZigzagLine',
            size: { width: 500, height: 0 },
        }         
        , {
            type: 'examples.ZigzagLine',
            size: { width: 700, height: 500 },
        }
        , {
            type: 'examples.CurveWithCircles',
            size: { width: 50, height: 200 },
        }
        , {
            type: 'examples.CurveEndsLineMiddle',
            size: { width: 50, height: 300 },
        }
        , {
            type: 'examples.SingleCircle',
            size: { width: 50, height: 300 },
        },
        {
            type: 'examples.VertLine',
            size: { width: 0, height: 100 },
        }, {
            type: 'examples.CurveLine',
            size: { width: 80, height: 50 },
        },{
            type: 'examples.SingleCurveline',
            size: { width: 500, height: 0 },
        }
        , {
            type: 'examples.SingleArrow',
            size: { width: 50, height: 200 },
        }, {
            type: 'examples.HalfRound',
            size: { width: 50, height: 200 },
        }, {
            type: 'examples.DoubleCircleLine',
            size: { width: 50, height: 200 },
        }, {
            type: 'examples.ArtLine',
            size: { width: 50, height: 200 },
        }
        ,{
            type: 'examples.DashedLineSegment',
            size: { width: 50, height: 200 },
        }
       
    ];

    App.config.stencil.shapes.sym = [
        {
            type: 'examples.PlusSym',
            size: { width: 100, height: 100 },
        },
        {
            type: 'examples.MinusSym',
            size: { width: 200, height: 0 },
        },
        {
            type: 'examples.Label',
            size: { width: 200, height: 0 },
        },
        {
            type: 'examples.Text',
            size: { width: 300, height: 0 },
        },
        {
            type: 'examples.LabelValue',
            size: { width: 300, height: 0 },
        },
        {
            type: 'examples.LabelDataLogger',
            size: { width: 300, height: 0 },
        }
    ];

    App.config.stencil.shapes.tds = [
        {
            type: 'examples.SignalShape',
            size: { width: 100, height: 100 },
        },
        {
            type: 'examples.SignalShape1',
            size: { width: 100, height: 100 },
        },       
        {
            type: 'examples.Ampere',
            size: { width: 100, height: 100 },
        },
        {
            type: 'examples.TableDrashSquare',
            size: { width: 100, height: 100 },
        },
        {
            type: 'examples.PointShape',
            size: { width: 100, height: 100 },
        }
    ];

})();