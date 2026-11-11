(function () {
  'use strict';

  /**
   * Shared channel between the search page and the AI assistant.
   *
   * - The page registers a context provider (`setContextProvider`) and its grid
   *   actions (`setActions`: search, export, clearFilters, undo,
   *   sendCorrespondence, updateCorrespondence).
   * - The assistant calls `context()` at the moment it needs data, so a
   *   long-running turn (e.g. one awaiting a chained search before charting)
   *   always sees fresh state rather than a snapshot from when the turn started.
   * - `openAndAsk()` lets the grid's "Explain" button open the assistant and run a question.
   * - `dockWidth` is the space the page reserves for a docked assistant.
   */
  angular.module('adminPortal').factory('assistantBridge', assistantBridge);

  var EMPTY_CONTEXT = { searchContext: null, activeParams: null, activeDraft: null, statusValues: [], insights: [] };

  function assistantBridge() {
    var provider = null;
    var actions = {};
    var opener = null;
    var asker = null;
    var pendingAsk = null;

    return {
      dockWidth: 0,

      /** { searchContext, activeParams, activeDraft, statusValues, insights } */
      context: function () { return provider ? provider() : EMPTY_CONTEXT; },
      setContextProvider: function (fn) { provider = fn; },

      actions: function () { return actions; },
      setActions: function (handlers) { actions = handlers || {}; },

      registerOpener: function (fn) { opener = fn; },
      registerAsker: function (fn) {
        asker = fn;
        if (fn && pendingAsk !== null) {
          var text = pendingAsk;
          pendingAsk = null;
          fn(text);
        }
      },
      /** Opens the assistant and runs `text` as if the user had typed and sent it. */
      openAndAsk: function (text) {
        if (opener) opener();
        if (asker) asker(text);
        else pendingAsk = text; // panel not rendered yet — it picks this up when it registers
      }
    };
  }
})();
