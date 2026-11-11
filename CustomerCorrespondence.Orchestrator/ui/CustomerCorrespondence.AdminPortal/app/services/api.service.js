(function () {
  'use strict';

  /**
   * Backend HTTP calls: correspondence search/insert, chat history and the AI
   * proxy. Every rejection from correspondenceApi carries the errorRecovery
   * classification ({ isRetryable, category, message, suggestion }) alongside
   * the raw $http fields.
   */
  angular.module('adminPortal')
    .factory('correspondenceApi', correspondenceApi)
    .factory('chatHistoryApi', chatHistoryApi)
    .factory('aiService', aiService);

  correspondenceApi.$inject = ['$http', '$q', 'APP_CONFIG', 'errorRecovery'];

  function correspondenceApi($http, $q, APP_CONFIG, errorRecovery) {
    var base = APP_CONFIG.apiBaseUrl;
    var opts = { timeout: APP_CONFIG.searchTimeoutMs };

    function unwrap(promise) {
      return promise.then(
        function (res) { return res.data; },
        function (err) { return $q.reject(angular.extend({}, err, errorRecovery.classifyError(err))); }
      );
    }

    return {
      search: function (request) {
        return unwrap($http.post(base + '/correspondence/search', request, opts));
      },
      // Portal endpoints: the API checks the user's permission and access policies, then runs the
      // same pipeline as /CommManager/processcorrespondencerequest.
      insert: function (payload) {
        return unwrap($http.post(base + '/portal/correspondence/requests', payload, opts));
      },
      resubmit: function (correspondenceId, payload) {
        return unwrap($http.post(base + '/portal/correspondence/' + encodeURIComponent(correspondenceId) + '/resubmit', payload, opts));
      },
      /**
       * Moves records (with their status history and PDF) to the recycle bin. Resolves to
       * { deleted: [ids], denied: [{ correspondenceId, message }], notFound: [ids] }.
       */
      deleteRecords: function (correspondenceIds) {
        return unwrap($http.post(base + '/portal/correspondence/delete', { correspondenceIds: correspondenceIds }, opts));
      },
      /** Everything about one record: fields, status history, payload (if allowed), PDF state, activity. */
      details: function (correspondenceId) {
        return unwrap($http.get(base + '/portal/correspondence/' + encodeURIComponent(correspondenceId) + '/details', opts));
      },
      /**
       * Letter preview PDF (not stored). body: { payload } (published template for its document type),
       * { templateId } or { template } with optional { correspondenceId }. Resolves to { blob, missing, template }.
       */
      previewLetter: function (body) {
        return $http.post(base + '/portal/letters/preview', body, angular.extend({ responseType: 'blob' }, opts)).then(function (res) {
          return {
            blob: res.data,
            missing: (res.headers('X-Letter-Missing') || '').split(',').filter(Boolean),
            template: res.headers('X-Letter-Template')
          };
        }, function (err) {
          if (err.data && typeof err.data.text === 'function') {
            return err.data.text().then(function (text) {
              var data;
              try { data = JSON.parse(text); } catch (e) { data = { message: text }; }
              return $q.reject(angular.extend({}, err, { data: data }, errorRecovery.classifyError(angular.extend({}, err, { data: data }))));
            });
          }
          return $q.reject(angular.extend({}, err, errorRecovery.classifyError(err)));
        });
      },
      /** PDF state per record: { storageConfigured, items: [{ correspondenceId, status, generatedUtc, sizeBytes, lastError }] }. */
      pdfStatus: function (correspondenceIds) {
        return unwrap($http.post(base + '/portal/correspondence/pdf/status', { correspondenceIds: correspondenceIds }, opts));
      },
      /** Generates (or regenerates) one record's PDF now. */
      generatePdf: function (correspondenceId) {
        return unwrap($http.post(base + '/portal/correspondence/' + encodeURIComponent(correspondenceId) + '/pdf', null, opts));
      },
      /** The stored PDF as a Blob, with the server's file name when it sends one. */
      downloadPdf: function (correspondenceId) {
        return $http.get(base + '/portal/correspondence/' + encodeURIComponent(correspondenceId) + '/pdf',
          angular.extend({ responseType: 'blob' }, opts)).then(function (res) {
          var disposition = res.headers('Content-Disposition') || '';
          var match = /filename="?([^";]+)"?/i.exec(disposition);
          return { blob: res.data, fileName: match ? match[1] : null };
        }, function (err) {
          // Error bodies arrive as a Blob too: read the JSON message out of it.
          if (err.data && typeof err.data.text === 'function') {
            return err.data.text().then(function (text) {
              var data = null;
              try { data = JSON.parse(text); } catch (e) { data = { message: text }; }
              return $q.reject(angular.extend({}, err, { data: data }, errorRecovery.classifyError(angular.extend({}, err, { data: data }))));
            });
          }
          return $q.reject(angular.extend({}, err, errorRecovery.classifyError(err)));
        });
      },
      /** Distinct customers / delivery methods within the user's data scope (grid checkbox filters). */
      getFieldValues: function (field) {
        return unwrap($http.get(base + '/correspondence/values/' + encodeURIComponent(field), opts));
      },
      // The authoritative list of status values that exist in the data — lets the AI validate a
      // status name (or suggest the closest match) instead of guessing from training data.
      getStatusValues: function () {
        return unwrap($http.get(base + '/correspondence/statuses', opts));
      },
      getAiUsageSummary: function () {
        return unwrap($http.get(base + '/Ai/usage/summary', opts));
      }
    };
  }

  chatHistoryApi.$inject = ['$http', 'APP_CONFIG'];

  // Conversations belong to the signed-in user: the API takes the owner from the access token.
  function chatHistoryApi($http, APP_CONFIG) {
    var base = APP_CONFIG.apiBaseUrl + '/chathistory/conversations';
    var opts = { timeout: APP_CONFIG.chatHistoryTimeoutMs };

    function data(res) { return res.data; }

    return {
      listConversations: function () {
        return $http.get(base, opts).then(data);
      },
      getConversation: function (conversationId) {
        return $http.get(base + '/' + encodeURIComponent(conversationId), opts).then(data);
      },
      createConversation: function (title) {
        return $http.post(base, { title: title }, opts).then(data);
      },
      appendMessage: function (conversationId, role, content) {
        return $http.post(base + '/' + encodeURIComponent(conversationId) + '/messages', { role: role, content: content }, opts)
          .then(function () { return undefined; });
      },
      deleteConversation: function (conversationId) {
        return $http.delete(base + '/' + encodeURIComponent(conversationId), opts).then(function () { return undefined; });
      }
    };
  }

  aiService.$inject = ['$http', '$log', 'APP_CONFIG', 'aiPrompt'];

  function aiService($http, $log, APP_CONFIG, aiPrompt) {
    /**
     * Sends one turn to the backend's AI proxy (the API key lives server-side).
     * options: { history, activeParams, activeDraft, statusValues, onUsage(usage) }
     * Resolves to an aiPrompt.parseResponse result.
     */
    function query(prompt, searchContext, options) {
      options = options || {};
      var body = aiPrompt.buildRequestBody(prompt, searchContext, options, APP_CONFIG);
      return $http.post(APP_CONFIG.apiBaseUrl + '/ai/messages', body).then(function (res) {
        var payload = res.data || {};
        $log.debug('[AI] stop_reason:', payload.stop_reason, '| content types:',
          (payload.content || []).map(function (b) { return b.type; }));
        if (payload.usage && options.onUsage) {
          options.onUsage({
            inputTokens: payload.usage.input_tokens || 0,
            outputTokens: payload.usage.output_tokens || 0
          });
        }
        return aiPrompt.parseResponse(payload);
      });
    }

    return { query: query };
  }
})();
