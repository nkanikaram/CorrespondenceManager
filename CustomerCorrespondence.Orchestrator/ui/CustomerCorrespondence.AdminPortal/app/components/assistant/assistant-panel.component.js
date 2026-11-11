(function () {
  'use strict';

  /**
   * <assistant-panel>
   * "Ask ECCO" — natural-language control of the correspondence grid. Reads live
   * grid state and invokes grid actions through assistantBridge.
   *
   * History entries (newest first):
   *   { q, a, isSearch, isSend, isUpdate, isExport, isClear, isUndo, isMulti, sendError, params, charts }
   */
  angular.module('adminPortal').component('assistantPanel', {
    templateUrl: 'app/components/assistant/assistant-panel.html',
    controller: AssistantPanelController
  });

  // How many prior exchanges are resent as short-term memory (bounds token usage).
  var CONTEXT_TURNS = 3;
  // A prior answer only needs to be recognizable in context, not replayed verbatim.
  var CONTEXT_ANSWER_CHAR_LIMIT = 300;
  // Fallback until real measured usage exists — a typical call's cost (system prompt + tools + data context).
  var ESTIMATED_TOKENS_PER_AI_CALL = 5490;

  var KEYS = {
    favorites: 'ecco.ai.favoriteQueries',
    savedViews: 'ecco.ai.savedViews',
    tokenTotals: 'ecco.ai.tokenUsageTotals',
    savedViewUses: 'ecco.ai.savedViewUses',
    voice: 'ecco.ai.voiceGender'
  };

  // Chat history stores plain text per message, so chart snapshots ride along as a hidden
  // HTML-comment marker on the saved answer and are parsed back out when a conversation reopens.
  var CHART_MARKER_RE = /\n\n<!--charts:([\s\S]+)-->$/;

  var SAMPLE_QUERIES = [
    'Show records in Processing status',
    'Top 20 most recent completed requests',
    'Show failed records from the last 30 days',
    'How many records are there for each customer?',
    'Find claim CL-12345',
    'Show submitted records',
    'Latest 10 failed requests',
    'Show records delivered by Email',
    'How many records are in each status?',
    'Clear all the filters in the grid',
    'Records from Jan 2024',
    'Records from today',
    'Show failed records from last week and export them',
    'Undo the last change',
    'Show me a chart of records by status',
    'Export just the claim number and status columns'
  ];

  // Capability hints, not data observations (those belong in the Insights card).
  var TIPS = [
    '🗣️ Speak naturally: "show me everything from Payment Guard Advantage last month" works as well as picking filters manually.',
    '✏️ Refine without restating: after a search, say "actually make that Completed instead" — the AI merges it with your current filters.',
    '↩️ Made a wrong turn? Just say "undo" to revert your last filter change.',
    '🔗 Chain requests: "show failed records from last week and export them" runs both actions in one go.',
    '🎤 Click the mic icon to dictate a query instead of typing it.',
    '⭐ Shift-click a saved view chip to remove it.',
    '⌨️ Use ↑ / ↓ in the message box to cycle through your last queries, like a terminal.',
    '📌 Star a question after asking it to pin it under Favorites for next time.',
    '📌 Save the grid\'s current filters as a named "Saved View" to reapply with one click later.',
    '📊 Ask for a chart — "chart the status breakdown" — to see a bar chart instead of a wall of numbers.',
    '🔊 Click the speaker icon on any answer to have it read aloud.',
    '📊 Check the Token Usage card — "+ New chat" resets the conversation history that gets resent on every message.'
  ];

  var EMPTY_TOTALS = { inputTokens: 0, outputTokens: 0, calls: 0 };

  AssistantPanelController.$inject = [
    '$scope', '$q', '$timeout', '$log', '$window',
    'assistantBridge', 'aiService', 'aiPrompt', 'chatHistoryApi', 'correspondenceApi',
    'correspondenceAnalytics', 'searchQuery', 'errorRecovery', 'storage', 'speech', 'markdown'
  ];

  function AssistantPanelController(
    $scope, $q, $timeout, $log, $window,
    assistantBridge, aiService, aiPrompt, chatHistoryApi, correspondenceApi,
    correspondenceAnalytics, searchQuery, errorRecovery, storage, speech, markdown
  ) {
    var $ctrl = this;
    var queryHistory = []; // terminal-style ↑/↓ recall
    var historyIdx = -1;   // -1 = composing a new query
    var draftText = '';
    var lastQuery = '';
    // The chat on screen. Replaced (not mutated) on New chat / open, so a save queued for the
    // previous chat still lands in that chat's conversation.
    var currentChat = { id: null };
    var fetchedPreviewIds = {};
    var recognition = null;

    $ctrl.sampleQueries = SAMPLE_QUERIES;
    $ctrl.tips = TIPS;
    $ctrl.describeParams = searchQuery.describeParams;

    $ctrl.$onInit = function () {
      $ctrl.text = '';
      $ctrl.history = [];
      $ctrl.activeTab = 'ask';
      $ctrl.tip = TIPS[Math.floor(Math.random() * TIPS.length)];
      $ctrl.favorites = storage.getJson(KEYS.favorites, []);
      $ctrl.savedViews = storage.getJson(KEYS.savedViews, []);
      $ctrl.tokenTotals = angular.extend({}, EMPTY_TOTALS, storage.getJson(KEYS.tokenTotals, {}));
      var uses = parseInt(storage.get(KEYS.savedViewUses), 10);
      $ctrl.savedViewUses = isFinite(uses) ? uses : 0;
      $ctrl.voiceGender = storage.get(KEYS.voice) === 'female' ? 'female' : 'male';
      $ctrl.voiceSupported = speech.canListen();
      $ctrl.voiceOutputSupported = speech.canSpeak();
      $ctrl.speakingIndex = null;
      $ctrl.conversations = [];
      $ctrl.previews = {};
      $ctrl.selectedConversations = {};
      $ctrl.reminders = [
        { label: '15 min', ms: 15 * 60 * 1000 },
        { label: '1 hr', ms: 60 * 60 * 1000 },
        { label: '4 hrs', ms: 4 * 60 * 60 * 1000 }
      ];

      assistantBridge.registerAsker(function (text) { $ctrl.send(text); });
      refreshOrgUsage();
      refreshConversations();
    };

    $ctrl.$onDestroy = function () {
      assistantBridge.registerAsker(null);
      if (recognition) recognition.stop();
      speech.stopSpeaking();
    };

    // ---- Live grid state ----
    function ctx() { return assistantBridge.context(); }
    function actions() { return assistantBridge.actions(); }

    $ctrl.insights = function () { return ctx().insights || []; };
    $ctrl.hasData = function () {
      var sc = ctx().searchContext;
      return !!(sc && sc.items && sc.items.length > 0);
    };
    $ctrl.allLoaded = function () {
      var sc = ctx().searchContext;
      return !!(sc && sc.allDataLoaded && sc.allAggregates);
    };
    $ctrl.loadedTotal = function () {
      var sc = ctx().searchContext;
      return sc && sc.allAggregates ? sc.allAggregates.total : 0;
    };
    $ctrl.hasActiveParams = function () { return !searchQuery.isEmpty(ctx().activeParams); };

    // ---- Tokens ----
    function refreshOrgUsage() {
      correspondenceApi.getAiUsageSummary()
        .then(function (u) { $ctrl.orgUsage = u; })
        .catch(function () { $ctrl.orgUsage = null; });
    }

    function recordUsage(usage) {
      $ctrl.lastCallUsage = usage;
      $ctrl.tokenTotals = {
        inputTokens: $ctrl.tokenTotals.inputTokens + usage.inputTokens,
        outputTokens: $ctrl.tokenTotals.outputTokens + usage.outputTokens,
        calls: $ctrl.tokenTotals.calls + 1
      };
      storage.setJson(KEYS.tokenTotals, $ctrl.tokenTotals);
      refreshOrgUsage();
    }

    $ctrl.totalTokens = function () { return $ctrl.tokenTotals.inputTokens + $ctrl.tokenTotals.outputTokens; };

    $ctrl.resetTokens = function () {
      $ctrl.tokenTotals = angular.copy(EMPTY_TOTALS);
      storage.setJson(KEYS.tokenTotals, $ctrl.tokenTotals);
      $ctrl.lastCallUsage = null;
      $ctrl.savedViewUses = 0;
      storage.set(KEYS.savedViewUses, '0');
    };

    /** Saved-view reuse skips an AI call entirely — this measures the tokens that saved. */
    $ctrl.savings = function () {
      var avg = $ctrl.tokenTotals.calls > 0 ? Math.round($ctrl.totalTokens() / $ctrl.tokenTotals.calls) : ESTIMATED_TOKENS_PER_AI_CALL;
      var actual = $ctrl.totalTokens();
      var saved = $ctrl.savedViewUses * avg;
      var without = actual + saved;
      return {
        avgPerCall: avg,
        measured: $ctrl.tokenTotals.calls > 0,
        actual: actual,
        saved: saved,
        without: without,
        actualPct: without > 0 ? Math.max(4, (actual / without) * 100) : 100
      };
    };

    // ---- Sending ----
    function truncateForContext(answer) {
      if (answer.length <= CONTEXT_ANSWER_CHAR_LIMIT) return answer;
      return answer.slice(0, CONTEXT_ANSWER_CHAR_LIMIT).trim() + '… [truncated for context]';
    }

    /** Last few exchanges in chronological order, answers trimmed. */
    function contextTurns() {
      var turns = [];
      $ctrl.history.slice(0, CONTEXT_TURNS).slice().reverse().forEach(function (h) {
        if (h.q) turns.push({ role: 'user', content: h.q });
        if (h.a) turns.push({ role: 'assistant', content: truncateForContext(h.a) });
      });
      return turns;
    }

    function queryOptions(turns) {
      var c = ctx();
      return {
        history: turns,
        activeParams: c.activeParams,
        columnFilters: c.columnFilters,
        activeDraft: c.activeDraft,
        statusValues: c.statusValues,
        onUsage: recordUsage
      };
    }

    function pushHistory(item) {
      $ctrl.history = [item].concat($ctrl.history);
    }

    /** Snapshot of a breakdown for a chart, read from whatever aggregates are loaded right now. */
    function buildChartData(metric, chartType) {
      var sc = ctx().searchContext;
      var agg = sc && sc.allAggregates;
      chartType = chartType || 'bar';
      if (metric === 'deliveryByStatus') {
        var crossTab = (agg && agg.deliveryStatusCounts) || {};
        // pie/donut can't hold two dimensions — honor it as small multiples rather than substituting a bar.
        if (chartType === 'pie' || chartType === 'donut') {
          return { title: 'Delivery Method by Status', crossTab: crossTab, chartType: chartType + '-multi' };
        }
        return { title: 'Delivery Method by Status', crossTab: crossTab, chartType: 'stacked' };
      }
      var titles = { status: 'Records by Status', customer: 'Records by Customer', delivery: 'Records by Delivery Method' };
      var counts = metric === 'status' ? ((agg && agg.statusCounts) || (sc && sc.pageStatusCounts) || {})
        : metric === 'customer' ? ((agg && agg.customerCounts) || {})
          : ((agg && agg.deliveryCounts) || {});
      return { title: titles[metric] || 'Records', counts: counts, chartType: chartType };
    }

    function zeroHint(params) {
      var c = ctx();
      return correspondenceAnalytics.zeroResultStatusHint(params, c.searchContext, c.statusValues);
    }

    /** Executes one resolved tool call; resolves to the history flags it contributes. */
    function runAction(action) {
      var a = actions();
      switch (action.type) {
        case 'search':
          // Awaited so a chart chained after this in the same turn reads the fresh breakdown.
          return $q.when(a.search && a.search(action.params)).then(function () {
            return { isSearch: true, params: action.params, zeroResultHint: zeroHint(action.params) };
          });
        case 'export':
          if (a.export) a.export(action.params);
          return $q.resolve({ isExport: true });
        case 'clear_filters':
          if (a.clearFilters) a.clearFilters();
          return $q.resolve({ isClear: true });
        case 'undo':
          if (a.undo) a.undo();
          return $q.resolve({ isUndo: true });
        case 'chart':
          return $q.resolve({ charts: [buildChartData(action.params.metric, action.params.chartType)] });
        case 'send_correspondence':
          return $q.when(a.sendCorrespondence && a.sendCorrespondence(action.params)).then(
            function (confirmation) { return { isSend: true, confirmation: confirmation }; },
            function (err) { return { isSend: true, sendError: errorRecovery.messageOf(err) }; });
        case 'update_correspondence':
          return $q.when(a.updateCorrespondence && a.updateCorrespondence(action.params)).then(
            function (confirmation) { return { isUpdate: true, confirmation: confirmation }; },
            function (err) { return { isUpdate: true, sendError: errorRecovery.messageOf(err) }; });
        default:
          return $q.resolve({});
      }
    }

    /**
     * A search-only turn can't narrate a summary/timeline yet (the records weren't loaded when the
     * model decided to search). Once the search resolves, ask the same question again silently and
     * use that narrative as this turn's answer.
     */
    function narrativeFollowUp(q, turns, answerText, hint) {
      var sc = ctx().searchContext;
      if (hint || !aiPrompt.NARRATIVE_RE.test(q) || !sc || !sc.total) return $q.resolve(answerText);
      return aiService.query(q, sc, queryOptions(turns)).then(
        function (follow) { return follow.type === 'text' ? follow.text : answerText; },
        function (err) {
          // Best-effort — the search itself already succeeded and is shown either way.
          $log.warn('[AI] Narrative follow-up failed:', err);
          return answerText;
        });
    }

    /** Applies a parsed AI result; resolves to { answer, charts } for persistence. */
    function handleResult(q, result, turns) {
      if (result.type === 'search') {
        return runAction(result).then(function (r) {
          var answer = r.zeroResultHint ? result.text + '\n\n' + r.zeroResultHint : result.text;
          return narrativeFollowUp(q, turns, answer, r.zeroResultHint);
        }).then(function (answer) {
          pushHistory({ q: q, a: answer, isSearch: true, params: result.params });
          return { answer: answer };
        });
      }

      if (result.type === 'multi') {
        var combined = { isMulti: true };
        var charts = [];
        var hint = null;
        var chain = $q.resolve();
        result.actions.forEach(function (action) {
          chain = chain.then(function () { return runAction(action); }).then(function (r) {
            if (r.charts) charts = charts.concat(r.charts);
            if (r.zeroResultHint) hint = r.zeroResultHint;
            Object.keys(r).forEach(function (k) {
              if (r[k] !== undefined && r[k] !== null && k !== 'charts' && k !== 'zeroResultHint' && k !== 'confirmation') combined[k] = r[k];
            });
          });
        });
        return chain.then(function () {
          var answer = hint ? result.text + '\n\n' + hint : result.text;
          if (charts.length) combined.charts = charts;
          pushHistory(angular.extend({ q: q, a: answer }, combined));
          return { answer: answer, charts: charts.length ? charts : undefined };
        });
      }

      if (result.type === 'chart') {
        var chartList = [buildChartData(result.params.metric, result.params.chartType)];
        pushHistory({ q: q, a: result.text, charts: chartList });
        return $q.resolve({ answer: result.text, charts: chartList });
      }

      if (result.type === 'text') {
        pushHistory({ q: q, a: result.text });
        return $q.resolve({ answer: result.text });
      }

      // export / clear_filters / undo / send_correspondence / update_correspondence
      return runAction(result).then(function (r) {
        var answer = r.confirmation || result.text;
        var entry = angular.extend({ q: q, a: answer }, r);
        delete entry.confirmation;
        pushHistory(entry);
        return { answer: answer };
      });
    }

    $ctrl.send = function (override) {
      var source = override !== undefined ? override : $ctrl.text;
      if (!source || !source.trim() || $ctrl.loading) return;
      var q = source.trim();

      queryHistory = queryHistory.filter(function (h) { return h !== q; }).concat([q]);
      historyIdx = -1;
      draftText = '';
      lastQuery = q;
      $ctrl.text = '';
      $ctrl.loading = true;
      $ctrl.error = null;
      $ctrl.activeTab = 'conversation'; // show the answer to what was just asked

      var turns = contextTurns();
      return aiService.query(q, ctx().searchContext, queryOptions(turns))
        .then(function (result) { return handleResult(q, result, turns); })
        .then(function (turn) { persistTurn(q, turn.answer, turn.charts); })
        .catch(function (err) {
          $log.error('[AI] query failed:', err);
          $ctrl.error = errorRecovery.classifyError(err);
        })
        .finally(function () { $ctrl.loading = false; });
    };

    $ctrl.retry = function () {
      if (!$ctrl.error || !$ctrl.error.isRetryable) return;
      $ctrl.isRetrying = true;
      $q.when($ctrl.send(lastQuery)).finally(function () { $ctrl.isRetrying = false; });
    };

    $ctrl.runSample = function (q) {
      $ctrl.send(q);
    };

    $ctrl.clearInput = function () {
      $ctrl.text = '';
      $ctrl.error = null;
    };

    $ctrl.onKeydown = function (e) {
      if (e.key === 'Enter' && !e.shiftKey) {
        e.preventDefault();
        $ctrl.send();
        return;
      }
      if (e.key === 'ArrowUp' && queryHistory.length > 0) {
        // Only intercept while the cursor is still on the first line.
        if (($ctrl.text || '').substring(0, e.target.selectionStart).indexOf('\n') !== -1) return;
        e.preventDefault();
        if (historyIdx === -1) {
          draftText = $ctrl.text;
          historyIdx = queryHistory.length - 1;
        } else if (historyIdx > 0) {
          historyIdx--;
        }
        $ctrl.text = queryHistory[historyIdx];
      }
      if (e.key === 'ArrowDown' && historyIdx !== -1) {
        e.preventDefault();
        if (historyIdx < queryHistory.length - 1) {
          historyIdx++;
          $ctrl.text = queryHistory[historyIdx];
        } else {
          historyIdx = -1;
          $ctrl.text = draftText;
        }
      }
    };

    // ---- Favorites & saved views ----
    $ctrl.isFavorite = function (q) { return $ctrl.favorites.indexOf(q) !== -1; };

    $ctrl.toggleFavorite = function (q) {
      if (!q) return;
      $ctrl.favorites = $ctrl.isFavorite(q)
        ? $ctrl.favorites.filter(function (x) { return x !== q; })
        : $ctrl.favorites.concat([q]);
      storage.setJson(KEYS.favorites, $ctrl.favorites);
    };

    $ctrl.saveViewDefault = function () {
      var suggested = searchQuery.describeParams(ctx().activeParams);
      return suggested.length > 40 ? suggested.slice(0, 40) + '…' : suggested;
    };

    $ctrl.promptSaveView = function () {
      if ($ctrl.hasActiveParams()) $ctrl.showSaveViewPrompt = true;
    };

    $ctrl.confirmSaveView = function (name) {
      $ctrl.showSaveViewPrompt = false;
      var params = ctx().activeParams;
      if (!params) return;
      $ctrl.savedViews = $ctrl.savedViews.filter(function (v) { return v.name !== name; })
        .concat([{ name: name, params: angular.copy(params) }]);
      storage.setJson(KEYS.savedViews, $ctrl.savedViews);
    };

    $ctrl.clickSavedView = function (view, e) {
      if (e.shiftKey) {
        $ctrl.savedViews = $ctrl.savedViews.filter(function (v) { return v.name !== view.name; });
        storage.setJson(KEYS.savedViews, $ctrl.savedViews);
        return;
      }
      var a = actions();
      if (a.search) a.search(angular.copy(view.params));
      pushHistory({ q: 'Applied saved view "' + view.name + '"', a: 'Showing: ' + searchQuery.describeParams(view.params), isSearch: true, params: view.params });
      $ctrl.activeTab = 'conversation';
      // No AI call was made — tracked so the token-savings comparison stays accurate.
      $ctrl.savedViewUses++;
      storage.set(KEYS.savedViewUses, String($ctrl.savedViewUses));
    };

    // ---- Reminders ----
    // Best-effort: fires only while this tab stays open. A real "email me a digest" needs a backend job.
    $ctrl.scheduleReminder = function (opt) {
      var insights = $ctrl.insights();
      var summary = insights.length ? insights[0]
        : $ctrl.hasData() ? ($ctrl.loadedTotal() || ctx().searchContext.total) + ' records loaded'
          : 'no data loaded yet';
      var hasNotifications = 'Notification' in $window;
      if (hasNotifications && $window.Notification.permission === 'default') {
        $q.when($window.Notification.requestPermission()).catch(angular.noop);
      }
      $timeout(function () {
        try {
          if (hasNotifications && $window.Notification.permission === 'granted') {
            new $window.Notification('ECCO Correspondence Manager', { body: 'Reminder: ' + summary });
            return;
          }
        } catch (e) {
          // Notification API blocked — fall through to the in-panel reminder.
        }
        pushHistory({ q: '', a: '🔔 Reminder (' + opt.label + ' later): ' + summary });
      }, opt.ms);
      pushHistory({ q: '', a: 'Okay — I\'ll remind you in ' + opt.label + ' (while this tab stays open).' });
    };

    // ---- Voice ----
    $ctrl.setVoice = function (g) {
      $ctrl.voiceGender = g;
      storage.set(KEYS.voice, g);
    };

    // ---- Tools panel: insights, tips, questions, saved views, favorites, usage, settings ----
    $ctrl.panel = null;
    $ctrl.openPanel = function (id) {
      $ctrl.panel = id || $ctrl.lastPanel || ($ctrl.insights().length ? 'insights' : 'samples');
      $ctrl.lastPanel = $ctrl.panel;
    };
    $ctrl.closePanel = function () { $ctrl.panel = null; };
    // Memoized on its inputs: ng-repeat over a fresh array each digest would never settle.
    var sectionsCache = { key: null, list: [] };
    $ctrl.panelSections = function () {
      var counts = [$ctrl.insights().length, $ctrl.savedViews.length, $ctrl.favorites.length];
      var key = counts.join('|');
      if (key !== sectionsCache.key) {
        sectionsCache = { key: key, list: [
          { id: 'insights', icon: '💡', label: 'Insights', count: counts[0] },
          { id: 'samples', icon: '💬', label: 'Questions' },
          { id: 'tips', icon: '✨', label: 'Tips' },
          { id: 'views', icon: '📌', label: 'Saved views', count: counts[1] },
          { id: 'favorites', icon: '⭐', label: 'Favorites', count: counts[2] },
          { id: 'usage', icon: '📊', label: 'Usage' },
          { id: 'settings', icon: '⚙️', label: 'Settings' }
        ] };
      }
      return sectionsCache.list;
    };
    $ctrl.openPreferences = function () {
      $ctrl.closePanel();
      $scope.$root.$broadcast('open:preferences');
    };

    $ctrl.toggleListening = function () {
      if ($ctrl.listening) {
        if (recognition) recognition.stop();
        return;
      }
      recognition = speech.listen(
        function (transcript) {
          $scope.$apply(function () { $ctrl.text = ($ctrl.text ? $ctrl.text + ' ' : '') + transcript; });
        },
        function () {
          recognition = null;
          $scope.$applyAsync(function () { $ctrl.listening = false; });
        });
      $ctrl.listening = !!recognition;
    };

    $ctrl.speak = function (idx, text) {
      if ($ctrl.speakingIndex === idx) {
        speech.stopSpeaking();
        $ctrl.speakingIndex = null;
        return;
      }
      var started = speech.speak(markdown.toSpeechText(text), $ctrl.voiceGender, function () {
        $scope.$applyAsync(function () { if ($ctrl.speakingIndex === idx) $ctrl.speakingIndex = null; });
      });
      $ctrl.speakingIndex = started ? idx : null;
    };

    // ---- Persisted conversations ----
    function decodeChartMarker(content) {
      var match = (content || '').match(CHART_MARKER_RE);
      if (!match) return { text: content || '' };
      try {
        return { text: content.slice(0, match.index), charts: JSON.parse(match[1]) };
      } catch (e) {
        return { text: content };
      }
    }

    function refreshConversations() {
      return chatHistoryApi.listConversations().then(function (list) {
        $ctrl.conversations = list || [];
        // The stored title is truncated — show the full first message instead, fetched once and cached.
        var toFetch = $ctrl.conversations.filter(function (c) { return !fetchedPreviewIds[c.conversationId]; });
        toFetch.forEach(function (c) { fetchedPreviewIds[c.conversationId] = true; });
        return $q.all(toFetch.map(function (c) {
          return chatHistoryApi.getConversation(c.conversationId).then(function (detail) {
            var firstUser = (detail.messages || []).filter(function (m) { return m.role === 'user'; })[0];
            $ctrl.previews[c.conversationId] = (firstUser && firstUser.content) || c.title || '';
          }, function () {
            delete fetchedPreviewIds[c.conversationId]; // retry on next refresh
          });
        }));
      }).catch(function (err) {
        // Best-effort — the history list never blocks the assistant itself.
        $log.warn('[AI] Failed to load chat history list:', err);
      });
    }

    // Saves are queued so a quick follow-up question can't race the first turn's
    // createConversation (which would split one chat into two) or reorder messages.
    var persistQueue = $q.resolve();

    function persistTurn(question, answer, charts) {
      var stored = charts && charts.length ? answer + '\n\n<!--charts:' + JSON.stringify(charts) + '-->' : answer;
      var chat = currentChat;
      persistQueue = persistQueue.then(function () {
        var idPromise = chat.id
          ? $q.resolve(chat.id)
          : chatHistoryApi.createConversation(question.length > 100 ? question.slice(0, 100) + '…' : question)
            .then(function (created) {
              chat.id = created.conversationId;
              if (chat === currentChat) $ctrl.activeConversationId = chat.id;
              return chat.id;
            });
        return idPromise
          .then(function (id) {
            return chatHistoryApi.appendMessage(id, 'user', question).then(function () {
              return chatHistoryApi.appendMessage(id, 'assistant', stored);
            });
          })
          .then(function () {
            $ctrl.persistFailed = false;
            refreshConversations();
          })
          .catch(function (err) {
            // Never let a save failure affect the live chat — but flag it so a missing History entry isn't silent.
            $log.warn('[AI] Failed to persist chat turn:', err);
            $ctrl.persistFailed = true;
          });
      });
      return persistQueue;
    }

    $ctrl.openConversation = function (conversationId) {
      chatHistoryApi.getConversation(conversationId).then(function (convo) {
        var items = [];
        var messages = convo.messages || [];
        messages.forEach(function (m, i) {
          if (m.role !== 'user') return;
          var next = messages[i + 1];
          var decoded = decodeChartMarker(next && next.role === 'assistant' ? next.content : '');
          items.unshift({ q: m.content, a: decoded.text, charts: decoded.charts });
        });
        $ctrl.history = items;
        currentChat = { id: conversationId };
        $ctrl.activeConversationId = conversationId;
        $ctrl.activeTab = 'conversation';
      }).catch(function (err) {
        $log.warn('[AI] Failed to load conversation:', err);
      });
    };

    $ctrl.newChat = function () {
      speech.stopSpeaking();
      $ctrl.speakingIndex = null;
      $ctrl.history = [];
      currentChat = { id: null };
      $ctrl.activeConversationId = null;
      $ctrl.activeTab = 'ask';
    };

    $ctrl.previewOf = function (c) {
      return $ctrl.previews[c.conversationId] || c.title || '(untitled)';
    };

    $ctrl.selectedCount = function () {
      return Object.keys($ctrl.selectedConversations).filter(function (id) { return $ctrl.selectedConversations[id]; }).length;
    };

    $ctrl.askDelete = function (conversationId, e) {
      e.stopPropagation();
      $ctrl.deleteConfirmId = conversationId;
    };

    $ctrl.confirmDelete = function () {
      var id = $ctrl.deleteConfirmId;
      $ctrl.deleteConfirmId = null;
      if (!id) return;
      chatHistoryApi.deleteConversation(id).then(function () {
        $ctrl.conversations = $ctrl.conversations.filter(function (c) { return c.conversationId !== id; });
        if (currentChat.id === id) $ctrl.newChat();
      }).catch(function (err) {
        $log.warn('[AI] Failed to delete conversation:', err);
      });
    };

    /** Deletes each id; keeps the ones that failed in the list and resolves to their ids. */
    function bulkDelete(ids) {
      return $q.all(ids.map(function (id) {
        return chatHistoryApi.deleteConversation(id).then(function () { return null; }, function () { return id; });
      })).then(function (results) {
        var failed = results.filter(Boolean);
        $ctrl.conversations = $ctrl.conversations.filter(function (c) {
          return ids.indexOf(c.conversationId) === -1 || failed.indexOf(c.conversationId) !== -1;
        });
        if (currentChat.id && ids.indexOf(currentChat.id) !== -1 && failed.indexOf(currentChat.id) === -1) {
          $ctrl.newChat();
        }
        if (failed.length) $log.warn('[AI] Failed to delete ' + failed.length + ' conversation(s)');
        return failed;
      });
    }

    function keepSelected(ids) {
      var next = {};
      ids.forEach(function (id) { next[id] = true; });
      $ctrl.selectedConversations = next;
    }

    $ctrl.confirmDeleteAll = function () {
      $ctrl.deleteAllConfirm = false;
      var ids = $ctrl.conversations.map(function (c) { return c.conversationId; });
      if (!ids.length) return;
      $ctrl.deletingAll = true;
      bulkDelete(ids).then(function (failed) {
        keepSelected(Object.keys($ctrl.selectedConversations).filter(function (id) { return failed.indexOf(id) !== -1; }));
      }).finally(function () { $ctrl.deletingAll = false; });
    };

    $ctrl.confirmDeleteSelected = function () {
      $ctrl.deleteSelectedConfirm = false;
      var ids = Object.keys($ctrl.selectedConversations).filter(function (id) { return $ctrl.selectedConversations[id]; });
      if (!ids.length) return;
      $ctrl.deletingSelected = true;
      bulkDelete(ids).then(keepSelected).finally(function () { $ctrl.deletingSelected = false; });
    };
  }
})();
