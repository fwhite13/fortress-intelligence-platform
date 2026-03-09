// SurveyJS Preview Interop for Fortress Form Tools
window.initSurveyPreview = function (elementId, jsonString) {
    var container = document.getElementById(elementId);
    if (!container) {
        console.warn('Survey preview container not found:', elementId);
        return;
    }

    try {
        var json = typeof jsonString === 'string' ? JSON.parse(jsonString) : jsonString;
        container.innerHTML = '';

        // Check if SurveyJS is loaded
        if (typeof SurveyCore !== 'undefined' && typeof SurveyUI !== 'undefined') {
            var model = new SurveyCore.Model(json);
            var surveyDiv = document.createElement('div');
            container.appendChild(surveyDiv);
            SurveyUI.renderSurvey(model, surveyDiv);
        } else if (typeof Survey !== 'undefined') {
            var model = new Survey.Model(json);
            container.innerHTML = '';
            var surveyDiv = document.createElement('div');
            container.appendChild(surveyDiv);
            if (Survey.SurveyNG) {
                Survey.SurveyNG.render(surveyDiv, { model: model });
            } else {
                // Fallback: show formatted JSON preview
                showJsonPreview(container, json);
            }
        } else {
            // SurveyJS not loaded — show a nice formatted preview
            showJsonPreview(container, json);
        }
    } catch (err) {
        container.innerHTML = '<div style="padding:16px;color:#ef4444;">' +
            '<strong>Preview Error:</strong> ' + err.message +
            '</div>';
        console.error('SurveyJS preview error:', err);
    }
};

function showJsonPreview(container, json) {
    var html = '<div style="font-family:Inter,sans-serif;">';
    html += '<h3 style="margin:0 0 16px 0;color:#1f2937;">' + (json.title || 'Survey Preview') + '</h3>';

    var pages = json.pages || [{ elements: json.elements || [] }];
    pages.forEach(function (page, pi) {
        html += '<div style="border:1px solid #e5e7eb;border-radius:8px;padding:16px;margin-bottom:12px;">';
        html += '<h4 style="margin:0 0 12px 0;color:#374151;">Page ' + (pi + 1) +
            (page.title ? ': ' + page.title : '') + '</h4>';

        var elements = page.elements || [];
        elements.forEach(function (el) {
            var required = el.isRequired ? ' <span style="color:#ef4444;">*</span>' : '';
            html += '<div style="margin-bottom:12px;">';
            html += '<label style="display:block;font-weight:500;margin-bottom:4px;color:#374151;">' +
                (el.title || el.name) + required + '</label>';

            switch (el.type) {
                case 'boolean':
                    html += '<input type="checkbox" disabled style="margin-right:8px;"><span style="color:#6b7280;">Yes / No</span>';
                    break;
                case 'comment':
                    html += '<textarea disabled style="width:100%;height:60px;border:1px solid #d1d5db;border-radius:4px;padding:8px;background:#f9fafb;" placeholder="Enter text..."></textarea>';
                    break;
                case 'dropdown':
                    html += '<select disabled style="width:100%;padding:8px;border:1px solid #d1d5db;border-radius:4px;background:#f9fafb;"><option>Select...</option></select>';
                    break;
                case 'radiogroup':
                    var choices = el.choices || ['Option 1', 'Option 2'];
                    choices.forEach(function (c) {
                        var label = typeof c === 'string' ? c : (c.text || c.value || c);
                        html += '<div><input type="radio" disabled><span style="margin-left:6px;color:#374151;">' + label + '</span></div>';
                    });
                    break;
                default:
                    var inputType = el.inputType || 'text';
                    html += '<input type="' + inputType + '" disabled style="width:100%;padding:8px;border:1px solid #d1d5db;border-radius:4px;background:#f9fafb;" placeholder="Enter ' + (el.title || el.name) + '...">';
            }
            html += '</div>';
        });
        html += '</div>';
    });

    html += '</div>';
    container.innerHTML = html;
}

// File download interop — used by Question Set export
window.downloadFile = function(filename, content) {
    var blob = new Blob([content], { type: 'application/json' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    URL.revokeObjectURL(url);
};

// Sprint 5: SurveyJS live preview rendering
window.renderSurvey = function(containerId, surveyJson) {
    try {
        var container = document.getElementById(containerId);
        if (!container) {
            console.warn('Survey container not found:', containerId);
            return;
        }
        container.innerHTML = '';
        if (typeof Survey !== 'undefined' && Survey.Model) {
            var survey = new Survey.Model(JSON.parse(surveyJson));
            survey.render(document.getElementById(containerId));
        } else if (typeof SurveyCore !== 'undefined' && typeof SurveyUI !== 'undefined') {
            var model = new SurveyCore.Model(JSON.parse(surveyJson));
            var div = document.createElement('div');
            container.appendChild(div);
            SurveyUI.renderSurvey(model, div);
        } else {
            // SurveyJS CDN not yet loaded — show a note
            container.innerHTML = '<p style="color:#666;padding:16px;">SurveyJS not loaded — see the JSON tab for content.</p>';
            console.warn('renderSurvey: SurveyJS library not available');
        }
    } catch(e) {
        var el = document.getElementById(containerId);
        if (el) el.innerHTML = '<p style="color:red;padding:16px;">Preview error: ' + e.message + '</p>';
        console.error('renderSurvey error:', e);
    }
};
