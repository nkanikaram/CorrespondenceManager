(function () {
  'use strict';

  var app = angular.module('adminPortal');

  var TEMPLATE = JSON.stringify({
    Process: {
      DocId: '',
      RequestingSystemName: '',
      ECCOTrackingClaimNumber: '',
      ECCOTrackingCustomerId: '',
      ECCOTrackingPolicyNumber: '',
      DeliveryMethod: 'Mail',
      DocumentDescription: '',
      MailDeliveryInfo: {
        RecipientFullName: '',
        RecipientFirstName: '',
        RecipientLastName: '',
        RecipientAddress: {
          RecipientAddressLine1: '',
          RecipientAddressLine2: null,
          RecipientAddressLine3: null,
          RecipientAddressCity: '',
          RecipientAddressST: '',
          RecipientAddressZIP: '',
          RecipientCountry: 'US'
        },
        NonUSAddressFlag: false,
        BadAddressFlag: false
      },
      CompositionInfo: { VersionCode: '', CompositionSchemaId: '', SolutionsId: '' },
      AdditionalData: {}
    }
  }, null, 2);

  /**
   * Builds correspondence payloads from AI-extracted fields. Shared by the
   * "new draft" and "patch the open draft" flows so they stay in sync.
   */
  app.factory('correspondencePayload', ['payloadValidation', function (payloadValidation) {
    function v(x, fallback) { return x !== undefined && x !== null ? x : (fallback === undefined ? '' : fallback); }

    function build(params, extraContextLines) {
      var payload = {
        Process: {
          DocId: '',
          RequestingSystemName: v(params.requestingSystemName),
          ECCOTrackingClaimNumber: v(params.claimNumber),
          ECCOTrackingCustomerId: v(params.customerId),
          ECCOTrackingPolicyNumber: v(params.policyNumber),
          DeliveryMethod: v(params.deliveryMethod, 'Mail'),
          DocumentDescription: v(params.documentDescription),
          MailDeliveryInfo: {
            RecipientFullName: v(params.recipientFullName),
            RecipientFirstName: v(params.recipientFirstName),
            RecipientLastName: v(params.recipientLastName),
            RecipientAddress: {
              RecipientAddressLine1: v(params.addressLine1),
              RecipientAddressLine2: null,
              RecipientAddressLine3: null,
              RecipientAddressCity: v(params.city),
              RecipientAddressST: v(params.state),
              RecipientAddressZIP: v(params.zip),
              RecipientCountry: v(params.country, 'US')
            },
            NonUSAddressFlag: false,
            BadAddressFlag: false
          },
          CompositionInfo: { VersionCode: v(params.versionCode), CompositionSchemaId: '', SolutionsId: v(params.solutionsId) },
          AdditionalData: {}
        }
      };

      // What the AI extracted, in human-readable form.
      var address = [params.addressLine1, params.city, params.state, params.zip].filter(Boolean).join(', ');
      var context = (extraContextLines || []).concat([
        params.documentDescription && '📋 Document: ' + params.documentDescription,
        params.recipientFullName && '👤 Recipient: ' + params.recipientFullName,
        params.claimNumber && '🎫 Claim: ' + params.claimNumber,
        params.policyNumber && '📄 Policy: ' + params.policyNumber,
        (params.addressLine1 || params.city || params.state) && '📍 Address: ' + address,
        params.deliveryMethod && '📮 Delivery: ' + params.deliveryMethod
      ]).filter(Boolean).join('\n');

      // Validate before opening the form — surfaces missing/malformed fields immediately.
      var validation = payloadValidation.validatePayload(payload);
      var issueLines = validation.errors.map(function (x) { return '❌ ' + x.message; })
        .concat(validation.warnings.map(function (x) { return '⚠️ ' + x.message; }));
      var base = context || 'AI generated correspondence request';

      return {
        payload: payload,
        contextWithValidation: issueLines.length ? base + '\n\n' + issueLines.join('\n') : base,
        validation: validation
      };
    }

    return { TEMPLATE: TEMPLATE, build: build };
  }]);

  /**
   * <payload-validation-display validation="…" json-payload="…" on-apply-suggestion="…(fieldName, autoFix)">
   * Summary badges plus blocking errors, optional warnings and enhancement suggestions.
   */
  app.component('payloadValidationDisplay', {
    bindings: { validation: '<', jsonPayload: '<', onApplySuggestion: '&?' },
    templateUrl: 'app/components/payload/payload-validation-display.html',
    controller: function () {
      var $ctrl = this;
      $ctrl.rawPreview = function () {
        try {
          return JSON.stringify(JSON.parse($ctrl.jsonPayload), null, 2).substring(0, 500) + '...';
        } catch (e) {
          return String($ctrl.jsonPayload || '').substring(0, 500);
        }
      };
      $ctrl.apply = function (issue) {
        if ($ctrl.onApplySuggestion) $ctrl.onApplySuggestion({ fieldName: issue.fieldName, autoFix: issue.autoFix });
      };
    }
  });

  /**
   * <insert-correspondence-modal show="…" ai-payload="…" ai-context="…"
   *     on-hide="…()" on-success="…(message)" on-error="…(message)">
   * JSON editor for POST /CommManager/processcorrespondencerequest with live validation.
   */
  app.component('insertCorrespondenceModal', {
    bindings: { show: '<', aiPayload: '<', aiContext: '<', onHide: '&', onSuccess: '&', onError: '&' , onPreviewLetter: '&?'},
    templateUrl: 'app/components/payload/insert-correspondence-modal.html',
    controller: ['correspondenceApi', 'payloadValidation', 'errorRecovery', function (correspondenceApi, payloadValidation, errorRecovery) {
      var $ctrl = this;
      $ctrl.json = TEMPLATE;
      $ctrl.validation = null;

      function validate(jsonString) {
        try {
          $ctrl.validation = payloadValidation.validatePayload(JSON.parse(jsonString));
        } catch (e) {
          $ctrl.validation = null; // JSON parse errors are reported on submit
        }
        return $ctrl.validation;
      }

      $ctrl.$onChanges = function () {
        // A freshly generated (or patched) AI payload replaces whatever is in the editor.
        if ($ctrl.aiPayload && $ctrl.show) {
          $ctrl.json = $ctrl.aiPayload;
          $ctrl.error = null;
          $ctrl.success = false;
          validate($ctrl.aiPayload);
        }
      };

      $ctrl.onJsonChange = function () {
        $ctrl.error = null;
        $ctrl.success = false;
        validate($ctrl.json);
      };

      $ctrl.hide = function () {
        if ($ctrl.loading) return;
        $ctrl.error = null;
        $ctrl.success = false;
        $ctrl.json = TEMPLATE;
        $ctrl.validation = null;
        $ctrl.onHide();
      };

      $ctrl.preview = function () {
        $ctrl.error = null;
        try { JSON.parse($ctrl.json); } catch (e) { $ctrl.error = 'Invalid JSON — please check the payload.'; return; }
        $ctrl.previewing = true;
        var p = $ctrl.onPreviewLetter({ payload: $ctrl.json });
        if (p && p.finally) p.finally(function () { $ctrl.previewing = false; });
        else $ctrl.previewing = false;
      };

      $ctrl.canSubmit = function () {
        return !$ctrl.loading && !!($ctrl.json || '').trim() && !($ctrl.validation && !$ctrl.validation.isValid);
      };

      $ctrl.submit = function () {
        $ctrl.error = null;
        $ctrl.success = false;
        var result = validate($ctrl.json);
        if (result && !result.isValid) {
          $ctrl.error = 'Please fix the blocking errors before submitting.';
          return;
        }
        var parsed;
        try {
          parsed = JSON.parse($ctrl.json);
        } catch (e) {
          $ctrl.error = 'Invalid JSON — please check the payload.';
          return;
        }
        $ctrl.loading = true;
        correspondenceApi.insert(parsed)
          .then(function (resp) {
            var docId = resp && (resp.docId || resp.DocId);
            $ctrl.success = true;
            $ctrl.json = TEMPLATE;
            $ctrl.validation = null;
            $ctrl.onSuccess({
              message: docId ? 'Correspondence request accepted — DocId: ' + docId : 'Correspondence request accepted for processing.'
            });
          })
          .catch(function (err) {
            var msg = errorRecovery.messageOf(err);
            $ctrl.error = msg;
            $ctrl.onError({ message: msg });
          })
          .finally(function () { $ctrl.loading = false; });
      };

      var FIELD_PATHS = {
        email: ['Process', 'MailDeliveryInfo', 'RecipientEmailAddress'],
        recipientName: ['Process', 'MailDeliveryInfo', 'RecipientFullName'],
        claimNumber: ['Process', 'ECCOTrackingClaimNumber'],
        policyNumber: ['Process', 'ECCOTrackingPolicyNumber']
      };

      $ctrl.applySuggestion = function (fieldName, autoFix) {
        var path = FIELD_PATHS[fieldName];
        if (!path) return;
        try {
          var parsed = JSON.parse($ctrl.json);
          var node = parsed;
          for (var i = 0; i < path.length - 1; i++) {
            if (!node[path[i]]) node[path[i]] = {};
            node = node[path[i]];
          }
          node[path[path.length - 1]] = autoFix;
          $ctrl.json = JSON.stringify(parsed, null, 2);
          validate($ctrl.json);
        } catch (e) {
          // Invalid JSON — the fix can't be applied.
        }
      };
    }]
  });

  /**
   * <payload-viewer-modal show="…" payload="…" is-encrypted="…" on-close="…()" on-copied="…()">
   * Stored request payload as formatted JSON or a flattened key/value table.
   */
  app.component('payloadViewerModal', {
    bindings: { show: '<', payload: '<', isEncrypted: '<', onClose: '&', onCopied: '&' },
    templateUrl: 'app/components/payload/payload-viewer-modal.html',
    controller: ['$window', function ($window) {
      var $ctrl = this;

      function flatten(obj, prefix, rows) {
        if (obj === null || obj === undefined) return;
        if (typeof obj !== 'object') {
          rows.push({ key: prefix || 'value', value: String(obj) });
          return;
        }
        if (Array.isArray(obj)) {
          obj.forEach(function (item, idx) {
            var key = prefix ? prefix + '[' + idx + ']' : '[' + idx + ']';
            if (typeof item === 'object' && item !== null) flatten(item, key, rows);
            else rows.push({ key: key, value: String(item) });
          });
          return;
        }
        Object.keys(obj).forEach(function (k) {
          var key = prefix ? prefix + '.' + k : k;
          var val = obj[k];
          if (val !== null && typeof val === 'object') flatten(val, key, rows);
          else rows.push({ key: key, value: String(val === null || val === undefined ? '' : val) });
        });
      }

      $ctrl.$onChanges = function (changes) {
        if (changes.show && $ctrl.show) $ctrl.mode = 'json';
        if (!changes.payload && !changes.show) return;
        var text = $ctrl.payload || '';
        try {
          var parsed = JSON.parse(text);
          $ctrl.formatted = JSON.stringify(parsed, null, 2);
          $ctrl.isValidJson = true;
          $ctrl.rows = [];
          flatten(parsed, '', $ctrl.rows);
        } catch (e) {
          $ctrl.formatted = text;
          $ctrl.isValidJson = false;
          $ctrl.rows = null;
        }
      };

      $ctrl.copy = function () {
        if (!$ctrl.payload || !$window.navigator.clipboard) return;
        $window.navigator.clipboard.writeText($ctrl.payload).then(function () { $ctrl.onCopied(); });
      };
    }]
  });

  /**
   * <search-refinement-panel refinement="…" on-apply-suggestion="…(suggestion)" on-apply-typo-fix="…(corrected)">
   * A typo fix and/or a few one-click structured filters implied by the free-text search.
   */
  app.component('searchRefinementPanel', {
    bindings: { refinement: '<', onApplySuggestion: '&', onApplyTypoFix: '&' },
    templateUrl: 'app/components/payload/search-refinement-panel.html',
    controller: function () {
      var $ctrl = this;
      var dismissed = {};
      $ctrl.collapsed = false;

      $ctrl.suggestions = function () {
        var list = ($ctrl.refinement && $ctrl.refinement.suggestions) || [];
        return list.filter(function (s) { return !dismissed[s.id]; });
      };
      $ctrl.visibleSuggestions = function () { return $ctrl.suggestions().slice(0, 3); };
      $ctrl.typoFix = function () { return $ctrl.refinement && $ctrl.refinement.typoCorrection; };
      $ctrl.hasContent = function () { return !!$ctrl.typoFix() || $ctrl.suggestions().length > 0; };
      $ctrl.count = function () { return ($ctrl.typoFix() ? 1 : 0) + $ctrl.suggestions().length; };
      $ctrl.dismiss = function (s) { dismissed[s.id] = true; };
      $ctrl.apply = function (s) {
        $ctrl.onApplySuggestion({ suggestion: s });
        dismissed[s.id] = true;
      };
    }
  });
})();
