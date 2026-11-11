(function () {
  'use strict';

  /**
   * Splits an AI answer into plain-text and table segments, strips raw markdown
   * punctuation (**bold**, ## headings) and picks a short headline for the
   * collapsed view. Also produces a speakable version for text-to-speech.
   */
  angular.module('adminPortal').factory('markdown', markdown);

  var TABLE_ROW = /^\s*\|.*\|\s*$/;
  var SEPARATOR_ROW = /^\s*\|?\s*:?-{2,}:?\s*(\|\s*:?-{2,}:?\s*)*\|?\s*$/;
  var SUMMARY_LIMIT = 160;

  function splitCells(line) {
    return line.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map(function (c) { return c.trim(); });
  }

  function stripInline(line) {
    return line
      .replace(/^#{1,6}\s*/, '')
      .replace(/\*\*(.*?)\*\*/g, '$1')
      .replace(/__(.*?)__/g, '$1')
      .replace(/(?<!\*)\*(?!\*)(.*?)(?<!\*)\*(?!\*)/g, '$1');
  }

  function stripMarkdown(line) {
    return stripInline(line).replace(/^[-*]\s+/, '• ').trim();
  }

  /** Walks lines, handing each markdown table (header + separator + rows) to onTable. */
  function walk(text, onLine, onTable) {
    var lines = text.split('\n');
    for (var i = 0; i < lines.length; i++) {
      var line = lines[i];
      var next = lines[i + 1];
      if (TABLE_ROW.test(line) && next !== undefined && SEPARATOR_ROW.test(next)) {
        var j = i + 2;
        var rows = [];
        while (j < lines.length && TABLE_ROW.test(lines[j])) {
          rows.push(splitCells(lines[j]));
          j++;
        }
        onTable(splitCells(line), rows);
        i = j - 1;
      } else {
        onLine(line);
      }
    }
  }

  function parseSegments(text) {
    var segments = [];
    var textBuf = [];
    function flushText() {
      if (textBuf.length) {
        segments.push({ kind: 'text', content: textBuf.map(stripMarkdown).join('\n').trim() });
        textBuf = [];
      }
    }
    walk(text || '', function (line) { textBuf.push(line); }, function (headers, rows) {
      flushText();
      segments.push({
        kind: 'table',
        headers: headers.map(stripMarkdown),
        rows: rows.map(function (r) { return r.map(stripMarkdown); })
      });
    });
    flushText();
    return segments;
  }

  function truncate(s) {
    return s.length > SUMMARY_LIMIT ? s.slice(0, SUMMARY_LIMIT).trim() + '…' : s;
  }

  /**
   * Headings like "## Summary" carry no information, so the first real
   * sentence/bullet wins — falling back to heading text only if nothing else exists.
   */
  function computeHeadline(text, segments) {
    var rawLines = (text || '').split('\n').map(function (l) { return l.trim(); }).filter(Boolean);
    var headingLines = [];
    var i;
    for (i = 0; i < rawLines.length; i++) {
      var line = rawLines[i];
      if (TABLE_ROW.test(line) || SEPARATOR_ROW.test(line)) continue;
      if (/^#{1,6}\s/.test(line)) {
        headingLines.push(line);
        continue;
      }
      var cleaned = stripMarkdown(line);
      if (cleaned) return truncate(cleaned);
    }
    for (i = 0; i < headingLines.length; i++) {
      var heading = stripMarkdown(headingLines[i]);
      if (heading) return truncate(heading);
    }
    var firstTable = segments.filter(function (s) { return s.kind === 'table'; })[0];
    if (firstTable) return 'Table with ' + firstTable.rows.length + ' row' + (firstTable.rows.length === 1 ? '' : 's');
    return '';
  }

  /** Everything the answer view needs: segments, headline and whether to offer "Show details". */
  function analyze(text) {
    var segments = parseSegments(text);
    var hasTable = segments.some(function (s) { return s.kind === 'table'; });
    var plainLength = segments.reduce(function (n, s) { return n + (s.kind === 'text' ? s.content.length : 0); }, 0);
    var isMultiline = segments.some(function (s) { return s.kind === 'text' && s.content.indexOf('\n') !== -1; });
    return {
      segments: segments,
      headline: computeHeadline(text, segments),
      needsDrillDown: hasTable || isMultiline || plainLength > SUMMARY_LIMIT
    };
  }

  // ---- Text-to-speech ----

  /** Drops punctuation nobody would say aloud and swaps symbols for words ("%" → "percent"). */
  function speakableLine(line) {
    return stripInline(line)
      .replace(/^[-*]\s+/, '')
      .replace(/`/g, '')
      .replace(/%/g, ' percent')
      .replace(/&/g, ' and ')
      .trim();
  }

  /** A sentence per row ("Payment Guard Advantage: 15,159"), or a summary if it's too long to recite. */
  function tableToSpeech(headers, rows) {
    if (rows.length === 0) return '';
    if (rows.length > 6) return 'a table with ' + rows.length + ' rows — the top one is ' + rows[0].join(', ');
    var keyCol = headers.length > 1 ? 0 : -1;
    return rows.map(function (row) {
      if (keyCol < 0) return row.join(', ');
      return row[keyCol] + ': ' + row.filter(function (_, i) { return i !== keyCol; }).join(', ');
    }).join('. ');
  }

  function toSpeechText(text) {
    var out = [];
    var bullets = [];
    function flushBullets() {
      if (!bullets.length) return;
      out.push(bullets.length === 1 ? bullets[0] : bullets.slice(0, -1).join(', ') + ', and ' + bullets[bullets.length - 1]);
      bullets = [];
    }
    walk(text || '', function (line) {
      var trimmed = line.trim();
      var cleaned = speakableLine(trimmed);
      if (/^[-*]\s+/.test(trimmed)) {
        if (cleaned) bullets.push(cleaned);
      } else {
        flushBullets();
        if (cleaned) out.push(cleaned);
      }
    }, function (headers, rows) {
      flushBullets();
      var spoken = tableToSpeech(headers.map(speakableLine), rows.map(function (r) { return r.map(speakableLine); }));
      if (spoken) out.push(spoken);
    });
    flushBullets();
    // Natural pauses rather than robotic hard stops on every line break.
    return out.join('. ').replace(/\.\.+/g, '.');
  }

  function markdown() {
    return {
      parseSegments: parseSegments,
      stripMarkdown: stripMarkdown,
      computeHeadline: computeHeadline,
      analyze: analyze,
      toSpeechText: toSpeechText
    };
  }
})();
