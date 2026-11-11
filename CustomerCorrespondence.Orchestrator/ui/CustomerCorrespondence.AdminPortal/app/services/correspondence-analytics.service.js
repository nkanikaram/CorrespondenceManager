(function () {
  'use strict';

  /**
   * Client-side, deterministic analysis of correspondence records: row
   * normalization, full-dataset aggregates, proactive insights, repeat-search
   * comparisons and status-name "did you mean" hints. No AI calls here.
   */
  angular.module('adminPortal').factory('correspondenceAnalytics', correspondenceAnalytics);

  var DAY_MS = 86400000;

  function first() {
    for (var i = 0; i < arguments.length; i++) {
      if (arguments[i] !== undefined && arguments[i] !== null) return arguments[i];
    }
    return undefined;
  }

  /** The API returns camelCase or PascalCase depending on version — normalize to grid fields. */
  function toGridRow(it) {
    var row = {};
    Object.keys(it).forEach(function (k) { row[k] = it[k]; });
    row.id = first(it.correspondenceId, it.CorrespondenceId);
    row.docId = first(it.docId, it.DocId);
    row.claimNumber = first(it.claimNumber, it.ClaimNumber);
    row.customer = first(it.requestingDomainName, it.RequestingDomainName);
    row.delivery = first(it.deliveryMethodCode, it.DeliveryMethodCode);
    row.created = first(it.requestCreationTS, it.RequestCreationTS);
    row.status = first(it.latestStatus, it.LatestStatus);
    row.sla = first(it.sla, it.Sla, null);
    row.payload = first(it.RequestPayload, it.requestPayload, it.payloadText, it.PayloadText, it.payload, it.Payload, '');
    return row;
  }

  function itemsOf(resp) {
    return (resp && (resp.items || resp.Items)) || [];
  }

  function totalOf(resp, fallback) {
    var t = resp ? first(resp.total, resp.Total) : undefined;
    return t === undefined ? (fallback || 0) : t;
  }

  /** The data has inconsistent casing ("Mail" vs "MAIL") — group them as one category. */
  function normalizeDelivery(raw) {
    return raw.length > 0 ? raw.charAt(0).toUpperCase() + raw.slice(1).toLowerCase() : raw;
  }

  function normalizeForAggregates(it) {
    return {
      status: first(it.latestStatus, it.LatestStatus, 'Unknown'),
      customer: first(it.requestingDomainName, it.RequestingDomainName, 'Unknown'),
      delivery: normalizeDelivery(first(it.deliveryMethodCode, it.DeliveryMethodCode, 'Unknown')),
      created: first(it.requestCreationTS, it.RequestCreationTS, null)
    };
  }

  function tally(items, field) {
    return items.reduce(function (acc, item) {
      var k = String(item[field]);
      acc[k] = (acc[k] || 0) + 1;
      return acc;
    }, {});
  }

  function single(key, value) {
    var o = {};
    o[key] = value;
    return o;
  }

  /**
   * Full-dataset aggregates from every fetched record (already normalized).
   * For dimensions constrained by the filter the count is exactly `total` —
   * every record matches the filter value by definition.
   */
  function computeAggregates(allItems, total, filterParams, now) {
    now = now || Date.now();
    var statusCounts = filterParams.latestStatus ? single(filterParams.latestStatus, total) : tally(allItems, 'status');
    var customerCounts = filterParams.customerName ? single(filterParams.customerName, total) : tally(allItems, 'customer');
    var deliveryCounts = filterParams.deliveryMethodCode
      ? single(normalizeDelivery(filterParams.deliveryMethodCode), total)
      : tally(allItems, 'delivery');

    // Delivery × Status cross-tab (always from fetched records).
    var deliveryStatusCounts = {};
    allItems.forEach(function (it) {
      var row = deliveryStatusCounts[it.delivery] || (deliveryStatusCounts[it.delivery] = {});
      row[it.status] = (row[it.status] || 0) + 1;
    });

    var dateGroups = {};
    allItems.forEach(function (it) {
      if (!it.created) return;
      var d = new Date(it.created);
      if (isNaN(d.getTime())) return;
      var date = d.toISOString().split('T')[0];
      dateGroups[date] = (dateGroups[date] || 0) + 1;
    });
    var volumeByDay = Object.keys(dateGroups)
      .map(function (date) { return { date: date, count: dateGroups[date] }; })
      .sort(function (a, b) { return a.date.localeCompare(b.date); });

    // Processing age buckets — SLA risk proxy.
    var ageBuckets = { 'Today': 0, '1–3 Days': 0, '3–7 Days': 0, '>7 Days': 0 };
    allItems.forEach(function (it) {
      if (String(it.status).toLowerCase() !== 'processing' || !it.created) return;
      var ageDays = (now - new Date(it.created).getTime()) / DAY_MS;
      if (ageDays < 1) ageBuckets['Today']++;
      else if (ageDays < 3) ageBuckets['1–3 Days']++;
      else if (ageDays < 7) ageBuckets['3–7 Days']++;
      else ageBuckets['>7 Days']++;
    });
    var processingAgeBuckets = Object.keys(ageBuckets).map(function (bucket) {
      return { bucket: bucket, count: ageBuckets[bucket] };
    });

    return {
      total: total,
      statusCounts: statusCounts,
      customerCounts: customerCounts,
      deliveryCounts: deliveryCounts,
      deliveryStatusCounts: deliveryStatusCounts,
      volumeByDay: volumeByDay,
      processingAgeBuckets: processingAgeBuckets,
      recordsFetched: allItems.length
    };
  }

  /** Whole-percent share clamped to [0, 100] so a stale count can never render as "5070%". */
  function shareOf(part, whole) {
    if (whole <= 0) return 0;
    return Math.max(0, Math.min(100, Math.round((part / whole) * 100)));
  }

  function topEntry(counts) {
    return Object.keys(counts)
      .map(function (k) { return [k, counts[k]]; })
      .sort(function (a, b) { return b[1] - a[1]; })[0];
  }

  /** Risk signals surfaced proactively in the assistant without the user having to ask. */
  function buildInsights(agg) {
    var insights = [];
    var totalCount = agg.total;
    if (totalCount === 0) return insights;

    var oldBucket = agg.processingAgeBuckets.filter(function (b) { return b.bucket === '>7 Days'; })[0];
    var oldProcessing = oldBucket ? oldBucket.count : 0;
    if (oldProcessing > 0) {
      insights.push('⚠️ ' + oldProcessing + ' record' + (oldProcessing === 1 ? ' has' : 's have') +
        ' been Processing for over 7 days — possible SLA risk.');
    }

    var failed = agg.statusCounts['Failed'] || 0;
    var failedPct = shareOf(failed, totalCount);
    if (failed > 0 && failedPct >= 10) {
      insights.push('🔻 ' + failed + ' of ' + totalCount + ' records are Failed (' + failedPct + '%) — higher than typical.');
    }

    if (Object.keys(agg.customerCounts).length > 1) {
      var topCustomer = topEntry(agg.customerCounts);
      var customerShare = shareOf(topCustomer[1], totalCount);
      if (customerShare >= 50) {
        insights.push('📊 "' + topCustomer[0] + '" is ' + topCustomer[1] + ' of ' + totalCount + ' loaded records (' + customerShare + '%).');
      }
    }

    if (Object.keys(agg.deliveryCounts).length > 1) {
      var topMethod = topEntry(agg.deliveryCounts);
      var deliveryShare = shareOf(topMethod[1], totalCount);
      if (deliveryShare >= 80) {
        insights.push('📮 ' + topMethod[0] + ' is used for ' + topMethod[1] + ' of ' + totalCount +
          ' deliveries (' + deliveryShare + '%) — other methods are barely used.');
      }
    }

    return insights.slice(0, 4);
  }

  var SNAPSHOT_FIELDS = ['freeText', 'correspondenceId', 'docId', 'claimNumber', 'policyNumber', 'customerName',
    'customerId', 'latestStatus', 'latestStatuses', 'deliveryMethodCode', 'fromDate', 'toDate'];

  /** Stable key for a filter combination; '' for an unfiltered search (not a meaningful comparison). */
  function snapshotSignature(requestBody) {
    var entries = SNAPSHOT_FIELDS
      .map(function (k) { return [k, requestBody[k]]; })
      .filter(function (e) { return e[1] !== undefined && e[1] !== null && e[1] !== ''; });
    return entries.length ? JSON.stringify(entries) : '';
  }

  /** "Failed count is up 40% since you last ran this search." */
  function compareWithSnapshot(previous, total, failedCount) {
    var lines = [];
    if (!previous) return lines;
    var totalDelta = total - previous.total;
    var failedDelta = failedCount - previous.failedCount;
    if (previous.total > 0 && Math.abs(totalDelta) / previous.total >= 0.2) {
      lines.push('📈 Result count is ' + (totalDelta > 0 ? 'up' : 'down') + ' ' +
        Math.round((Math.abs(totalDelta) / previous.total) * 100) +
        '% vs. the last time you ran this search (was ' + previous.total + ', now ' + total + ').');
    }
    if (previous.failedCount > 0 && Math.abs(failedDelta) / previous.failedCount >= 0.2) {
      lines.push('📈 Failed count is ' + (failedDelta > 0 ? 'up' : 'down') + ' ' +
        Math.round((Math.abs(failedDelta) / previous.failedCount) * 100) +
        '% vs. last time (was ' + previous.failedCount + ', now ' + failedCount + ').');
    } else if (previous.failedCount === 0 && failedCount > 0) {
      lines.push('📈 This search had 0 Failed records last time — now has ' + failedCount + '.');
    }
    return lines;
  }

  /**
   * Background anomaly check: is the last-24h Failed rate well above the rolling
   * baseline from earlier scans? Returns { failedRate, alert } (alert may be null).
   */
  function evaluateBackgroundScan(items, history) {
    var failedCount = items.filter(function (it) {
      return String(first(it.latestStatus, it.LatestStatus, '')).toLowerCase() === 'failed';
    }).length;
    var failedRate = failedCount / Math.max(items.length, 1);
    var alert = null;
    if (history.length >= 2) {
      var baseline = history.reduce(function (sum, p) { return sum + p.failedRate; }, 0) / history.length;
      if (baseline > 0 && failedRate >= baseline * 1.5 && failedRate - baseline >= 0.1) {
        alert = '🚨 Failed rate across all recent records is ' + Math.round(failedRate * 100) + '% — well above the ' +
          Math.round(baseline * 100) + '% baseline from earlier scans today.';
      }
    }
    return { failedRate: failedRate, alert: alert };
  }

  /** Plain Levenshtein distance — statusValues is at most a few dozen entries. */
  function editDistance(a, b) {
    var m = a.length;
    var n = b.length;
    var dp = [];
    var i;
    var j;
    for (i = 0; i <= m; i++) {
      dp.push([i]);
      for (j = 1; j <= n; j++) dp[i].push(i === 0 ? j : 0);
    }
    for (i = 1; i <= m; i++) {
      for (j = 1; j <= n; j++) {
        dp[i][j] = a.charAt(i - 1) === b.charAt(j - 1)
          ? dp[i - 1][j - 1]
          : 1 + Math.min(dp[i - 1][j - 1], dp[i - 1][j], dp[i][j - 1]);
      }
    }
    return dp[m][n];
  }

  function closestStatusMatch(attempted, known) {
    var a = attempted.toLowerCase();
    var best = null;
    for (var i = 0; i < known.length; i++) {
      var k = known[i];
      if (k.toLowerCase() === a) return null; // exact match — not the problem
      var dist = editDistance(a, k.toLowerCase());
      if (dist <= Math.max(2, Math.ceil(k.length * 0.4)) && (!best || dist < best.dist)) {
        best = { value: k, dist: dist };
      }
    }
    return best ? best.value : null;
  }

  /**
   * After a 0-result search, checks whether it used a status close to but not
   * exactly a known value — a zero-token safety net for the AI's own mistakes.
   */
  function zeroResultStatusHint(params, searchContext, statusValues) {
    if (!searchContext || searchContext.total !== 0 || !statusValues || statusValues.length === 0) return null;
    var attempted = params.statuses && params.statuses.length > 0 ? params.statuses : (params.status ? [params.status] : []);
    for (var i = 0; i < attempted.length; i++) {
      var suggestion = closestStatusMatch(attempted[i], statusValues);
      if (suggestion) return '💡 No records matched status "' + attempted[i] + '" — did you mean "' + suggestion + '"?';
    }
    return null;
  }

  function correspondenceAnalytics() {
    return {
      toGridRow: toGridRow,
      itemsOf: itemsOf,
      totalOf: totalOf,
      normalizeDelivery: normalizeDelivery,
      normalizeForAggregates: normalizeForAggregates,
      computeAggregates: computeAggregates,
      shareOf: shareOf,
      buildInsights: buildInsights,
      snapshotSignature: snapshotSignature,
      compareWithSnapshot: compareWithSnapshot,
      evaluateBackgroundScan: evaluateBackgroundScan,
      editDistance: editDistance,
      closestStatusMatch: closestStatusMatch,
      zeroResultStatusHint: zeroResultStatusHint
    };
  }
})();
