/*
 * Agentic PITSTOP - shell behaviour and shared client helpers.
 *
 * Everything screen-specific lives in its own file. This one owns the parts of
 * the shell that exist on every page: theme, sidebar, global search, the toast
 * and modal roots, and the single AJAX wrapper that all screens post through.
 */
window.APS = (function ($) {
    'use strict';

    var THEME_KEY = 'aps.theme';

    // -------------------------------------------------------------------------
    // Escaping
    // -------------------------------------------------------------------------

    /**
     * Escapes text before it goes into markup. Every value the client renders
     * originates from the database and may contain a customer's free text, so it
     * passes through here rather than being trusted.
     */
    function escapeHtml(value) {
        if (value === null || value === undefined) { return ''; }
        return String(value)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#39;');
    }

    function escapeAttr(value) { return escapeHtml(value); }

    // -------------------------------------------------------------------------
    // AJAX
    // -------------------------------------------------------------------------

    function antiforgeryToken() {
        return $('#apsAntiforgery input[name="__RequestVerificationToken"]').val() || '';
    }

    /**
     * Posts to a controller action and resolves with the JSON envelope
     * { success, message, data } that every APS action returns.
     *
     * Failures are surfaced as a toast by default so a caller only has to handle
     * the success path.
     */
    function post(url, data, options) {
        options = options || {};

        return $.ajax({
            url: url,
            type: 'POST',
            data: data,
            dataType: 'json',
            headers: {
                'RequestVerificationToken': antiforgeryToken(),
                'X-Requested-With': 'XMLHttpRequest'
            },
            processData: options.processData !== false,
            contentType: options.contentType !== undefined
                ? options.contentType
                : 'application/x-www-form-urlencoded; charset=UTF-8'
        }).then(function (response) {
            if (!response.success && options.quiet !== true) {
                toast(response.message || 'The request could not be completed.', 'error');
            }
            return response;
        }, function (xhr) {
            var message = xhr.status === 403
                ? 'You do not have access to this action.'
                : xhr.status === 401
                    ? 'Your session has expired. Reloading...'
                    : 'The server could not be reached.';

            if (options.quiet !== true) { toast(message, 'error'); }

            // An expired session is not something the user can act on from here.
            // Reloading the current page would silently start a fresh window, so
            // the browser is sent to the expiry page instead.
            if (xhr.status === 401) {
                window.setTimeout(function () {
                    window.location.href = window.APS_EXPIRED_URL || '/Home/SessionExpired';
                }, 1200);
            }
            return { success: false, message: message };
        });
    }

    /** Posts a file with the anti-forgery header attached. */
    function postFile(url, formData, options) {
        return post(url, formData, $.extend({}, options, {
            processData: false,
            contentType: false
        }));
    }

    /** Serialises a form and posts it. */
    function postForm(url, form, options) {
        return post(url, $(form).serialize(), options);
    }

    /** Marks a button busy for the duration of a promise. */
    function busy(button, promise) {
        var $button = $(button);
        var original = $button.html();

        $button.addClass('is-busy').prop('disabled', true)
               .html('<i class="fa-solid fa-spinner"></i>Working...');

        return promise.always(function () {
            $button.removeClass('is-busy').prop('disabled', false).html(original);
        });
    }

    // -------------------------------------------------------------------------
    // Toast
    // -------------------------------------------------------------------------
    var TOAST_HOLD_MS = 15000;

    function toast(message, type) {
        var icon = type === 'success' ? 'fa-circle-check'
            : type === 'error' ? 'fa-circle-exclamation'
                : 'fa-circle-info';

        var timer;

        var $toast = $('<div class="toast"></div>')
            .addClass('toast-' + (type || 'info'))
            .html('<i class="fa-solid ' + icon + ' toast-icon" aria-hidden="true"></i>' +
                '<span class="toast-text">' + escapeHtml(message) + '</span>' +
                '<button type="button" class="toast-close" aria-label="Dismiss">' +
                '<i class="fa-solid fa-xmark" aria-hidden="true"></i>' +
                '</button>');

        function dismiss() {
            window.clearTimeout(timer);
            $toast.fadeOut(160, function () { $(this).remove(); });
        }

        $toast.on('click', '.toast-close', dismiss);
        timer = window.setTimeout(dismiss, TOAST_HOLD_MS);

        $('#toastRoot').append($toast);
    }

    // -------------------------------------------------------------------------
    // Modal
    // -------------------------------------------------------------------------

    function openModal(title, bodyHtml, footerHtml) {
        var markup =
            '<div class="modal">' +
              '<div class="modal-header">' +
                '<h3>' + escapeHtml(title) + '</h3>' +
                '<button class="icon-button" data-modal-close aria-label="Close">' +
                  '<i class="fa-solid fa-xmark"></i></button>' +
              '</div>' +
              '<div class="modal-body">' + bodyHtml + '</div>' +
              '<div class="modal-footer">' + (footerHtml || '') + '</div>' +
            '</div>';

        $('#modalRoot').html(markup).addClass('open').attr('aria-hidden', 'false');
        $('#modalRoot').find('input, select, textarea, button').not('[data-modal-close]').first().trigger('focus');
    }

    function closeModal() {
        $('#modalRoot').removeClass('open').attr('aria-hidden', 'true').empty();
    }
    /**
     * Same dialog, but the body is markup the caller has already escaped.
     * Use APS.escapeHtml on every value before putting it in. confirmAction
     * stays the safe default for plain text.
     */
    function confirmActionHtml(title, bodyHtml, confirmLabel, onConfirm) {
        openModal(title, bodyHtml,
            '<button class="btn" data-modal-close>Cancel</button>' +
            '<button class="btn btn-danger" id="apsConfirmButton">' +
            escapeHtml(confirmLabel) + '</button>');

        $('#apsConfirmButton').on('click', function () {
            closeModal();
            onConfirm();
        });
    }
    /** Confirmation dialog. Calls onConfirm only when the user confirms. */
    function confirmAction(title, message, confirmLabel, onConfirm) {
        openModal(title,
            '<p>' + escapeHtml(message) + '</p>',
            '<button class="btn" data-modal-close>Cancel</button>' +
            '<button class="btn btn-danger" id="apsConfirmButton">' + escapeHtml(confirmLabel) + '</button>');

        $('#apsConfirmButton').on('click', function () {
            closeModal();
            onConfirm();
        });
    }

    // -------------------------------------------------------------------------
    // Formatting
    // -------------------------------------------------------------------------

    /** Maps a review status to the badge class used across the portal. */
    function statusBadgeClass(status) {
        var value = String(status || '').toLowerCase();
        if (value.indexOf('approved') >= 0 || value.indexOf('success') >= 0 || value === 'active') {
            return 'badge-success';
        }
        if (value.indexOf('reject') >= 0 || value.indexOf('fail') >= 0 || value === 'inactive') {
            return 'badge-danger';
        }
        if (value.indexOf('pending') >= 0 || value.indexOf('clarification') >= 0 || value.indexOf('processing') >= 0) {
            return 'badge-warning';
        }
        return 'badge-neutral';
    }

    // -------------------------------------------------------------------------
    // Shell
    // -------------------------------------------------------------------------

    function applyTheme(theme) {
        document.documentElement.setAttribute('data-theme', theme);
        try { window.localStorage.setItem(THEME_KEY, theme); } catch (e) { /* private mode */ }
    }

    function initShell() {
        var stored = 'light';
        try { stored = window.localStorage.getItem(THEME_KEY) || 'light'; } catch (e) { /* private mode */ }
        applyTheme(stored);

        $('#themeToggle').on('click', function () {
            var current = document.documentElement.getAttribute('data-theme');
            applyTheme(current === 'dark' ? 'light' : 'dark');
        });

        // Desktop: collapse to icons and remember it. Phone: slide the menu in and out.
        var SIDEBAR_KEY = 'aps.sidebar';

        function isPhone() { return window.matchMedia('(max-width: 640px)').matches; }
        function isCollapsed() { return document.documentElement.classList.contains('sidebar-collapsed'); }

        function setCollapsed(collapsed) {
            document.documentElement.classList.toggle('sidebar-collapsed', collapsed);
            $('#sidebarToggle').attr('aria-expanded', String(!collapsed));
            try { window.localStorage.setItem(SIDEBAR_KEY, collapsed ? 'collapsed' : 'expanded'); } catch (e) { /* private mode */ }
        }

        $('#sidebarToggle')
            .attr('aria-controls', 'sidebar')
            .attr('aria-expanded', String(!isCollapsed()))
            .on('click', function (event) {
                event.stopPropagation();
                if (isPhone()) {
                    $('#sidebar').toggleClass('open-mobile');
                    return;
                }
                setCollapsed(!isCollapsed());
            });

        // Close the slide-in navigation when tapping the content area on a phone.
        $('.main-content').on('click', function () {
            $('#sidebar').removeClass('open-mobile');
        });

        $(document).on('click', '[data-modal-close]', closeModal);
        $('#modalRoot').on('click', function (event) {
            if (event.target === this) { closeModal(); }
        });

        $(document).on('keydown', function (event) {
            if (event.key === 'Escape') { closeModal(); }
            if ((event.ctrlKey || event.metaKey) && event.key.toLowerCase() === 'k') {
                event.preventDefault();
                $('#globalSearch').trigger('focus');
            }
        });

        // Global search jumps to the matching screen in the sidebar.
        $('#globalSearch, #mobileGlobalSearch').on('keydown', function (event) {
            if (event.key !== 'Enter') { return; }

            var term = $(this).val().trim().toLowerCase();
            if (!term) { return; }

            var match = $('#primaryNav .nav-item').filter(function () {
                return $(this).text().toLowerCase().indexOf(term) >= 0;
            }).first();

            if (match.length) {
                window.location.href = match.attr('href');
            } else {
                toast('No screen matches "' + term + '".', 'info');
            }
        });
    }

    $(initShell);

    // Partial views render inside <body>, before the layout loads jQuery at the
    // end of the document. A partial therefore cannot run code directly and
    // cannot declare a Razor section either. It pushes a callback onto
    // window.apsReady instead, and the queue is drained here once jQuery and
    // this file are both available.
    $(function () {
        var queue = window.apsReady || [];

        function run(callback) {
            try { callback(jQuery); }
            catch (error) {
                if (window.console && window.console.error) {
                    window.console.error('APS ready callback failed', error);
                }
            }
        }

        while (queue.length) { run(queue.shift()); }

        // Anything registered after this point runs immediately.
        window.apsReady = { push: run };
    });

    return {
        post: post,
        postForm: postForm,
        postFile: postFile,
        busy: busy,
        toast: toast,
        openModal: openModal,
        closeModal: closeModal,
        confirmAction: confirmAction,
        confirmActionHtml: confirmActionHtml,
        escapeHtml: escapeHtml,
        escapeAttr: escapeAttr,
        statusBadgeClass: statusBadgeClass
    };
})(jQuery);
