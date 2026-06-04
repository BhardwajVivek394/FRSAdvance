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
                d: 'M0 25.9001H125.5,M20.9166 51L20.9166 0.80005,M41.8333 51L41.8333 0.80005,M62.7499 51L62.7499 0.80005,M83.6666 51L83.6666 0.80005,M104.583 51L104.583 0.80005',
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

    //joint.shapes.standard.Path.define('examples.AxleCounter', {
    //    size: {
    //        width: 100,
    //        height: 100
    //    },
    //    attrs: {
    //        path: {
    //            type: 'path',
    //            fill: "none",
    //            stroke: "#6a7596",
    //            strokeWidth: 3,
    //            d: 'M 35,65 L 0,100',
    //            pointerEvents: 'bounding-box'
    //        },
    //        circle1: {
    //            cx: 36,
    //            cy: 65,
    //            r: 5,
    //            stroke: '#6a7596',
    //            fill: 'white'
    //        },
    //        circle2: {
    //            cx: 0,
    //            cy: 100,
    //            r: 5,
    //            stroke: '#6a7596',
    //            fill: 'white'
    //        },
    //        text: {
    //            'fill': '#dcd7d7',
    //            'fontSize': 20,
    //            'fontWeight': 'bold',
    //        }

    //    },
    //    // inherit joint.shapes.standard.Link.markup
    //}, {
    //    markup: [
    //        {
    //            tagName: 'path',
    //            selector: 'path'
    //        }, {
    //            tagName: 'circle',
    //            selector: 'circle1'
    //        }, {
    //            tagName: 'circle',
    //            selector: 'circle2'
    //        },
    //        {
    //            tagName: 'text',
    //            selector: 'label'
    //        }]
    //});

    //joint.shapes.standard.Path.define('examples.Shaunt', {
    //    size: {
    //        width: 100,
    //        height: 100
    //    },
    //    attrs: {
    //        body: {
    //            type: 'Path',
    //            fill: "none",
    //            stroke: "#6a7596",
    //            strokeWidth: 3,
    //            refD: 'M0,0V512c212.08,0,394-128.94,471.76-312.71A510.34,510.34,0,0,0,512,0ZM174.24,402.24A47,47,0,1,1,188,369,46.89,46.89,0,0,1,174.24,402.24Zm0-228A47,47,0,1,1,188,141,46.89,46.89,0,0,1,174.24,174.24Zm225,0A47,47,0,1,1,413,141,46.89,46.89,0,0,1,399.24,174.24Z',
    //            pointerEvents: 'bounding-box'
    //        },
    //        text: {
    //            'fill': '#dcd7d7',
    //            'font-size': 20,
    //            'font-weight': 'bold',
    //        }

    //    },

    //    // inherit joint.shapes.standard.Link.markup
    //});

    //joint.shapes.standard.Path.define('examples.Shaunt2', {
    //    size: {
    //        width: 100,
    //        height: 100
    //    },
    //    attrs: {
    //        body: {
    //            type: 'Path',
    //            fill: "none",
    //            stroke: "#6a7596",
    //            strokeWidth: 3,
    //            refD: 'M11.55,13.5v37h38S50.55,14.5,11.55,13.5Zm11,20a4,4,0,1,1,4-4A4,4,0,0,1,22.55,33.5Zm9,10a4,4,0,1,1,4-4A4,4,0,0,1,31.55,43.5Z',
    //            pointerEvents: 'bounding-box'
    //        },
    //        text: {
    //            'fill': '#dcd7d7',
    //            'font-size': 20,
    //            'font-weight': 'bold',
    //        }

    //    }
    //    // inherit joint.shapes.standard.Link.markup
    //});

    joint.shapes.standard.Path.define('examples.LocationBox', {
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
                refD: 'M50,250,l0,-220,490,0,490,0,0,220,0,220,-490,0,-490,0,0,-220z,m960,0,l0,-200,-470,0,-470,0,0,200,0,200,470,0,470,0,0,-200z',
                pointerEvents: 'bounding-box'
            },
            text: {
                'fill': '#dcd7d7',
                'font-size': 20,
                'font-weight': 'bold',
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
    joint.shapes.standard.Path.define('examples.AttributeBox', {
        size: { width: 300, height: 100 },
        attrs: {
            body: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: 'M50,250,l0,-220,490,0,490,0,0,220,0,220,-490,0,-490,0,0,-220z,m960,0,l0,-200,-470,0,-470,0,0,200,0,200,470,0,470,0,0,-200z',
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

    joint.shapes.standard.Path.define('examples.VertLine', {
        size: {
            width: 64,
            height: 200
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 4,
                refD: 'M32,198 L32,2',
                strokeLinecap: 'round',
                strokeLinejoin: 'round',
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: -10,
                y: -10
            }
        }
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
        ]
    });


    joint.shapes.standard.Path.define('examples.HoriLine', {
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
                d: 'M 0 0 200 1',
                //d: 'M0.5 6H84',
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
         
    //joint.shapes.standard.Path.define('examples.VertCurveline', {
    //    size: {
    //        width: 100,
    //        height: 200
    //    },
    //    resizeTool: true,
    //    attrs: {
    //        path: {
    //            type: 'path',
    //            fill: "none",
    //            stroke: "#6a7596",
    //            strokeWidth: 3,
    //            d: 'M36.4,198c0-36.7,0-73.4,0-110.1c-4.9-0.1-8.9-4.2-8.9-9.1s4-9,8.9-9.1c0-22.6,0-45.2,0-67.7',
    //            pointerEvents: 'bounding-box'
    //        },
    //        label: {
    //            textVerticalAnchor: 'middle',
    //            textAnchor: 'middle',
    //            refX: '50%',
    //            refY: '50%',
    //            fontSize: 14,
    //            fill: '#333333'
    //        },
    //        text: {
    //            fill: '#dcd7d7',
    //            fontSize: 20,
    //            fontWeight: 'bold',
    //            x: 30
    //        }
    //    }
    //}, {
    //    markup: [
    //        {
    //            tagName: 'path',
    //            selector: 'path'
    //        },
    //        {
    //            tagName: 'text',
    //            selector: 'label'
    //        }
    //    ],
    //});

    joint.shapes.standard.Path.define('examples.RoundZigzagLine', {
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
                refD: 'm5.24,15.68h12.07c.54-5.27,4.28-9.33,8.72-9.92,4.92-.65,8.43,3.27,8.72,3.6.51.58,3.28,3.78,2.46,8.15-.68,3.63-3.38,5.58-3.98,6-.5-.27-3.64-2.06-4.48-5.87-.81-3.65,1.09-6.45,1.64-7.26.37-.54,2.68-3.83,7.01-4.42,3.65-.5,7.71,1.05,9.92,4.55,2.17,3.45,1.86,7.7,0,10.61-.88,1.39-1.99,2.26-2.78,2.78-.79-.66-2.05-1.88-2.91-3.79-.15-.33-2.28-5.34.69-9.92.57-.88,2.69-3.84,6.57-4.42,5.36-.81,8.94,3.7,9.16,3.98,2.52,3.28,2.69,7.68,1.07,10.93-.77,1.55-1.82,2.57-2.53,3.16-.78-.45-2.02-1.3-2.97-2.78-2.15-3.35-1.4-7.76.44-10.67.43-.68,2.42-3.82,6.19-4.55,4.82-.93,9.9,2.56,11.37,7.14,1.4,4.37-.97,8.23-1.07,8.4-.98,1.54-2.19,2.47-2.97,2.97-.77-.61-1.99-1.75-2.84-3.54-2.12-4.42-.06-8.72.13-9.09.3-.6,2.67-5.16,7.77-5.68,5.46-.56,8.8,4.03,9.03,4.36,1.59,2.26,1.81,4.6,1.85,5.68h11.23',
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: 30
            }
        }
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

    joint.shapes.standard.Path.define('examples.CurveLine', {
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
                refD: 'm3.1,14.71c2.15-6.14,8-10.25,14.46-10.17,6.61.08,12.46,4.53,14.34,10.93',
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: 30
            }
        }
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

    joint.shapes.standard.Path.define('examples.CurveWithCircles', {
        size: {
            width: 80,
            height: 40
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                d: 'M64.55,12.88c-0.28-0.44-4.45-6.79-11.1-6.73c-6.49,0.06-12.79,6.21-13.23,14.68c-0.02,5.97-3.99,11.15-9.51,12.6c-4.9,1.29-10.23-0.58-13.44-4.74',
                pointerEvents: 'bounding-box'
            },
            circle1: {
                r: 7.96,
                cx: 16.32,
                cy: 20.78,
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 3
            },
            circle2: {
                r: 7.96,
                cx: 63.68,
                cy: 20.78,
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 3
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: 30
            }
        }
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'circle',
                selector: 'circle1'
            },
            {
                tagName: 'circle',
                selector: 'circle2'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ]
    });


    joint.shapes.standard.Path.define('examples.ZigzagLine', {
        size: {
            width: 100,
            height: 30
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: 'M76.79,18.31 L62.71,18.31 L57.4,8.58 L52.66,23.42 L46.35,7.51 L41.93,23.8 L37.25,6.87 L32.45,22.98 L27.46,6.24 L22.28,23.55 L17.86,15.4 L3.21,15.4',
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: 30
            }
        }
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

    joint.shapes.standard.Path.define('examples.CurveEndsLineMiddle', {
        size: {
            width: 80,
            height: 40
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: `
                M11.32,12.04
                a7.96,7.96 0 1,1 0,15.92
                M68.68,27.96
                a7.96,7.96 0 1,1 0,-15.92
                M19.23,20
                L60.77,20
            `,
                pointerEvents: 'bounding-box',
                strokeLinejoin: 'round',
                strokeLinecap: 'round'
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: 30
            }
        }
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
        ]
    });

    joint.shapes.standard.Path.define('examples.ConnectedCirclesLine', {
        size: {
            width: 80,
            height: 40
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 3,
                d: `
                M24.23,20.27 L55.77,20.27
                M16.32,20.27
                m-7.96,0
                a7.96,7.96 0 1,0 15.92,0
                a7.96,7.96 0 1,0 -15.92,0
                M63.68,20.27
                m-7.96,0
                a7.96,7.96 0 1,0 15.92,0
                a7.96,7.96 0 1,0 -15.92,0
            `,
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: 30
            }
        }
    }, {
        markup: [
            { tagName: 'path', selector: 'path' },
            { tagName: 'text', selector: 'label' }
        ]
    });


    joint.shapes.standard.Path.define('examples.ArrowLine', {
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
                d: '30.85,40.13,49,32,30.85,23.87,30.85,40.13',
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: 30
            },
            line: {
                x1: 25.3,
                y1: 32,
                x2: 197.49,
                y2: 32,
                stroke: '#6a7596',
                strokeWidth: 4,
                strokeLinecap: 'round',
                strokeLinejoin: 'round'
            },
            polygon: {
                points: '30.85,40.13 0.49,32 30.85,23.87 30.85,40.13',
                fill: '#6a7596'
            }
        }
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            },
            {
                tagName: 'text',
                selector: 'label'
            },
            {
                tagName: 'line',
                selector: 'line'
            },
            {
                tagName: 'polygon',
                selector: 'polygon'
            }
        ],
    });

    joint.shapes.standard.Path.define('examples.SingleCircle', {
        size: {
            width: 20,
            height: 20
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: `
                M10,10
                m-7.96,0
                a7.96,7.96 0 1,0 15.92,0
                a7.96,7.96 0 1,0 -15.92,0
            `,
                pointerEvents: 'bounding-box',
                strokeLinejoin: 'round',
                strokeLinecap: 'round'
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: 30
            }
        }
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
        ]
    });

    joint.shapes.standard.Path.define('examples.SingleCurveline', {
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
                refD: 'm72.97,20.32c-.85,8.5-7.94,15.06-16.31,15.26-8.66.21-16.23-6.45-17.12-15.26-.2-8.78-7.47-15.9-16.25-15.9S7.23,11.54,7.03,20.32',
                //d: 'M0.5 6H84',
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

    joint.shapes.standard.Path.define('examples.SingleArrow', {
        size: {
            width: 300,
            height: 500
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: 'm2.9,15.26c1.77-4.84,3.55-9.68,5.32-14.53,1.63,4.84,3.25,9.68,4.88,14.53',
                //d: 'M0.5 6H84',
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


    joint.shapes.standard.Path.define('examples.HalfRound', {
        size: {
            width: 300,
            height: 500
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: 'm78.75,76.55c0,34.01-27.99,62-62,62V14.55c34.01,0,62,27.99,62,62Z',
                //d: 'M0.5 6H84',
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


    joint.shapes.standard.Path.define('examples.DoubleCircleLine', {
        size: {
            width: 40,
            height: 80
        },
        attrs: {
            path: {
                type: 'path',
                fill: 'red',
                stroke: '#000000',
                strokeWidth: 3,
                refD: `
                M 20,8.36
                m -7.96,0
                a 7.96,7.96 0 1,0 15.92,0
                a 7.96,7.96 0 1,0 -15.92,0

                M 20,63.68
                m -7.96,0
                a 7.96,7.96 0 1,0 15.92,0
                a 7.96,7.96 0 1,0 -15.92,0

                M 12.07,16.29
                L 12.07,63.83

                M 27.96,16.29
                L 27.96,63.83
            `,
                pointerEvents: 'bounding-box'
            }
        }
    }, {
        markup: [
            {
                tagName: 'path',
                selector: 'path'
            }
        ]
    });

    joint.shapes.standard.Path.define('examples.ArtLine', {
        size: {
            width: 40,
            height: 80
        },
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 3,
                refD: "M9.48,2.1v20.08c0.41-0.49,4.14-4.82,9.95-4.45c5.04,0.32,9.29,4.01,10.61,8.76c1.22,4.4-0.2,9.17-3.65,12.32c-0.47,0.52-3.34,3.61-8.05,3.69c-5.02,0.09-8.1-3.3-8.53-3.79c0.77-0.67,3.87-3.17,8.43-3.08c5.76,0.12,9.13,4.3,9.62,4.93c0.54,0.69,3.29,4.38,2.61,9.28c-0.78,5.6-5.76,10.88-11.79,10.94c-5.18,0.06-8.62-3.75-9.24-4.45v21.55",
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: 30
            }
        }
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
        ]
    });

    joint.shapes.standard.Path.define('examples.DashedLineSegment', {
        size: {
            width: 98.19,
            height: 64
        },
        attrs: {
            path: {
                refD: 'M3.59,32 L94.59,32',
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 4,
                strokeDasharray: '18,18',
                strokeLinecap: 'round',
                strokeLinejoin: 'round',
                pointerEvents: 'bounding-box'
            },
            label: {
                text: '',
                fill: '#333',
                fontSize: 14,
                refX: '50%',
                refY: '50%',
                textAnchor: 'middle',
                textVerticalAnchor: 'middle'
            }
        }
    }, {
        markup: [
            { tagName: 'path', selector: 'path' },
            { tagName: 'text', selector: 'label' }
        ]
    });


    joint.shapes.standard.Path.define('examples.RectBox', {
        size: {
            width: 100,
            height: 100
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 1,
                refD: 'M1.21,1.21 H14.79 V14.79 H1.21 Z',
                pointerEvents: 'bounding-box'
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 12,
                fill: '#000000',
            },
            shadow: {
                opacity: 0.5,
                color: '#000',
                blur: 5,
                offset: { x: 0, y: 5 }
            }
        }
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
        ]
    });

    joint.shapes.standard.Path.define('examples.ChokeDesign', {
        size: {
            width: 512,  // SVG width
            height: 512  // SVG height
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",  // Stroke color from the SVG
                strokeWidth: 3,
                refD: `
                M440.889,163.556V78.222H0v355.556h440.889v-85.333H512V163.556H440.889z
                M398.222,391.111H42.667V120.889h355.556V391.111z
                M469.333,305.778h-28.444v-99.556h28.444V305.778z
                M92.444,170.667h56.889v163.556h-56.889z
                M192,170.667h56.889v163.556h-56.889z
            `,
                pointerEvents: 'bounding-box',
                strokeLinejoin: 'round',
                strokeLinecap: 'round'
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
                fill: '#dcd7d7',
                fontSize: 20,
                fontWeight: 'bold',
                x: 30
            }
        }
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
        ]
    });



    joint.shapes.standard.Path.define('examples.DrashSquare', {
        size: {
            width: 100,
            height: 100
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 2.5,
                strokeDasharray: '0 0 0 3 3 4',
                strokeLinecap: 'round',
                strokeLinejoin: 'round',
                refD: 'M0.97,0.97 H15.03 V15.03 H0.97 Z',
                pointerEvents: 'bounding-box'
            },
            label: {
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 12,
                fill: '#000000'
            }
        }
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
        ]
    });


    joint.shapes.standard.Path.define('examples.PlusSym', {
        size: {
            width: 100,
            height: 100
        },
        attrs: {
            path1: {
                type: 'path',
                fill: '#d4d4d4',
                stroke: "#6a7596",
                strokeWidth: 3,
                d: 'm16,32c-.8,0-1.45-.65-1.45-1.45V1.45c0-.8.65-1.45,1.45-1.45s1.45.65,1.45,1.45v29.09c0,.8-.65,1.45-1.45,1.45Z',
                pointerEvents: 'bounding-box'
            },
            path2: {
                type: 'path',
                fill: '#d4d4d4',
                stroke: "#6a7596",
                strokeWidth: 1,
                d: 'm30.55,17.45H1.45c-.8,0-1.45-.65-1.45-1.45s.65-1.45,1.45-1.45h29.09c.8,0,1.45.65,1.45,1.45s-.65,1.45-1.45,1.45Z',// M 10 10 30 10 30 30 z
                pointerEvents: 'bounding-box',

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
                selector: 'path1'
            },
            {
                tagName: 'path',
                selector: 'path2'
            },
            {
                tagName: 'text',
                selector: 'label'
            }]
    });

    joint.shapes.standard.Path.define('examples.MinusSym', {
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
                d: 'm30.55,17.5H1.45c-.8,0-1.45-.67-1.45-1.5s.65-1.5,1.45-1.5h29.09c.8,0,1.45.67,1.45,1.5s-.65,1.5-1.45,1.5Z',
                //d: 'M0.5 6H84',
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

    joint.shapes.standard.Path.define('examples.Label', {
        size: {
            width: 100,
            height: 200
        },
        resizeTool: true,
        attrs: {
            body: {
                refWidth: '100%',
                refHeight: '100%',
                fill: '#2ecc71',
                stroke: '#27ae60',
                strokeWidth: 2
            },
            label: {
                text: 'Stencil Label',
                refX: '50%',
                refY: '50%',
                textAnchor: 'middle',
                textVerticalAnchor: 'middle',
                fontSize: 14,
                fill: '#000000'
            }
        }
    }, {
        markup: [
            {
                tagName: 'rect',
                selector: 'body'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });

    joint.shapes.standard.Path.define('examples.Text', {
        size: {
            width: 100,
            height: 200
        },
        resizeTool: true,
        attrs: {
            body: {
                refWidth: '100%',
                refHeight: '100%',
                fill: '#2ecc71',
                stroke: '#27ae60',
                strokeWidth: 2
            },
            label: {
                text: 'Stencil Text',
                refX: '50%',
                refY: '50%',
                textAnchor: 'middle',
                textVerticalAnchor: 'middle',
                fontSize: 14,
                fill: '#000000'
            }
        }
    }, {
        markup: [
            {
                tagName: 'rect',
                selector: 'body'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });

    joint.shapes.standard.Path.define('examples.LabelValue', {
        size: {
            width: 100,
            height: 200
        },
        resizeTool: true,
        attrs: {
            body: {
                refWidth: '100%',
                refHeight: '100%',
                fill: '#2ecc71',
                stroke: '#27ae60',
                strokeWidth: 2
            },
            label: {
                text: 'Stencil Value',
                refX: '50%',
                refY: '50%',
                textAnchor: 'middle',
                textVerticalAnchor: 'middle',
                fontSize: 14,
                fill: '#000000'
            }
        }
    }, {
        markup: [
            {
                tagName: 'rect',
                selector: 'body'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });

    joint.shapes.standard.Path.define('examples.LabelDataLogger', {
        size: {
            width: 100,
            height: 200
        },
        resizeTool: true,
        attrs: {
            body: {
                refWidth: '100%',
                refHeight: '100%',
                fill: '#2ecc71',
                stroke: '#27ae60',
                strokeWidth: 2
            },
            label: {
                text: 'Stencil DataLogger',
                refX: '50%',
                refY: '50%',
                textAnchor: 'middle',
                textVerticalAnchor: 'middle',
                fontSize: 14,
                fill: '#000000'
            }
        }
    }, {
        markup: [
            {
                tagName: 'rect',
                selector: 'body'
            },
            {
                tagName: 'text',
                selector: 'label'
            }
        ],
    });

    joint.shapes.standard.Path.define('examples.SignalShape', {
        size: {
            width: 1024,
            height: 1536
        },
        attrs: {
            path: {
                refD: `M6626.1,14174.8c-2.4,69.7-19.3,315.4-211.3,528.3c-237.2,263-557.8,271.9-611.7,272.5
            c-455.1,3.7-910.2,7.4-1365.3,11.1c-84.9-0.9-311.2-14.9-522.7-175.2c-325.3-246.4-345.6-634-347.6-689.6
            c-0.3-2407.1-0.5-4814.1-0.8-7221.2c-1-101.9,9.1-414.2,234.3-689.5c369.6-451.9,973.2-381.9,1013.8-376.5
            c-6.9-1328.1-14-2656.2-20.9-3984.3c-225.9-2.9-451.8-5.9-677.7-8.8c-9.6-0.8-37.1-4.5-60.8-26.4
            c-35.8-33.2-32.5-80.1-32-86.2c1.5-413.4,2.9-826.7,4.4-1240.1c-0.5-6-3.1-44.4,26.4-75.7c28.6-30.4,65.8-30.8,71.9-30.8
            c638.6-2.4,1277.3-4.8,1916-7.3c4.8-1,55.7-10.6,94.5,26.1c31.7,30,32.6,69.4,32.6,76.4c3.9,411.3,7.6,822.7,11.4,1234
            c0.7,7.1,4.3,51.8-29.4,88.9c-35,38.5-82.3,37.8-89.1,37.6c-223.2,4.1-446.3,8.1-669.5,12.2c3.6,1324.4,7.4,2648.9,11,3973.3
            c39.1-4.1,695.6-61.7,1062.1,431.5c194.1,261.2,208.4,544.3,207.6,659.6C6657.6,9334.7,6641.8,11754.7,6626.1,14174.8z
            M5110,6613.2c-438.9,0-794.8,355.8-794.8,794.8c0,438.9,355.8,794.7,794.8,794.7c438.9,0,794.8-355.7,794.8-794.7
            C5904.8,6969,5548.9,6613.2,5110,6613.2z
            M5110,8627.5c-438.9,0-794.8,355.8-794.8,794.8s355.8,794.8,794.8,794.8c438.9,0,794.8-355.8,794.8-794.8
            S5548.9,8627.5,5110,8627.5z
            M5110,10641.8c-438.9,0-794.8,355.8-794.8,794.8c0,438.9,355.8,794.8,794.8,794.8c438.9,0,794.8-355.8,794.8-794.8
            C5904.8,10997.7,5548.9,10641.8,5110,10641.8z
            M5110,12656.2c-438.9,0-794.8,355.8-794.8,794.8s355.8,794.8,794.8,794.8c438.9,0,794.8-355.8,794.8-794.8
            S5548.9,12656.2,5110,12656.2z`,
                fill: '#000000',
                stroke: '#6a7596',
                strokeWidth: 2
            },
            label: {
                text: '',
                fontSize: 16,
                fill: '#333',
                refX: '50%',
                refY: '50%',
                textAnchor: 'middle',
                textVerticalAnchor: 'middle'
            }
        }
    }, {
        markup: [
            { tagName: 'path', selector: 'path' },
            { tagName: 'text', selector: 'label' }
        ]
    });

    joint.shapes.standard.Path.define('examples.SignalShape1', {
        size: {
            width: 180,
            height: 50
        },
        attrs: {
            path: {
                refD: `M93.03,2.52 
                l-18.69,18.94 
                H4.48 
                v26.02 
                h171.03 
                v-26.04 
                h-44.7 
                c-6.18-6.29-12.36-12.59-18.54-18.88 
                l-19.25-.04Z`,
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 3,
                strokeLinecap: 'round',
                strokeLinejoin: 'round'
            },
            label: {
                refX: '50%',
                refY: '50%',
                textAnchor: 'middle',
                textVerticalAnchor: 'middle',
                fontSize: 14,
                fill: '#000000'
            }
        }
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
        ]
    });

    joint.shapes.standard.Path.define('examples.DrashTriangle', {
        size: {
            width: 100,
            height: 100
        },
        attrs: {
            path: {
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 1.5,
                strokeDasharray: '0 0 0 3 3 4',
                strokeLinecap: 'round',
                strokeLinejoin: 'round',
                refD: 'M1,7.34 L7.81,1 C10.21,3.16,12.6,5.32,15,7.49 V15 H1 V7.34 Z',
                pointerEvents: 'bounding-box'
            },
            label: {
                text: '',
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            }
        }
    }, {
        markup: [
            { tagName: 'path', selector: 'path' },
            { tagName: 'text', selector: 'label' }
        ]
    });

    joint.shapes.standard.Path.define('examples.DrashCurved', {
        size: {
            width: 100,
            height: 100
        },
        attrs: {
            path: {
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 1.5,
                strokeDasharray: '0 0 0 3 3 4',
                strokeLinecap: 'round',
                strokeLinejoin: 'round',
                refD: 'M1.06,12.41 C1.86,13.09,4.37,15.02,8,15 C11.72,14.97,14.25,12.88,15.03,12.18 V1 H1 C1.02,4.8,1.04,8.61,1.06,12.41 Z',
                pointerEvents: 'bounding-box'
            },
            label: {
                text: '',
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            }
        }
    }, {
        markup: [
            { tagName: 'path', selector: 'path' },
            { tagName: 'text', selector: 'label' }
        ]
    });


    joint.shapes.standard.Path.define('examples.Ampere', {
        size: {
            width: 398,
            height: 299
        },
        attrs: {
            path: {
                fill: '#000000',
                stroke: 'none',
                refD: `
                M1850 2984 c-503 -64 -912 -338 -1160 -776 l-32 -58 -324 0 -324 0 0
                -660 0 -660 328 0 329 0 12 -27 c38 -85 172 -265 271 -363 214 -215 480 -355
                785 -416 126 -25 414 -25 540 0 305 61 571 201 785 416 99 98 233 278 271 363
                l12 27 319 0 318 0 0 660 0 660 -314 0 -314 0 -32 58 c-192 341 -499 594 -860
                710 -147 47 -249 63 -420 67 -85 2 -171 1 -190 -1z m441 -43 c292 -59 541
                -193 754 -406 108 -108 206 -237 262 -349 14 -28 28 -53 32 -56 19 -15 96
                -263 117 -375 26 -138 26 -383 0 -520 -56 -302 -194 -564 -411 -780 -216 -217
                -478 -355 -780 -411 -137 -26 -382 -26 -520 0 -301 56 -564 194 -780 411 -218
                217 -355 476 -411 780 -26 137 -26 382 0 520 105 567 526 1022 1079 1167 198
                51 459 59 658 19z m-1657 -848 c-34 -80 -75 -212 -96 -308 -29 -135 -32 -425
                -5 -560 19 -92 69 -260 92 -305 7 -14 16 -35 19 -47 l7 -23 -310 0 -311 0 0
                640 0 640 310 0 310 0 -16 -37z m3326 -603 l0 -640 -300 0 c-165 0 -300 2
                -300 4 0 3 13 37 29 78 150 373 145 793 -13 1161 l-16 37 300 0 300 0 0 -640z
                M1980 2404 c-59 -10 -67 -15 -79 -40 -14 -31 -528 -1452 -547 -1511
                -20 -65 -5 -78 94 -78 49 0 85 5 94 13 9 7 46 102 84 212 l69 200 338 -2 339
                -3 69 -195 c38 -107 76 -201 84 -210 12 -11 38 -15 106 -15 98 0 113 8 102 55
                -8 31 -536 1495 -554 1532 -6 15 -19 30 -28 32 -27 8 -144 14 -171 10z m192
                -627 l136 -392 -139 -3 c-76 -1 -201 -1 -277 0 l-137 3 135 392 c74 216 137
                393 140 393 3 0 66 -177 142 -393z
            `,
                pointerEvents: 'bounding-box'
            },
            label: {
                text: '', // You can set your label here
                textVerticalAnchor: 'middle',
                textAnchor: 'middle',
                refX: '50%',
                refY: '50%',
                fontSize: 14,
                fill: '#333333'
            }
        }
    }, {
        markup: [
            { tagName: 'path', selector: 'path' },
            { tagName: 'text', selector: 'label' }
        ]
    });

    joint.shapes.standard.Path.define('examples.TableDrashSquare', {
        size: {
            width: 200,
            height: 120
        },
        attrs: {
            path: {
                fill: 'none',
                stroke: '#6a7596',
                strokeWidth: 2.5,
                strokeDasharray: '0 0 0 3 3 4',
                strokeLinecap: 'round',
                strokeLinejoin: 'round',
                refD: 'M 0 0 H 200 V 120 H 0 Z',
                pointerEvents: 'bounding-box'
            },
            fo: {
                refWidth: '100%',
                refHeight: '100%'
            },
            body: {
                html: ``
            }
        }
    }, {
        markup: [
            { tagName: 'path', selector: 'path' },
            {
                tagName: 'foreignObject',
                selector: 'fo',
                attributes: {
                    width: '100%',
                    height: '100%'
                },
                children: [{
                    tagName: 'body',
                    namespaceURI: 'http://www.w3.org/1999/xhtml',
                    selector: 'body'
                }]
            }
        ]
    });

    joint.shapes.standard.Path.define('examples.PointShape', {
        size: {
            width: 128,
            height: 128
        },
        attrs: {
            path: {
                refD: `M7.32,4.17v-.43c-.3.04-.47,0-.55-.09-.13-.16-.06-.4.02-.66.07-.23.18-.57.52-.84.38-.29.79-.29.92-.29.72,0,1.44.01,2.16.02.11,0,.8.03,1.16.54.14.2.2.41.24.62.12.56.13.76-.54.69v.43c.65,0,1.29.01,1.94.02.07,0,.19.01.33.06,0,0,.15.06.29.18.2.18.36.61.34,1.15h1.02s.14.02.23.09c.17.13.17.35.17.37,0,1.23,0,2.46,0,3.69,0,.05-.02.23-.16.39-.12.13-.25.17-.31.19h-.95c.03.45-.03.93-.43,1.2-.05.03-.31.16-.34.16h-1.39v1.17c.17,0,.33,0,.5,0,.34.05.58.35.55.68-.03.34-.33.61-.69.59-2.12-.01-4.25-.02-6.37-.04-.29-.08-.5-.33-.52-.62-.02-.32.2-.62.53-.7.17,0,.34,0,.51,0v-1.08h-1.57c-.52-.27-1.04-.54-1.56-.81l-.04-1.35c-.11.02-.44.05-.79-.14-.43-.23-.57-.63-.6-.72H.51c-.05-.19-.09-.44-.08-.74,0-.24.04-.45.08-.61h1.35c.02-.08.15-.56.63-.83.38-.21.74-.17.86-.15l.04-1.35c.54-.27,1.08-.54,1.62-.81h2.31ZM11.44,3.37c.07-.22-.3-1.04-.47-1.1h-3.26c-.31.02-.61.79-.58,1.1h4.31ZM10.89,3.74h-3.2v.43h3.2v-.43ZM5.78,4.54h-.55v6.77h.55v-6.77ZM12.37,5.59v-.95l-.09-.09h-6.13v1.05h6.22ZM14.15,5.96v2.19c0,.09-.07.16-.15.18-.09.02-.19-.03-.22-.12v-3.14c0-.41-.71-.62-1.05-.52v6.77c.27,0,.69.04.89-.19.03-.03.16-.32.16-.34v-1.23c.02-.08.09-.14.18-.14.09,0,.18.07.19.17v.37h.9s.05,0,.07-.01c.06-.03.1-.09.1-.17,0-1.24,0-2.48-.01-3.73,0,0,0-.05-.03-.07-.02-.02-.05-.02-.06-.02h-.95ZM4.85,4.73c-.26.07-.8.28-1.01.44-.05.04-.1.07-.1.15v5.17c0,.08.01.13.06.18.13.15.85.36,1.05.52v-6.46ZM12.37,5.96h-6.22v1.79h6.22v-1.79ZM3.38,6.76c-.37-.04-.84-.01-1.01.37-.06.13-.06,1.47,0,1.6.17.38.65.41,1.01.37v-2.34ZM1.96,7.68H.79v.62h1.17v-.62ZM12.37,8.11h-6.22v1.72h6.22v-1.72ZM12.37,10.21h-6.22v1.11h6.22v-1.11ZM11.63,11.68h-4.74v1.17h4.74v-1.17ZM6.15,13.18c-.2-.02-.36.14-.35.31,0,.17.18.32.38.28,2.08,0,4.15-.02,6.23-.02.17,0,.31-.14.3-.3,0-.17-.15-.31-.33-.29-2.08.01-4.15.02-6.23.03Z`,
                fill: '#000000',
                stroke: '#6a7596',
                strokeWidth: 1
            },
            label: {
                text: '',
                fontSize: 10,
                fill: '#333',
                refX: '50%',
                refY: '50%',
                textAnchor: 'middle',
                textVerticalAnchor: 'middle'
            }
        }
    }, {
        markup: [
            { tagName: 'path', selector: 'path' },
            { tagName: 'text', selector: 'label' }
        ]
    });


})(joint);
