/*! Rappid v3.3.0 - HTML5 Diagramming Framework - TRIAL VERSION

Copyright (c) 2021 client IO

 2021-06-28 


This Source Code Form is subject to the terms of the Rappid Trial License
, v. 2.0. If a copy of the Rappid License was not distributed with this
file, You can obtain one at http://jointjs.com/license/rappid_v2.txt
 or from the Rappid archive as was distributed by client IO. See the LICENSE file.*/


joint.setTheme('bpmn');

/* GRAPH */

var example = window.example;
var inputs = window.inputs;
var toolbarConfig = window.toolbarConfig;
var bpmn2 = joint.shapes.bpmn2;


//var graph = new joint.dia.Graph({ type: 'bpmn' });
var graph = new joint.dia.Graph;

var freeTransform = null;
var commandManager = new joint.dia.CommandManager({ graph: graph });
var keyboard = new joint.ui.Keyboard();

/* PAPER + SCROLLER */

var paper = new joint.dia.Paper({
    width: 1000,
    height: 1000,
    model: graph,
    gridSize: 5,
    async: true,
    sorting: joint.dia.Paper.sorting.APPROX,
    interactive: { linkMove: false },
    snapLabels: true,
    // Connections
    defaultLink: function () {
        return new bpmn2.Flow({
            attrs: {
                line: {
                    flowType: 'sequence'
                }
            }
        });
    },
    validateConnection: function (cellViewS, magnetS, cellViewT, magnetT, end) {
        var source = cellViewS.model;
        var target = cellViewT.model;
        // don't allow loop links
        if (source === target) return false;
        // don't allow link to link connection
        if (source.isLink()) return false;
        if (target.isLink()) return false;
        // don't allow group connections
        var sourceType = source.get('type');
        var targetType = target.get('type');
        if (sourceType === 'bpmn2.Group' || targetType === 'bpmn2.Group') return false;
        return true;
    },
    defaultAnchor: {
        name: 'perpendicular'
    },
    defaultConnectionPoint: {
        name: 'boundary',
        args: { stroke: true }
    },
    // Embedding
    embeddingMode: true,
    frontParentOnly: true,
    validateEmbedding: function (childView, parentView) {
        var parentType = parentView.model.get('type');
        var childType = childView.model.get('type');
        if (parentType === 'bpmn2.HeaderedPool' && childType !== 'bpmn2.HeaderedPool') return true;
        if (parentType === 'bpmn2.Activity' && childType === 'bpmn2.Event') return true;
        return false;
    },
    // Highlighting
    highlighting: {
        default: {
            name: 'mask',
            options: {
                attrs: {
                    'stroke': '#3498db',
                    'stroke-width': 3,
                    'stroke-linejoin': 'round'
                }
            }
        }
    }
}).on({

    'blank:pointerdown': function (evt, x, y) {
        closeTools();
        selection.startSelecting(evt, x, y);
    },

    'blank:contextmenu': function (evt, x, y) {
        paperScroller.startPanning(evt, x, y);
    },

    'cell:pointerup': function (cellView) {
        openTools(cellView);
    },
    'element:mouseenter': function (ele) {
       
    },
    'link:mouseenter': function (linkView) {
        // Open tool only if there is none yet
       
        if (linkView.hasTools()) return;

        var ns = joint.linkTools;
        var toolsView = new joint.dia.ToolsView({
            name: 'link-hover',
            tools: [
                new ns.Vertices({ vertexAdding: false }),
                new ns.SourceArrowhead(),
                new ns.TargetArrowhead()
            ]
        });

        linkView.addTools(toolsView);
    },

    'link:mouseleave': function (linkView) {
        // Remove only the hover tool, not the pointerdown tool
        if (linkView.hasTools('link-hover')) {
            linkView.removeTools();
        }
    },

    'link:connect': function (linkView) {
        // Change the link type based on the connected elements
        var link = linkView.model;
        var source = link.getSourceCell();
        var target = link.getTargetCell();
        if (!source || !target) return;
        var types = [source.get('type'), target.get('type')];
        var linkType = link.get('type');
        if (types.indexOf('bpmn2.Annotation') > -1) {
            if (linkType === 'bpmn2.AnnotationLink') return;
            replaceLink(graph, link, bpmn2.AnnotationLink);
            return;
        }
        if (types.indexOf('bpmn2.Conversation') > -1) {
            if (linkType === 'bpmn2.ConversationLink') return;
            replaceLink(graph, link, bpmn2.ConversationLink);
            return;
        }
        if (types.indexOf('bpmn2.DataObject') > -1) {
            if (linkType === 'bpmn2.DataAssociation') return;
            replaceLink(graph, link, bpmn2.DataAssociation);
            return;
        }
        if (types.indexOf('bpmn2.DataStore') > -1) {
            if (linkType === 'bpmn2.DataAssociation') return;
            replaceLink(graph, link, bpmn2.DataAssociation);
            return;
        }
        if (linkType !== 'bpmn2.Flow') {
            replaceLink(graph, link, bpmn2.Flow);
            return;
        }
    }

});

var paperScroller = new joint.ui.PaperScroller({
    autoResizePaper: true,
    padding: 50,
    paper: paper,
    scrollWhileDragging: true
});

document.getElementById('paper-container').appendChild(paperScroller.el);
paperScroller.center();

/* SELECTION */

var selection = new joint.ui.Selection({
    paper: paper,
    graph: graph,
    useModelGeometry: true,
    filter: ['bpmn2.HeaderedPool'] // don't allow to select a pool
});

/* STENCIL */

var stencil = new joint.ui.Stencil({
    graph: graph,
    paper: paper,
    width: '100%',
    height: '100%',
    dragEndClone: function (cell) {

        var clone = cell.clone();
        var type = clone.get('type');
        // some types of the elements need resizing after they are dropped
        var sizeMultiplier = { 'bpmn2.HeaderedPool': 8, 'bpmn.Choreography': 2 }[type];
        if (sizeMultiplier) {
            var originalSize = clone.get('size');
            clone.set('size', {
                width: originalSize.width * sizeMultiplier,
                height: originalSize.height * sizeMultiplier
            });
            if (type === 'bpmn2.HeaderedPool') {
                clone.set('padding', { top: 0, left: 30, right: 0, bottom: 0 });
            }
        }

        return clone;
    }
});

stencil.render();
document.getElementById('stencil-container').appendChild(stencil.el);

stencil.load([
    new bpmn2.Activity,
    new bpmn2.Gateway,
    new bpmn2.Event,
    new bpmn2.Conversation,
    //new bpmn2.DataObject,
    //new bpmn2.DataStore,
    new bpmn2.Annotation({
        attrs: {
            body: {
                fill: '#ffffff'
            }
        }
    }),
    //new bpmn2.Group({
    //    size: {
    //        width: 80,
    //        height: 80
    //    },
    //    attrs: {
    //        label: { text: 'Group' }
    //    }
    //}),
    // Legacy BPMN Shapes
    //new bpmn2.HeaderedPool({
    //    padding: { top: 0, left: 15, right: 0, bottom: 0 },
    //    size: { width: 80, height: 50 },
    //    lanes: [{ label: 'Lane 1' }, { label: 'Lane 2' }],
    //    attrs: {
    //        body: { fill: '#FFFFFF' },
    //        headerLabel: { text: 'Header' }
    //    }
    //}),
    //new joint.shapes.bpmn.Choreography({
    //    participants: ['Participant 1', 'Participant 2']
    //}),
    new joint.shapes.examples.PointMachine,
    new joint.shapes.examples.Signal,
    new joint.shapes.examples.CustomLink
   
]);

joint.layout.GridLayout.layout(stencil.getGraph(), {
    columns: 100,
    columnWidth: 'compact',
    marginX: 20,
    marginY: 20,
    columnGap: 40,
    verticalGap: 20,
    resizeToFit: false
});

stencil.on('element:drop', function (elementView) {
    // open inspector after a new element dropped from stencil
    openTools(elementView);
});

/* KEYBOARD */

keyboard.on('delete backspace', function () {
    graph.removeCells(selection.collection.toArray());
});

/* TOOLBAR */

var toolbar = new joint.ui.Toolbar({
    tools: toolbarConfig.tools,
    references: {
        paperScroller: paperScroller,
        commandManager: commandManager
    }
});

toolbar.on({
    'to-json:pointerclick': function () {
        var windowFeatures = 'menubar=no,location=no,resizable=yes,scrollbars=yes,status=no';
        var windowName = _.uniqueId('json_output');
        var jsonWindow = window.open('', windowName, windowFeatures);
        if (jsonWindow) {
            jsonWindow.document.write(JSON.stringify(graph.toJSON()));
        }
    },
    'clear:pointerclick': function () {
        graph.clear();
        paperScroller.center(500, 500);
    },
    'print:pointerclick': function () {
        paper.print();
    }
});

toolbar.render();
document.getElementById('toolbar-container').appendChild(toolbar.el);

/* TOOLTIPS */

new joint.ui.Tooltip({
    target: '[data-tooltip]',
    content: function (el) { return el.dataset.tooltip; },
    top: '.joint-toolbar',
    padding: 10,
    direction: 'top'
});

// Create tooltips for all the shapes in stencil.
stencil.getGraph().get('cells').each(function (cell) {
    new joint.ui.Tooltip({
        target: '.joint-stencil [model-id="' + cell.id + '"]',
        content: cell.get('type').split('.')[1],
        bottom: '.joint-stencil',
        direction: 'bottom',
        padding: 0
    });
});

// load an example graph
//graph.fromJSON(example);
paperScroller.positionContent('top-left', { padding: 50, useModelGeometry: true });

/* ACTIONS */

function closeTools() {
    paper.removeTools();
    joint.ui.Inspector.close();
    joint.ui.FreeTransform.clear(paper);
    freeTransform = null;
    joint.ui.Halo.clear(paper);
}

function openTools(cellView) {

    debugger;
    closeTools();
  
    var cell = cellView.model;
    var type = cell.get('type');
   
    selection.collection.reset([]);
    // Add the cell into the selection collection silently
    // so no selection box is rendered above the cellView.
    selection.collection.add(cell, { silent: true });
   
    if (cell.isElement()) {
        let minSize;

        if (cell instanceof bpmn2.HeaderedPool) {
            minSize = cell.getMinimalSize();
        } else {
            minSize = { width: 30, height: 30 }
        }

        freeTransform = new joint.ui.FreeTransform({
            cellView: cellView,
            allowOrthogonalResize: false,
            allowRotation: false,
            minWidth: minSize.width,
            minHeight: minSize.height
        }).render();

        var halo = new joint.ui.Halo({
            cellView: cellView,
            theme: 'default',
            type: 'toolbar',
            useModelGeometry: true,
            boxContent: function (cellView) {
                return cellView.model.get('type');
            }
        });
        halo.removeHandle('rotate');
        halo.removeHandle('resize');
        if (type === 'bpmn2.Group' || type === 'bpmn2.HeaderedPool') {
            halo.removeHandle('link');
            halo.removeHandle('fork');
            halo.removeHandle('unlink');
        }
        halo.render();
    }
   
    if (cell.isLink()) {

        var ns = joint.linkTools;
        var toolsView = new joint.dia.ToolsView({
            name: 'link-pointerdown',
            tools: [
                new ns.Vertices(),
                new ns.SourceAnchor(),
                new ns.TargetAnchor(),
                new ns.SourceArrowhead(),
                new ns.TargetArrowhead(),
                new ns.Segments,
                new ns.Boundary({ padding: 15 }),
                new ns.Remove({ offset: -20, distance: 40 })
            ]
        });

        cellView.addTools(toolsView);
    }

    joint.ui.Inspector.create('#inspector-container', {
        cell: cell,
        inputs: inputs[type],
        groups: {
            general: { label: type, index: 1 },
            appearance: { index: 2 },
            defaults: { index: 3 }
        }
    });
}

function replaceLink(graph, link, linkConstructor) {
    var link2 = new linkConstructor({
        source: link.source(),
        target: link.target(),
        vertices: link.vertices()
    });
    link.remove();
    link2.addTo(graph);
}

graph.on('change', function (cell, opt) {
    if (!opt.inspector || !freeTransform || cell.get('type') !== 'bpmn2.HeaderedPool') {
        return;
    }
    var minSize = cell.getMinimalSize();
    freeTransform.options.minWidth = minSize.width;
    freeTransform.options.minHeight = minSize.height;
})
