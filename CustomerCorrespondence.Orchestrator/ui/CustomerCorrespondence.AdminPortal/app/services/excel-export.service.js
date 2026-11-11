(function () {
  'use strict';

  /**
   * Excel export as SpreadsheetML (.xls) — no third-party library required.
   * (The npm `xlsx`/SheetJS package has unpatched high-severity CVEs, so it's avoided.)
   */
  angular.module('adminPortal').factory('excelExport', excelExport);

  function findPayload(r) {
    var keys = Object.keys(r);
    for (var i = 0; i < keys.length; i++) {
      if (/^payload/i.test(keys[i]) || /payloadtext$/i.test(keys[i])) {
        var v = r[keys[i]];
        if (v !== null && v !== undefined && v !== '') return String(v);
      }
    }
    return '';
  }

  function first(a, b) {
    return a !== undefined && a !== null ? a : b;
  }

  // Keyed the same way the AI's export_results.columns argument names them, so "export just
  // claim number and status" can filter/reorder without a translation layer.
  var COLUMNS = [
    { key: 'id', header: 'Id', get: function (r) { return first(r.correspondenceId, r.CorrespondenceId); } },
    { key: 'docId', header: 'DocId', get: function (r) { return first(r.docId, r.DocId); } },
    { key: 'claimNumber', header: 'ClaimNumber', get: function (r) { return first(r.claimNumber, r.ClaimNumber); } },
    { key: 'customer', header: 'Customer', get: function (r) { return first(r.requestingDomainName, r.RequestingDomainName); } },
    { key: 'delivery', header: 'Delivery', get: function (r) { return first(r.deliveryMethodCode, r.DeliveryMethodCode); } },
    { key: 'created', header: 'Created', get: function (r) { return first(r.requestCreationTS, r.RequestCreationTS); } },
    { key: 'status', header: 'Status', get: function (r) { return first(r.latestStatus, r.LatestStatus); } },
    { key: 'ageHours', header: 'AgeHours', get: function (r) { var s = first(r.sla, r.Sla); return s ? Math.round(s.elapsedSeconds / 360) / 10 : null; } },
    { key: 'sla', header: 'SLA', get: function (r) { var s = first(r.sla, r.Sla); return s ? s.state : null; } },
    { key: 'slaDue', header: 'SLADue', get: function (r) { var s = first(r.sla, r.Sla); return s ? s.dueUtc : null; } },
    { key: 'payload', header: 'Payload', get: function (r) { return first(r.payloadText, first(r.PayloadText, findPayload(r))); } }
  ];

  // Cell background per Status, mirroring the grid's row highlights.
  var STATUS_FILL = {
    completed: '#D6F5E8',
    processing: '#FFF3CD',
    submitted: '#DCF1FE',
    failed: '#FBDADA'
  };

  function toRow(record, columns) {
    var cols = columns && columns.length ? COLUMNS.filter(function (c) { return columns.indexOf(c.key) !== -1; }) : COLUMNS;
    var row = {};
    cols.forEach(function (c) { row[c.header] = c.get(record); });
    return row;
  }

  function xmlEscape(v) {
    return String(v === undefined || v === null ? '' : v)
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;')
      .replace(/"/g, '&quot;');
  }

  function buildWorkbook(rows) {
    var headers = Object.keys(rows[0]);
    var statusHeader = headers.filter(function (h) { return h.toLowerCase() === 'status'; })[0];

    var headerRow = '<Row ss:Height="20">' + headers.map(function (h) {
      return '<Cell ss:StyleID="Header"><Data ss:Type="String">' + xmlEscape(h) + '</Data></Cell>';
    }).join('') + '</Row>';

    var dataRows = rows.map(function (r, i) {
      var statusVal = statusHeader ? String(first(r[statusHeader], '')).toLowerCase() : '';
      var styleId = STATUS_FILL[statusVal] ? 'Status_' + statusVal : (i % 2 === 1 ? 'RowAlt' : 'Row');
      return '<Row>' + headers.map(function (h) {
        return '<Cell ss:StyleID="' + styleId + '"><Data ss:Type="String">' + xmlEscape(r[h]) + '</Data></Cell>';
      }).join('') + '</Row>';
    }).join('');

    var colWidths = headers.map(function (h) {
      return '<Column ss:Width="' + Math.min(Math.max(h.length * 8, 70), 220) + '"/>';
    }).join('');

    var statusStyles = Object.keys(STATUS_FILL).map(function (key) {
      return `<Style ss:ID="Status_${key}">
   <Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Weight="1" ss:Color="#D0D0D0"/></Borders>
   <Interior ss:Color="${STATUS_FILL[key]}" ss:Pattern="Solid"/>
  </Style>`;
    }).join('\n  ');

    return `<?xml version="1.0"?>
<?mso-application progid="Excel.Sheet"?>
<Workbook xmlns="urn:schemas-microsoft-com:office:spreadsheet"
 xmlns:o="urn:schemas-microsoft-com:office:office"
 xmlns:x="urn:schemas-microsoft-com:office:excel"
 xmlns:ss="urn:schemas-microsoft-com:office:spreadsheet">
 <Styles>
  <Style ss:ID="Header">
   <Font ss:Bold="1" ss:Color="#FFFFFF" ss:Size="10"/>
   <Interior ss:Color="#0078D4" ss:Pattern="Solid"/>
   <Alignment ss:Vertical="Center"/>
   <Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Weight="1" ss:Color="#005A9E"/></Borders>
  </Style>
  <Style ss:ID="Row">
   <Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Weight="1" ss:Color="#D0D0D0"/></Borders>
  </Style>
  <Style ss:ID="RowAlt">
   <Interior ss:Color="#F5F5F5" ss:Pattern="Solid"/>
   <Borders><Border ss:Position="Bottom" ss:LineStyle="Continuous" ss:Weight="1" ss:Color="#D0D0D0"/></Borders>
  </Style>
  ${statusStyles}
 </Styles>
 <Worksheet ss:Name="Export">
  <Table>
   ${colWidths}
   ${headerRow}
   ${dataRows}
  </Table>
  <WorksheetOptions xmlns="urn:schemas-microsoft-com:office:excel">
   <FreezePanes/><FrozenNoSplit/><SplitHorizontal>1</SplitHorizontal><TopRowBottomPane>1</TopRowBottomPane>
   <ActivePane>2</ActivePane>
  </WorksheetOptions>
  <AutoFilter x:Range="R1C1:R1C${headers.length}" xmlns="urn:schemas-microsoft-com:office:excel"/>
 </Worksheet>
</Workbook>`;
  }

  /** Builds and downloads an .xls from raw API records. No-op for an empty list. */
  function download(records, filename, columns) {
    if (!records || !records.length) return;
    var xml = buildWorkbook(records.map(function (r) { return toRow(r, columns); }));
    var blob = new Blob([xml], { type: 'application/vnd.ms-excel;charset=utf-8;' });
    var url = URL.createObjectURL(blob);
    var a = document.createElement('a');
    a.href = url;
    a.download = filename;
    document.body.appendChild(a);
    a.click();
    document.body.removeChild(a);
    setTimeout(function () { URL.revokeObjectURL(url); }, 0);
  }

  function excelExport() {
    return {
      COLUMNS: COLUMNS,
      toRow: toRow,
      buildWorkbook: buildWorkbook,
      download: download
    };
  }
})();
