/*! Rappid v3.3.0 - HTML5 Diagramming Framework - TRIAL VERSION

Copyright (c) 2021 client IO

 2021-06-28 


This Source Code Form is subject to the terms of the Rappid Trial License
, v. 2.0. If a copy of the Rappid License was not distributed with this
file, You can obtain one at http://jointjs.com/license/rappid_v2.txt
 or from the Rappid archive as was distributed by client IO. See the LICENSE file.*/


(function (joint) {

    'use strict';

    joint.shapes.standard.Ellipse.define('app.CircularModel', {
        attrs: {
            root: {
                magnet: false
            }
        },
        ports: {
            groups: {
                'in': {
                    markup: [{
                        tagName: 'circle',
                        selector: 'portBody',
                        attributes: {
                            'r': 10
                        }
                    }],
                    attrs: {
                        portBody: {
                            magnet: true,
                            fill: '#61549c',
                            strokeWidth: 0
                        },
                        portLabel: {
                            fontSize: 11,
                            fill: '#61549c',
                            fontWeight: 800
                        }
                    },
                    position: {
                        name: 'ellipse',
                        args: {
                            startAngle: 0,
                            step: 30
                        }
                    },
                    label: {
                        position: {
                            name: 'radial',
                            args: null
                        }
                    }
                },
                'out': {
                    markup: [{
                        tagName: 'circle',
                        selector: 'portBody',
                        attributes: {
                            'r': 10
                        }
                    }],
                    attrs: {
                        portBody: {
                            magnet: true,
                            fill: '#61549c',
                            strokeWidth: 0
                        },
                        portLabel: {
                            fontSize: 11,
                            fill: '#61549c',
                            fontWeight: 800
                        }
                    },
                    position: {
                        name: 'ellipse',
                        args: {
                            startAngle: 180,
                            step: 30
                        }
                    },
                    label: {
                        position: {
                            name: 'radial',
                            args: null
                        }
                    }
                }
            }
        }
    }, {
        portLabelMarkup: [{
            tagName: 'text',
            selector: 'portLabel'
        }]
    });

    joint.shapes.standard.Rectangle.define('app.RectangularModel', {
        attrs: {
            root: {
                magnet: false
            }
        },
        ports: {
            groups: {
                'in': {
                    markup: [{
                        tagName: 'circle',
                        selector: 'portBody',
                        attributes: {
                            'r': 10
                        }
                    }],
                    attrs: {
                        portBody: {
                            magnet: true,
                            fill: '#61549c',
                            strokeWidth: 0
                        },
                        portLabel: {
                            fontSize: 11,
                            fill: '#61549c',
                            fontWeight: 800
                        }
                    },
                    position: {
                        name: 'left'
                    },
                    label: {
                        position: {
                            name: 'left',
                            args: {
                                y: 0
                            }
                        }
                    }
                },
                'out': {
                    markup: [{
                        tagName: 'circle',
                        selector: 'portBody',
                        attributes: {
                            'r': 10
                        }
                    }],
                    position: {
                        name: 'right'
                    },
                    attrs: {
                        portBody: {
                            magnet: true,
                            fill: '#61549c',
                            strokeWidth: 0
                        },
                        portLabel: {
                            fontSize: 11,
                            fill: '#61549c',
                            fontWeight: 800
                        }
                    },
                    label: {
                        position: {
                            name: 'right',
                            args: {
                                y: 0
                            }
                        }
                    }
                }
            }
        }
    }, {
        portLabelMarkup: [{
            tagName: 'text',
            selector: 'portLabel'
        }]
    });

    joint.shapes.standard.Link.define('app.Link', {
        router: {
            name: 'normal'
        },
        connector: {
            name: 'rounded'
        },
        labels: [],
        attrs: {
            line: {
                stroke: '#8f8f8f',
                strokeDasharray: '0',
                strokeWidth: 2,
                fill: 'none',
                sourceMarker: {
                    type: 'path',
                    d: 'M 0 0 0 0',
                    stroke: 'none'
                },
                targetMarker: {
                    type: 'path',
                    d: 'M 0 -5 -10 0 0 5 z',
                    stroke: 'none'
                }
            }
        }
    }, {
        defaultLabel: {
            attrs: {
                rect: {
                    fill: '#ffffff',
                    stroke: '#8f8f8f',
                    strokeWidth: 1,
                    refWidth: 10,
                    refHeight: 10,
                    refX: -5,
                    refY: -5
                }
            }
        },

        getMarkerWidth: function (type) {
            var d = (type === 'source') ? this.attr('line/sourceMarker/d') : this.attr('line/targetMarker/d');
            return this.getDataWidth(d);
        },

        getDataWidth: _.memoize(function (d) {
            return (new g.Path(d)).bbox().width;
        })

    }, {

        connectionPoint: function (line, view, magnet, opt, type, linkView) {
            var markerWidth = linkView.model.getMarkerWidth(type);
            opt = { offset: markerWidth, stroke: true };
            // connection point for UML shapes lies on the root group containg all the shapes components
            var modelType = view.model.get('type');
            if (modelType.indexOf('uml') === 0) opt.selector = 'root';
            // taking the border stroke-width into account
            if (modelType === 'standard.InscribedImage') opt.selector = 'border';
            return joint.connectionPoints.boundary.call(this, line, view, magnet, opt, type, linkView);
        }
    });


    joint.shapes.standard.Path.define('examples.Track', {
        size: {
            width: 300,
            height: 100
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                //d: 'M 130 13 130 20 3 20 3 6 3 20 3 6 3 35 3 20 130 20 130 30 130 35 130 6 130 6',
                d: 'M 130 8.75 L 130 17.5 L 0 17.5 L 0 0 L 0 17.5 L 0 0 L 0 30 L 0 17.5 L 130 17.5 L 130 31 L 130 31 L 130 0 L 130 0',
                pointerEvents: 'bounding-box'
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            },
            text: {
                'fill': '#dcd7d7',
                'fontSize': 20,
                'fontWeight': 'bold',
                x: 30

            }
        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });
    joint.shapes.standard.Path.define('examples.Track1', {
        size: {
            width: 100,
            height: 100
        },
        //  position: { x: 200, y: 200 },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                //refD: 'M 130 13 130 20 3 20 3 6 3 20 3 6 3 30 3 20 130 20 130 30 130 30 130 6 130 6',
                d: 'M 172 8.75 L 172 17.5 L 0 17.5 L 0 0 L 0 17.5 L 0 0 L 0 30 L 0 17.5 L 172 17.5 L 172 31 L 172 31 L 172 0 L 172 0',
                pointerEvents: 'bounding-box',
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            },
            text: {
                'fill': '#dcd7d7',
                'fontSize': 20,
                'fontWeight': 'bold',
                x: 45

            }

        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });
    joint.shapes.standard.Path.define('examples.Track2', {
        size: {
            width: 400,
            height: 100
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                //refD: 'M 130 13 130 20 3 20 3 6 3 20 3 6 3 30 3 20 130 20 130 30 130 30 130 6 130 6',
                d: 'M 250 8.75 L 250 17.5 L 0 17.5 L 0 0 L 0 17.5 L 0 0 L 0 30 L 0 17.5 L 250 17.5 L 250 31 L 250 31 L 250 0 L 250 0',
                pointerEvents: 'bounding-box'
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            },
            text: {
                'fill': '#dcd7d7',
                'fontSize': 20,
                'fontWeight': 'bold',
                x: 85

            }
        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });

    joint.shapes.standard.Path.define('examples.Track3', {
        size: {
            width: 100,
            height: 200
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "#6a7596",
                stroke: "#6a7596",
                strokeWidth: 0,
                refD: 'M 0 15.923147231319994 L 0 14.07685276868002 L 57.62352941176469 14.07685276868002 L 57.62352941176469 0 L 59.99999999999999 0 L 59.99999999999999 30 L 57.62352941176469 30 L 57.62352941176469 15.923147231319994 Z',
                pointerEvents: 'bounding-box'
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            },
            text: {
                'fill': '#dcd7d7',
                'fontSize': 20,
                'fontWeight': 'bold',
                x: -10,
                y: -10

            }
        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });

    joint.shapes.standard.Path.define('examples.Track4', {
        size: {
            width: 100,
            height: 200
        },        
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "#6a7596",
                stroke: "#6a7596",
                strokeWidth: 0,
                refD: 'M460,247.92v16.16H68.16V387.29H52V124.71H68.16V247.92Z',
                pointerEvents: 'bounding-box'
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            },
            text: {
                'fill': '#dcd7d7',
                'fontSize': 20,
                'fontWeight': 'bold',
                x: 20,
                y: -10

            }
        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });

    joint.shapes.standard.Path.define('examples.Track5', {
        size: {
            width: 100,
            height: 200
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: 'M113,92.16C271,68.9,218.3,459.9,429,449.64',
                pointerEvents: 'bounding-box'
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            },
            text: {
                'fill': '#dcd7d7',
                'fontSize': 20,
                'fontWeight': 'bold',
                x: 20,
                y: -10

            }
        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });

    joint.shapes.standard.Path.define('examples.Track6', {
        size: {
            width: 100,
            height: 200
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: 'M113,449.64C323.7,459.9,271,68.9,429,92.16',
                pointerEvents: 'bounding-box'
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            },
            text: {
                'fill': '#dcd7d7',
                'fontSize': 20,
                'fontWeight': 'bold',
                x: 20,
                y: -10

            }
        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });

    joint.shapes.standard.Path.define('examples.AxleCounter', {
        size: {
            width: 100,
            height: 100
        },
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                d: 'M 35,65 L 0,100',
                pointerEvents: 'bounding-box'
            },
            circle1: {
                cx: 36,
                cy: 65,
                r: 5,
                stroke: '#6a7596',
                fill: 'white'
            },
            circle2: {
                cx: 0,
                cy: 100,
                r: 5,
                stroke: '#6a7596',
                fill: 'white'
            },
            text: {
                'fill': '#dcd7d7',
                'fontSize': 20,
                'fontWeight': 'bold',
            }

        },
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            }, {
                tagName: 'circle',
                selector: 'circle1'
            }, {
                tagName: 'circle',
                selector: 'circle2'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });

    joint.shapes.standard.Path.define('examples.Shaunt', {
        size: {
            width: 100,
            height: 100
        },
        attrs: {
            body: {
                type: 'Path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: 'M0,0V512c212.08,0,394-128.94,471.76-312.71A510.34,510.34,0,0,0,512,0ZM174.24,402.24A47,47,0,1,1,188,369,46.89,46.89,0,0,1,174.24,402.24Zm0-228A47,47,0,1,1,188,141,46.89,46.89,0,0,1,174.24,174.24Zm225,0A47,47,0,1,1,413,141,46.89,46.89,0,0,1,399.24,174.24Z',
                pointerEvents: 'bounding-box'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
            }

        },

        // inherit joint.shapes.standard.Link.markup
    });

    joint.shapes.standard.Path.define('examples.Shaunt2', {
        size: {
            width: 100,
            height: 100
        },
        attrs: {
            body: {
                type: 'Path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: 'M11.55,13.5v37h38S50.55,14.5,11.55,13.5Zm11,20a4,4,0,1,1,4-4A4,4,0,0,1,22.55,33.5Zm9,10a4,4,0,1,1,4-4A4,4,0,0,1,31.55,43.5Z',
                pointerEvents: 'bounding-box'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
            }

        }
        // inherit joint.shapes.standard.Link.markup
    });

    joint.shapes.standard.Path.define('examples.PointMachine', {
        size: {
            width: 200,
            height: 500
        },
        attrs: {
            body: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: 'M 0 118.93414425282985 C 52.67356710657964 122.33815551063621 39.5 -7.385860259567636 79 0.33122569136571656',
                pointerEvents: 'bounding-box'
            },
            circle1: {
                cx: 35,
                cy: 54,
                r: 6,
                stroke: 'black',
                fill: '#d4d4d4'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
                y: 15,
                x: -15
            }
        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'body'
            },
            {
                tagName: 'circle',
                selector: 'circle1'
            }, {
                tagName: 'text',
                selector: 'label'
            }]
    });
    joint.shapes.standard.Path.define('examples.PointMachine1', {
        size: {
            width: 200,
            height: 500
        },
        attrs: {
            body: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: 'M 80 119.91121699592735 C 31.585760517799372 123.76157717469565 51.26213592233012 1.1001029082195146 0 0',
                pointerEvents: 'bounding-box'
            },
            circle1: {
                cx: 54,
                cy: 50,
                r: 6,
                stroke: 'black',
                fill: '#d4d4d4'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
                y: 15,
                x: 20
            }
        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'body'
            },
            {
                tagName: 'circle',
                selector: 'circle1'
            }, {
                tagName: 'text',
                selector: 'label'
            }]
    });
    //joint.dia.Element.define('examples.Signal', {
    //    size: { width: 100, height: 50 },
    //    attrs: {
    //        bg: {
    //            refWidth: '100%',
    //            refHeight: '100%',
    //            fill: 'transparent'
    //        },
    //        path1: {
    //            d: 'M 0 50 L 0 20 L 40 20',
    //            stroke: 'black',
    //            fill: 'none'
    //        },
    //        circle1: {
    //            cx: 40,
    //            cy: 20,
    //            r: 10,
    //            stroke: 'black',
    //            fill: 'white'
    //        },
    //        circle2: {
    //            cx: 60,
    //            cy: 20,
    //            r: 10,
    //            stroke: 'black',
    //            fill: 'white'
    //        }
    //    }
    //}, {
    //    markup: [{
    //        tagName: 'rect',
    //        selector: 'bg'
    //    }, {
    //        tagName: 'path',
    //        selector: 'path1'
    //    }, {
    //        tagName: 'circle',
    //        selector: 'circle1'
    //    }, {
    //        tagName: 'circle',
    //        selector: 'circle2'
    //    }]
    //});
    joint.dia.Element.define('examples.Signal45', {
        size: { width: 100, height: 60 },
        attrs: {
            circle1: {
                cx: 10,
                cy: 10,
                r: 10,
                stroke: 'black',
                fill: '#d4d4d4'
            },
            path1: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 1,
                d: 'M 17,3 L 3,17',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
            }
        }
    }, {
        markup: [
            {
                tagName: 'circle',
                selector: 'circle1'
            },
            {
                tagName: 'path',
                selector: 'path1'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });
    joint.dia.Element.define('examples.Signal90', {
        size: { width: 100, height: 60 },
        attrs: {
            circle1: {
                cx: 10,
                cy: 10,
                r: 10,
                stroke: 'black',
                fill: '#d4d4d4'
            },
            path1: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 1,
                d: 'M 20,10 L 0, 10 20',
                pointerEvents: 'bounding-box',

            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
            }
        }
    }, {
        markup: [
            {
                tagName: 'circle',
                selector: 'circle1'
            },
            {
                tagName: 'path',
                selector: 'path1'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });
    joint.dia.Element.define('examples.Signald45', {
        size: { width: 100, height: 60 },
        attrs: {
            circle1: {
                cx: 10,
                cy: 10,
                r: 10,
                stroke: 'black',
                fill: '#d4d4d4'
            },
            path1: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 1,
                d: 'M 17,3 L 3,17',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path2: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 1,
                d: 'M 18,4 L 4,18',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
            }
        }
    }, {
        markup: [
            {
                tagName: 'circle',
                selector: 'circle1'
            },
            {
                tagName: 'path',
                selector: 'path1'
            }, {
                tagName: 'path',
                selector: 'path2'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });

    joint.dia.Element.define('examples.Signald90', {
        size: { width: 100, height: 60 },
        attrs: {
            circle1: {
                cx: 10,
                cy: 10,
                r: 10,
                stroke: 'black',
                fill: '#d4d4d4'
            },
            path1: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 1,
                d: 'M 9,0 V 20',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path2: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 1,
                d: 'M 11,0 V 20',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
            }
        }
    }, {
        markup: [
            {
                tagName: 'circle',
                selector: 'circle1'
            },
            {
                tagName: 'path',
                selector: 'path1'
            }, {
                tagName: 'path',
                selector: 'path2'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });
    joint.shapes.standard.Path.define('examples.Route', {
        size: {
            width: 100,
            height: 100
        },
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                d: 'M 50,50 L 0,80',
                pointerEvents: 'bounding-box'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
            }
        },
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });
    joint.dia.Element.define('examples.Post', {
        size: { width: 100, height: 60 },
        attrs: {
            path1: {
                d: 'M 0 75 L 0 20 L 40 20',
                stroke: '#6a7596',
                strokeWidth: 3,
                fill: 'none',
                pointerEvents: 'bounding-box'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
            }
        }
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path1'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });
    joint.dia.Element.define('examples.Post1', {
        size: { width: 100, height: 60 },
        attrs: {
            path1: {
                d: 'M56,16.5v50H8v-.89H55.11V16.5Z',
                stroke: '#6a7596',
                strokeWidth: 3,
                fill: 'none',
                pointerEvents: 'bounding-box'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
                y: 85,
                x: 30
            }
        }
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path1'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });

    joint.dia.Element.define('examples.Post2', {
        size: { width: 100, height: 60 },
        attrs: {
            path1: {
                d: 'M 0 75 L 0 20 L -40 20',
                stroke: '#6a7596',
                strokeWidth: 3,
                fill: 'none',
                pointerEvents: 'bounding-box'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
                y: 15,
                x: -40
            }
        }
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path1'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });

    joint.dia.Element.define('examples.Post3', {
        size: { width: 100, height: 60 },
        attrs: {
            path1: {
                d: 'M56,16.5v50H100v-.89H55.11V16.5Z',
                stroke: '#6a7596',
                strokeWidth: 3,
                fill: 'none',
                pointerEvents: 'bounding-box'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
                y: 85,
                x: 40
            }
        }
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path1'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });
    joint.shapes.standard.Path.define('examples.Gate', {
        size: {
            width: 100,
            height: 100
        },
        attrs: {
            body: {
                type: 'Path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 1,
                refD: 'M49.64,4l-.1.19a12.41,12.41,0,0,0-1.11,4.21s0,.07,0,.11v.09a17.83,17.83,0,0,0,.1,3.46.37.37,0,0,0,0,.12,19,19,0,0,0,1,4.2l.08.21,1.44-.36V17h.5v-.8l14.78-3.66v.68h.5v-.8L72.32,11Zm15.92,8.13-1.91.47-1.9.47-1.91.47-1.9.47L56,14.53h0l-1.9.47h0l-1.91.47-1.16.28L49.93,16A19.59,19.59,0,0,1,49,12.11.46.46,0,0,1,49,12a16.69,16.69,0,0,1-.09-3.28s0-.07,0-.11V8.5a11.85,11.85,0,0,1,1-3.86L70.44,11l-3.6.89ZM55.83,8.11a1.95,1.95,0,1,0,1.94,2A1.94,1.94,0,0,0,55.83,8.11Zm0,3.39a1.45,1.45,0,1,1,1.44-1.44A1.45,1.45,0,0,1,55.83,11.5Zm0,99.84a1.95,1.95,0,1,0-1.94-1.94A1.94,1.94,0,0,0,55.83,111.34Zm0-3.39a1.45,1.45,0,1,1-1.44,1.45A1.45,1.45,0,0,1,55.83,107.95Zm11-62.55h-.5v-2h.5Zm0,20.14h-.5v-2h.5Zm0-48.34h-.5v-2h.5Zm0,40.29h-.5v-2h.5Zm0,4h-.5v-2h.5Zm0-8.06h-.5v-2h.5Zm0-20.14h-.5v-2h.5Zm0-14.1v2h-.5v-2Zm0,18.13h-.5v-2h.5Zm0-8.06h-.5v-2h.5Zm0-6v2h-.5v-2Zm0,26.19h-.5v-2h.5Zm0,44.31h-.5v-2h.5Zm0-24.17h-.5v-2h.5Zm-.5,34.24h.5v2h-.5Zm.5-2h-.5v-2h.5Zm0-60.43h-.5v-2h.5Zm0,56.4h-.5v-2h.5Zm0-24.17h-.5v-2h.5Zm0,4h-.5v-2h.5Zm0,12.09h-.5v-2h.5Zm0-4h-.5v-2h.5Zm0-4h-.5v-2h.5Zm0,27v-.83h-.5v.66l-.63-.19h0l-1.91-.59h0l-1.91-.59h0l-1.9-.59h0L58.07,106h0l-1.91-.59h0l-1.91-.59h0l-1.91-.59h0l-.78-.23v-.82h-.5v.67l-1.42-.44-.1.2a12.41,12.41,0,0,0-1.11,4.2s0,.08,0,.12V108a17.73,17.73,0,0,0,.1,3.45.39.39,0,0,0,0,.13,19,19,0,0,0,1,4.2l.08.2,22.7-5.61ZM49,111.44s0,0,0-.12a16.69,16.69,0,0,1-.09-3.28s0-.08,0-.11v-.09a11.79,11.79,0,0,1,1-3.86l20.53,6.32-20.51,5.07A19.94,19.94,0,0,1,49,111.44ZM51.56,29h-.5V27h.5Zm0,16h-.5V43h.5Zm0,16h-.5V59h.5Zm0-8h-.5V51h.5Zm0-14v2h-.5V39Zm0,58.13h-.5v-2h.5Zm0-48.11h-.5V47h.5Zm0-30.07v2h-.5V19Zm0,4v2h-.5V23Zm0,8v2h-.5V31Zm0,4v2h-.5V35Zm0,22h-.5V55h.5Zm0,8h-.5v-2h.5Zm0,24.05h-.5v-2h.5Zm0-4h-.5v-2h.5Zm-.5,14h.5v2h-.5Zm.5-6h-.5v-2h.5Zm0-20h-.5v-2h.5Zm0,8h-.5v-2h.5Zm0-12h-.5v-2h.5Zm0,8h-.5v-2h.5Z',
                pointerEvents: 'bounding-box'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
            }

        }
        // inherit joint.shapes.standard.Link.markup
    });

    joint.shapes.standard.Path.define('examples.RightArrow', {
        size: {
            width: 100,
            height: 100
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "#6a7596",
                stroke: "#6a7596",
                strokeWidth: 0,
                d: 'M 7.080153649167727 11.482511923688406 L 7.080153649167727 0 L 0 14.948330683624818 L 7.080153649167727 30 L 7.080153649167727 18.974562798092222 L 60 18.974562798092222 L 60 11.482511923688406 Z',
                pointerEvents: 'bounding-box'
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            },
            text: {
                'fill': '#dcd7d7',
                'fontSize': 20,
                'fontWeight': 'bold',
                x: -10,
                y: -30

            }
        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });
    joint.shapes.standard.Path.define('examples.LeftArrow', {
        size: {
            width: 100,
            height: 100
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "#6a7596",
                stroke: "#6a7596",
                strokeWidth: 0,
                d: 'M 60 15.04568931267383 L 52.928900255754485 0 L 52.928900255754485 11.021056813667059 L 0 11.021056813667059 L 0 18.52205005959476 L 52.928900255754485 18.52205005959476 L 52.928900255754485 30 Z',
                pointerEvents: 'bounding-box'
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            },
            text: {
                'fill': '#dcd7d7',
                'fontSize': 20,
                'fontWeight': 'bold',
                x: -10,
                y: -30

            }
        }
        // inherit joint.shapes.standard.Link.markup
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });

    //joint.shapes.standard.Path.define('examples.BusBar', {
    //    size: {
    //        width: 100,
    //        height: 100
    //    },
    //    attrs: {
    //        body: {
    //            type: 'Path',
    //            stroke: "#6a7596",
    //            strokeWidth: 1,
    //            //refD: 'M139.35,28.3l-.06-.91A1.86,1.86,0,0,1,139.35,28.3Z,M139.45,29.91l-.7-.35c-1.91,3-4.27,4.64-7,5a8.48,8.48,0,0,1-1,.06c-4.19,0-7.44-3.2-7.58-3.35A1.38,1.38,0,0,1,123,31L108.63,10.72c-2.11-1.66-4.06-2.22-6-1.73-4.35,1.14-7.75,7.66-8.7,10.08a2,2,0,1,1-3.72-1.46c.16-.43,4.19-10.59,11.4-12.48,3.25-.86,6.52,0,9.73,2.66a2,2,0,0,1,.36.4l14.39,20.33c.5.45,2.77,2.36,5.16,2a5.79,5.79,0,0,0,3.85-2.8l-.68-.34,4.65-3.09.19,3,.06.91Z,M141.4,89.86H3.23a3.23,3.23,0,1,1,0-6.47H141.4a3.23,3.23,0,0,1,0,6.47Z,M55,12.79v3.84H6V12.79Z,M45.94,26.61V22.39h9v4.22Z,M19.26,26.59V22.41h9v4.18Z,M5.93,26.61V22.39h9v4.22Z,M32.61,26.6c0-.24,0-.4,0-.55,0-1.18,0-2.37,0-3.64h9v4.2Z',
    //            refD: 'M139.45 27.57l-.7-.35c-1.91 3-4.27 4.64-7 5a8.48 8.48 0 0 1-1 .06c-4.19 0-7.44-3.2-7.58-3.35a1.38 1.38 0 0 1-.22-.26L108.63 8.38c-2.11-1.66-4.06-2.22-6-1.73-4.35 1.14-7.75 7.66-8.7 10.08a2 2 0 0 1-3.72-1.46c.16-.43 4.19-10.59 11.4-12.48 3.25-.86 6.52 0 9.73 2.66a2 2 0 0 1 .36.4l14.39 20.33c.5.45 2.77 2.36 5.16 2a5.79 5.79 0 0 0 3.85-2.8l-.68-.34L139.1 22l.19 3 .06.91zM97.38 55.71c2-5.31 4-10.58 6-15.83a1.23 1.23 0 0 1 .89-.64c1.42-.07 2.85-.06 4.27 0a1.13 1.13 0 0 1 .85.53c2 5.22 4 10.45 5.92 15.68a1.67 1.67 0 0 1 0 .43h-4.79c-.23 0-.54-.48-.64-.79-.63-1.92-.62-1.93-2.63-1.93-1 0-1.93.06-2.88 0s-1.1.28-1.3 1c-.48 1.78-.52 1.77-2.4 1.77h-3c-.07-.06-.12-.12-.29-.22zm9.08-11.83h-.26l-1.7 5.64h3.67zM128 49.13l4.18 1.27a6 6 0 0 1-4.89 5.48 18 18 0 0 1-5.5-.07c-2.75-.47-4.39-2.42-5.17-5a11.46 11.46 0 0 1 0-6.75 6.67 6.67 0 0 1 4.63-4.65 11 11 0 0 1 6.54 0c2.31.69 3.6 2.38 4.37 4.71l-4.39 1c-.12-.2-.23-.37-.31-.55a3.14 3.14 0 0 0-5.77.28 8.86 8.86 0 0 0 0 5.77 2.57 2.57 0 0 0 2.36 1.74 2.82 2.82 0 0 0 2.95-1.21 18.8 18.8 0 0 0 1-2.02zM141.4 87.52H3.23a3.23 3.23 0 0 1 0-6.47H141.4a3.23 3.23 0 0 1 0 6.47zM11.61 56.82V38.13h9.59c3.88.15 6.48 2.18 7.15 6a19.87 19.87 0 0 1 0 6.76c-.57 3.21-2.75 5.13-6 5.67a14.27 14.27 0 0 1-2.14.22c-2.85.06-5.68.04-8.6.04zm5.78-14.37v10.16h2c2.22-.09 3.19-.93 3.47-3.15a14.49 14.49 0 0 0 .09-3c-.26-3.46-2.05-4.7-5.56-4.01zM49.12 43.59l-4.59 1a12.4 12.4 0 0 0-2.33-2.2A3.42 3.42 0 0 0 37.25 45a17.91 17.91 0 0 0-.05 4.87 3.09 3.09 0 0 0 2.93 3 3.35 3.35 0 0 0 3.79-2.26c.17-.4.3-.82.49-1.34l4.88 1.48A7.23 7.23 0 0 1 42.4 57a16.82 16.82 0 0 1-4.76-.26c-3.53-.73-5.44-3.19-6.15-6.58a15.19 15.19 0 0 1-.21-4.29c.39-4.71 3.14-7.51 7.85-8a15.81 15.81 0 0 1 4.05.12c3 .52 4.99 2.43 5.94 5.6zM55 10.46v3.84H6v-3.84zM45.94 24.28v-4.22h9v4.22zM19.26 24.25v-4.18h9v4.18zM5.93 24.28v-4.22h9v4.22zM32.61 24.27v-.55-3.64h9v4.2z',
    //            pointerEvents: 'bounding-box'
    //        },
    //        text: {
    //            'fill': '#FFFFFF',
    //            'font-size': 20,
    //            'font-weight': 'bold',
    //        }

    //    }
    //});

    joint.dia.Element.define('examples.BusBar', {
        size: { width: 25, height: 25 },
        attrs: {
            path1: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M54.59,26l-.06-.91A1.86,1.86,0,0,1,54.59,26Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',
            },
            path2: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M54.69,27.57l-.7-.35c-1.91,3-4.27,4.64-7,5a8.48,8.48,0,0,1-1,.06c-4.19,0-7.44-3.2-7.58-3.35a1.38,1.38,0,0,1-.22-.26L23.87,8.38c-2.11-1.66-4.06-2.22-6-1.73-4.35,1.14-7.75,7.66-8.7,10.08a2,2,0,0,1-3.72-1.46c.16-.43,4.19-10.59,11.4-12.48,3.25-.86,6.52,0,9.73,2.66a2,2,0,0,1,.36.4L41.36,26.18c.5.45,2.77,2.36,5.16,2a5.79,5.79,0,0,0,3.85-2.8l-.68-.34L54.34,22l.19,3,.06.91Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path3: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M97.38,55.71c2-5.31,4-10.58,6-15.83a1.23,1.23,0,0,1,.89-.64c1.42-.07,2.85-.06,4.27,0a1.13,1.13,0,0,1,.85.53c2,5.22,4,10.45,5.92,15.68a1.67,1.67,0,0,1,0,.43c-1.61,0-3.2,0-4.79,0-.23,0-.54-.48-.64-.79-.63-1.92-.62-1.93-2.63-1.93-1,0-1.93.06-2.88,0s-1.1.28-1.3,1c-.48,1.78-.52,1.77-2.4,1.77l-3,0C97.6,55.87,97.55,55.81,97.38,55.71Zm9.08-11.83-.26,0-1.7,5.64h3.67Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path4: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M128,49.13l4.18,1.27a6,6,0,0,1-4.89,5.48,18,18,0,0,1-5.5-.07c-2.75-.47-4.39-2.42-5.17-5a11.46,11.46,0,0,1,0-6.75,6.67,6.67,0,0,1,4.63-4.65,11,11,0,0,1,6.54,0c2.31.69,3.6,2.38,4.37,4.71l-4.39,1c-.12-.2-.23-.37-.31-.55a3.14,3.14,0,0,0-5.77.28,8.86,8.86,0,0,0,0,5.77,2.57,2.57,0,0,0,2.36,1.74A2.82,2.82,0,0,0,127,51.15,18.8,18.8,0,0,0,128,49.13Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path5: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M139.29,10.46V14.3h-49V10.46Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path6: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M130.26,24.28V20.06h9v4.22Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path7: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M103.59,24.25V20.07h9v4.18Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path8: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M90.26,24.28V20.06h9v4.22Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path9: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M116.94,24.27c0-.24,0-.4,0-.55,0-1.18,0-2.37,0-3.64h9v4.2Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path10: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M141.4,87.52H3.23a3.23,3.23,0,0,1,0-6.47H141.4a3.23,3.23,0,0,1,0,6.47Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path11: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M11.61,56.82V38.13h1.68c2.64,0,5.28,0,7.91,0,3.88.15,6.48,2.18,7.15,6a19.87,19.87,0,0,1,0,6.76c-.57,3.21-2.75,5.13-6,5.67a14.27,14.27,0,0,1-2.14.22C17.36,56.84,14.53,56.82,11.61,56.82Zm5.78-14.37V52.61c.74,0,1.38,0,2,0,2.22-.09,3.19-.93,3.47-3.15a14.49,14.49,0,0,0,.09-3C22.69,43,20.9,41.76,17.39,42.45Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            path12: {
                type: 'path',
                fill: "#6a7596",
                //stroke: "#6a7596",
                //strokeWidth: 1,
                d: 'M49.12,43.59l-4.59,1a12.4,12.4,0,0,0-2.33-2.2A3.42,3.42,0,0,0,37.25,45a17.91,17.91,0,0,0-.05,4.87,3.09,3.09,0,0,0,2.93,3,3.35,3.35,0,0,0,3.79-2.26c.17-.4.3-.82.49-1.34l4.88,1.48A7.23,7.23,0,0,1,42.4,57a16.82,16.82,0,0,1-4.76-.26c-3.53-.73-5.44-3.19-6.15-6.58a15.19,15.19,0,0,1-.21-4.29c.39-4.71,3.14-7.51,7.85-8a15.81,15.81,0,0,1,4.05.12C46.18,38.51,48.17,40.42,49.12,43.59Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
                x: 0,
                y: 110
            }
        }
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path1'
            },
            {
                tagName: 'path',
                selector: 'path2'
            },
            {
                tagName: 'path',
                selector: 'path3'
            },
            {
                tagName: 'path',
                selector: 'path4'
            },
            {
                tagName: 'path',
                selector: 'path5'
            },
            {
                tagName: 'path',
                selector: 'path6'
            },
            {
                tagName: 'path',
                selector: 'path7'
            },
            {
                tagName: 'path',
                selector: 'path8'
            },
            {
                tagName: 'path',
                selector: 'path9'
            },
            {
                tagName: 'path',
                selector: 'path10'
            },
            {
                tagName: 'path',
                selector: 'path11'
            },
            {
                tagName: 'path',
                selector: 'path12'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ]
    });

})(joint);
