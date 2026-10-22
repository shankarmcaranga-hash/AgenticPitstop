/* ---------------------------------------------------------------------------
   API Configurator client behaviour.

   Convenience only. Every action that changes data or reaches a network is
   re-checked on the server, including the wizard's own field rules.
   --------------------------------------------------------------------------- */
(function () {
    'use strict';

    var element = document.getElementById('apiconfigSettings');
    var settings = {
        previewUrl: element ? element.getAttribute('data-preview-url') : '',
        startStep: element ? parseInt(element.getAttribute('data-start-step'), 10) : 1,
        readOnly: element ? element.getAttribute('data-read-only') === 'true' : false
    };

    var form = document.getElementById('wizardForm');

    // -----------------------------------------------------------------------
    // Confirmation prompts, used across every tab
    // -----------------------------------------------------------------------
    document.addEventListener('click', function (event) {
        var trigger = event.target.closest('[data-confirm]');
        if (!trigger) return;

        if (!window.confirm(trigger.getAttribute('data-confirm'))) {
            event.preventDefault();
            event.stopPropagation();
        }
    });

    if (!form) return;   // the wizard is only on the Configure tab

    // -----------------------------------------------------------------------
    // Step navigation
    // -----------------------------------------------------------------------
    var totalSteps = 6;
    var currentStep = parseInt(settings.startStep, 10);
    if (isNaN(currentStep) || currentStep < 1 || currentStep > totalSteps) currentStep = 1;

    var dirty = false;
    var progress = document.getElementById('wizardProgress');
    var stepField = document.getElementById('cfgCurrentStep');

    function showStep(step) {
        currentStep = Math.min(Math.max(step, 1), totalSteps);

        document.querySelectorAll('.apiconfig-pane').forEach(function (pane) {
            pane.classList.toggle('active', parseInt(pane.getAttribute('data-pane'), 10) === currentStep);
        });

        document.querySelectorAll('.apiconfig-step').forEach(function (item) {
            var bar = document.getElementById('wizardProgress');
            if (bar) { bar.style.width = (step * 100 / 6) + '%'; }
            var number = parseInt(item.getAttribute('data-step'), 10);
            item.classList.remove('active', 'done');
            if (number === currentStep) item.classList.add('active');
            else if (number < currentStep) item.classList.add('done');
        });

        if (stepField) stepField.value = currentStep;
        setProgress();

        var next = document.getElementById('wizardNext');
        if (next) next.innerHTML = currentStep === totalSteps
            ? 'Back to step 1'
            : 'Next<i class="fa-solid fa-arrow-right"></i>';
    }

    function setProgress() {
        if (!progress) return;
        progress.textContent = 'Step ' + currentStep + ' of ' + totalSteps + (dirty ? ' \u2013 unsaved changes' : '');
    }

    document.querySelectorAll('.apiconfig-step').forEach(function (item) {
        item.addEventListener('click', function () {
            showStep(parseInt(item.getAttribute('data-step'), 10));
        });
    });

    var prev = document.getElementById('wizardPrev');
    if (prev) prev.addEventListener('click', function () {
        showStep(currentStep === 1 ? totalSteps : currentStep - 1);
    });

    var next = document.getElementById('wizardNext');
    if (next) next.addEventListener('click', function () {
        if (currentStep === totalSteps) { showStep(1); return; }
        if (!validateStep(currentStep)) return;
        showStep(currentStep + 1);
    });

    function value(id) {
        var element = document.getElementById(id);
        return element ? element.value.trim() : '';
    }

    function validateStep(step) {
        var missing = [];

        if (step === 1) {
            if (!value('cfgCustomer')) missing.push('Customer');
            if (!value('cfgApplication')) missing.push('Target application');
            if (!value('cfgName')) missing.push('Connector name');
            if (!value('cfgCode')) missing.push('Connector code');
            if (!value('cfgOperation')) missing.push('Operation code');
        }

        if (step === 2) {
            var baseUrl = value('cfgBaseUrl');
            var endpoint = value('cfgEndpoint');

            if (!baseUrl) missing.push('Base URL');
            else if (baseUrl.toLowerCase().indexOf('https://') !== 0) missing.push('Base URL must use HTTPS');
            else if (baseUrl.replace(/^https:\/\//i, '').indexOf('/') !== -1) {
                // A path here plus a path in the template produces a doubled URL
                // and a 404 that is easy to miss on the approval screen.
                missing.push('Base URL must be host only - move the path into the endpoint template');
            }

            if (!endpoint) missing.push('Endpoint template');
            else if (endpoint.charAt(0) !== '/') missing.push('Endpoint template must start with /');
        }

        if (step === 4) {
            if (!isJsonOrEmpty(value('cfgSourceJson'))) missing.push('Sample source data is not valid JSON');
            if (!isJsonOrEmpty(value('cfgPayloadTemplate'))) missing.push('Payload template is not valid JSON');
        }

        if (missing.length === 0) return true;
        notify('Check this step: ' + missing.join(', '));
        return false;
    }

    function isJsonOrEmpty(text) {
        if (!text) return true;
        try { JSON.parse(text); return true; } catch (e) { return false; }
    }

    // -----------------------------------------------------------------------
    // Dynamic header and query rows
    // -----------------------------------------------------------------------
    function addRow(containerId, nameField, valueField, placeholder) {
        var container = document.getElementById(containerId);
        if (!container) return;

        var row = document.createElement('div');
        row.className = 'apiconfig-kv';
        row.innerHTML =
            '<input name="' + nameField + '" placeholder="' + placeholder + '" />' +
            '<input name="' + valueField + '" placeholder="Value or {{Placeholder}}" />' +
            '<button type="button" class="btn btn-sm apiconfig-remove">Remove</button>';

        container.appendChild(row);
        markDirty();
    }

    var addHeader = document.getElementById('addHeader');
    if (addHeader) addHeader.addEventListener('click', function () {
        addRow('headerRows', 'headerName', 'headerValue', 'Header name');
    });

    var addQuery = document.getElementById('addQuery');
    if (addQuery) addQuery.addEventListener('click', function () {
        addRow('queryRows', 'queryName', 'queryValue', 'Parameter name');
    });

    document.addEventListener('click', function (event) {
        if (!event.target.classList.contains('apiconfig-remove')) return;
        var row = event.target.closest('.apiconfig-kv');
        if (row) { row.remove(); markDirty(); }
    });

    // -----------------------------------------------------------------------
    // Customer filters the application list
    // -----------------------------------------------------------------------
    function filterApplications() {
        var customer = document.getElementById('cfgCustomer');
        var application = document.getElementById('cfgApplication');
        if (!customer || !application) return;

        var customerId = customer.value;
        var selected = application.value;
        var stillValid = false;

        Array.prototype.forEach.call(application.options, function (option) {
            // getAttribute, not dataset coercion: an id of 0 must not be
            // mistaken for the empty placeholder option.
            var owner = option.getAttribute('data-customer');
            if (owner === null) return;

            var matches = !customerId || String(owner) === String(customerId);
            option.hidden = !matches;
            option.disabled = !matches;
            if (matches && option.value === selected) stillValid = true;
        });

        if (!stillValid && selected) {
            application.value = '';
            notify('The application was cleared because it does not belong to the selected customer.');
        } else if (!stillValid) {
            application.value = '';
        }
    }

    var customerSelect = document.getElementById('cfgCustomer');
    if (customerSelect) customerSelect.addEventListener('change', function () {
        filterApplications();
        markDirty();
    });
    filterApplications();

    // -----------------------------------------------------------------------
    // Authentication field visibility
    // -----------------------------------------------------------------------
    function showAuthFields() {
        var auth = document.getElementById('cfgAuth');
        if (!auth) return;

        document.querySelectorAll('.apiconfig-auth').forEach(function (group) {
            group.style.display = group.getAttribute('data-auth') === auth.value ? '' : 'none';
        });
    }

    var authSelect = document.getElementById('cfgAuth');
    if (authSelect) authSelect.addEventListener('change', function () {
        showAuthFields();
        markDirty();
    });
    showAuthFields();

    // -----------------------------------------------------------------------
    // Resolve preview - server side, no target call
    // -----------------------------------------------------------------------
    var previewBtn = document.getElementById('previewPayloadBtn');
    if (previewBtn) previewBtn.addEventListener('click', function () {
        var output = document.getElementById('payloadPreview');
        var token = form.querySelector('input[name="__RequestVerificationToken"]');

        previewBtn.disabled = true;
        previewBtn.textContent = 'Resolving...';
        if (output) output.textContent = 'Resolving on the server...';

        var body = new URLSearchParams();
        body.append('payloadTemplate', value('cfgPayloadTemplate'));
        body.append('sourceJson', value('cfgSourceJson'));
        if (token) body.append('__RequestVerificationToken', token.value);

        fetch(settings.previewUrl, {
            method: 'POST',
            headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
            body: body.toString()
        })
        .then(function (response) { return response.json(); })
        .then(function (data) {
            if (output) output.textContent = data.resolved || data.message;
            notify(data.message);
        })
        .catch(function () {
            if (output) output.textContent = 'The preview request failed.';
            notify('The preview request failed.');
        })
        .finally(function () {
            previewBtn.disabled = false;
            previewBtn.innerHTML = '<i class="fa-solid fa-wand-magic-sparkles"></i>Resolve preview';
        });
    });

    // -----------------------------------------------------------------------
    // Load sample - fills the form only, writes nothing
    // -----------------------------------------------------------------------
    var loadSample = document.getElementById('loadSample');
    if (loadSample) loadSample.addEventListener('click', function () {
        function set(id, text) {
            var element = document.getElementById(id);
            if (element) element.value = text;
        }

        set('cfgName', 'Correspondence List - Get');
        set('cfgCode', 'GET_CORRESPONDENCE_LIST_API');
        set('cfgOperation', 'GET_CORRESPONDENCE_LIST');
        set('cfgMethod', 'POST');
        set('cfgBaseAlias', 'sample-api');
        set('cfgBaseUrl', 'https://api.example.com');
        set('cfgEndpoint', '/api/v1/resource/{{PolicyId}}');
        set('cfgAllowlist', 'api.example.com:443');
        set('cfgSuccessCodes', '200,201,202');
        set('cfgTimeout', '30');
        set('cfgRetries', '3');
        set('cfgIdempotency', 'Idempotency-Key');
        set('cfgCorrelation', 'X-Correlation-Id');

        set('cfgSourceJson', JSON.stringify({
            PolicyId: 'RCA00076',
            BeginDate: '02/01/2010',
            EndDate: '12/26/2023'
        }, null, 2));

        set('cfgPayloadTemplate', JSON.stringify({
            policyId: '{{PolicyId}}',
            beginDate: '{{BeginDate}}',
            endDate: '{{EndDate}}',
            correlationId: '{{System.ExecutionId}}'
        }, null, 2));

        markDirty();
        notify('Sample values loaded into the form. Nothing is saved until you click Save draft.');
    });

    // -----------------------------------------------------------------------
    // Dirty tracking and submit
    // -----------------------------------------------------------------------
    function markDirty() {
        dirty = true;
        setProgress();
    }

    form.addEventListener('change', markDirty);
    form.addEventListener('keyup', markDirty);

    form.addEventListener('submit', function (event) {
        // Validate, Connection test and Publish carry formnovalidate because
        // they read the saved draft from SQL, not the form.
        if (event.submitter && event.submitter.hasAttribute('formnovalidate')) {
            dirty = false;
            return;
        }

        // A required control inside a hidden pane cannot be focused, so the
        // browser blocks the submit with no visible message. Jump to the
        // offending step first.
        if (!form.checkValidity()) {
            var invalid = form.querySelector(':invalid');
            var pane = invalid ? invalid.closest('.apiconfig-pane') : null;

            if (pane) showStep(parseInt(pane.getAttribute('data-pane'), 10));

            event.preventDefault();
            form.reportValidity();
            notify('Fill in the highlighted field before saving.');
            return;
        }

        dirty = false;
    });

    window.addEventListener('beforeunload', function (event) {
        if (dirty && !settings.readOnly) {
            event.preventDefault();
            event.returnValue = '';
        }
    });

    function notify(message) {
        if (window.showToast) window.showToast(message);
        else console.info(message);
    }

    showStep(currentStep);
})();

/* ---------------------------------------------------------------------------
   Simulator: selecting a connector loads its saved sample data and limits the
   execution modes to what the published version permits.
   --------------------------------------------------------------------------- */
(function () {
    'use strict';

    var connector = document.getElementById('simConnector');
    if (!connector) return;

    connector.addEventListener('change', function () {
        var option = connector.options[connector.selectedIndex];
        var source = document.getElementById('simSource');
        var mode = document.getElementById('simMode');
        if (!option || !mode) return;

        var sample = option.getAttribute('data-sample');
        if (sample && source) source.value = sample;

        Array.prototype.forEach.call(mode.options, function (o) { o.disabled = false; });

        var allowed = option.getAttribute('data-mode');
        if (allowed === 'AttendedOnly') {
            mode.value = 'Attended';
            mode.querySelector('option[value="UnattendedAfterApproval"]').disabled = true;
        } else if (allowed === 'UnattendedOnly') {
            mode.value = 'UnattendedAfterApproval';
            mode.querySelector('option[value="Attended"]').disabled = true;
        }
    });
})();
