/*
 * Collection Pipelines screen: setup, Postman-style API steps, drag-and-drop
 * mapping, schedule and runs. The server validates everything again on save.
 */
(function ($) {
    'use strict';

    var root = document.getElementById('pipelineApp');
    if (!root) { return; }

    var URLS = JSON.parse(root.getAttribute('data-urls'));
    var USE_CASES = JSON.parse(root.getAttribute('data-use-cases') || '[]');

    var SOURCES = [['inflow', 'Inflow field'], ['json', 'Earlier step response'], ['record', 'Record value'],
    ['system', 'System value'], ['const', 'Fixed value']];
    var OPS = [['eq', 'equals'], ['ne', 'not equals'], ['contains', 'contains'], ['startsWith', 'starts with'],
    ['endsWith', 'ends with'], ['empty', 'is empty'], ['notEmpty', 'is not empty'],
    ['in', 'is one of (a,b,c)'], ['gt', 'greater than'], ['lt', 'less than']];
    var BODY_MODES = [['JSON', 'JSON'], ['FORM', 'x-www-form-urlencoded'], ['MULTIPART', 'form-data'], ['NONE', 'None']];
    var SYSTEM = ['NewGuid', 'UtcNow', 'Now', 'Today', 'InflowDataID', 'UseCaseID'];
    var RECORD = ['InflowDataID', 'ReviewStatusCode', 'ValidationStatusCode'];

    var S = {
        list: [], catalog: { collections: [], inflowFields: [], connectors: [] }, columnsBy: {}, records: [],   
        requests: {}, p: null, tab: 'setup', stepIndex: 0, stepTab: 'body',
        sampleRecord: '', testRecord: '', responses: {}, dirty: false, drag: null, lastTest: null, runs: [],
        open: {}, dragging: false
    };

    // --- helpers ----------------------------------------------------------------
    function esc(v) { return APS.escapeHtml(v === null || v === undefined ? '' : v); }
    function parse(text, fallback) { try { return text ? JSON.parse(text) : fallback; } catch (e) { return fallback; } }
    function getPath(obj, path) {
        return String(path).split('.').reduce(function (o, k) { return o === undefined || o === null ? undefined : o[k]; }, obj);
    }
    function opt(value, label, selected) {
        return '<option value="' + esc(value) + '"' + (String(value) === String(selected) ? ' selected' : '') + '>' + esc(label) + '</option>';
    }
    function sel(bind, list, selected, placeholder, after, attrs) {
        var known = list.some(function (o) { return String(o[0]) === String(selected); });
        return '<select data-bind="' + bind + '"' + (after ? ' data-after="' + after + '"' : '') + (attrs || '') + '>' +
            (placeholder !== null && placeholder !== undefined ? opt('', placeholder, known ? null : '') : '') +
            (selected !== '' && selected !== null && selected !== undefined && !known ? opt(selected, selected + ' (not available)', selected) : '') +
            list.map(function (o) { return opt(o[0], o[1], selected); }).join('') + '</select>';
    }
    function inp(bind, value, attrs) {
        return '<input data-bind="' + bind + '" value="' + esc(value === null || value === undefined ? '' : value) + '"' + (attrs || '') + ' />';
    }
    function field(label, html, cls) { return '<div class="pl-field ' + (cls || '') + '"><label>' + label + '</label>' + html + '</div>'; }
    function section(title, help, body) {
        return '<section class="pl-section"><div class="pl-section-title">' + esc(title) + '</div>' +
            (help ? '<p class="pl-help">' + esc(help) + '</p>' : '') + body + '</section>';
    }
    function note(text) { return '<div class="pl-note">' + esc(text) + '</div>'; }
    function addBtn(act, label, attrs) {
        return '<button type="button" class="btn btn-sm" data-act="' + act + '"' + (attrs || '') + '><i class="fa-solid fa-plus"></i>' + esc(label) + '</button>';
    }
    function removeBtn(path) {
        return '<button type="button" class="btn btn-sm pl-remove" data-act="remove" data-path="' + path + '" aria-label="Remove"><i class="fa-solid fa-trash"></i></button>';
    }
    function badge(status) {
        var map = { Draft: 'neutral', Published: 'success', Paused: 'warning', Running: 'info', Succeeded: 'success', PartiallyFailed: 'warning', Failed: 'danger', Skipped: 'neutral' };
        return '<span class="badge badge-' + (map[status] || 'neutral') + '">' + esc(status) + '</span>';
    }
    function fields() { return S.catalog.inflowFields.map(function (f) { return [f.code, f.label || f.code]; }); }
    function connectors() {
        return S.catalog.connectors.map(function (c) {
            return [c.ConnectorVersionId, c.ConnectorName + ' · v' + c.VersionNumber + ' (' + (c.HttpMethod || 'POST') + ')'];
        });
    }
    function connectorOf(versionId) {
        return S.catalog.connectors.filter(function (c) { return String(c.ConnectorVersionId) === String(versionId); })[0];
    }
    function collectionName(id) {
        var hit = S.catalog.collections.filter(function (c) { return String(c.id) === String(id); })[0];
        return hit ? hit.name : '';
    }

    function stepIndexOf(key) {
        for (var i = 0; i < S.p.steps.length; i++) { if (S.p.steps[i].key === key) { return i; } }
        return -1;
    }
    function markDirty() { S.dirty = true; $('#plDirty').prop('hidden', false); }

    function blank() {
        return {
            pipelineId: 0, pipelineCode: '', pipelineName: '', useCaseId: '', collectionId: '', status: 'Draft',
            scheduleType: 'MANUAL', intervalMinutes: 60, dailyTime: '02:00', batchSize: 50, maxRecordAttempts: 3,
            driver: { match: 'all', conditions: [] }, mapping: { columns: [], dedup: ['InflowDataID'] }, steps: []
        };
    }
    function stepPayload(s) {
        return {
            key: s.key,
            kind: s.kind || 'API',
            sourceCollectionId: Number(s.sourceCollectionId || 0),
            connectorId: Number(s.connectorId || 0),
            connectorVersionId: Number(s.connectorVersionId || 0),
            bodyMode: s.bodyMode, bindings: s.bindings, loopPath: s.loopPath, itemConditions: s.itemConditions,
            sampleResponseJson: s.sampleResponseJson || null
        };
    }

    // --- JSON path (same grammar as the server) -------------------------------------
    function jpParse(path) {
        path = String(path || '').trim();
        if (!/^[$@]/.test(path)) { return null; }
        var segs = [], i = 1;
        while (i < path.length) {
            if (path[i] === '.') {
                var deep = path[i + 1] === '.';
                i += deep ? 2 : 1;
                if (path[i] === '*') { segs.push({ w: true, deep: deep }); i++; continue; }
                var start = i;
                while (i < path.length && path[i] !== '.' && path[i] !== '[') { i++; }
                if (i === start) { return null; }
                segs.push({ p: path.slice(start, i), deep: deep });
            } else if (path[i] === '[') {
                var end = path.indexOf(']', i);
                if (end < 0) { return null; }
                var inner = path.slice(i + 1, end).trim();
                if (inner === '*') { segs.push({ w: true, deep: false }); }
                else if (/^(['"]).*\1$/.test(inner)) { segs.push({ p: inner.slice(1, -1), deep: false }); }
                else if (/^\d+$/.test(inner)) { segs.push({ i: Number(inner) }); }
                else { return null; }
                i = end + 1;
            } else { return null; }
        }
        return { root: path[0], segs: segs };
    }
    function descendants(n, out) {
        out.push(n);
        if (n && typeof n === 'object') { Object.keys(n).forEach(function (k) { descendants(n[k], out); }); }
        return out;
    }
    function jpSelect(rootNode, item, path) {
        var parsed = jpParse(path);
        if (!parsed) { return []; }
        var cur = [parsed.root === '@' ? item : rootNode].filter(function (x) { return x !== undefined && x !== null; });
        parsed.segs.forEach(function (s) {
            var next = [];
            cur.forEach(function (n) {
                if (s.p !== undefined) {
                    (s.deep ? descendants(n, []) : [n]).forEach(function (x) {
                        if (x && typeof x === 'object' && !Array.isArray(x) && Object.prototype.hasOwnProperty.call(x, s.p)) { next.push(x[s.p]); }
                    });
                } else if (s.i !== undefined) {
                    if (Array.isArray(n) && s.i < n.length) { next.push(n[s.i]); }
                } else {
                    (s.deep ? descendants(n, []) : [n]).forEach(function (x) {
                        if (x && typeof x === 'object') { Object.keys(x).forEach(function (k) { next.push(x[k]); }); }
                    });
                }
            });
            cur = next;
        });
        return cur;
    }

    // --- JSON tree ----------------------------------------------------------------------
    function keyPath(base, key) {
        return /^[A-Za-z_][A-Za-z0-9_]*$/.test(key) ? base + '.' + key : base + "['" + String(key).replace(/'/g, '') + "']";
    }
    function tree(node, path, o, name, depth) {
        depth = depth || 0;
        var label = '<span class="pl-k">' + esc(name === undefined ? (path.charAt(0) === '@' ? 'item' : 'response') : name) + '</span>';
        var dragAttrs = o.mode === 'response'
            ? ' draggable="true" data-drag="json" data-step="' + esc(o.step) + '" data-path="' + esc(path) +
            '" title="' + esc(path) + ' (drag to map this whole branch as JSON)"'
            : '';
        if (node !== null && typeof node === 'object') {
            var isArr = Array.isArray(node);
            var keys = isArr ? node.slice(0, 3).map(function (_, k) { return k; }) : Object.keys(node);
            var loopBtn = isArr && o.loopable
                ? '<button type="button" class="pl-mini" data-act="set-loop" data-loop="' + esc(path + '[*]') + '" data-stepkey="' + esc(o.step) + '">Loop here</button>'
                : '';
            var children = keys.map(function (k) {
                return tree(node[k], isArr ? path + '[' + k + ']' : keyPath(path, k), o, isArr ? '[' + k + ']' : k, depth + 1);
            }).join('');
            var more = isArr && node.length > 3 ? '<div class="pl-more">… ' + (node.length - 3) + ' more</div>' : '';
            var nodeKey = (o.step || o.mode || '') + '|' + path;
            var isOpen = Object.prototype.hasOwnProperty.call(S.open, nodeKey) ? S.open[nodeKey] : depth < 3;

            return '<details class="pl-node" data-node="' + esc(nodeKey) + '"' + (isOpen ? ' open' : '') + '><summary' + dragAttrs + '>' + label +
                '<span class="pl-type">' + (isArr ? '[' + node.length + ']' : '{ }') + '</span>' + loopBtn +
                '</summary><div class="pl-children">' + children + more + '</div></details>';
        }

        var text = node === null ? 'null' : String(node);
        var shown = esc(text.length > 80 ? text.slice(0, 80) + '…' : text); 
        var attrs;

        if (o.mode === 'request') {
            var bound = o.bound.indexOf(path) >= 0;
            attrs = ' class="pl-leaf pl-pick' + (bound ? ' is-bound' : '') + '" data-act="pick-target" data-target="' + esc(path) +
                '" data-current="' + esc(text) + '" title="' + (bound ? 'Dynamic. Click to make it fixed again: ' : 'Click to make dynamic: ') + esc(path) + '"';
        } else if (o.mode === 'response') {
            attrs = ' class="pl-leaf pl-drag" draggable="true" data-drag="json" data-step="' + esc(o.step) + '" data-path="' + esc(path) +
                '" title="' + esc(path) + ' (drag onto a column or value; click to copy)"';
        } else {
            attrs = ' class="pl-leaf"';
        }
        return '<div' + attrs + '>' + label + '<span class="pl-v">' + shown + '</span></div>';
    }

    // --- loading ------------------------------------------------------------------------
    function loadList(openId) {
        return $.getJSON(URLS.list).done(function (r) {
            if (!r.success) { return; }
            S.list = r.data || [];
            renderList();
            if (openId) { openPipeline(openId, true); }
        });
    }
    function loadCatalog() {
        if (!S.p || !S.p.useCaseId) { return $.Deferred().resolve().promise(); }
        return $.getJSON(URLS.catalog, { useCaseId: S.p.useCaseId }).done(function (r) {
            if (r.success) { S.catalog = r.data; }
        });
    }
    function loadColumns(collectionId) {
        collectionId = Number(collectionId || (S.p && S.p.collectionId) || 0);
        if (!collectionId || S.columnsBy[collectionId]) { return $.Deferred().resolve().promise(); }

        return $.getJSON(URLS.columns, { collectionId: collectionId }).done(function (r) {
            if (r.success) {
                S.columnsBy[collectionId] = (r.data || []).filter(function (c) { return Number(c.IsSystemColumn) !== 1; });
            }
        });
    }
    function loadRecords() {
        if (!S.p || !S.p.useCaseId) { S.records = []; return $.Deferred().resolve().promise(); }
        return $.getJSON(URLS.records, { useCaseId: S.p.useCaseId }).done(function (r) {
            if (r.success) { S.records = r.data || []; }
        });
    }
    function loadRequest(versionId) {
        if (!versionId || S.requests[versionId]) { return $.Deferred().resolve().promise(); }
        return $.getJSON(URLS.request, { versionId: versionId }).done(function (r) {
            if (r.success) { S.requests[versionId] = r.data; }
        });
    }
    function loadRuns() {
        if (!S.p || !S.p.pipelineId) { S.runs = []; return $.Deferred().resolve().promise(); }
        return $.getJSON(URLS.runs, { id: S.p.pipelineId }).done(function (r) {
            if (r.success) { S.runs = r.data || []; }
        });
    }

    function confirmLeave() { return !S.dirty || window.confirm('Discard the unsaved changes to this pipeline?'); }

    function openPipeline(id, skipConfirm) {
        if (!skipConfirm && !confirmLeave()) { return; }
        $.getJSON(URLS.get, { id: id }).done(function (r) {
            if (!r.success) { APS.toast(r.message, 'error'); return; }

            var x = r.data.pipeline;
            var p = blank();
            p.pipelineId = x.PipelineId;
            p.pipelineCode = x.PipelineCode;
            p.pipelineName = x.PipelineName;
            p.useCaseId = String(x.UseCaseID);
            p.collectionId = String(x.CollectionID);
            p.status = x.Status;
            p.scheduleType = x.ScheduleType;
            p.intervalMinutes = x.IntervalMinutes || 60;
            p.dailyTime = x.DailyTime ? String(x.DailyTime).substring(0, 5) : '02:00';
            p.batchSize = x.BatchSize;
            p.maxRecordAttempts = x.MaxRecordAttempts;
            p.driver = parse(x.DriverJson, { match: 'all', conditions: [] });
            p.mapping = parse(x.MappingJson, { columns: [], dedup: [] });
            p.driver.conditions = p.driver.conditions || [];
            p.mapping.columns = p.mapping.columns || [];
            p.mapping.dedup = p.mapping.dedup || [];
            p.steps = (r.data.steps || []).map(function (s) {
                return {
                    key: s.StepKey, kind: s.StepKind || 'API', sourceCollectionId: s.SourceCollectionId || 0,
                    connectorId: s.ConnectorId, connectorVersionId: s.ConnectorVersionId,
                    bodyMode: s.BodyMode || 'JSON', bindings: parse(s.BindingsJson, []), loopPath: s.LoopPath || '',
                    itemConditions: parse(s.ItemConditionsJson, []) || [], sampleResponseJson: s.SampleResponseJson || ''
                };
            });

            S.p = p;
            S.responses = {};
            S.stepIndex = 0;
            S.dirty = false;
            S.lastTest = null;
            p.steps.forEach(function (s) {
                if (s.sampleResponseJson) { S.responses[s.key] = { json: parse(s.sampleResponseJson, null), saved: true }; }
            });

            S.columnsBy = {};
            var columnLoads = [loadColumns(p.collectionId)].concat((p.mapping.outputs || []).map(function (o) { return loadColumns(o.collectionId); }));
            $.when.apply($, [loadCatalog(), loadRecords(), loadRuns()].concat(columnLoads)).always(function () {
                $.when.apply($, p.steps.map(function (s) { return loadRequest(s.connectorVersionId); })).always(render);
            });
        });
    }

    // --- rendering ----------------------------------------------------------------------
    function render() { renderList(); renderEditor(); }

    function renderList() {
        $('#plList').html(S.list.length ? S.list.map(function (x) {
            return '<button type="button" class="pl-item' + (S.p && S.p.pipelineId === x.PipelineId ? ' active' : '') +
                '" data-act="open" data-id="' + x.PipelineId + '"><strong>' + esc(x.PipelineName) + '</strong>' +
                '<span class="pl-sub">' + esc(x.PipelineCode) + ' · ' + esc(x.TableName) + '</span>' +
                '<span class="pl-item-meta">' + badge(x.Status) + (x.LastRunStatus ? badge(x.LastRunStatus) : '') + '</span></button>';
        }).join('') : '<div class="pl-note">No pipelines yet.</div>');
    }

    function renderEditor() {
        var p = S.p;
        if (!p) {
            $('#plEditor').html('<div class="pl-welcome"><i class="fa-solid fa-diagram-project"></i>' +
                '<strong>Fill a collection from APIs</strong><span>Select a pipeline, or create one with <b>New</b>.</span></div>');
            return;
        }

        var tabs = [['setup', '1', 'Setup'], ['steps', '2', 'API steps'], ['mapping', '3', 'Mapping'], ['schedule', '4', 'Schedule & runs']];
        var actions =
            '<button type="button" class="btn" data-act="save"><i class="fa-solid fa-floppy-disk"></i>Save draft</button>' +
            (p.status === 'Published'
                ? '<button type="button" class="btn" data-act="run"><i class="fa-solid fa-play"></i>Run now</button>' +
                '<button type="button" class="btn" data-act="pause"><i class="fa-solid fa-pause"></i>Pause</button>'
                : p.status === 'Paused'
                    ? '<button type="button" class="btn" data-act="resume"><i class="fa-solid fa-play"></i>Resume</button>'
                    : '') +
            '<button type="button" class="btn btn-primary" data-act="publish"' + (p.pipelineId && p.status === 'Draft' ? '' : ' disabled') + '>' +
            '<i class="fa-solid fa-rocket"></i>Publish</button>';

        var head = '<div class="pl-head"><div><h2 class="card-title">' + esc(p.pipelineName || 'New pipeline') + '</h2>' +
            '<div class="pl-head-meta">' + badge(p.status) +
            '<span class="badge badge-warning" id="plDirty"' + (S.dirty ? '' : ' hidden') + '>Unsaved changes</span></div></div>' +
            '<div class="pl-actions">' + actions + '</div></div>';

        var strip = '<div class="pl-tabs">' + tabs.map(function (t) {
            return '<button type="button" class="pl-tab' + (S.tab === t[0] ? ' active' : '') + '" data-act="tab" data-tab="' + t[0] + '">' +
                '<span class="pl-num">' + t[1] + '</span>' + t[2] + '</button>';
        }).join('') + '</div>';

        var body = S.tab === 'steps' ? stepsHtml() : S.tab === 'mapping' ? mappingHtml() : S.tab === 'schedule' ? scheduleHtml() : setupHtml();
        $('#plEditor').html(head + strip + '<div class="pl-body">' + body + '</div>');
    }

    // --- tab 1: setup -------------------------------------------------------------------
    function conditionRow(prefix, c, stepKeys, isItem) {
        var sources = isItem
            ? [['json', 'Response item'], ['inflow', 'Inflow field'], ['record', 'Record value']]
            : [['inflow', 'Inflow field'], ['record', 'Record value']];
        var left;
        if (c.source === 'json') {
            left = sel(prefix + '.step', stepKeys.map(function (k) { return [k, k]; }), c.step, null) +
                inp(prefix + '.path', c.path, ' placeholder="@.document.typeOfContent" data-drop="cond" data-prefix="' + prefix + '"');
        } else if (c.source === 'record') {
            left = sel(prefix + '.path', RECORD.map(function (v) { return [v, v]; }), c.path, 'Select');
        } else {
            left = sel(prefix + '.path', fields(), c.path, 'Select field');
        }
        var value = c.op === 'empty' || c.op === 'notEmpty' ? '' : inp(prefix + '.value', c.value, ' placeholder="Value"');
        return '<div class="pl-row">' + sel(prefix + '.source', sources, c.source, null, 'condSource') + left +
            sel(prefix + '.op', OPS, c.op, null, 'rerender') + value + removeBtn(prefix) + '</div>';
    }

    function setupHtml() {
        var p = S.p;
        var collections = S.catalog.collections.map(function (c) { return [c.id, c.name + ' (' + c.table + ')']; });

        var basics = '<div class="pl-grid">' +
            field('Pipeline name', inp('pipelineName', p.pipelineName, ' maxlength="150"')) +
            field('Code', inp('pipelineCode', p.pipelineCode, ' maxlength="50" placeholder="e.g. CWA_IMAGES"')) +
            field('Use case', sel('useCaseId', USE_CASES.map(function (u) { return [u.id, u.label]; }), p.useCaseId, 'Select', 'useCase')) +
            field('Target collection', sel('collectionId', collections, p.collectionId, p.useCaseId ? 'Select' : 'Select a use case first', 'collection')) +
            '</div>' +
            (p.useCaseId && !collections.length ? note('This use case has no collection with a created table yet. Create one in Configuration → Collections.') : '');

        var driver =
            '<div class="pl-inline">Process records that meet ' +
            sel('driver.match', [['all', 'all'], ['any', 'any']], p.driver.match) + ' of these conditions:</div>' +
            p.driver.conditions.map(function (c, i) { return conditionRow('driver.conditions.' + i, c, [], false); }).join('') +
            (p.driver.conditions.length ? '' : note('No conditions: every record of the use case is processed.')) +
            addBtn('add-driver', 'Add condition');

        return section('Pipeline', '', basics) +
            section('Which records to process',
                'Each InflowData record of the use case is processed once. Records that do not meet the conditions are skipped and checked again later.',
                driver);
    }

    // --- tab 2: API steps ---------------------------------------------------------------
    function sourceControls(prefix, obj, stepIdx) {
        var earlier = S.p.steps.slice(0, stepIdx).map(function (s) { return s.key; });
        var value;
        switch (obj.source) {
            case 'inflow':
                value = sel(prefix + '.value', fields(), obj.value, 'Select field');
                break;
            case 'json':
                value = earlier.length
                    ? sel(prefix + '.step', earlier.map(function (k) { return [k, k]; }), obj.step, 'Step') +
                    inp(prefix + '.value', obj.value, ' placeholder="@.document.name" data-drop="binding" data-prefix="' + prefix + '"')
                    : '<span class="pl-hint">No earlier step to take a value from.</span>';
                break;
            case 'record':
                value = sel(prefix + '.value', RECORD.map(function (v) { return [v, v]; }), obj.value, 'Select');
                break;
            case 'system':
                value = sel(prefix + '.value', SYSTEM.map(function (v) { return [v, v]; }), obj.value, 'Select');
                break;
            default:
                value = inp(prefix + '.value', obj.value, ' placeholder="Fixed value" data-drop="binding" data-prefix="' + prefix + '"');
        }
        return sel(prefix + '.source', SOURCES, obj.source || 'const', null, 'source') + value;
    }

    function bindingRow(i, j, b) {
        var prefix = 'steps.' + i + '.bindings.' + j;
        var target = inp(prefix + '.target', b.target, ' class="pl-name" placeholder="' + (b.in === 'body' ? '$.path.to.node' : 'Name') + '"');
        var kind = b.in === 'form' && S.p.steps[i].bodyMode === 'MULTIPART'
            ? sel(prefix + '.kind', [['text', 'Text'], ['file', 'File path']], b.kind || 'text') : '';
        return '<div class="pl-row">' + target + kind + sourceControls(prefix, b, i) + removeBtn(prefix) + '</div>';
    }

    function bodyHtml(i) {
        var s = S.p.steps[i];
        var modes = '<div class="pl-radios">' + BODY_MODES.map(function (m) {
            return '<label><input type="radio" name="plBody' + i + '" data-bind="steps.' + i + '.bodyMode" data-after="bodyMode" value="' + m[0] + '"' +
                (s.bodyMode === m[0] ? ' checked' : '') + ' />' + m[1] + '</label>';
        }).join('') + '</div>';

        if (s.bodyMode === 'NONE') { return modes + note('This request has no body.'); }

        if (s.bodyMode === 'JSON') {
            var req = S.requests[s.connectorVersionId];
            var tpl = req ? parse(req.payloadTemplate, undefined) : undefined;
            var bound = s.bindings.filter(function (b) { return b.in === 'body'; }).map(function (b) { return b.target; });

            var left = !req ? note('Choose a connector to see its request body.')
                : tpl === undefined
                    ? '<pre class="pl-raw">' + esc(req.payloadTemplate || '(empty)') + '</pre>' +
                    note('This template is not plain JSON, so nodes cannot be picked. Add dynamic values by path on the right.')
                    : '<div class="pl-tree">' + tree(tpl, '$', { mode: 'request', bound: bound }) + '</div>';

            var rows = s.bindings.map(function (b, j) { return b.in === 'body' ? bindingRow(i, j, b) : ''; }).join('');

            return modes + '<div class="pl-cols">' +
                '<div><div class="pl-label">Request body: click a value to make it dynamic</div>' + left + '</div>' +
                '<div><div class="pl-label">Dynamic values</div>' + (rows || note('Nothing is dynamic yet. Every request would be identical.')) +
                addBtn('add-binding', 'Add by path', ' data-in="body"') + '</div></div>';
        }

        var formRows = s.bindings.map(function (b, j) { return b.in === 'form' ? bindingRow(i, j, b) : ''; }).join('');
        return modes + '<div class="pl-label">Form fields</div>' + (formRows || note('No form fields yet.')) +
            addBtn('add-binding', 'Add field', ' data-in="form"') +
            (s.bodyMode === 'MULTIPART' ? note('File fields take a full file path. Only files inside the document folders can be sent.') : '');
    }

    function namedHtml(i, where, label, statics) {
        var s = S.p.steps[i];
        var staticRows = (statics || []).map(function (x) {
            return '<div class="pl-row pl-static"><code>' + esc(x.name) + '</code><span class="pl-hint">' + esc(x.value || '') + '</span>' +
                '<span class="badge badge-neutral">connector</span></div>';
        }).join('');
        var rows = s.bindings.map(function (b, j) { return b.in === where ? bindingRow(i, j, b) : ''; }).join('');
        return (staticRows ? '<div class="pl-label">Set by the connector</div>' + staticRows : '') +
            '<div class="pl-label">' + label + ' values set by this pipeline</div>' + (rows || note('None.')) +
            addBtn('add-binding', 'Add ' + label.toLowerCase(), ' data-in="' + where + '"');
    }

    function responseHtml(i) {
        var s = S.p.steps[i];
        var r = S.responses[s.key];
        var head = '<div class="pl-label">Response</div>';

        if (!r) { return head + note('Choose a sample record and press Send. The response is kept as this step\'s sample for mapping.'); }
        if (r.error) {
            return head + '<div class="pl-resp-meta"><span class="badge badge-danger">' + esc(r.statusCode || 'Error') + '</span>' +
                '<span class="pl-hint">' + (r.durationMs || 0) + ' ms</span></div><div class="callout callout-danger">' + esc(r.error) + '</div>';
        }

        var meta = r.saved
            ? '<span class="badge badge-neutral">Saved sample</span>'
            : '<span class="badge badge-success">' + esc(r.statusCode) + '</span><span class="pl-hint">' + (r.durationMs || 0) + ' ms' +
            (r.loopItems !== null && r.loopItems !== undefined ? ' · ' + r.loopItems + ' loop item(s)' : '') + '</span>';

        return head + '<div class="pl-resp-meta">' + meta + '</div>' +
            (r.json ? '<div class="pl-tree">' + tree(r.json, '$', { mode: 'response', step: s.key, loopable: true }) + '</div>' : note('Empty response.'));
    }

    function stepsHtml() {
        var p = S.p;
        if (!p.useCaseId) { return note('Choose the use case on the Setup tab first.'); }

        var bar = '<div class="pl-steps-bar">' + p.steps.map(function (s, i) {
            return '<button type="button" class="pl-step' + (i === S.stepIndex ? ' active' : '') + '" data-act="step" data-index="' + i + '">' +
                '<span class="pl-num">' + (i + 1) + '</span>' + esc(s.key) + '</button>' +
                (i < p.steps.length - 1 ? '<i class="fa-solid fa-arrow-right pl-arrow"></i>' : '');
        }).join('') + addBtn('add-step', 'Add step') + '</div>';

        if (!p.steps.length) {
            return bar + note('Add the first API call. Each step calls one published connector; a loop path turns one response into many items, and each item runs the next step.');
        }

        var i = Math.min(S.stepIndex, p.steps.length - 1);
        var s = p.steps[i];
        var pre = 'steps.' + i;
        var req = S.requests[s.connectorVersionId];

        var urlbar = '<div class="pl-urlbar">' +
            (isRows
                ? '<span class="pl-method pl-method-rows">ROWS</span><span class="pl-url">' +
                esc('Every row of ' + (collectionName(s.sourceCollectionId) || 'the collection') + ' for this record') + '</span>'
                : req ? '<span class="pl-method">' + esc(req.method || 'POST') + '</span><span class="pl-url">' + esc((req.baseUrl || '') + (req.endpoint || '')) + '</span>'
                    : '<span class="pl-url pl-hint">Choose a connector</span>') +
            '<select id="plSample" aria-label="Sample record"><option value="">Sample record…</option>' +
            S.records.map(function (r) { return opt(r.id, r.label, S.sampleRecord); }).join('') + '</select>' +
            '<button type="button" class="btn btn-primary" data-act="send"><i class="fa-solid fa-paper-plane"></i>Send</button></div>';

        var isRows = (s.kind || 'API') === 'ROWS';

        var top = '<div class="pl-grid">' +
            field('Step key', inp(pre + '.key', s.key, ' maxlength="30" data-after="stepKey"')) +
            field('Step type', sel(pre + '.kind', [['API', 'API call'], ['ROWS', 'Collection rows']], s.kind || 'API', null, 'stepKind')) +
            (isRows
                ? field('Source collection',
                    sel(pre + '.sourceCollectionId',
                        S.catalog.collections.map(function (c) { return [c.id, c.name + ' (' + c.table + ')']; }),
                        s.sourceCollectionId || '', 'Select a collection', 'rerender', ' data-type="number"'), 'pl-wide')
                : field('Connector', sel(pre + '.connectorVersionId', connectors(), s.connectorVersionId || '', 'Select a published connector', 'connector'), 'pl-wide')) +
            field('Loop over (optional)', inp(pre + '.loopPath', s.loopPath, ' placeholder="$..correspondences[*]"')) +
            '<div class="pl-field pl-step-tools"><label>&nbsp;</label><div>' +
            '<button type="button" class="btn btn-sm" data-act="move-step" data-dir="-1"' + (i === 0 ? ' disabled' : '') + ' aria-label="Move left"><i class="fa-solid fa-arrow-left"></i></button>' +
            '<button type="button" class="btn btn-sm" data-act="move-step" data-dir="1"' + (i === p.steps.length - 1 ? ' disabled' : '') + ' aria-label="Move right"><i class="fa-solid fa-arrow-right"></i></button>' +
            '<button type="button" class="btn btn-sm" data-act="remove-step" aria-label="Remove step"><i class="fa-solid fa-trash"></i></button>' +
            '</div></div></div>';

        var count = function (where) { return s.bindings.filter(function (b) { return b.in === where; }).length; };
        var subtabs = isRows
            ? [['conditions', 'Item conditions', s.itemConditions.length]]
            : [['body', 'Body', count('body') + count('form')], ['params', 'Params', count('query')],
            ['headers', 'Headers', count('header')], ['path', 'Path', count('path')],
            ['conditions', 'Item conditions', s.itemConditions.length]];

        if (isRows) { S.stepTab = 'conditions'; }

        var sub = '<div class="pl-subtabs">' + subtabs.map(function (t) {
            return '<button type="button" class="pl-subtab' + (S.stepTab === t[0] ? ' active' : '') + '" data-act="subtab" data-subtab="' + t[0] + '">' +
                t[1] + (t[2] ? ' <span class="pl-count">' + t[2] + '</span>' : '') + '</button>';
        }).join('') + '</div>';

        var panel;
        if (S.stepTab === 'params') {
            panel = namedHtml(i, 'query', 'Query', req && req.query);
        } else if (S.stepTab === 'headers') {
            panel = namedHtml(i, 'header', 'Header', req && req.headers);
        } else if (S.stepTab === 'path') {
            var holders = ((req && req.endpoint) || '').match(/\{\{\s*[A-Za-z0-9_.]+\s*\}\}/g) || [];
            panel = (holders.length ? note('Placeholders in the endpoint: ' + holders.join(', ')) : note('The endpoint has no {{placeholders}}.')) +
                namedHtml(i, 'path', 'Path', null);
        } else if (S.stepTab === 'conditions') {
            var keys = p.steps.slice(0, i + 1).map(function (x) { return x.key; });
            panel = note('Only loop items that meet these conditions continue to the next step or become rows.') +
                s.itemConditions.map(function (c, j) { return conditionRow(pre + '.itemConditions.' + j, c, keys, true); }).join('') +
                addBtn('add-cond', 'Add condition');
        } else {
            panel = bodyHtml(i);
        }

        return bar + top + urlbar + '<div class="pl-split"><div class="pl-pane">' + sub + panel + '</div>' +
            '<div class="pl-pane">' + responseHtml(i) + '</div></div>';
    }

    // --- tab 3: mapping -----------------------------------------------------------------
    //function findMap(column) {
    //    return S.p.mapping.columns.filter(function (m) { return m.column === column; })[0];
    //}
    //function ensureMap(column) {
    //    var m = findMap(column);
    //    if (!m) { m = { column: column, source: 'json' }; S.p.mapping.columns.push(m); }
    //    return m;
    //}
    //function kindOf(m) { return !m ? '' : m.source === 'file' ? 'file' : m.source === 'const' ? 'const' : 'value'; }
    //function describe(m) {
    //    if (m.source === 'json' && m.path) {
    //        var st = S.p.steps[stepIndexOf(m.step)];
    //        var warn = st && !st.loopPath && m.path.charAt(0) === '@'
    //            ? '<span class="badge badge-warning" title="This step has no loop, so @ means the whole response. Drag the value again from the tree.">Check path</span>'
    //            : '';
    //        return '<span class="badge badge-info">' + esc(m.step) + '</span><code>' + esc(m.path) + '</code>' + warn;
    //    }
    //    if (m.source === 'inflow' && m.value) {
    //        var f = S.catalog.inflowFields.filter(function (x) { return x.code === m.value; })[0];
    //        return '<span class="badge badge-neutral">Inflow</span>' + esc(f ? f.label : m.value);
    //    }
    //    if ((m.source === 'system' || m.source === 'record') && m.value) {
    //        return '<span class="badge badge-neutral">' + (m.source === 'system' ? 'System' : 'Record') + '</span>' + esc(m.value);
    //    }
    //    return '';
    //}

    //function stepSourceHtml(s) {
    //    var r = S.responses[s.key];
    //    var json = r && r.json;
    //    var title = '<summary><i class="fa-solid fa-plug"></i>Step ' + esc(s.key) + '</summary>';
    //    if (!json) { return '<details class="pl-src">' + title + note('Send this step on the API steps tab to get a sample.') + '</details>'; }

    //    var body = '';
    //    if (s.loopPath) {
    //        var item = jpSelect(json, null, s.loopPath)[0];
    //        body += '<div class="pl-label">Each loop item (@)</div>' +
    //            (item !== undefined ? '<div class="pl-tree">' + tree(item, '@', { mode: 'response', step: s.key }) + '</div>'
    //                : note('The loop path matched nothing in the sample.'));
    //    }
    //    body += '<div class="pl-label">Whole response ($)</div><div class="pl-tree">' + tree(json, '$', { mode: 'response', step: s.key }) + '</div>';
    //    return '<details class="pl-src" open>' + title + body + '</details>';
    //}

    //function fileEditor(name, m) {
    //    var f = m.file || {};
    //    var tokens = ['UseCaseName', 'UseCaseCode'].concat(S.p.mapping.columns
    //        .filter(function (x) { return x.source !== 'file'; }).map(function (x) { return x.column; }));

    //    return '<div class="pl-file">' +
    //        '<div class="pl-dropzone' + (f.contentPath ? ' is-set' : '') + '" data-drop="file-content" data-column="' + esc(name) + '">' +
    //        '<span class="pl-sub">Content (base64)</span>' +
    //        (f.contentPath ? '<code>' + esc(f.step + '  ' + f.contentPath) + '</code>' : '<span>Drop the content node</span>') + '</div>' +
    //        '<div class="pl-dropzone' + (f.namePath ? ' is-set' : '') + '" data-drop="file-name" data-column="' + esc(name) + '">' +
    //        '<span class="pl-sub">File name</span>' +
    //        (f.namePath ? '<code>' + esc(f.step + '  ' + f.namePath) + '</code>' : '<span>Drop the file-name node</span>') + '</div>' +
    //        '<label class="pl-sub">Folder under the collection file root</label>' +
    //        '<input class="pl-folder" data-column="' + esc(name) + '" value="' + esc(f.folder || '') + '" placeholder="{UseCaseName}/{ClientName}/{CompanyCode}/{PolicyID}" />' +
    //        '<div class="pl-tokens">' + tokens.map(function (t) {
    //            return '<button type="button" class="pl-mini" data-act="add-token" data-column="' + esc(name) + '" data-token="' + esc(t) + '">{' + esc(t) + '}</button>';
    //        }).join('') + '</div></div>';
    //}

    //function columnRow(c) {
    //    var name = c.ColumnName;
    //    var m = findMap(name);
    //    var length = c.ColumnLength === null || c.ColumnLength === undefined ? '' : '(' + (Number(c.ColumnLength) === -1 ? 'max' : c.ColumnLength) + ')';
    //    var kinds = [['', 'Not mapped'], ['value', 'From a value'], ['const', 'Fixed value'], ['file', 'File (save content)']];

    //    var cell;
    //    if (m && m.source === 'file') {
    //        cell = fileEditor(name, m);
    //    } else if (m && m.source === 'const') {
    //        cell = '<input class="pl-const" data-column="' + esc(name) + '" value="' + esc(m.value || '') + '" placeholder="Fixed value" />';
    //    } else if (m) {
    //        var text = describe(m);
    //        cell = '<div class="pl-dropzone' + (text ? ' is-set' : '') + '" data-drop="column" data-column="' + esc(name) + '">' +
    //            (text ? text + '<button type="button" class="pl-mini" data-act="clear-map" data-column="' + esc(name) + '" aria-label="Clear">×</button>'
    //                : '<span>Drop a value here</span>') + '</div>';
    //    } else {
    //        cell = '<div class="pl-dropzone" data-drop="column" data-column="' + esc(name) + '"><span>Drop a value here</span></div>';
    //    }

    //    return '<tr><td><strong>' + esc(name) + '</strong><span class="pl-sub">' + esc((c.DataTypeName || '') + length) +
    //        (Number(c.IsNullable) === 1 ? '' : ' · required') + '</span></td>' +
    //        '<td><select class="pl-kind" data-column="' + esc(name) + '">' +
    //        kinds.map(function (k) { return opt(k[0], k[1], kindOf(m)); }).join('') + '</select>' + cell + '</td>' +
    //        '<td class="pl-center"><input type="checkbox" class="pl-dedup" data-column="' + esc(name) + '"' +
    //        (S.p.mapping.dedup.indexOf(name) >= 0 ? ' checked' : '') + ' aria-label="Duplicate check" /></td></tr>';
    //}

    //function mappingHtml() {
    //    var p = S.p;
    //    if (!p.collectionId) { return note('Choose the target collection on the Setup tab first.'); }
    //    if (!S.columns.length) { return note('Loading the collection columns…'); }

    //    var chips = function (kind, list) {
    //        return list.map(function (x) {
    //            return '<span class="pl-chip" draggable="true" data-drag="' + kind + '" data-value="' + esc(x[0]) + '">' + esc(x[1]) + '</span>';
    //        }).join('');
    //    };

    //    var sources = p.steps.map(stepSourceHtml).join('') +
    //        '<details class="pl-src" open><summary><i class="fa-solid fa-table-list"></i>Inflow fields</summary>' +
    //        '<div class="pl-chips">' + chips('inflow', fields()) + '</div></details>' +
    //        '<details class="pl-src"><summary><i class="fa-solid fa-gear"></i>System and record values</summary>' +
    //        '<div class="pl-chips">' + chips('system', SYSTEM.map(function (v) { return [v, v]; })) +
    //        chips('record', RECORD.map(function (v) { return [v, 'Record ' + v]; })) + '</div></details>';

    //    var rows = S.columns.map(columnRow).join('') +
    //        '<tr class="pl-sysrow"><td><strong>InflowDataID</strong><span class="pl-sub">automatic</span></td>' +
    //        '<td><span class="pl-hint">Set from the record being processed.</span></td>' +
    //        '<td class="pl-center"><input type="checkbox" class="pl-dedup" data-column="InflowDataID"' +
    //        (p.mapping.dedup.indexOf('InflowDataID') >= 0 ? ' checked' : '') + ' aria-label="Duplicate check" /></td></tr>';

    //    return '<div class="pl-map">' +
    //        '<div class="pl-map-src"><div class="pl-label">Drag values from here</div>' + sources + '</div>' +
    //        '<div class="pl-map-cols"><div class="pl-label">Collection columns</div>' +
    //        '<div class="pl-auto"><i class="fa-solid fa-lock"></i>Filled automatically: <code>InflowDataID</code> <code>UseCaseID</code> ' +
    //        '<code>CreatedBy</code>; IsActive and CreatedDate use their defaults.</div>' +
    //        '<div class="table-wrap"><table class="pl-coltable"><thead><tr><th>Column</th><th>Value</th>' +
    //        '<th title="A row is skipped when all ticked columns match an existing row">Duplicate check</th></tr></thead>' +
    //        '<tbody>' + rows + '</tbody></table></div></div></div>';
    //}
    // --- outputs -----------------------------------------------------------------------
    function outputs() {
        var m = S.p.mapping;
        if (!m.outputs) { m.outputs = []; }

        // Old shape: one output after the last step, into the pipeline's collection.
        if (!m.outputs.length && (m.columns || []).length) {
            m.outputs.push({
                step: S.p.steps.length ? S.p.steps[S.p.steps.length - 1].key : '',
                collectionId: Number(S.p.collectionId || 0),
                columns: m.columns,
                dedup: m.dedup || ['InflowDataID']
            });
            m.columns = [];
            m.dedup = [];
        }
        return m.outputs;
    }

    function outputAt(index) { return outputs()[Number(index)]; }

    function findMap(o, column) {
        return (o.columns || []).filter(function (m) { return m.column === column; })[0];
    }
    function columnIndex(o, column) {
        for (var i = 0; i < (o.columns || []).length; i++) {
            if (o.columns[i].column === column) { return i; }
        }
        return -1;
    }
    function ensureMap(o, column) {
        var m = findMap(o, column);
        if (!m) { m = { column: column, source: 'json' }; o.columns.push(m); }
        return m;
    }
    function kindOf(m) {
        if (!m) { return ''; }
        if (m.source === 'file') { return 'file'; }
        if (m.source === 'const') { return 'const'; }
        if (m.source === 'rowid') { return 'rowid'; }
        return 'value';
    }
    function describe(m) {
        if (m.source === 'json' && m.path) { return '<span class="badge badge-info">' + esc(m.step) + '</span><code>' + esc(m.path) + '</code>'; }
        if (m.source === 'inflow' && m.value) {
            var f = S.catalog.inflowFields.filter(function (x) { return x.code === m.value; })[0];
            return '<span class="badge badge-neutral">Inflow</span>' + esc(f ? f.label : m.value);
        }
        if ((m.source === 'system' || m.source === 'record') && m.value) {
            return '<span class="badge badge-neutral">' + (m.source === 'system' ? 'System' : 'Record') + '</span>' + esc(m.value);
        }
        return '';
    }

    function fileEditor(o, oi, name, m) {
        var f = m.file || {};
        var tokens = ['UseCaseName', 'UseCaseCode'].concat((o.columns || [])
            .filter(function (x) { return x.source !== 'file'; }).map(function (x) { return x.column; }));

        var roots = [['', 'Collections (default)']].concat((S.catalog.fileRoots || []).map(function (r) { return [r, r]; }));
        var parts = f.parts || [];

        var partRows = parts.map(function (part, pi) {
            var prefix = 'mapping.outputs.' + oi + '.columns.' + columnIndex(o, name) + '.file.parts.' + pi;
            var picker = part.source === 'json'
                ? sel(prefix + '.step', S.p.steps.map(function (x) { return [x.key, x.key]; }), part.step, 'Step') +
                inp(prefix + '.value', part.value, ' placeholder="@.policy.planCode" data-drop="folder-part" data-prefix="' + prefix + '"')
                : part.source === 'inflow'
                    ? sel(prefix + '.value', fields(), part.value, 'Select field')
                    : part.source === 'system'
                        ? sel(prefix + '.value', SYSTEM.map(function (v) { return [v, v]; }), part.value, 'Select')
                        : inp(prefix + '.value', part.value, ' placeholder="Folder name" data-drop="folder-part" data-prefix="' + prefix + '"');

            return '<div class="pl-row pl-part">' +
                '<span class="pl-part-num">' + (pi + 1) + '</span>' +
                sel(prefix + '.source', [['json', 'From a value'], ['inflow', 'Inflow field'], ['system', 'System value'], ['const', 'Fixed text']], part.source || 'const', null, 'folderPart') +
                picker +
                '<button type="button" class="btn btn-sm" data-act="remove-part" data-output="' + oi + '" data-column="' + esc(name) + '" data-index="' + pi + '" aria-label="Remove level"><i class="fa-solid fa-trash"></i></button>' +
                '</div>';
        }).join('');

        return '<div class="pl-file">' +
            '<div class="pl-dropzone' + (f.contentPath ? ' is-set' : '') + '" data-drop="file-content" data-output="' + oi + '" data-column="' + esc(name) + '">' +
            '<span class="pl-sub">Content (base64)</span>' +
            (f.contentPath ? '<code>' + esc((f.contentStep || f.step) + '  ' + f.contentPath) + '</code>' : '<span>Drop the content node</span>') + '</div>' +
            '<div class="pl-dropzone' + (f.namePath ? ' is-set' : '') + '" data-drop="file-name" data-output="' + oi + '" data-column="' + esc(name) + '">' +
            '<span class="pl-sub">File name</span>' +
            (f.namePath ? '<code>' + esc((f.nameStep || f.step) + '  ' + f.namePath) + '</code>' : '<span>Drop the file-name node</span>') + '</div>' +
            '<label class="pl-sub">File root</label>' +
            '<select class="pl-root" data-output="' + oi + '" data-column="' + esc(name) + '">' +
            roots.map(function (r) { return opt(r[0], r[1], f.root || ''); }).join('') + '</select>' +

            '<label class="pl-sub">Folder levels under that root</label>' +
            (partRows || note('No levels yet: files are saved directly under the root.')) +
            '<div class="pl-add-row">' +
            '<button type="button" class="btn btn-sm" data-act="add-part" data-output="' + oi + '" data-column="' + esc(name) + '">' +
            '<i class="fa-solid fa-plus"></i>Add level</button>' +
            (parts.length ? '' :
                '<button type="button" class="btn btn-sm" data-act="use-template" data-output="' + oi + '" data-column="' + esc(name) + '">Use the {Token} text instead</button>') +
            '</div>' +
            (parts.length ? '' :
                '<input class="pl-folder" data-output="' + oi + '" data-column="' + esc(name) + '" value="' + esc(f.folder || '') + '" placeholder="{UseCaseName}/{ClientName}/{PolicyID}" />' +
                '<div class="pl-tokens">' + tokens.map(function (t) {
                    return '<button type="button" class="pl-mini" data-act="add-token" data-output="' + oi + '" data-column="' + esc(name) + '" data-token="' + esc(t) + '">{' + esc(t) + '}</button>';
                }).join('') + '</div>') +
            '</div>';
    }

    function columnRow(o, oi, c) {
        var name = c.ColumnName;
        var m = findMap(o, name);
        var length = c.ColumnLength === null || c.ColumnLength === undefined ? '' : '(' + (Number(c.ColumnLength) === -1 ? 'max' : c.ColumnLength) + ')';
        var kinds = [['', 'Not mapped'], ['value', 'From a value'], ['const', 'Fixed value'],
        ['rowid', 'ID of a row written earlier'], ['file', 'File (save content)']];
        var cell;
        if (m && m.source === 'file') {
            cell = fileEditor(o, oi, name, m);
        } else if (m && m.source === 'const') {
            cell = '<input class="pl-const" data-output="' + oi + '" data-column="' + esc(name) + '" value="' + esc(m.value || '') + '" placeholder="Fixed value" />';
        } else if (m && m.source === 'rowid') {
            var earlier = outputs().slice(0, oi).map(function (x) { return [x.step, 'Row written after ' + x.step]; });
            cell = '<select class="pl-rowid" data-output="' + oi + '" data-column="' + esc(name) + '">' +
                opt('', 'Choose the earlier output', m.value) +
                earlier.map(function (e) { return opt(e[0], e[1], m.value); }).join('') + '</select>' +
                '<span class="pl-hint">Links this row to the one written after that step.</span>';
        } else {
            var text = m ? describe(m) : '';
            cell = '<div class="pl-dropzone' + (text ? ' is-set' : '') + '" data-drop="column" data-output="' + oi + '" data-column="' + esc(name) + '">' +
                (text ? text + '<button type="button" class="pl-mini" data-act="clear-map" data-output="' + oi + '" data-column="' + esc(name) + '" aria-label="Clear">×</button>'
                    : '<span>Drop a value here</span>') + '</div>';
        }

        return '<tr><td><strong>' + esc(name) + '</strong><span class="pl-sub">' + esc((c.DataTypeName || '') + length) +
            (Number(c.IsNullable) === 1 ? '' : ' · required') + '</span></td>' +
            '<td><select class="pl-kind" data-output="' + oi + '" data-column="' + esc(name) + '">' +
            kinds.map(function (k) { return opt(k[0], k[1], kindOf(m)); }).join('') + '</select>' + cell + '</td>' +
            '<td class="pl-center"><input type="checkbox" class="pl-dedup" data-output="' + oi + '" data-column="' + esc(name) + '"' +
            ((o.dedup || []).indexOf(name) >= 0 ? ' checked' : '') + ' aria-label="Duplicate check" /></td></tr>';
    }

    function outputCard(o, i, count) {
        var columns = S.columnsBy[o.collectionId];
        if (o.collectionId && !columns) { loadColumns(o.collectionId).always(renderEditor); }

        var head = '<div class="pl-out-head">' +
            '<span class="pl-out-num">' + (i + 1) + '</span>' +
            '<label>Rows after step</label>' +
            sel('mapping.outputs.' + i + '.step', S.p.steps.map(function (s) { return [s.key, s.key]; }), o.step, 'Select step', 'rerender') +
            '<label>into</label>' +
            sel('mapping.outputs.' + i + '.collectionId', S.catalog.collections.map(function (c) { return [c.id, c.name]; }),
                o.collectionId, 'Select collection', 'outputCollection', ' data-type="number"') +
            (count > 1 ? '<button type="button" class="btn btn-sm" data-act="remove-output" data-index="' + i + '" aria-label="Remove output"><i class="fa-solid fa-trash"></i></button>' : '') +
            '</div>';

        function autoBox(flagKey, label) {
            var on = o[flagKey] !== false;
            return '<label class="pl-auto-check"><input type="checkbox" class="pl-auto-flag" data-output="' + i + '" data-flag="' + flagKey + '"' +
                (on ? ' checked' : '') + ' /><code>' + label + '</code></label>';
        }

        var body = !o.collectionId ? note('Choose the collection these rows are written into.')
            : !columns ? note('Loading the collection columns…')
                : '<div class="pl-auto"><i class="fa-solid fa-lock"></i>Fill automatically: ' +
                autoBox('autoInflowDataId', 'InflowDataID') + autoBox('autoUseCaseId', 'UseCaseID') + autoBox('autoCreatedBy', 'CreatedBy') +
                '<span class="pl-hint">Untick a column the target table does not have.</span></div>' +
                '<div class="table-wrap"><table class="pl-coltable"><thead><tr><th>Column</th><th>Value</th>' +
                '<th title="A row is skipped when all ticked columns match an existing row">Duplicate check</th></tr></thead><tbody>' +
                columns.map(function (c) { return columnRow(o, i, c); }).join('') +
                '<tr class="pl-sysrow"><td><strong>InflowDataID</strong><span class="pl-sub">automatic</span></td>' +
                '<td><span class="pl-hint">Set from the record being processed.</span></td>' +
                '<td class="pl-center"><input type="checkbox" class="pl-dedup" data-output="' + i + '" data-column="InflowDataID"' +
                ((o.dedup || []).indexOf('InflowDataID') >= 0 ? ' checked' : '') + ' aria-label="Duplicate check" /></td></tr>' +
                '</tbody></table></div>';

        return '<section class="pl-out">' + head + body + '</section>';
    }

    function stepSourceHtml(s) {
        var r = S.responses[s.key];
        var json = r && r.json;
        var title = '<summary><i class="fa-solid fa-plug"></i>Step ' + esc(s.key) + '</summary>';

        if (!json) {
            return '<details class="pl-src">' + title +
                note('Send this step on the API steps tab to get a sample.') + '</details>';
        }

        var body = '';
        if ((s.kind || 'API') === 'ROWS') {
            var firstRow = Array.isArray(json) ? json[0] : null;
            body += '<div class="pl-label">Each row (@)</div>' +
                (firstRow ? '<div class="pl-tree">' + tree(firstRow, '@', { mode: 'response', step: s.key }) + '</div>'
                    : note('No rows were found for the sample record.'));
        } else if (s.loopPath) {
            var item = jpSelect(json, null, s.loopPath)[0];
            body += '<div class="pl-label">Each loop item (@)</div>' +
                (item !== undefined
                    ? '<div class="pl-tree">' + tree(item, '@', { mode: 'response', step: s.key }) + '</div>'
                    : note('The loop path matched nothing in the sample.'));
        }
        body += '<div class="pl-label">Whole response ($)</div>' +
            '<div class="pl-tree">' + tree(json, '$', { mode: 'response', step: s.key }) + '</div>';

        return '<details class="pl-src" open>' + title + body + '</details>';
    }

    function mappingHtml() {
        var p = S.p;
        if (!p.steps.length) { return note('Add at least one API step on the previous tab first.'); }

        var chips = function (kind, list) {
            return list.map(function (x) {
                return '<span class="pl-chip" draggable="true" data-drag="' + kind + '" data-value="' + esc(x[0]) + '">' + esc(x[1]) + '</span>';
            }).join('');
        };

        var sources = p.steps.map(stepSourceHtml).join('') +
            '<details class="pl-src" open><summary><i class="fa-solid fa-table-list"></i>Inflow fields</summary>' +
            '<div class="pl-chips">' + chips('inflow', fields()) + '</div></details>' +
            '<details class="pl-src"><summary><i class="fa-solid fa-gear"></i>System and record values</summary>' +
            '<div class="pl-chips">' + chips('system', SYSTEM.map(function (v) { return [v, v]; })) +
            chips('record', RECORD.map(function (v) { return [v, 'Record ' + v]; })) + '</div></details>';

        var list = outputs();
        var cards = list.length
            ? list.map(function (o, i) { return outputCard(o, i, list.length); }).join('')
            : note('No output yet. Add one to say where rows are written.');

        return '<div class="pl-map">' +
            '<div class="pl-map-src">' +
            '<div class="pl-src-bar">' +
            '<div class="pl-label" style="margin:0">Drag values from here</div>' +
            '<div class="pl-src-tools">' +
            '<button type="button" class="pl-mini" data-tree-all="open" title="Expand all"><i class="fa-solid fa-angles-down"></i></button>' +
            '<button type="button" class="pl-mini" data-tree-all="close" title="Collapse all"><i class="fa-solid fa-angles-up"></i></button>' +
            '</div>' +
            '</div>' +
            '<div class="pl-src-search">' +
            '<i class="fa-solid fa-magnifying-glass"></i>' +
            '<input type="search" id="mapSearch" placeholder="Find a field, e.g. name or planCode" aria-label="Find a field" />' +
            '</div>' +
            sources +
            '</div>' +
            '<div class="pl-map-cols"><div class="pl-label">Outputs</div>' + cards +
            '<div class="pl-add-row"><button type="button" class="btn btn-sm" data-act="add-output">' +
            '<i class="fa-solid fa-plus"></i>Add output</button></div></div></div>';
    }

    // --- tab 4: schedule and runs ---------------------------------------------------------
    function testResultHtml() {
        var t = S.lastTest;
        if (!t) { return ''; }

        var chips = '<div class="pl-chips">' + badge(t.status) +
            '<span class="badge badge-neutral">' + t.recordsConsidered + ' record(s)</span>' +
            '<span class="badge badge-success">' + t.recordsSucceeded + ' ok</span>' +
            '<span class="badge badge-danger">' + t.recordsFailed + ' failed</span>' +
            '<span class="badge badge-info">' + (t.previewRows || []).length + ' row(s) prepared</span></div>' +
            (t.message ? note(t.message) : '');

        var rows = t.previewRows || [];
        var keys = rows.length ? Object.keys(rows[0]) : [];
        var preview = rows.length
            ? '<div class="pl-label">Rows that would be inserted</div><div class="table-wrap"><table><thead><tr>' +
            keys.map(function (k) { return '<th>' + esc(k) + '</th>'; }).join('') + '</tr></thead><tbody>' +
            rows.map(function (r) {
                return '<tr>' + keys.map(function (k) { return '<td>' + esc(r[k]) + '</td>'; }).join('') + '</tr>';
            }).join('') + '</tbody></table></div>'
            : '';

        var log = (t.log || []).length
            ? '<div class="pl-label">Step log</div><div class="table-wrap"><table><thead><tr><th>Record</th><th>Step</th><th>Item</th>' +
            '<th>Outcome</th><th>HTTP</th><th>ms</th><th>Message</th></tr></thead><tbody>' +
            t.log.map(function (l) {
                return '<tr><td>' + esc(l.inflowDataId) + '</td><td>' + esc(l.stepKey) + '</td><td>' + esc(l.itemIndex) + '</td>' +
                    '<td>' + badge(l.outcome) + '</td><td>' + esc(l.statusCode) + '</td><td>' + esc(l.durationMs) + '</td>' +
                    '<td>' + esc(l.message) + '</td></tr>';
            }).join('') + '</tbody></table></div>'
            : '';

        return '<div class="pl-test">' + chips + preview + log + '</div>';
    }

    function runsHtml() {
        if (!S.runs.length) { return note('No runs yet.'); }
        return '<div class="table-wrap pl-runs"><table><thead><tr><th>#</th><th>Type</th><th>Status</th><th>Records</th>' +
            '<th>Inserted</th><th>Skipped</th><th>Files</th><th>Started</th><th>By</th></tr></thead><tbody>' +
            S.runs.map(function (r) {
                return '<tr class="pl-click" data-act="run-items" data-run="' + r.PipelineRunId + '"><td>' + r.PipelineRunId + '</td>' +
                    '<td>' + esc(r.TriggerType) + '</td><td>' + badge(r.Status) + '</td>' +
                    '<td>' + r.RecordsSucceeded + ' / ' + r.RecordsConsidered + (r.RecordsFailed ? ' · ' + r.RecordsFailed + ' failed' : '') + '</td>' +
                    '<td>' + r.RowsInserted + '</td><td>' + r.RowsSkipped + '</td><td>' + r.FilesSaved + '</td>' +
                    '<td>' + esc(new Date(r.StartedOn).toLocaleString()) + '</td><td>' + esc(r.StartedBy) + '</td></tr>';
            }).join('') + '</tbody></table></div>';
    }

    function scheduleHtml() {
        var p = S.p;
        var schedule = '<div class="pl-grid">' +
            field('Schedule', sel('scheduleType', [['MANUAL', 'Manual only'], ['INTERVAL', 'Every N minutes'], ['DAILY', 'Daily at a time']], p.scheduleType, null, 'rerender')) +
            (p.scheduleType === 'INTERVAL' ? field('Every (minutes)', inp('intervalMinutes', p.intervalMinutes, ' type="number" min="5" max="1440" data-type="number"')) : '') +
            (p.scheduleType === 'DAILY' ? field('Run at', inp('dailyTime', p.dailyTime, ' type="time"')) : '') +
            field('Records per run', inp('batchSize', p.batchSize, ' type="number" min="1" max="500" data-type="number"')) +
            field('Attempts per record', inp('maxRecordAttempts', p.maxRecordAttempts, ' type="number" min="1" max="10" data-type="number"')) +
            '</div>';

        var test = '<div class="pl-inline"><select id="plTestRecord" aria-label="Test record"><option value="">Up to 3 waiting records</option>' +
            S.records.map(function (r) { return opt(r.id, r.label, S.testRecord); }).join('') + '</select>' +
            '<button type="button" class="btn" data-act="test"' + (p.pipelineId ? '' : ' disabled') + '><i class="fa-solid fa-flask"></i>Run test</button></div>' +
            testResultHtml();

        return section('Schedule', 'Scheduled runs start only after the pipeline is published. Any saved edit returns it to draft.', schedule) +
            section('Test run', 'Runs the saved pipeline and shows what it would do. Nothing is written: no rows, no files, no record status.', test) +
            section('Run history', '', '<div class="pl-inline"><button type="button" class="btn btn-sm" data-act="refresh-runs">' +
                '<i class="fa-solid fa-rotate"></i>Refresh</button></div>' + runsHtml());
    }

    // --- field changes --------------------------------------------------------------------
    function renameStep(oldKey, newKey) {
        var p = S.p;
        p.steps.forEach(function (s) {
            s.bindings.forEach(function (b) { if (b.step === oldKey) { b.step = newKey; } });
            s.itemConditions.forEach(function (c) { if (c.step === oldKey) { c.step = newKey; } });
        });
        p.mapping.columns.forEach(function (m) {
            if (m.step === oldKey) { m.step = newKey; }
            if (m.file && m.file.step === oldKey) { m.file.step = newKey; }
        });
        if (S.responses[oldKey]) { S.responses[newKey] = S.responses[oldKey]; delete S.responses[oldKey]; }
    }

    var AFTER = {
        rerender: function () { },

        stepKind: function (step) {
            // Swapping the type clears whatever belonged to the other kind.
            if (step.kind === 'ROWS') {
                step.bindings = [];
                step.bodyMode = 'NONE';
                step.connectorId = 0;
                step.connectorVersionId = 0;
                step.loopPath = '';
            } else {
                step.sourceCollectionId = 0;
                step.bodyMode = 'JSON';
            }
            delete S.responses[step.key];
        },

        folderPart: function (part) {
            part.value = '';
            part.step = part.source === 'json' && S.p.steps.length ? S.p.steps[0].key : null;
        },

        useCase: function (p) {
            p.collectionId = '';
            p.mapping = { columns: [], dedup: [], outputs: [] };
            p.driver.conditions = [];
            S.columnsBy = {};
            $.when(loadCatalog(), loadRecords()).always(render);
        },
        collection: function (p) {
            loadColumns(p.collectionId).always(render);
        },
        outputCollection: function (o) {
            o.columns = [];
            o.dedup = ['InflowDataID'];
            loadColumns(o.collectionId).always(render);
        },
        source: function (obj, old, value, path) {
            obj.value = '';
            var stepIdx = Number(path.split('.')[1]);
            obj.step = value === 'json' && stepIdx > 0 ? S.p.steps[stepIdx - 1].key : null;
        },
        condSource: function (obj, old, value, path) {
            obj.path = '';
            obj.step = value === 'json' && path.indexOf('steps.') === 0 ? S.p.steps[Number(path.split('.')[1])].key : null;
        },
        connector: function (step, old, value) {
            step.connectorVersionId = Number(value) || 0;
            var c = connectorOf(value);
            step.connectorId = c ? c.ConnectorId : 0;
            loadRequest(step.connectorVersionId).always(render);
        },
        bodyMode: function (step) {
            step.bindings = step.bindings.filter(function (b) {
                if (b.in === 'body') { return step.bodyMode === 'JSON'; }
                if (b.in === 'form') { return step.bodyMode === 'FORM' || step.bodyMode === 'MULTIPART'; }
                return true;
            });
            if (step.bodyMode === 'FORM') { step.bindings.forEach(function (b) { if (b.in === 'form') { b.kind = 'text'; } }); }
        },
        stepKey: function (step, old, value) {
            var clean = String(value || '').toLowerCase().replace(/[^a-z0-9_]/g, '').replace(/^[^a-z]+/, '').slice(0, 30) || 'step';
            if (clean !== old && stepIndexOf(clean) >= 0) { clean = old; APS.toast('Another step already uses that key.', 'error'); }
            step.key = clean;
            if (old && old !== clean) { renameStep(old, clean); }
        }
    };

    $(root).on('input change', '[data-bind]', function (e) {
        var $el = $(this);
        if (e.type === 'input' && $el.attr('data-after')) { return; }
        if (this.type === 'radio' && !this.checked) { return; }

        var path = String($el.attr('data-bind'));
        var value = $el.val();
        if ($el.attr('data-type') === 'number') { value = value === '' ? '' : Number(value); }

        var parts = path.split('.');
        var last = parts.pop();
        var parent = parts.length ? getPath(S.p, parts.join('.')) : S.p;
        var old = parent[last];
        parent[last] = value;
        markDirty();

        var after = $el.attr('data-after');
        if (after && e.type === 'change') {
            AFTER[after](parent, old, value, path);
            render();
        }
    });

    $(root).on('change', '#plSample', function () { S.sampleRecord = $(this).val(); });
    $(root).on('change', '#plTestRecord', function () { S.testRecord = $(this).val(); });

    $(root).on('change', '.pl-kind', function () {
        var o = outputAt($(this).attr('data-output'));
        var column = $(this).attr('data-column');
        var kind = $(this).val();

        o.columns = (o.columns || []).filter(function (m) { return m.column !== column; });
        if (kind === 'value') { o.columns.push({ column: column, source: 'json' }); }
        if (kind === 'const') { o.columns.push({ column: column, source: 'const', value: '' }); }
        if (kind === 'rowid') { o.columns.push({ column: column, source: 'rowid', value: '' }); }
        if (kind === 'file') {
            var withSample = S.p.steps.filter(function (s) { return S.responses[s.key]; }).pop();
            o.columns.push({
                column: column, source: 'file',
                file: { step: withSample ? withSample.key : '', contentPath: '', namePath: '', folder: '{UseCaseName}' }
            });
        }
        markDirty();
        render();
    });

    $(root).on('input', '.pl-const', function () {
        ensureMap(outputAt($(this).attr('data-output')), $(this).attr('data-column')).value = $(this).val();
        markDirty();
    });

    $(root).on('change', '.pl-rowid', function () {
        ensureMap(outputAt($(this).attr('data-output')), $(this).attr('data-column')).value = $(this).val();
        markDirty();
        renderEditor();
    });

    $(root).on('input', '.pl-folder', function () {
        var m = ensureMap(outputAt($(this).attr('data-output')), $(this).attr('data-column'));
        if (m.file) { m.file.folder = $(this).val(); markDirty(); }
    });
    $(root).on('change', '.pl-root', function () {
        var m = ensureMap(outputAt($(this).attr('data-output')), $(this).attr('data-column'));
        if (m.file) { m.file.root = $(this).val(); markDirty(); }
    });

    $(root).on('change', '.pl-dedup', function () {
        var o = outputAt($(this).attr('data-output'));
        var column = $(this).attr('data-column');
        o.dedup = o.dedup || [];
        var at = o.dedup.indexOf(column);
        if (this.checked && at < 0) { o.dedup.push(column); }
        if (!this.checked && at >= 0) { o.dedup.splice(at, 1); }
        markDirty();
    });
    $(root).on('change', '.pl-auto-flag', function () {
        outputAt($(this).attr('data-output'))[$(this).attr('data-flag')] = this.checked;
        markDirty();
    });

    // --- drag and drop --------------------------------------------------------------------
    $(root).on('dragstart', '[data-drag]', function (e) {
        var $t = $(this);
        S.drag = { kind: $t.attr('data-drag'), step: $t.attr('data-step'), path: $t.attr('data-path'), value: $t.attr('data-value') };
        e.originalEvent.dataTransfer.setData('text/plain', S.drag.path || S.drag.value || '');
        e.originalEvent.dataTransfer.effectAllowed = 'copy';
        $(root).addClass('pl-dragging');
    });
    $(root).on('dragend', '[data-drag]', function () {
        S.drag = null;
        $(root).removeClass('pl-dragging');
        $('.pl-drop-over').removeClass('pl-drop-over');
    });
    $(root).on('dragover', '[data-drop]', function (e) {
        if (!S.drag) { return; }
        e.preventDefault();
        $(this).addClass('pl-drop-over');
    });
    $(root).on('dragleave', '[data-drop]', function () { $(this).removeClass('pl-drop-over'); });
    $(root).on('drop', '[data-drop]', function (e) {
        e.preventDefault();
        $(this).removeClass('pl-drop-over');
        if (!S.drag) { return; }
        applyDrop($(this), S.drag);
        S.drag = null;
    });

    function applyDrop($t, d) {
        var kind = $t.attr('data-drop');

        if (kind === 'binding' || kind === 'cond') {
            var prefix = $t.attr('data-prefix');
            var obj = getPath(S.p, prefix);
            var stepIdx = Number(prefix.split('.')[1]);
            var key = kind === 'binding' ? 'value' : 'path';

            if (d.kind === 'json') {
                var from = stepIndexOf(d.step);
                if (kind === 'binding' ? from >= stepIdx : from > stepIdx) {
                    APS.toast(kind === 'binding' ? 'A request value can only come from an earlier step.' : 'Use this step or an earlier one.', 'error');
                    return;
                }
                obj.source = 'json';
                obj.step = d.step;
                obj[key] = d.path;
            } else if (d.kind === 'inflow' || d.kind === 'record' || (d.kind === 'system' && kind === 'binding')) {
                obj.source = d.kind;
                obj.step = null;
                obj[key] = d.value;
            } else {
                return;
            }
        } else if (kind === 'folder-part') {
            var partObj = getPath(S.p, $t.attr('data-prefix'));
            if (d.kind === 'json') { partObj.source = 'json'; partObj.step = d.step; partObj.value = d.path; }
            else { partObj.source = d.kind; partObj.step = null; partObj.value = d.value; }
        } else if (kind === 'column') {
            var m = ensureMap(outputAt($t.attr('data-output')), $t.attr('data-column'));
            m.file = null;
            m.step = null;
            m.path = null;
            m.value = null;
            if (d.kind === 'json') { m.source = 'json'; m.step = d.step; m.path = d.path; }
            else { m.source = d.kind; m.value = d.value; }
        } else if (kind === 'file-content' || kind === 'file-name') {
            if (d.kind !== 'json') { APS.toast('Drop a node from a step response.', 'error'); return; }
            var f = ensureMap(outputAt($t.attr('data-output')), $t.attr('data-column'));
            f.source = 'file';
            f.file = f.file || { step: d.step, contentPath: '', namePath: '', folder: '{UseCaseName}' };
            // Each path keeps its own step, so dropping one never rewrites the other.
            if (kind === 'file-content') { f.file.contentStep = d.step; f.file.contentPath = d.path; }
            else { f.file.nameStep = d.step; f.file.namePath = d.path; }
            f.file.step = f.file.contentStep || f.file.nameStep;
        }

        markDirty();
        render();
    }

    // --- actions --------------------------------------------------------------------------
    function cleanMapping() {
        return {
            columns: [],
            dedup: [],
            outputs: outputs().map(function (o) {
                return {
                    step: o.step,
                    collectionId: Number(o.collectionId || 0),
                    autoInflowDataId: o.autoInflowDataId !== false,
                    autoUseCaseId: o.autoUseCaseId !== false,
                    autoCreatedBy: o.autoCreatedBy !== false,
                    dedup: o.dedup || [],
                    columns: (o.columns || []).filter(function (c) {
                        if (c.source === 'file') {
                            if (!(c.file && c.file.contentPath && c.file.namePath)) { return false; }
                            // Drop any folder level left empty, so it never saves as "_".
                            c.file.parts = (c.file.parts || []).filter(function (x) { return x.value; });
                            return true;
                        }
                        if (c.source === 'const') { return true; }
                        if (c.source === 'rowid') { return !!c.value; }
                        if (c.source === 'json') { return !!c.path; }
                        return !!c.value;
                    })
                };
            }).filter(function (o) { return o.step && o.collectionId && o.columns.length; })
        };
    }

    function save(button) {
        var incomplete = S.p.mapping.columns.filter(function (c) {
            return c.source === 'file' && !(c.file && c.file.contentPath && c.file.namePath);
        })[0];
        if (incomplete) {
            APS.toast('Column ' + incomplete.column + ': drop both the content node and the file-name node before saving.', 'error');
            S.tab = 'mapping';
            render();
            return;
        }
        var p = S.p;
        return APS.busy(button, APS.post(URLS.save, {
            pipelineId: p.pipelineId, pipelineCode: p.pipelineCode, pipelineName: p.pipelineName,
            useCaseId: p.useCaseId || 0, collectionId: p.collectionId || 0,
            stepsJson: JSON.stringify(p.steps.map(stepPayload)),
            driverJson: JSON.stringify(p.driver),
            mappingJson: JSON.stringify(cleanMapping()),
            scheduleType: p.scheduleType,
            intervalMinutes: p.scheduleType === 'INTERVAL' ? p.intervalMinutes : '',
            dailyTime: p.scheduleType === 'DAILY' ? p.dailyTime : '',
            batchSize: p.batchSize, maxRecordAttempts: p.maxRecordAttempts
        })).then(function (r) {
            if (!r.success) { return; }
            APS.toast(r.message, 'success');
            S.dirty = false;
            S.p.pipelineId = r.data && r.data.id ? r.data.id : S.p.pipelineId;
            S.p.status = 'Draft';
            loadList();
            render();
        });
    }

    function send(button) {
        var p = S.p;
        if (!S.sampleRecord) { APS.toast('Choose a sample record to send with.', 'error'); return; }

        var steps = p.steps.slice(0, S.stepIndex + 1).map(stepPayload);
        APS.busy(button, APS.post(URLS.send, {
            useCaseId: p.useCaseId, inflowDataId: S.sampleRecord, stepsJson: JSON.stringify(steps)
        })).then(function (r) {
            if (!r.success) { return; }
            (r.data || []).forEach(function (x) {
                var json = x.response ? parse(x.response, null) : null;
                S.responses[x.step] = { statusCode: x.statusCode, durationMs: x.durationMs, error: x.error, loopItems: x.loopItems, json: json };
                var step = p.steps[stepIndexOf(x.step)];
                if (step && json && !x.error) { step.sampleResponseJson = x.response; markDirty(); }
            });
            render();
        });
    }

    function showRunItems(runId) {
        $.getJSON(URLS.runItems, { runId: runId }).done(function (r) {
            if (!r.success) { APS.toast(r.message, 'error'); return; }
            var rows = (r.data || []).map(function (x) {
                return '<tr><td>' + esc(x.InflowDataID) + '</td><td>' + esc(x.StepKey) + '</td><td>' + esc(x.ItemIndex) + '</td>' +
                    '<td>' + badge(x.Outcome) + '</td><td>' + esc(x.HttpStatusCode) + '</td><td>' + esc(x.DurationMs) + '</td>' +
                    '<td>' + esc(x.Message) + '</td></tr>';
            }).join('');
            APS.openModal('Run #' + runId,
                rows ? '<div class="table-wrap pl-modal-table"><table><thead><tr><th>Record</th><th>Step</th><th>Item</th><th>Outcome</th>' +
                    '<th>HTTP</th><th>ms</th><th>Message</th></tr></thead><tbody>' + rows + '</tbody></table></div>'
                    : '<p>No items were logged for this run.</p>',
                '<button type="button" class="btn" data-modal-close>Close</button>');
        });
    }

    $(root).on('click', '[data-act]', function (e) {
        var $b = $(this);
        var act = $b.attr('data-act');
        var p = S.p;

        switch (act) {
            case 'new-pipeline':
                if (!confirmLeave()) { return; }
                S.p = blank();
                S.tab = 'setup';
                S.stepIndex = 0;
                S.responses = {};
                S.columns = [];
                S.records = [];
                S.lastTest = null;
                S.runs = [];
                S.dirty = false;
                render();
                return;
            case 'open':
                openPipeline(Number($b.attr('data-id')));
                return;
            case 'tab':
                S.tab = $b.attr('data-tab');
                if (S.tab === 'schedule') { loadRuns().always(renderEditor); } else { renderEditor(); }
                return;
            case 'step':
                S.stepIndex = Number($b.attr('data-index'));
                renderEditor();
                return;
            case 'subtab':
                S.stepTab = $b.attr('data-subtab');
                renderEditor();
                return;
            case 'save':
                save(this);
                return;
            case 'publish':
                if (S.dirty) { APS.toast('Save the draft before publishing.', 'error'); return; }
                APS.confirmAction('Publish pipeline',
                    'Once published, this pipeline calls the APIs and writes to the collection on its schedule, without further approval. Continue?',
                    'Publish', function () {
                        APS.post(URLS.publish, { pipelineId: p.pipelineId }).then(function (r) {
                            if (!r.success) { return; }
                            APS.toast(r.message, 'success');
                            loadList(p.pipelineId);
                        });
                    });
                return;
            case 'pause':
            case 'resume':
                APS.post(URLS.status, { pipelineId: p.pipelineId, status: act === 'pause' ? 'Paused' : 'Published' }).then(function (r) {
                    if (!r.success) { return; }
                    APS.toast(r.message, 'success');
                    loadList(p.pipelineId);
                });
                return;
            case 'run':
                APS.busy(this, APS.post(URLS.run, { pipelineId: p.pipelineId })).then(function (r) {
                    if (!r.success) { return; }
                    APS.toast(r.message, 'success');
                    S.tab = 'schedule';
                    window.setTimeout(function () { loadRuns().always(renderEditor); }, 1500);
                });
                return;
            case 'test':
                if (S.dirty) { APS.toast('Save the draft first. A test run uses the saved pipeline.', 'error'); return; }
                APS.busy(this, APS.post(URLS.test, { pipelineId: p.pipelineId, inflowDataId: S.testRecord || '' })).then(function (r) {
                    if (!r.success) { return; }
                    S.lastTest = r.data;
                    S.tab = 'schedule';
                    loadRuns().always(renderEditor);
                });
                return;
            case 'refresh-runs':
                loadRuns().always(renderEditor);
                return;
            case 'run-items':
                showRunItems(Number($b.attr('data-run')));
                return;
            case 'send':
                send(this);
                return;
            case 'add-step':
                var n = p.steps.length + 1;
                while (stepIndexOf('step' + n) >= 0) { n++; }
                p.steps.push({
                    key: 'step' + n, kind: 'API', sourceCollectionId: 0, connectorId: 0, connectorVersionId: 0,
                    bodyMode: 'JSON', bindings: [], loopPath: '', itemConditions: [], sampleResponseJson: ''
                });
                S.stepIndex = p.steps.length - 1;
                S.stepTab = 'body';
                break;
            case 'remove-step':
                if (!window.confirm('Remove step "' + p.steps[S.stepIndex].key + '"?')) { return; }
                delete S.responses[p.steps[S.stepIndex].key];
                p.steps.splice(S.stepIndex, 1);
                S.stepIndex = Math.max(0, S.stepIndex - 1);
                break;
            case 'move-step':
                var j = S.stepIndex + Number($b.attr('data-dir'));
                if (j < 0 || j >= p.steps.length) { return; }
                var moved = p.steps[S.stepIndex];
                p.steps[S.stepIndex] = p.steps[j];
                p.steps[j] = moved;
                S.stepIndex = j;
                break;
            case 'add-driver':
                p.driver.conditions.push({ source: 'inflow', path: '', op: 'notEmpty', value: '' });
                break;
            case 'add-cond':
                p.steps[S.stepIndex].itemConditions.push({ source: 'json', step: p.steps[S.stepIndex].key, path: '', op: 'eq', value: '' });
                break;
            case 'add-binding':
                var where = $b.attr('data-in');
                p.steps[S.stepIndex].bindings.push({ in: where, target: where === 'body' ? '$.' : '', kind: 'text', source: 'const', step: null, value: '' });
                break;
            case 'pick-target':
                var bindings = p.steps[S.stepIndex].bindings;
                var target = $b.attr('data-target');
                var existing = bindings.filter(function (x) { return x.in === 'body' && x.target === target; })[0];
                if (existing) { bindings.splice(bindings.indexOf(existing), 1); }
                else { bindings.push({ in: 'body', target: target, kind: 'text', source: 'const', step: null, value: $b.attr('data-current') }); }
                break;
            case 'set-loop':
                var s = p.steps[stepIndexOf($b.attr('data-stepkey'))];
                if (!s) { return; }
                e.preventDefault();
                s.loopPath = $b.attr('data-loop');
                APS.toast('Loop path set to ' + s.loopPath, 'success');
                break;
            case 'add-output':
                outputs().push({
                    step: p.steps.length ? p.steps[p.steps.length - 1].key : '',
                    collectionId: Number(p.collectionId || 0),
                    columns: [], dedup: ['InflowDataID']
                });
                loadColumns(p.collectionId);
                break;
            case 'remove-output':
                outputs().splice(Number($b.attr('data-index')), 1);
                break;
            case 'add-part':
                var partMap = ensureMap(outputAt($b.attr('data-output')), $b.attr('data-column'));
                if (!partMap.file) { return; }
                partMap.file.parts = partMap.file.parts || [];
                partMap.file.parts.push({ source: 'json', step: p.steps.length ? p.steps[0].key : '', value: '' });
                break;
            case 'remove-part':
                var dropMap = ensureMap(outputAt($b.attr('data-output')), $b.attr('data-column'));
                dropMap.file.parts.splice(Number($b.attr('data-index')), 1);
                break;
            case 'use-template':
                var tplMap = ensureMap(outputAt($b.attr('data-output')), $b.attr('data-column'));
                tplMap.file.parts = [];
                break;
            case 'clear-map':
                var out = outputAt($b.attr('data-output'));
                var column = $b.attr('data-column');
                out.columns = out.columns.filter(function (m) { return m.column !== column; });
                break;
            case 'add-token':
                var m = ensureMap(outputAt($b.attr('data-output')), $b.attr('data-column'));
                if (!m.file) { return; }
                var folder = m.file.folder || '';
                m.file.folder = folder + (folder && folder.slice(-1) !== '/' ? '/' : '') + '{' + $b.attr('data-token') + '}';
                break;
            case 'remove':
                var parts = String($b.attr('data-path')).split('.');
                var index = Number(parts.pop());
                getPath(p, parts.join('.')).splice(index, 1);
                break;
            default:
                return;
        }

        markDirty();
        render();
    });

    // Clicking a response node copies its path.
    $(root).on('click', '.pl-drag', function () {
        var path = $(this).attr('data-path');
        if (navigator.clipboard) { navigator.clipboard.writeText(path).catch(function () { }); }
        APS.toast('Path copied: ' + path, 'info');
    });

    window.addEventListener('beforeunload', function (e) {
        if (S.dirty) { e.preventDefault(); e.returnValue = ''; }
    });

    // --- keeping your place in the tree -------------------------------------------
    // 'toggle' does not bubble, so it is captured rather than delegated.
    root.addEventListener('toggle', function (e) {
        var node = e.target;
        if (!node.matches || !node.matches('details.pl-node')) { return; }
        var key = node.getAttribute('data-node');
        if (key) { S.open[key] = node.open; }
    }, true);

    // A drag that starts on a branch must not open or close it.
    $(root).on('dragstart', '.pl-node > summary', function () { S.dragging = true; });
    $(root).on('dragend drop', function () {
        window.setTimeout(function () { S.dragging = false; }, 0);
    });
    $(root).on('click', '.pl-node > summary', function (e) {
        if (S.dragging) { e.preventDefault(); }
    });

    // --- finding a field ------------------------------------------------------------
    function filterSources(term) {
        var $scope = $('.pl-map-src');
        term = String(term || '').toLowerCase().trim();

        if (!term) {
            $scope.find('.pl-hidden').removeClass('pl-hidden');
            return;
        }

        $scope.find('.pl-node, .pl-leaf, .pl-chip').addClass('pl-hidden');

        $scope.find('.pl-leaf, .pl-chip').each(function () {
            var $item = $(this);
            var haystack = ($item.text() + ' ' + ($item.attr('data-path') || '') + ' ' + ($item.attr('data-value') || '')).toLowerCase();
            if (haystack.indexOf(term) < 0) { return; }

            $item.removeClass('pl-hidden');
            $item.parents('details.pl-node').removeClass('pl-hidden').prop('open', true);
            $item.closest('details.pl-src').prop('open', true);
        });
    }

    $(root).on('input', '#mapSearch', function () { filterSources($(this).val()); });

    $(root).on('click', '[data-tree-all]', function () {
        var open = $(this).attr('data-tree-all') === 'open';
        $('.pl-map-src details.pl-node').each(function () {
            this.open = open;
            var key = this.getAttribute('data-node');
            if (key) { S.open[key] = open; }
        });
    });

    loadList();
    render();
})(jQuery);