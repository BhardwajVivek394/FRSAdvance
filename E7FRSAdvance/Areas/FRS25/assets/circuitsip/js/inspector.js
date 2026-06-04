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
    var attributeList = [];
    var dlAttributeList = [];
    var attributeValueList = [];

    var asset = function () {
        var assetsList = [];
        $.ajax({
            url: '/FRS25/AssetType/GetAttributesByAssestId',
            type: 'Post',
            data: '{id:' + $("#hdnassetTypeId").val() + '}',
            contentType: 'application/json',
            async: false,
            success: function (data) {
                if (data != null) {
                    $.each(data, function (id, val) {

                        var asset = {};
                        if (val.Title != null) {
                            if (val.AliasName != null && val.AliasName != undefined && val.AliasName != '') {
                                val.Title = val.AliasName;
                            }
                            asset.value = val.Title;
                            asset.content = '<span>' + val.Title + '</span>'
                            attributeList.push(asset);

                            var assetValue = {};
                            assetValue.value = val.Id;
                            assetValue.content = '<span>' + val.Title + '</span>'
                            attributeValueList.push(assetValue);
                        }
                    });
                }
            },
            error: function (response) {
            }
        });
        return assetsList;
    };
    var dlasset = function () {
        $.ajax({
            url: '/FRS25/AssetType/GetDataloggerAttribute',
            type: 'Post',
            data: '{id:' + $("#hdndlAssetTypeId").val() + '}',
            contentType: 'application/json',
            async: false,
            success: function (data) {

                if (data != null) {
                    $.each(data, function (id, val) {

                        var asset = {};
                        if (val.Title != null) {
                            if (val.AliasName != null && val.AliasName != undefined && val.AliasName != '') {
                                val.Title = val.AliasName;
                            }
                            asset.value = val.Title;
                            asset.content = '<span>' + val.Title + '</span>'
                            dlAttributeList.push(asset);


                        }
                    });
                }
            },
            error: function (response) {
            }
        });
    };
    asset();
    dlasset();
    var options = {

        attributeLists: attributeList,
        attributeValueList: attributeValueList,
        dlAttributeList: dlAttributeList,
        colorPalette: [
            { content: 'transparent', icon: '/assets/sip/transparent-icon.svg' },
            { content: '#f6f6f6' },
            { content: '#dcd7d7' },
            { content: '#8f8f8f' },
            { content: '#c6c7e2' },
            { content: '#feb663' },
            { content: '#fe854f' },
            { content: '#b75d32' },
            { content: '#31d0c6' },
            { content: '#7c68fc' },
            { content: '#61549c' },
            { content: '#6a6c8a' },
            { content: '#4b4a67' },
            { content: '#3c4260' },
            { content: '#33334e' },
            { content: '#222138' },
            { content: '#FFBF00' },
            { content: '#00FF00' },
            { content: '#FF0E0E' },
            { content: '#00008B' },
            { content: '#00A4E0' },
            { content: '#CF88B6' },
            { content: '#2da9dc' },
            { content: '#ec74cd' },
            { content: '#fa0000' },
            { content: '#9aacba' },
            { content: '#fb7f39' },
            { content: '#b34b16' },
            { content: '#fdf9ee' },
            { content: '#3E83BE' },
            { content: '#4A5D6C' },
            { content: '#4769B5' },
            { content: '#BD3140' },
            { content: '#B31564' },
            { content: '#7B9BE0' },
            { content: '#536896' },
            { content: '#6F4E37' },
            { content: '#EEEEE9' },
            { content: '#FBFAF8' },
            { content: '#54BA4A' },
            { content: '#FFAA05' },
            { content: '#085885' },
        ],

        colorPaletteReset: [
            { content: undefined, icon: '/assets/sip/no-color-icon.svg' },
            { content: '#f6f6f6' },
            { content: '#dcd7d7' },
            { content: '#8f8f8f' },
            { content: '#c6c7e2' },
            { content: '#feb663' },
            { content: '#fe854f' },
            { content: '#b75d32' },
            { content: '#31d0c6' },
            { content: '#7c68fc' },
            { content: '#61549c' },
            { content: '#6a6c8a' },
            { content: '#4b4a67' },
            { content: '#3c4260' },
            { content: '#33334e' },
            { content: '#222138' }
        ],

        fontWeight: [
            { value: '300', content: '<span style="font-weight: 300">Light</span>' },
            { value: 'Normal', content: '<span style="font-weight: Normal">Normal</span>' },
            { value: 'Bold', content: '<span style="font-weight: Bolder">Bold</span>' }
        ],

        fontFamily: [
            { value: 'Alegreya Sans', content: '<span style="font-family: Alegreya Sans">Alegreya Sans</span>' },
            { value: 'Averia Libre', content: '<span style="font-family: Averia Libre">Averia Libre</span>' },
            { value: 'Roboto Condensed', content: '<span style="font-family: Roboto Condensed">Roboto Condensed</span>' }
        ],

        strokeStyle: [
            { value: '0', content: 'Solid' },
            { value: '2,5', content: 'Dotted' },
            { value: '10,5', content: 'Dashed' }
        ],

        side: [
            { value: 'top', content: 'Top Side' },
            { value: 'right', content: 'Right Side' },
            { value: 'bottom', content: 'Bottom Side' },
            { value: 'left', content: 'Left Side' }
        ],

        portLabelPositionRectangle: [
            { value: { name: 'top', args: { y: -12 } }, content: 'Above' },
            { value: { name: 'right', args: { y: 0 } }, content: 'On Right' },
            { value: { name: 'bottom', args: { y: 12 } }, content: 'Below' },
            { value: { name: 'left', args: { y: 0 } }, content: 'On Left' }
        ],

        portLabelPositionEllipse: [
            { value: 'radial', content: 'Horizontal' },
            { value: 'radialOriented', content: 'Angled' }
        ],

        imageIcons: [
            { value: '/assets/sip/image-icon1.svg', content: '<img height="42px" src="/assets/sip/image-icon1.svg"/>' },
            { value: '/assets/sip/image-icon2.svg', content: '<img height="80px" src="/assets/sip/image-icon2.svg"/>' },
            { value: '/assets/sip/image-icon3.svg', content: '<img height="80px" src="/assets/sip/image-icon3.svg"/>' },
            { value: '/assets/sip/image-icon4.svg', content: '<img height="80px" src="/assets/sip/image-icon4.svg"/>' }
        ],

        imageGender: [
            { value: '/assets/sip/member-male.png', content: '<img height="50px" src="/assets/sip/member-male.png" style="margin: 5px 0 0 2px;"/>' },
            { value: '/assets/sip/member-female.png', content: '<img height="50px" src="/assets/sip/member-female.png" style="margin: 5px 0 0 2px;"/>' }
        ],

        arrowheadSize: [
            { value: 'M 0 0 0 0', content: 'None' },
            { value: 'M 0 -3 -6 0 0 3 z', content: 'Small' },
            { value: 'M 0 -5 -10 0 0 5 z', content: 'Medium' },
            { value: 'M 0 -10 -15 0 0 10 z', content: 'Large' },
        ],

        strokeWidth: [
            { value: 1, content: '<div style="background:#fff;width:2px;height:30px;margin:0 14px;border-radius: 2px;"/>' },
            { value: 2, content: '<div style="background:#fff;width:4px;height:30px;margin:0 13px;border-radius: 2px;"/>' },
            { value: 4, content: '<div style="background:#fff;width:8px;height:30px;margin:0 11px;border-radius: 2px;"/>' },
            { value: 8, content: '<div style="background:#fff;width:16px;height:30px;margin:0 8px;border-radius: 2px;"/>' }
        ],

        router: [
            { value: 'normal', content: '<p style="background:#fff;width:2px;height:30px;margin:0 14px;border-radius: 2px;"/>' },
            { value: 'orthogonal', content: '<p style="width:20px;height:30px;margin:0 5px;border-bottom: 2px solid #fff;border-left: 2px solid #fff;"/>' },
            { value: 'oneSide', content: '<p style="width:20px;height:30px;margin:0 5px;border: 2px solid #fff;border-top: none;"/>' }
        ],

        connector: [
            { value: 'normal', content: '<p style="width:20px;height:20px;margin:5px;border-top:2px solid #fff;border-left:2px solid #fff;"/>' },
            { value: 'rounded', content: '<p style="width:20px;height:20px;margin:5px;border-top-left-radius:30%;border-top:2px solid #fff;border-left:2px solid #fff;"/>' },
            { value: 'smooth', content: '<p style="width:20px;height:20px;margin:5px;border-top-left-radius:100%;border-top:2px solid #fff;border-left:2px solid #fff;"/>' }
        ],

        labelPosition: [
            { value: 30, content: 'Close to source' },
            { value: 0.5, content: 'In the middle' },
            { value: -30, content: 'Close to target' },
        ],

        portMarkup: [{
            value: [{
                tagName: 'rect',
                selector: 'portBody',
                attributes: {
                    'width': 20,
                    'height': 20,
                    'x': -10,
                    'y': -10
                }
            }],
            content: 'Rectangle'
        }, {
            value: [{
                tagName: 'circle',
                selector: 'portBody',
                attributes: {
                    'r': 10
                }
            }],
            content: 'Circle'
        }, {
            value: [{
                tagName: 'path',
                selector: 'portBody',
                attributes: {
                    'd': 'M -10 -10 10 -10 0 10 z'
                }
            }],
            content: 'Triangle'
        }]
    };

    App.config.inspector = {
        'examples.Label': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.Text': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'text',
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.LabelValue': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeValueList,
                            label: 'Label',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.LabelDataLogger': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.dlAttributeList,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.AttributeBox': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.VertLine': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.ZigzagLine': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.RoundZigzagLine': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.CurveEndsLineMiddle': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.ArtLine': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.SingleCircle': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.LocationBox': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.SingleCurveline': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.SingleArrow': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.RectBox': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        },
                        filter: {
                            type: 'text',
                            label: 'Box Shadow (SVG Filter URL)',
                            defaultValue: 'url(#dropShadow)',
                            group: 'presentation',
                            index: 5
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.DoubleCircleLine': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.CurveLine': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.DrashSquare': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.ChokeDesign': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.SignalShape': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.Ampere': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },

        'examples.TableDrashSquare': {
            inputs: {
                attrs: {
                    label: {
                        text: {
                            type: 'select-box',
                            options: options.attributeLists,
                            label: 'Text',
                            group: 'text',
                            index: 1
                        },
                        fontSize: {
                            type: 'range',
                            min: 5,
                            max: 80,
                            unit: 'px',
                            label: 'Font size',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 2
                        },
                        fontFamily: {
                            type: 'select-box',
                            options: options.fontFamily,
                            label: 'Font family',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 3
                        },
                        fontWeight: {
                            type: 'select-box',
                            options: options.fontWeight,
                            label: 'Font thickness',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 4
                        },
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'text',
                            when: { ne: { 'attrs/label/text': '' } },
                            index: 5
                        }
                    },
                    path: {
                        fill: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Fill',
                            group: 'presentation',
                            index: 1
                        },
                        stroke: {
                            type: 'color-palette',
                            options: options.colorPalette,
                            label: 'Outline',
                            group: 'presentation',
                            index: 2
                        },
                        strokeWidth: {
                            type: 'range',
                            min: 0,
                            max: 30,
                            step: 1,
                            defaultValue: 1,
                            unit: 'px',
                            label: 'Outline thickness',
                            group: 'presentation',
                            when: { ne: { 'attrs/body/stroke': 'transparent' } },
                            index: 3
                        },
                        strokeDasharray: {
                            type: 'select-box',
                            options: options.strokeStyle,
                            label: 'Outline style',
                            group: 'presentation',
                            when: {
                                and: [
                                    { ne: { 'attrs/body/stroke': 'transparent' } },
                                    { ne: { 'attrs/body/strokeWidth': 0 } }
                                ]
                            },
                            index: 4
                        }
                    }
                },
                ports: {
                    items: {
                        group: 'ports',
                        type: 'list',
                        label: 'Ports',
                        item: {
                            type: 'object',
                            properties: {
                                group: {
                                    type: 'select-button-group',
                                    label: 'Group',
                                    defaultValue: 'out',
                                    options: [
                                        { value: 'in', content: 'IN' },
                                        { value: 'out', content: 'OUT' }
                                    ]
                                },
                                attrs: {
                                    portLabel: {
                                        text: { type: 'text', label: 'Label' }
                                    },
                                    portBody: {
                                        fill: {
                                            type: 'color-palette',
                                            options: options.colorPaletteReset,
                                            label: 'Override Group Fill',
                                            index: 1
                                        }
                                    }
                                }
                            }
                        }
                    },
                    groups: {
                        'in': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { inPorts: [] } } },
                                        group: 'inPorts',
                                        index: 1
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 3
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { inPorts: [] } } },
                                    group: 'inPorts',
                                    index: 4
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'inPorts',
                                index: 5,
                                overwrite: true
                            }
                        },
                        'out': {
                            attrs: {
                                portBody: {
                                    fill: {
                                        type: 'color-palette',
                                        options: options.colorPalette,
                                        label: 'Fill',
                                        when: { not: { equal: { outPorts: [] } } },
                                        group: 'outPorts',
                                        index: 2
                                    }
                                }
                            },
                            position: {
                                name: {
                                    type: 'select-box',
                                    options: options.side,
                                    label: 'Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 4
                                }
                            },
                            label: {
                                position: {
                                    type: 'select-box',
                                    options: options.portLabelPositionRectangle,
                                    label: 'Text Position',
                                    when: { not: { equal: { outPorts: [] } } },
                                    group: 'outPorts',
                                    index: 5
                                }
                            },
                            markup: {
                                type: 'select-box',
                                options: options.portMarkup,
                                label: 'Port Shape',
                                group: 'outPorts',
                                index: 6,
                                overwrite: true
                            }
                        }
                    }
                }
            },
            groups: {
                inPorts: {
                    label: 'Input Ports Defaults',
                    index: 1
                },
                outPorts: {
                    label: 'Output Ports Defaults',
                    index: 2
                },
                ports: {
                    label: 'Ports',
                    index: 3
                },
                presentation: {
                    label: 'Presentation',
                    index: 4
                },
                text: {
                    label: 'Text',
                    index: 5
                }
            }
        },
    };

})();
