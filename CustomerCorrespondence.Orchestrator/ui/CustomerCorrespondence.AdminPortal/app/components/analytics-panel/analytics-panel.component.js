(function () {
  'use strict';

  /**
   * <analytics-panel aggregates="…" loading="…" filter-label="…">
   * Dashboard over the full-dataset aggregates: KPI cards, status donut, volume
   * over time (day/week/month), delivery pie, per-channel status mix and the
   * Processing backlog by age.
   */
  angular.module('adminPortal').component('analyticsPanel', {
    templateUrl: 'app/components/analytics-panel/analytics-panel.html',
    bindings: { aggregates: '<', loading: '<', filterLabel: '<' },
    controller: AnalyticsPanelController
  });

  var STATUS_COLORS = { Completed: '#22d3a0', Processing: '#f59e0b', Submitted: '#60a5fa', Failed: '#f87171', Unknown: '#6b7280' };
  var PALETTE = ['#60a5fa', '#a78bfa', '#34d399', '#fb923c', '#f472b6', '#facc15', '#38bdf8'];
  var AGE_COLORS = ['#22d3a0', '#f59e0b', '#fb923c', '#f87171'];
  var STATUS_ORDER = ['Completed', 'Processing', 'Submitted', 'Failed'];

  // Volume chart drawing area (viewBox units).
  var VOL = { width: 640, height: 180, left: 36, right: 8, top: 8, bottom: 22 };

  AnalyticsPanelController.$inject = ['chartGeometry'];

  function AnalyticsPanelController(chartGeometry) {
    var $ctrl = this;

    // Set here rather than in $onInit — the first $onChanges runs before $onInit.
    $ctrl.granularity = 'day';
    $ctrl.granularities = [{ id: 'day', label: 'Day' }, { id: 'week', label: 'Week' }, { id: 'month', label: 'Month' }];
    $ctrl.ageLegend = ['Low', 'Medium', 'High', 'Critical'].map(function (label, i) { return { label: label, color: AGE_COLORS[i] }; });

    $ctrl.$onChanges = function () {
      if ($ctrl.aggregates) build($ctrl.aggregates);
    };

    $ctrl.setGranularity = function (g) {
      $ctrl.granularity = g;
      buildVolume($ctrl.aggregates.volumeByDay);
    };

    function pct(n, total) {
      return total > 0 ? ((n / total) * 100).toFixed(1) : '0.0';
    }

    function statusOrder(a, b) {
      var ia = STATUS_ORDER.indexOf(a);
      var ib = STATUS_ORDER.indexOf(b);
      return (ia === -1 ? 99 : ia) - (ib === -1 ? 99 : ib);
    }

    function build(agg) {
      var total = agg.total;
      var sc = agg.statusCounts;

      $ctrl.kpis = [
        { label: 'Completed', color: '#22d3a0', value: sc.Completed || 0, sub: 'records' },
        { label: 'Processing', color: '#f59e0b', value: sc.Processing || 0, sub: 'in queue' },
        { label: 'Submitted', color: '#60a5fa', value: sc.Submitted || 0, sub: 'records' },
        { label: 'Failed', color: '#f87171', value: sc.Failed || 0, sub: 'records' }
      ].map(function (k) { k.pct = pct(k.value, total); return k; });

      // Status donut.
      var statusEntries = Object.keys(sc).map(function (name, i) {
        return { label: name, count: sc[name], color: STATUS_COLORS[name] || PALETTE[i % PALETTE.length] };
      });
      $ctrl.statusWedges = chartGeometry.wedges(statusEntries, 150, 0.66);

      // Delivery pie with outer % labels for slices ≥ 5%.
      var dc = agg.deliveryCounts;
      var deliveryEntries = Object.keys(dc).map(function (name, i) {
        return { label: name, count: dc[name], color: PALETTE[i % PALETTE.length] };
      });
      var deliveryTotal = deliveryEntries.reduce(function (s, e) { return s + e.count; }, 0) || 1;
      var pieSize = 150;
      $ctrl.deliveryWedges = chartGeometry.wedges(deliveryEntries, pieSize, 0).map(function (w) {
        var share = w.count / deliveryTotal;
        var mid = (w.start + w.end) / 2;
        var p = chartGeometry.arcPoint(pieSize / 2, pieSize / 2 + 14, mid);
        w.share = share;
        w.labelX = p[0];
        w.labelY = p[1];
        w.anchor = p[0] >= pieSize / 2 ? 'start' : 'end';
        w.showLabel = share >= 0.05;
        w.labelText = Math.round(share * 100) + '%';
        return w;
      });

      // Delivery channel performance — 100% stacked bar per delivery method.
      var dsc = agg.deliveryStatusCounts;
      var statuses = {};
      Object.keys(dsc).forEach(function (m) { Object.keys(dsc[m]).forEach(function (s) { statuses[s] = true; }); });
      $ctrl.perfStatuses = Object.keys(statuses).sort(statusOrder).map(function (s, i) {
        return { name: s, color: STATUS_COLORS[s] || PALETTE[i % PALETTE.length] };
      });
      $ctrl.perfRows = Object.keys(dsc).map(function (method) {
        var counts = dsc[method];
        var rowTotal = Object.keys(counts).reduce(function (s, k) { return s + counts[k]; }, 0);
        return {
          method: method.length > 16 ? method.slice(0, 16) + '…' : method,
          fullName: method,
          parts: $ctrl.perfStatuses
            .filter(function (s) { return counts[s.name]; })
            .map(function (s) {
              var p = rowTotal > 0 ? Math.round((counts[s.name] / rowTotal) * 100) : 0;
              return { name: s.name, color: s.color, pct: p, count: counts[s.name] };
            })
        };
      });

      // Processing backlog by age.
      var totalProcessing = agg.processingAgeBuckets.reduce(function (s, b) { return s + b.count; }, 0);
      $ctrl.totalProcessing = totalProcessing;
      $ctrl.ageRows = agg.processingAgeBuckets.map(function (b, i) {
        return {
          bucket: b.bucket,
          count: b.count,
          pct: totalProcessing > 0 ? (b.count / totalProcessing) * 100 : 0,
          color: AGE_COLORS[i],
          glow: i >= 2
        };
      });

      buildVolume(agg.volumeByDay);
    }

    function groupVolume(data, g) {
      if (g === 'day') return data.slice(-90);
      var grouped = {};
      data.forEach(function (d) {
        var date = new Date(d.date);
        var key;
        if (g === 'week') {
          var monday = new Date(date);
          monday.setDate(date.getDate() - ((date.getDay() + 6) % 7));
          key = monday.toISOString().split('T')[0];
        } else {
          key = date.getFullYear() + '-' + String(date.getMonth() + 1).padStart(2, '0');
        }
        grouped[key] = (grouped[key] || 0) + d.count;
      });
      return Object.keys(grouped).sort().map(function (k) { return { date: k, count: grouped[k] }; });
    }

    function buildVolume(volumeByDay) {
      var data = groupVolume(volumeByDay || [], $ctrl.granularity);
      var plotW = VOL.width - VOL.left - VOL.right;
      var plotH = VOL.height - VOL.top - VOL.bottom;
      var max = Math.max.apply(null, data.map(function (d) { return d.count; }).concat([1]));
      var step = data.length > 1 ? plotW / (data.length - 1) : 0;

      var points = data.map(function (d, i) {
        return {
          date: d.date,
          count: d.count,
          x: VOL.left + (data.length > 1 ? i * step : plotW / 2),
          y: VOL.top + plotH - (d.count / max) * plotH
        };
      });

      var baseline = VOL.top + plotH;
      var line = points.map(function (p, i) { return (i ? 'L ' : 'M ') + p.x + ' ' + p.y; }).join(' ');
      $ctrl.volume = {
        empty: points.length === 0,
        viewBox: '0 0 ' + VOL.width + ' ' + VOL.height,
        line: line,
        area: points.length ? line + ' L ' + points[points.length - 1].x + ' ' + baseline + ' L ' + points[0].x + ' ' + baseline + ' Z' : '',
        points: points,
        hitWidth: Math.max(step, 4),
        plotTop: VOL.top,
        plotHeight: plotH,
        yTicks: [0, 0.5, 1].map(function (f) {
          return { y: VOL.top + plotH - f * plotH, label: Math.round(max * f), x1: VOL.left, x2: VOL.width - VOL.right };
        }),
        xLabels: xLabels(points),
        labelY: VOL.height - 6,
        labelX: VOL.left - 6
      };
    }

    function xLabels(points) {
      if (!points.length) return [];
      var idx = points.length === 1 ? [0] : [0, Math.floor((points.length - 1) / 2), points.length - 1];
      return idx.filter(function (v, i, a) { return a.indexOf(v) === i; }).map(function (i, n, arr) {
        return { x: points[i].x, text: points[i].date, anchor: n === 0 ? 'start' : (n === arr.length - 1 ? 'end' : 'middle') };
      });
    }
  }
})();
