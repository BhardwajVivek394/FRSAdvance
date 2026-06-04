/*! Rappid v3.3.0 - HTML5 Diagramming Framework - TRIAL VERSION

Copyright (c) 2021 client IO

 2021-06-28 


This Source Code Form is subject to the terms of the Rappid Trial License
, v. 2.0. If a copy of the Rappid License was not distributed with this
file, You can obtain one at http://jointjs.com/license/rappid_v2.txt
 or from the Rappid archive as was distributed by client IO. See the LICENSE file.*/
var divisionImageBase64 = '';

var DivisionImage = function () {
    var divisionId = $("#hdnDivisionId").val();
    if (divisionId == null || divisionId == undefined || divisionId == '') {
        var query = window.location.search.substring(1);
        if (query != null && query != undefined && query != '') {
            const myArray = query.split("=");
            if (myArray != null && myArray != undefined && myArray.length > 0) {
                divisionId = myArray[1];
            }
        }
    }

    $.ajax({
        url: '/Division/GetDivision',
        type: 'Post',
        data: '{id:' + divisionId + '}',
        contentType: 'application/json',
        async: false,
        success: function (data) {
            divisionImageBase64 = data.ImagePath;
        },
        error: function (response) {
        }
    });
};

(function (joint) {
    DivisionImage();

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
 
    joint.shapes.standard.Path.define('examples.Line1', {
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

    joint.shapes.standard.Path.define('examples.Line2', {
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

    joint.shapes.standard.Path.define('examples.Line3', {
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

    joint.shapes.standard.Path.define('examples.Line4', {
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
                strokeWidth: 12,
                //d: 'M 130 13 130 20 3 20 3 6 3 20 3 6 3 35 3 20 130 20 130 30 130 35 130 6 130 6',
                d: 'M0 6H216',
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
            height: 200
        },
        resizeTool: true,
        attrs: {
            path: {
                type: 'path',
                fill: "none",
                stroke: "#6a7596",
                strokeWidth: 6,
                d: 'M0 25.9001H125.5,M20.9166 51L20.9166 0.80005,M41.8333 51L41.8333 0.80005,M62.7499 51L62.7499 0.80005,M83.6666 51L83.6666 0.80005,M104.583 51L104.583 0.80005',
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

    joint.dia.Element.define('examples.division', {
        size: { width: 100, height: 60 },
        attrs: {
            circle1: {
                cx: 40,
                cy: 40,
                r: 40,
                stroke: 'black',
                fill: '#d4d4d4'
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
    joint.dia.Element.define('examples.site', {
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
    joint.shapes.basic.Image.define('examples.Image1', {
        size: {
            width: 100,
            height: 200
        },
        resizeTool: true,
        attrs: {
            image: {
                width: 500, height: 500, 'xlink:href': divisionImageBase64
            },
            text: { text: 'image', 'font-size': 9, display: '', stroke: '#000000', 'stroke-width': 0 }
        }
    
    });

})(joint);
