/*
 * Document viewer: renders PDF and TIFF pages with pdf.js and tiff.js, keeps the
 * extracted fields beside the page, and lets a reviewer correct one value at a
 * time. Images are drawn to a canvas rather than handed to <img>, so page
 * navigation and zoom behave the same for every format.
 */
window.APSDocViewer = (function ($) {
    'use strict';

    var ready = false;

    function init(workerUrl) {
        if (ready) { return; }
        if (window.pdfjsLib) { window.pdfjsLib.GlobalWorkerOptions.workerSrc = workerUrl; }
        ready = true;
    }

    function esc(v) { return APS.escapeHtml(v === null || v === undefined ? '' : v); }

    /**
     * One rendered document. Holds the loaded file and draws a page on demand,
     * so switching page or zoom never refetches it.
     */
    function Surface($canvas) {
        var pdf = null, tiff = null, kind = null, pages = 1;

        function load(url, documentKind) {
            kind = documentKind;
            pdf = tiff = null;

            if (kind === 'pdf') {
                return window.pdfjsLib.getDocument({ url: url, withCredentials: true }).promise.then(function (doc) {
                    pdf = doc;
                    pages = doc.numPages;
                    return pages;
                });
            }

            if (kind === 'tiff') {
                return $.ajax({ url: url, xhrFields: { responseType: 'arraybuffer' } }).then(function (buffer) {
                    window.Tiff.initialize({ TOTAL_MEMORY: 64 * 1024 * 1024 });
                    tiff = new window.Tiff({ buffer: buffer });
                    pages = tiff.countDirectory();
                    return pages;
                });
            }

            // An ordinary image is one page.
            return $.Deferred().resolve(1).promise();
        }

        function draw(page, zoom, url) {
            var canvas = $canvas[0];
            var context = canvas.getContext('2d');
            var scale = zoom / 100;

            if (kind === 'pdf') {
                return pdf.getPage(page).then(function (p) {
                    var viewport = p.getViewport({ scale: scale * 1.5 });
                    canvas.width = viewport.width;
                    canvas.height = viewport.height;
                    canvas.style.width = Math.round(viewport.width / 1.5) + 'px';
                    return p.render({ canvasContext: context, viewport: viewport }).promise;
                });
            }

            if (kind === 'tiff') {
                tiff.setDirectory(page - 1);
                var source = tiff.toCanvas();
                canvas.width = source.width;
                canvas.height = source.height;
                canvas.style.width = Math.round(source.width * scale) + 'px';
                context.drawImage(source, 0, 0);
                return $.Deferred().resolve().promise();
            }

            var image = new Image();
            var done = $.Deferred();
            image.onload = function () {
                canvas.width = image.width;
                canvas.height = image.height;
                canvas.style.width = Math.round(image.width * scale) + 'px';
                context.drawImage(image, 0, 0);
                done.resolve();
            };
            image.onerror = function () { done.reject(); };
            image.src = url;
            return done.promise();
        }

        return { load: load, draw: draw, pageCount: function () { return pages; } };
    }

    /**
     * Builds the viewer into a panel. `options` carries the documents, the URLs
     * and a save callback, so this file knows nothing about the controller.
     */
    function render($panel, data, options) {
        var current = 0, page = 1, zoom = 100;
        var surface = null;

        $panel.html('<div class="dv-doc">' + data.sections.map(function (s) {
            return '<section class="dv-doc-sec dl-span-' + Number(s.width) + '" data-sec="' + esc(s.key) + '">' +
                '<div class="dv-doc-title">' + esc(options.titles[s.key] || s.key) + '</div>' +
                '<div class="dv-doc-body"></div></section>';
        }).join('') + '</div>');

        function doc() { return data.documents[current]; }
        function url(extra) { return options.fileUrl(doc().token, extra); }

        function pageData() {
            var pages = doc().pages || [];
            for (var i = 0; i < pages.length; i++) {
                if (Number(pages[i].page) === page) { return pages[i]; }
            }
            return { page: page, fields: [], summary: null };
        }

        function pageCount() {
            return Math.max(1, Number(doc().pageCount || 1), surface ? surface.pageCount() : 1);
        }

        // --- list ------------------------------------------------------------
        function drawList() {
            $panel.find('[data-sec="documents"] .dv-doc-body').html(data.documents.map(function (x, k) {
                var icon = x.kind === 'pdf' ? 'fa-file-pdf' : (x.kind === 'image' || x.kind === 'tiff') ? 'fa-file-image' : 'fa-file-lines';
                return '<button type="button" class="dv-doc-item' + (k === current ? ' active' : '') + '" data-doc="' + k + '">' +
                    '<i class="fa-solid ' + icon + '"></i>' +
                    '<span class="dv-doc-name" title="' + esc(x.name) + '">' + esc(x.name) + '</span>' +
                    '<span class="dv-doc-count">' + x.fieldCount + '</span></button>';
            }).join(''));
        }

        // --- page ------------------------------------------------------------
        function toolbar(inPopup) {
            var total = pageCount();
            return '<div class="dv-toolbar">' +
                '<div class="dv-toolbar-file"><strong title="' + esc(doc().name) + '">' + esc(doc().name) + '</strong></div>' +
                '<div class="dv-pager-nav">' +
                '<button type="button" class="btn btn-sm" data-nav="-1"' + (page <= 1 ? ' disabled' : '') + ' aria-label="Previous page"><i class="fa-solid fa-chevron-left"></i></button>' +
                '<span class="dv-page-label">Page ' + page + ' of ' + total + '</span>' +
                '<button type="button" class="btn btn-sm" data-nav="1"' + (page >= total ? ' disabled' : '') + ' aria-label="Next page"><i class="fa-solid fa-chevron-right"></i></button>' +
                '</div>' +
                '<div class="dv-zoom">' +
                '<button type="button" class="btn btn-sm" data-zoom="-25" aria-label="Zoom out"><i class="fa-solid fa-magnifying-glass-minus"></i></button>' +
                '<button type="button" class="btn btn-sm dv-zoom-value" data-zoom="reset">' + zoom + '%</button>' +
                '<button type="button" class="btn btn-sm" data-zoom="25" aria-label="Zoom in"><i class="fa-solid fa-magnifying-glass-plus"></i></button>' +
                (inPopup ? '' : '<button type="button" class="btn btn-sm" data-expand><i class="fa-solid fa-expand"></i>View</button>') +
                '<a class="btn btn-sm" href="' + esc(url('&download=true')) + '" aria-label="Download"><i class="fa-solid fa-download"></i></a>' +
                '</div></div>';
        }

        function drawPreview() {
            var $body = $panel.find('[data-sec="preview"] .dv-doc-body');

            if (doc().kind === 'none') {
                $body.html(toolbar(false) +
                    '<div class="dv-empty"><span class="dv-empty-icon"><i class="fa-solid fa-file-circle-exclamation"></i></span>' +
                    '<strong>No preview</strong><span>' + esc(doc().ext || 'This file type') + ' cannot be shown here. Use Download.</span></div>');
                return;
            }

            $body.html(toolbar(false) + '<div class="dv-canvas-wrap"><canvas class="dv-canvas"></canvas></div>');

            surface = Surface($body.find('.dv-canvas'));
            $body.find('.dv-canvas-wrap').addClass('is-loading');

            surface.load(url(), doc().kind)
                .then(function () {
                    return surface.draw(page, zoom, url());
                })
                .then(function () {
                    $body.find('.dv-canvas-wrap').removeClass('is-loading');
                    // The page count may only be known once the file is loaded.
                    $body.find('.dv-toolbar').replaceWith(toolbar(false));
                })
                .catch(function () {
                    $body.html(toolbar(false) + errorBox('This document could not be opened.'));
                });
        }

        function redrawPage() {
            if (!surface) { return; }
            var $wrap = $panel.find('.dv-canvas-wrap');
            $wrap.addClass('is-loading');
            $.when(surface.draw(page, zoom, url())).always(function () { $wrap.removeClass('is-loading'); });
            $panel.find('.dv-toolbar').replaceWith(toolbar(false));
        }

        function errorBox(text) { return '<div class="callout callout-danger">' + esc(text) + '</div>'; }

        // --- fields ------------------------------------------------------------
        function fieldRows(editable) {
            var data = pageData();

            if (!data.fields.length) {
                return '<div class="dv-kv-empty">No data was extracted from page ' + page + '.</div>';
            }

            return '<table class="dv-kv"><tbody>' + data.fields.map(function (f, i) {
                var canEdit = editable && f.rowKey;
                return '<tr data-field="' + i + '" data-key="' + esc(f.rowKey) + '">' +
                    '<th>' + esc(f.field) + '</th>' +
                    '<td><span class="dv-kv-value">' + esc(f.value) + '</span>' +
                    (canEdit ? '<button type="button" class="dv-kv-edit" data-edit aria-label="Edit"><i class="fa-solid fa-pen"></i></button>' : '') +
                    '</td></tr>';
            }).join('') + '</tbody></table>';
        }

        function drawFields() {
            var data = pageData();
            var summary = data.summary
                ? '<div class="dv-summary"><div class="dv-summary-title"><i class="fa-solid fa-lightbulb"></i>Page ' + page + ' summary</div>' +
                '<p>' + esc(data.summary) + '</p></div>'
                : '';

            $panel.find('[data-sec="fields"] .dv-doc-body')
                .addClass('is-flex')
                .html('<div class="dv-fields"><div class="dv-kv-scroll">' + fieldRows(data.canEdit !== false && options.canEdit) + '</div>' + summary + '</div>');
        }

        // --- the popup ------------------------------------------------------------
        function openPopup() {
            var $wrap = $('<div class="dv-modal"><div class="dv-modal-card">' +
                '<div class="dv-modal-head"><strong>' + esc(doc().name) + '</strong>' +
                '<button type="button" class="btn btn-sm" data-close><i class="fa-solid fa-xmark"></i>Close</button></div>' +
                '<div class="dv-modal-body">' +
                '<div class="dv-modal-page"></div>' +
                '<aside class="dv-modal-fields"></aside>' +
                '</div></div></div>').appendTo('body');

            function drawPopup() {
                $wrap.find('.dv-modal-page').html(toolbar(true) + '<div class="dv-canvas-wrap"><canvas class="dv-canvas"></canvas></div>');

                var popupSurface = Surface($wrap.find('.dv-canvas'));
                popupSurface.load(url(), doc().kind).then(function () {
                    return popupSurface.draw(page, zoom, url());
                });

                var data = pageData();
                $wrap.find('.dv-modal-fields').html(
                    '<div class="dv-doc-title">Extracted data</div>' +
                    '<div class="dv-kv-scroll">' + fieldRows(options.canEdit) + '</div>' +
                    (data.summary ? '<div class="dv-summary"><div class="dv-summary-title">' +
                        '<i class="fa-solid fa-lightbulb"></i>Page ' + page + ' summary</div><p>' + esc(data.summary) + '</p></div>' : ''));
            }

            $wrap.on('click', '[data-close]', function () { $wrap.remove(); });
            $wrap.on('click', function (e) { if (e.target === $wrap[0]) { $wrap.remove(); } });

            $wrap.on('click', '[data-nav]', function () {
                var next = page + Number($(this).data('nav'));
                if (next < 1 || next > pageCount()) { return; }
                page = next;
                drawPopup();
                drawFields();          // the panel behind follows the popup
                $panel.find('.dv-toolbar').replaceWith(toolbar(false));
                redrawPage();
            });

            $wrap.on('click', '[data-zoom]', function () {
                var step = $(this).data('zoom');
                zoom = step === 'reset' ? 100 : Math.min(300, Math.max(50, zoom + Number(step)));
                drawPopup();
            });

            bindEditing($wrap);
            drawPopup();
        }

        // --- editing one row ---------------------------------------------------------
        function bindEditing($scope) {
            $scope.on('click', '[data-edit]', function () {
                var $row = $(this).closest('tr');
                if ($row.find('input').length) { return; }

                var value = $row.find('.dv-kv-value').text();
                $row.find('td').html(
                    '<div class="dv-kv-edit-row">' +
                    '<input class="dv-kv-input" value="' + esc(value) + '" maxlength="4000" />' +
                    '<button type="button" class="btn btn-sm btn-primary" data-save>Save</button>' +
                    '<button type="button" class="btn btn-sm" data-cancel>Cancel</button>' +
                    '</div>');
                $row.find('.dv-kv-input').trigger('focus');
            });

            $scope.on('click', '[data-cancel]', function () { redrawFieldsEverywhere(); });

            $scope.on('keydown', '.dv-kv-input', function (e) {
                if (e.key === 'Enter') { $(this).closest('td').find('[data-save]').trigger('click'); }
                if (e.key === 'Escape') { redrawFieldsEverywhere(); }
            });

            $scope.on('click', '[data-save]', function () {
                var $row = $(this).closest('tr');
                var index = Number($row.data('field'));
                var newValue = $row.find('.dv-kv-input').val();

                APS.busy(this, options.save($row.data('key'), newValue)).then(function (r) {
                    if (!r || !r.success) { return; }

                    // Keep the loaded data in step, so a reload is not needed.
                    var fields = pageData().fields;
                    if (fields[index]) { fields[index].value = newValue; }

                    APS.toast(r.message || 'Saved.', 'success');
                    redrawFieldsEverywhere();
                });
            });
        }

        function redrawFieldsEverywhere() {
            drawFields();
            $('.dv-modal').each(function () {
                var data = pageData();
                $(this).find('.dv-modal-fields .dv-kv-scroll').html(fieldRows(options.canEdit));
            });
        }

        // --- panel events ---------------------------------------------------------------
        $panel.on('click', '[data-doc]', function () {
            current = Number($(this).data('doc'));
            page = 1;
            zoom = 100;
            drawList();
            drawPreview();
            drawFields();
        });

        $panel.on('click', '[data-nav]', function () {
            var next = page + Number($(this).data('nav'));
            if (next < 1 || next > pageCount()) { return; }
            page = next;
            redrawPage();
            drawFields();
        });

        $panel.on('click', '[data-zoom]', function () {
            var step = $(this).data('zoom');
            zoom = step === 'reset' ? 100 : Math.min(300, Math.max(50, zoom + Number(step)));
            redrawPage();
        });

        $panel.on('click', '[data-expand]', openPopup);

        bindEditing($panel);

        drawList();
        drawPreview();
        drawFields();
    }

    return { init: init, render: render };
})(jQuery);