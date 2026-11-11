(function () {
  'use strict';

  var app = angular.module('adminPortal');

  // Fixed categorical palette, never cycled per render — a given rank (entries are sorted by
  // count, descending) gets the same color whether it's drawn as a bar or a slice.
  app.constant('CHART_COLORS', ['#4aa3f0', '#4ec994', '#e8c15a', '#c586c0', '#f4886b', '#6a9fd8', '#78c9c0', '#d4a373']);

  /** SVG geometry shared by the pie/donut charts here and in the analytics panel. */
  app.factory('chartGeometry', function () {
    function arcPoint(c, radius, deg) {
      var rad = (deg * Math.PI) / 180;
      return [c + radius * Math.cos(rad), c + radius * Math.sin(rad)];
    }

    /** Path for one wedge of a circle centred at (c, c); innerR > 0 makes it a donut segment. */
    function wedgePath(c, r, innerR, startAngle, endAngle) {
      // A full 360° arc has identical start/end points and renders nothing — stop just short.
      if (endAngle - startAngle >= 359.99) endAngle = startAngle + 359.99;
      var largeArc = endAngle - startAngle > 180 ? 1 : 0;
      var p1 = arcPoint(c, r, startAngle);
      var p2 = arcPoint(c, r, endAngle);
      if (!innerR) {
        return 'M ' + c + ' ' + c + ' L ' + p1[0] + ' ' + p1[1] + ' A ' + r + ' ' + r + ' 0 ' + largeArc + ' 1 ' + p2[0] + ' ' + p2[1] + ' Z';
      }
      var i2 = arcPoint(c, innerR, endAngle);
      var i1 = arcPoint(c, innerR, startAngle);
      return 'M ' + p1[0] + ' ' + p1[1] + ' A ' + r + ' ' + r + ' 0 ' + largeArc + ' 1 ' + p2[0] + ' ' + p2[1] +
        ' L ' + i2[0] + ' ' + i2[1] + ' A ' + innerR + ' ' + innerR + ' 0 ' + largeArc + ' 0 ' + i1[0] + ' ' + i1[1] + ' Z';
    }

    /** [{ label, count, color }] → same entries plus `d`, walking clockwise from 12 o'clock. */
    function wedges(entries, size, innerRatio) {
      var r = size / 2;
      var total = entries.reduce(function (s, e) { return s + e.count; }, 0) || 1;
      var angle = -90;
      return entries.map(function (e) {
        var start = angle;
        angle += (e.count / total) * 360;
        return angular.extend({}, e, {
          start: start,
          end: angle,
          d: wedgePath(r, r, r * (innerRatio || 0), start, angle)
        });
      });
    }

    return { arcPoint: arcPoint, wedgePath: wedgePath, wedges: wedges };
  });

  /**
   * <mini-chart chart="{ title, chartType, counts | crossTab }">
   * chartType: 'bar' | 'pie' | 'donut' (uses counts) or
   *            'stacked' | 'pie-multi' | 'donut-multi' (uses crossTab: outer → inner → count).
   * Rendered inline in the assistant's Conversation tab.
   */
  app.component('miniChart', {
    bindings: { chart: '<' },
    templateUrl: 'app/components/charts/mini-chart.html',
    controller: ['CHART_COLORS', 'chartGeometry', function (CHART_COLORS, chartGeometry) {
      var $ctrl = this;
      var MAX_ROWS = 8;
      var MAX_GROUPS = 6;

      function sortedEntries(counts) {
        return Object.keys(counts || {})
          .map(function (k) { return { label: k, count: counts[k] }; })
          .sort(function (a, b) { return b.count - a.count; });
      }

      function color(i) { return CHART_COLORS[i % CHART_COLORS.length]; }

      // One fixed segment order across all rows/groups (by combined volume) so a given status
      // keeps the same color everywhere instead of being colored per row.
      function crossTabModel(crossTab, limit) {
        var groups = Object.keys(crossTab || {})
          .map(function (label) {
            var inner = crossTab[label];
            var total = Object.keys(inner).reduce(function (s, k) { return s + inner[k]; }, 0);
            return { label: label, inner: inner, total: total };
          })
          .sort(function (a, b) { return b.total - a.total; })
          .slice(0, limit);
        var segmentTotals = {};
        groups.forEach(function (g) {
          Object.keys(g.inner).forEach(function (k) { segmentTotals[k] = (segmentTotals[k] || 0) + g.inner[k]; });
        });
        var segments = Object.keys(segmentTotals)
          .sort(function (a, b) { return segmentTotals[b] - segmentTotals[a]; })
          .map(function (name, i) { return { name: name, color: color(i) }; });
        return { groups: groups, segments: segments };
      }

      $ctrl.$onChanges = function () {
        var c = $ctrl.chart || {};
        $ctrl.kind = c.chartType;
        $ctrl.empty = false;

        if (c.chartType === 'bar') {
          var bars = sortedEntries(c.counts).slice(0, MAX_ROWS);
          var max = Math.max.apply(null, bars.map(function (e) { return e.count; }).concat([1]));
          $ctrl.bars = bars.map(function (e, i) {
            return angular.extend(e, { color: color(i), width: Math.max(2, (e.count / max) * 100) });
          });
          $ctrl.empty = bars.length === 0;
        } else if (c.chartType === 'pie' || c.chartType === 'donut') {
          var sorted = sortedEntries(c.counts);
          var slices = sorted.slice(0, MAX_ROWS);
          var overflow = sorted.slice(MAX_ROWS).reduce(function (s, e) { return s + e.count; }, 0);
          if (overflow > 0) slices.push({ label: 'Other', count: overflow });
          slices.forEach(function (e, i) { e.color = color(i); });
          $ctrl.size = 120;
          $ctrl.wedges = chartGeometry.wedges(slices, $ctrl.size, c.chartType === 'donut' ? 0.6 : 0);
          $ctrl.empty = slices.length === 0;
        } else {
          var model = crossTabModel(c.crossTab, c.chartType === 'stacked' ? MAX_ROWS : MAX_GROUPS);
          var colorOf = {};
          model.segments.forEach(function (s) { colorOf[s.name] = s.color; });
          $ctrl.segments = model.segments;
          $ctrl.empty = model.groups.length === 0;

          if (c.chartType === 'stacked') {
            var rowMax = Math.max.apply(null, model.groups.map(function (g) { return g.total; }).concat([1]));
            $ctrl.rows = model.groups.map(function (g) {
              return {
                label: g.label,
                total: g.total,
                parts: model.segments.filter(function (s) { return g.inner[s.name]; }).map(function (s) {
                  return { name: s.name, count: g.inner[s.name], color: s.color, width: Math.max(2, (g.inner[s.name] / rowMax) * 100) };
                })
              };
            });
          } else {
            $ctrl.size = 84;
            var innerRatio = c.chartType === 'donut-multi' ? 0.58 : 0;
            $ctrl.groups = model.groups.map(function (g) {
              var entries = model.segments
                .filter(function (s) { return g.inner[s.name]; })
                .map(function (s) { return { label: s.name, count: g.inner[s.name], color: colorOf[s.name] }; });
              return { label: g.label, total: g.total, wedges: chartGeometry.wedges(entries, $ctrl.size, innerRatio) };
            });
          }
        }
      };
    }]
  });

  /**
   * <markdown-answer text="…">
   * A short headline up front with the full detail (including markdown tables)
   * behind a "Show details" toggle; raw markdown punctuation is stripped.
   */
  app.component('markdownAnswer', {
    bindings: { text: '<' },
    template:
      '<span ng-if="!$ctrl.model.needsDrillDown" class="pre-wrap">{{ $ctrl.model.headline }}</span>' +
      '<div ng-if="$ctrl.model.needsDrillDown && !$ctrl.expanded">' +
      '  <span class="pre-wrap">{{ $ctrl.model.headline }}</span> ' +
      '  <button type="button" class="link-btn" ng-click="$ctrl.expanded = true">Show details</button>' +
      '</div>' +
      '<div ng-if="$ctrl.model.needsDrillDown && $ctrl.expanded">' +
      '  <div ng-repeat="seg in $ctrl.model.segments track by $index">' +
      '    <div ng-if="seg.kind === \'text\' && seg.content" class="pre-wrap md-text">{{ seg.content }}</div>' +
      '    <div ng-if="seg.kind === \'table\'" class="md-table">' +
      '      <table><thead><tr><th ng-repeat="h in seg.headers track by $index">{{ h }}</th></tr></thead>' +
      '      <tbody><tr ng-repeat="row in seg.rows track by $index"><td ng-repeat="cell in row track by $index">{{ cell }}</td></tr></tbody></table>' +
      '    </div>' +
      '  </div>' +
      '  <button type="button" class="link-btn link-btn--muted" ng-click="$ctrl.expanded = false">Show less</button>' +
      '</div>',
    controller: ['markdown', function (markdown) {
      var $ctrl = this;
      $ctrl.$onChanges = function () {
        $ctrl.model = markdown.analyze($ctrl.text || '');
      };
    }]
  });
})();
