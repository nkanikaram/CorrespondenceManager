(function () {
  'use strict';

  /**
   * Search refinement — two deterministic things only: catch obvious typos, and
   * suggest a real structured filter when the free-text search implies one.
   * Suggestion queries use the AI SearchParams field names (status, delivery,
   * sortBy: "requestCreationTS"), matching what the search page applies.
   */
  angular.module('adminPortal').factory('searchRefinement', searchRefinement);

  var COMMON_TYPOS = {
    cliam: ['claim', 'claimed'],
    clam: ['claim'],
    polic: ['policy'],
    ploicy: ['policy'],
    recipent: ['recipient'],
    recipiant: ['recipient'],
    adress: ['address'],
    adres: ['address'],
    procesing: ['processing'],
    proccessing: ['processing'],
    submited: ['submitted'],
    completd: ['completed'],
    faild: ['failed'],
    pended: ['pending'],
    delivry: ['delivery'],
    cusotmer: ['customer'],
    custmer: ['customer'],
    requet: ['request'],
    requuest: ['request']
  };

  function isoDate(d) {
    return d.toISOString().split('T')[0];
  }

  function extend(base, extra) {
    var out = {};
    Object.keys(base).forEach(function (k) { out[k] = base[k]; });
    Object.keys(extra).forEach(function (k) { out[k] = extra[k]; });
    return out;
  }

  function detectTypos(queryText) {
    if (!queryText) return null;
    var words = queryText.toLowerCase().split(/\s+/);
    for (var i = 0; i < words.length; i++) {
      if (Object.prototype.hasOwnProperty.call(COMMON_TYPOS, words[i])) {
        return { field: 'query', original: words[i], corrected: COMMON_TYPOS[words[i]][0] };
      }
    }
    return null;
  }

  function suggestFilters(query, searchText, now) {
    var suggestions = [];
    var text = (searchText || '').toLowerCase();
    now = now || new Date();

    // Every suggestion REPLACES the literal free-text search with a structured filter, so the
    // old freeText is cleared — ANDing "status = Completed" with "text contains 'completed
    // submission'" would silently return zero results even though the filter "worked".
    var base = extend(query || {}, { freeText: undefined });
    var recentFirst = { sortBy: 'requestCreationTS', sortDirection: 'desc' };

    if (text.indexOf('failed') !== -1 || text.indexOf('error') !== -1) {
      suggestions.push({
        id: 'filter-failed', type: 'filter', title: 'Show Failed Requests',
        description: 'Filter by Failed status with recent first',
        query: extend(extend(base, { status: 'Failed' }), recentFirst), icon: '⚠️', confidence: 0.95
      });
    }

    if (text.indexOf('processing') !== -1 || text.indexOf('pending') !== -1 || text.indexOf('progress') !== -1) {
      suggestions.push({
        id: 'filter-processing', type: 'filter', title: 'Show Processing Requests',
        description: 'View requests currently being processed',
        query: extend(extend(base, { status: 'Processing' }), recentFirst), icon: '⏳', confidence: 0.9
      });
    }

    // "processed" (past tense = done) means Completed here, NOT Processing — those are
    // opposite states in this domain, so it deliberately doesn't trigger the suggestion above.
    if (text.indexOf('completed') !== -1 || text.indexOf('delivered') !== -1 || text.indexOf('processed') !== -1) {
      suggestions.push({
        id: 'filter-completed', type: 'filter', title: 'Show Completed Requests',
        description: 'View successfully delivered correspondence',
        query: extend(extend(base, { status: 'Completed' }), recentFirst), icon: '✅', confidence: 0.9
      });
    }

    if (text.indexOf('today') !== -1) {
      var today = isoDate(now);
      suggestions.push({
        id: 'filter-today', type: 'filter', title: 'Requests from Today (' + today + ')',
        query: extend(base, { fromDate: today, toDate: today }), icon: '📅', confidence: 0.95
      });
    }

    if (text.indexOf('last 7') !== -1 || text.indexOf('past week') !== -1) {
      var from = new Date(now.getTime() - 7 * 24 * 60 * 60 * 1000);
      suggestions.push({
        id: 'filter-week', type: 'filter', title: 'Requests from Last 7 Days',
        query: extend(base, { fromDate: isoDate(from), toDate: isoDate(now) }), icon: '📊', confidence: 0.9
      });
    }

    if (text.indexOf('email') !== -1 && text.indexOf('delivery') === -1) {
      suggestions.push({
        id: 'filter-email', type: 'filter', title: 'Email Delivery Only',
        description: 'Show requests sent via email',
        query: extend(base, { delivery: 'Email' }), icon: '📧', confidence: 0.85
      });
    }

    if (text.indexOf('mail') !== -1 && text.indexOf('email') === -1) {
      suggestions.push({
        id: 'filter-mail', type: 'filter', title: 'Physical Mail Delivery',
        description: 'Show requests sent via postal mail',
        query: extend(base, { delivery: 'Mail' }), icon: '📮', confidence: 0.85
      });
    }

    return suggestions;
  }

  function analyzeSearchQuery(query, searchText, now) {
    var suggestions = suggestFilters(query, searchText, now)
      .sort(function (a, b) { return b.confidence - a.confidence; });
    return {
      originalQuery: query,
      suggestions: suggestions,
      typoCorrection: detectTypos(searchText) || undefined
    };
  }

  function searchRefinement() {
    return {
      detectTypos: detectTypos,
      suggestFilters: suggestFilters,
      analyzeSearchQuery: analyzeSearchQuery
    };
  }
})();
