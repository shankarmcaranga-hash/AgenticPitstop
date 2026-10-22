/*
 * Source builder: a dialog for choosing where a screen reads its data.
 *
 * A source is one table, several joined tables, or a stored procedure, always
 * filtered to the open record or its use case. The dialog only writes a spec;
 * SQL checks every table and column against the catalogue when it is read.
 */
window.APSSource = (function ($) {
    'use strict';

    var CATALOG = { joinable: {}, hints: [], procedures: {} };

    function init(catalog) {
        CATALOG.joinable = catalog.joinable || {};
        CATALOG.hints = catalog.hints || [];
        CATALOG.procedures = catalog.procedures || {};
    }

    function esc(v) { return APS.escapeHtml(v === null || v === undefined ? '' : v); }
    function tableNames() { return Object.keys(CATALOG.joinable).sort(); }
    function procNames() { return Object.keys(CATALOG.procedures).sort(); }
    function columnsOf(table) { return (CATALOG.joinable[table] || {}).columns || []; }
    function tableMeta(table) { return CATALOG.joinable[table] || { hasInflow: false, hasUseCase: false }; }

    function opt(value, label, selected) {
        return '<option value="' + esc(value) + '"' + (String(value) === String(selected) ? ' selected' : '') + '>' +
            esc(label) + '</option>';
    }

    function blank() {
        return {
            kind: 'TABLE', from: { table: '', alias: 't0' }, joins: [], columns: [],
            filters: {}, jsonColumn: '', jsonRoot: ''
        };
    }

    /** Every table in the spec, as {alias, table}. */
    function sources(spec) {
        var list = [];
        if (spec.from && spec.from.table) { list.push({ alias: spec.from.alias || 't0', table: spec.from.table }); }
        (spec.joins || []).forEach(function (j) {
            if (j.table) { list.push({ alias: j.alias, table: j.table }); }
        });
        return list;
    }

    /** The column names this source will return, used to fill value dropdowns. */
    function columns(spec) {
        if (!spec) { return []; }

        if (spec.kind === 'PROC') {
            return ((CATALOG.procedures[spec.procedure] || {}).columns || []).slice();
        }

        return (spec.columns || []).map(function (c) { return c.as || c.column; }).filter(Boolean);
    }

    /** A short line for the screen, so a configured source reads at a glance. */
    function describe(spec) {
        if (!spec) { return ''; }
        if (spec.kind === 'PROC') { return spec.procedure || 'No procedure chosen'; }

        var list = sources(spec);
        if (!list.length) { return 'No table chosen'; }

        var text = list.map(function (s) { return s.table; }).join(' + ');
        var count = (spec.columns || []).length;
        return text + ' · ' + count + ' column' + (count === 1 ? '' : 's');
    }

    /** A join the catalogue suggests between two tables, if there is one. */
    function suggestOn(leftTable, leftAlias, rightTable, rightAlias) {
        var hit = CATALOG.hints.filter(function (h) {
            return (h.pt === leftTable && h.rt === rightTable) || (h.pt === rightTable && h.rt === leftTable);
        })[0];

        if (!hit) { return [{ left: leftAlias + '.', right: rightAlias + '.' }]; }

        return hit.pt === leftTable
            ? [{ left: leftAlias + '.' + hit.pc, right: rightAlias + '.' + hit.rc }]
            : [{ left: leftAlias + '.' + hit.rc, right: rightAlias + '.' + hit.pc }];
    }

    // --- the dialog ---------------------------------------------------------
    function open(options) {
        var spec = $.extend(true, blank(), options.spec || {});
        spec.joins = spec.joins || [];
        spec.columns = spec.columns || [];
        spec.filters = spec.filters || {};

        APS.openModal(options.title || 'Choose the data source',
            '<div class="sb" id="sbBody"></div>',
            '<button type="button" class="btn" data-modal-close>Cancel</button>' +
            '<button type="button" class="btn btn-primary" id="sbApply">Use this source</button>');

        render();

        function render() {
            $('#sbBody').html(
                kindBar() +
                (spec.kind === 'PROC' ? procPanel() : tablePanel()) +
                jsonPanel() +
                summaryPanel());
        }

        function kindBar() {
            var kinds = [['TABLE', 'One table'], ['JOIN', 'Joined tables'], ['PROC', 'Stored procedure']];
            return '<div class="sb-kinds">' + kinds.map(function (k) {
                return '<button type="button" class="sb-kind' + (spec.kind === k[0] ? ' active' : '') +
                    '" data-kind="' + k[0] + '">' + esc(k[1]) + '</button>';
            }).join('') + '</div>';
        }

        function procPanel() {
            var info = CATALOG.procedures[spec.procedure];
            return '<div class="sb-block">' +
                '<label class="sb-label">Stored procedure</label>' +
                '<select id="sbProc"><option value="">Select</option>' +
                procNames().map(function (n) { return opt(n, n, spec.procedure); }).join('') + '</select>' +
                (info && info.error
                    ? '<p class="sb-warn">Its columns cannot be read: ' + esc(info.error) + '</p>'
                    : info
                        ? '<p class="sb-hint">Returns: ' + esc((info.columns || []).join(', ')) + '</p>'
                        : '<p class="sb-hint">It is called with @InflowDataID and @UseCaseID where it accepts them.</p>') +
                '</div>';
        }

        function tablePanel() {
            var list = sources(spec);

            var main = '<div class="sb-block">' +
                '<label class="sb-label">Main table</label>' +
                '<select id="sbMainTable"><option value="">Select a table</option>' +
                tableNames().map(function (n) { return opt(n, n, spec.from.table); }).join('') + '</select>' +
                '</div>';

            var joins = spec.kind !== 'JOIN' ? '' :
                '<div class="sb-block">' +
                '<div class="sb-label-row"><label class="sb-label">Joined tables</label>' +
                '<button type="button" class="btn btn-sm" id="sbAddJoin"' + (spec.from.table ? '' : ' disabled') + '>' +
                '<i class="fa-solid fa-plus"></i>Add table</button></div>' +
                (spec.joins.length ? spec.joins.map(joinRow).join('') : '<p class="sb-hint">No joined tables yet.</p>') +
                '</div>';

            var cols = !list.length ? '' :
                '<div class="sb-block">' +
                '<div class="sb-label-row"><label class="sb-label">Columns to return</label>' +
                '<span class="sb-count">' + spec.columns.length + ' selected</span></div>' +
                '<div class="sb-cols">' + list.map(columnGroup).join('') + '</div>' +
                '</div>';

            var filters = !list.length ? '' :
                '<div class="sb-block">' +
                '<label class="sb-label">Filter to the open record</label>' +
                '<div class="sb-filters">' +
                '<div><span>Record</span>' + filterSelect('inflowDataId', 'InflowDataID') + '</div>' +
                '<div><span>Use case</span>' + filterSelect('useCaseId', 'UseCaseID') + '</div>' +
                '</div>' +
                '<p class="sb-hint">At least one is required, so a screen can never read another record.</p>' +
                '</div>';

            return main + joins + cols + filters;
        }

        function joinRow(join, index) {
            var pair = (join.on && join.on[0]) || { left: '', right: '' };
            var leftOptions = [];
            sources(spec).forEach(function (s) {
                if (s.alias === join.alias) { return; }
                columnsOf(s.table).forEach(function (c) { leftOptions.push(s.alias + '.' + c.name); });
            });
            var rightOptions = columnsOf(join.table).map(function (c) { return join.alias + '.' + c.name; });

            return '<div class="sb-join" data-join="' + index + '">' +
                '<select class="sb-join-type">' +
                opt('LEFT', 'Left join', join.type) + opt('INNER', 'Inner join', join.type) + '</select>' +
                '<select class="sb-join-table"><option value="">Select a table</option>' +
                tableNames().map(function (n) { return opt(n, n, join.table); }).join('') + '</select>' +
                '<span class="sb-alias">' + esc(join.alias) + '</span>' +
                '<div class="sb-on">' +
                '<span>on</span>' +
                '<select class="sb-on-left">' + leftOptions.map(function (o) { return opt(o, o, pair.left); }).join('') + '</select>' +
                '<span>=</span>' +
                '<select class="sb-on-right">' + rightOptions.map(function (o) { return opt(o, o, pair.right); }).join('') + '</select>' +
                '</div>' +
                '<button type="button" class="btn btn-sm sb-join-remove" aria-label="Remove"><i class="fa-solid fa-trash"></i></button>' +
                '</div>';
        }

        function columnGroup(s) {
            return '<details class="sb-group" open><summary>' + esc(s.table) + ' <code>' + esc(s.alias) + '</code></summary>' +
                columnsOf(s.table).map(function (c) {
                    var on = spec.columns.some(function (x) { return x.alias === s.alias && x.column === c.name; });
                    return '<label class="sb-col"><input type="checkbox" class="sb-col-check" data-alias="' + esc(s.alias) +
                        '" data-column="' + esc(c.name) + '"' + (on ? ' checked' : '') + ' />' +
                        '<span>' + esc(c.name) + '<small>' + esc(c.type) + '</small></span></label>';
                }).join('') + '</details>';
        }

        function filterSelect(key, columnName) {
            var choices = [];
            sources(spec).forEach(function (s) {
                if (columnsOf(s.table).some(function (c) { return c.name === columnName; })) {
                    choices.push(s.alias + '.' + columnName);
                }
            });

            // Choose one automatically when there is only one possibility.
            if (!spec.filters[key] && choices.length === 1) { spec.filters[key] = choices[0]; }

            return '<select class="sb-filter" data-filter="' + key + '">' +
                opt('', 'Not used', spec.filters[key]) +
                choices.map(function (c) { return opt(c, c, spec.filters[key]); }).join('') + '</select>';
        }

        function jsonPanel() {
            var available = columns(spec);
            if (!available.length) { return ''; }

            return '<div class="sb-block">' +
                '<label class="sb-label">JSON column (optional)</label>' +
                '<div class="sb-filters">' +
                '<div><span>Column holding JSON</span>' +
                '<select id="sbJsonColumn"><option value="">None</option>' +
                available.map(function (c) { return opt(c, c, spec.jsonColumn); }).join('') + '</select></div>' +
                '<div><span>Path inside it</span>' +
                '<input id="sbJsonRoot" value="' + esc(spec.jsonRoot) + '" placeholder="e.g. Vision Agent.final_summary" /></div>' +
                '</div>' +
                '<p class="sb-hint">Values inside the JSON then read like ordinary columns, named a.b.c.</p>' +
                '</div>';
        }

        function summaryPanel() {
            var problem = validate();
            return '<div class="sb-summary' + (problem ? ' is-error' : '') + '">' +
                '<i class="fa-solid ' + (problem ? 'fa-triangle-exclamation' : 'fa-circle-check') + '"></i>' +
                esc(problem || describe(spec)) + '</div>';
        }

        function validate() {
            if (spec.kind === 'PROC') { return spec.procedure ? null : 'Choose the stored procedure.'; }
            if (!spec.from.table) { return 'Choose the main table.'; }

            var bad = spec.joins.filter(function (j) {
                return !j.table || !j.on || !j.on[0] || !j.on[0].left || !j.on[0].right;
            })[0];
            if (bad) { return 'Complete the join condition for each joined table.'; }

            if (!spec.columns.length) { return 'Choose at least one column.'; }
            if (!spec.filters.inflowDataId && !spec.filters.useCaseId) {
                return 'Choose the record or use case filter.';
            }
            return null;
        }

        // --- events ---------------------------------------------------------
        var $body = $('#sbBody');

        $body.on('click', '[data-kind]', function () {
            spec.kind = $(this).data('kind');
            if (spec.kind !== 'JOIN') { spec.joins = []; }
            if (spec.kind === 'PROC') { spec.columns = []; }
            render();
        });

        $body.on('change', '#sbProc', function () {
            spec.procedure = $(this).val();
            spec.jsonColumn = '';
            render();
        });

        $body.on('change', '#sbMainTable', function () {
            spec.from.table = $(this).val();
            spec.from.alias = 't0';
            spec.columns = [];
            spec.filters = {};
            spec.joins = [];
            render();
        });

        $body.on('click', '#sbAddJoin', function () {
            var alias = 't' + (spec.joins.length + 1);
            spec.joins.push({ type: 'LEFT', table: '', alias: alias, on: [{ left: '', right: '' }] });
            render();
        });

        $body.on('change', '.sb-join-table', function () {
            var join = spec.joins[Number($(this).closest('.sb-join').data('join'))];
            join.table = $(this).val();
            join.on = join.table ? suggestOn(spec.from.table, spec.from.alias, join.table, join.alias) : [{ left: '', right: '' }];
            render();
        });

        $body.on('change', '.sb-join-type', function () {
            spec.joins[Number($(this).closest('.sb-join').data('join'))].type = $(this).val();
        });

        $body.on('change', '.sb-on-left, .sb-on-right', function () {
            var $row = $(this).closest('.sb-join');
            var join = spec.joins[Number($row.data('join'))];
            join.on = [{ left: $row.find('.sb-on-left').val(), right: $row.find('.sb-on-right').val() }];
            $('#sbBody .sb-summary').replaceWith(summaryPanel());
        });

        $body.on('click', '.sb-join-remove', function () {
            spec.joins.splice(Number($(this).closest('.sb-join').data('join')), 1);
            spec.joins.forEach(function (j, i) { j.alias = 't' + (i + 1); });
            spec.columns = spec.columns.filter(function (c) {
                return sources(spec).some(function (s) { return s.alias === c.alias; });
            });
            render();
        });

        $body.on('change', '.sb-col-check', function () {
            var alias = $(this).data('alias');
            var column = $(this).data('column');

            if (this.checked) {
                // A repeated name is given a prefix, so both columns survive.
                var clash = spec.columns.some(function (c) { return (c.as || c.column) === column; });
                spec.columns.push({ alias: alias, column: column, as: clash ? alias + '_' + column : column });
            } else {
                spec.columns = spec.columns.filter(function (c) { return !(c.alias === alias && c.column === column); });
            }
            render();
        });

        $body.on('change', '.sb-filter', function () {
            spec.filters[$(this).data('filter')] = $(this).val();
            $('#sbBody .sb-summary').replaceWith(summaryPanel());
        });

        $body.on('change', '#sbJsonColumn', function () { spec.jsonColumn = $(this).val(); });
        $body.on('input', '#sbJsonRoot', function () { spec.jsonRoot = $(this).val(); });

        $('#sbApply').on('click', function () {
            var problem = validate();
            if (problem) { APS.toast(problem, 'error'); return; }

            APS.closeModal();
            options.onSave(spec, columns(spec));
        });
    }

    return { init: init, open: open, columns: columns, describe: describe, blank: blank };
})(jQuery);