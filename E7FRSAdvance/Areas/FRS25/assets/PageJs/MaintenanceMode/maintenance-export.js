// Maintenance Mode export (Excel / CSV / PDF) — same report format as the 616 web
// (MaintenanceMode/_List.cshtml there). Data comes from a DownloadMaintenanceMode action — by default
// MaintenanceModeController's; the Alerts page passes AlertsController's (only Alerts menu access needed).
//
//   e7MaintExport(format, criteria, period[, url])
//     format   : 'excel' | 'csv' | 'pdf'
//     criteria : SearchCriteria sent to the server (ZoneIds, DivisionIds, SiteIds, AssetTypeIds,
//                AssetIds, FromDate/ToDate as MM/DD/YYYY, FromTime/ToTime as HH:mm)
//     period   : { fromDate, fromTime, toDate, toTime } as shown in the report (DD/MM/YYYY)
//
// Needs ExcelJS, FileSaver (saveAs), jsPDF + autotable, jQuery and showSuccess/showError.
(function (window) {
    'use strict';

    // The layout loads jQuery after the page body, so it is looked up when an export
    // runs (see e7MaintExport), not when this file loads.
    var $;

    var HEADERS = [
        'Sr.No', 'Zone', 'Division', 'Station', 'Asset Type',
        'Asset Number', 'Date and Time of Maintenance mode to activate', 'Date and Time of Maintenance mode to clear', 'Status'
    ];

    function pad(n) { return String(n).padStart(2, '0'); }

    function parseMSDate(dateStr) {
        if (!dateStr) return '';
        var match = /\/Date\((-?\d+)\)\//.exec(dateStr);
        if (!match) return dateStr; // already a plain string, return as-is
        var date = new Date(parseInt(match[1], 10));
        if (date.getFullYear() <= 1) return ''; // DateTime.MinValue → never cleared
        return pad(date.getDate()) + '/' + pad(date.getMonth() + 1) + '/' + date.getFullYear() + ' ' +
            pad(date.getHours()) + ':' + pad(date.getMinutes()) + ':' + pad(date.getSeconds());
    }

    function fileName(extension) {
        var now = new Date();
        return 'MaintenanceMode_' + now.getFullYear() + pad(now.getMonth() + 1) + pad(now.getDate()) + '_' +
            pad(now.getHours()) + pad(now.getMinutes()) + '.' + extension;
    }

    function toRow(item, index) {
        return [
            index + 1,
            item.Zone || '',
            item.Division || '',
            item.Site || '',
            item.AssetType || '',
            item.Asset || '',
            parseMSDate(item.ActiveTime),
            parseMSDate(item.InActiveTime),
            item.IsMaintenceMode ? 'Active' : 'Cleared'
        ];
    }

    // ===== EXCEL =====
    function buildExcel(data, period) {
        var workbook = new ExcelJS.Workbook();
        workbook.creator = 'RDPMS';
        workbook.created = new Date();

        var worksheet = workbook.addWorksheet('Maintenance Mode');
        var columnCount = 9;

        // Title row
        var titleRow = worksheet.addRow(['Maintenance Mode Report']);
        titleRow.height = 30;
        worksheet.mergeCells(1, 1, 1, columnCount);
        titleRow.getCell(1).fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF4F46E5' } };
        titleRow.getCell(1).font = { bold: true, color: { argb: 'FFFFFFFF' }, size: 16, name: 'Calibri' };
        titleRow.getCell(1).alignment = { vertical: 'middle', horizontal: 'center' };

        // Period + summary rows
        var totalCount = data.length;
        var activeCount = data.filter(function (x) { return x.IsMaintenceMode; }).length;

        var periodRow = worksheet.addRow(['Period: ' + period.fromDate + ' ' + period.fromTime + '  →  ' + period.toDate + ' ' + period.toTime]);
        periodRow.height = 20;
        worksheet.mergeCells(2, 1, 2, columnCount);
        periodRow.getCell(1).font = { italic: true, size: 10, color: { argb: 'FF64748B' } };
        periodRow.getCell(1).alignment = { horizontal: 'center' };

        var summaryRow = worksheet.addRow(['Total: ' + totalCount + '   |   Active: ' + activeCount + '   |   Cleared: ' + (totalCount - activeCount) + '   |   Generated: ' + new Date().toLocaleString()]);
        summaryRow.height = 18;
        worksheet.mergeCells(3, 1, 3, columnCount);
        summaryRow.getCell(1).font = { italic: true, size: 10, color: { argb: 'FF64748B' } };
        summaryRow.getCell(1).alignment = { horizontal: 'center' };

        worksheet.addRow([]);

        // Header row
        var headerRow = worksheet.addRow(HEADERS);
        headerRow.height = 28;
        headerRow.eachCell(function (cell) {
            cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: 'FF1E3A5F' } };
            cell.font = { bold: true, color: { argb: 'FFFFFFFF' }, size: 11, name: 'Calibri' };
            cell.alignment = { vertical: 'middle', horizontal: 'center', wrapText: true };
            cell.border = {
                top: { style: 'medium', color: { argb: 'FF1E3A5F' } },
                left: { style: 'medium', color: { argb: 'FF1E3A5F' } },
                bottom: { style: 'medium', color: { argb: 'FF1E3A5F' } },
                right: { style: 'medium', color: { argb: 'FF1E3A5F' } }
            };
        });

        // Data rows
        $.each(data, function (index, item) {
            var excelRow = worksheet.addRow(toRow(item, index));
            excelRow.height = 22;
            excelRow.eachCell(function (cell, colNumber) {
                cell.fill = { type: 'pattern', pattern: 'solid', fgColor: { argb: (index + 1) % 2 === 0 ? 'FFF1F5F9' : 'FFFFFFFF' } };
                cell.font = { size: 10, name: 'Calibri' };
                cell.alignment = { vertical: 'middle', horizontal: colNumber === 1 ? 'center' : 'left' };
                cell.border = {
                    top: { style: 'thin', color: { argb: 'FFE2E8F0' } },
                    left: { style: 'thin', color: { argb: 'FFE2E8F0' } },
                    bottom: { style: 'thin', color: { argb: 'FFE2E8F0' } },
                    right: { style: 'thin', color: { argb: 'FFE2E8F0' } }
                };
                if (colNumber === 9) { // Status
                    cell.alignment = { vertical: 'middle', horizontal: 'center' };
                    cell.font = { size: 10, name: 'Calibri', bold: true, color: { argb: cell.value === 'Active' ? 'FFB45309' : 'FF047857' } };
                }
            });
        });

        // Column widths
        worksheet.columns.forEach(function (column, index) {
            var maxLength = HEADERS[index] ? HEADERS[index].length : 12;
            column.eachCell({ includeEmpty: true }, function (cell) {
                var cellLength = cell.value ? cell.value.toString().length : 10;
                if (cellLength > maxLength) maxLength = cellLength;
            });
            column.width = Math.min(maxLength + 2, 35);
        });
        worksheet.getColumn(1).width = 8;
        worksheet.getColumn(7).width = 25;
        worksheet.getColumn(8).width = 25;
        worksheet.getColumn(9).width = 12;

        $('#loader').show();
        workbook.xlsx.writeBuffer().then(function (buffer) {
            var blob = new Blob([buffer], { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' });
            saveAs(blob, fileName('xlsx'));
            $('#loader').hide();
            showSuccess('Excel downloaded successfully!', 'Success');
        });
    }

    // ===== CSV =====
    function buildCsv(data, period) {
        var csv = [];
        csv.push('"Maintenance Mode Report"');
        csv.push('"Period: ' + period.fromDate + ' ' + period.fromTime + '  to  ' + period.toDate + ' ' + period.toTime + '"');
        csv.push('"Total: ' + data.length + '  |  Active: ' + data.filter(function (x) { return x.IsMaintenceMode; }).length + '  |  Generated: ' + new Date().toLocaleString() + '"');
        csv.push('');
        csv.push(HEADERS.map(function (h) { return '"' + h + '"'; }).join(','));
        $.each(data, function (index, item) {
            csv.push(toRow(item, index).map(function (v) { return '"' + String(v).replace(/"/g, '""') + '"'; }).join(','));
        });

        var blob = new Blob(['﻿' + csv.join('\n')], { type: 'text/csv;charset=utf-8;' });
        saveAs(blob, fileName('csv'));
        showSuccess('CSV downloaded successfully!', 'Success');
    }

    // ===== PDF =====
    function buildPdf(data, period) {
        var jsPDF = window.jspdf.jsPDF;
        var doc = new jsPDF({ orientation: 'landscape', unit: 'pt', format: 'a4' });
        var pageW = doc.internal.pageSize.getWidth();

        var totalCount = data.length;
        var activeCount = data.filter(function (x) { return x.IsMaintenceMode; }).length;
        var now = new Date();
        var generatedNow = pad(now.getDate()) + '/' + pad(now.getMonth() + 1) + '/' + now.getFullYear() + '  ' +
            pad(now.getHours()) + ':' + pad(now.getMinutes()) + ':' + pad(now.getSeconds());

        var metaText = 'Period: ' + period.fromDate + ' ' + period.fromTime + '  to  ' + period.toDate + ' ' + period.toTime +
            '   |   Total: ' + totalCount + '   Active: ' + activeCount + '   Cleared: ' + (totalCount - activeCount) +
            '   |   Generated: ' + generatedNow;

        doc.autoTable({
            startY: 58,
            head: [[
                'Sr.No', 'Zone', 'Division', 'Station', 'Asset Type', 'Asset Number',
                'Date and Time of \n Maintenance mode to activate',
                'Date and Time of \n Maintenance mode to clear',
                'Status'
            ]],
            body: data.map(toRow),
            theme: 'grid',
            tableWidth: 'auto',
            styles: { fontSize: 8, cellPadding: 4, valign: 'middle', halign: 'center', lineColor: [226, 232, 240], lineWidth: 0.4, overflow: 'linebreak' },
            headStyles: {
                fillColor: [30, 58, 95], textColor: [255, 255, 255], fontStyle: 'bold', fontSize: 8,
                halign: 'center', valign: 'middle', minCellHeight: 28, cellPadding: { top: 5, bottom: 5, left: 3, right: 3 }
            },
            alternateRowStyles: { fillColor: [241, 245, 249] },
            bodyStyles: { textColor: [51, 51, 51] },
            columnStyles: {
                0: { cellWidth: 30 }, 1: { cellWidth: 70 }, 2: { cellWidth: 110 }, 3: { cellWidth: 110 }, 4: { cellWidth: 100 },
                5: { cellWidth: 100 }, 6: { cellWidth: 122 }, 7: { cellWidth: 122 }, 8: { cellWidth: 50 }
            },
            margin: { top: 58, left: 14, right: 14 },
            didParseCell: function (cellData) {
                if (cellData.section === 'body' && cellData.column.index === 8) {
                    if (cellData.cell.raw === 'Active') { cellData.cell.styles.textColor = [180, 83, 9]; cellData.cell.styles.fontStyle = 'bold'; }
                    else if (cellData.cell.raw === 'Cleared') { cellData.cell.styles.textColor = [4, 120, 87]; cellData.cell.styles.fontStyle = 'bold'; }
                }
            },
            didDrawPage: function () {
                // Title bar
                doc.setFillColor(79, 70, 229);
                doc.rect(0, 0, pageW, 36, 'F');
                doc.setFont('helvetica', 'bold');
                doc.setFontSize(15);
                doc.setTextColor(255, 255, 255);
                doc.text('Maintenance Mode Report', pageW / 2, 23, { align: 'center' });
                // Meta bar
                doc.setFillColor(240, 244, 255);
                doc.rect(0, 36, pageW, 18, 'F');
                doc.setFont('helvetica', 'italic');
                doc.setFontSize(8);
                doc.setTextColor(71, 85, 105);
                doc.text(metaText, pageW / 2, 47, { align: 'center' });
                // Page number
                doc.setFont('helvetica', 'normal');
                doc.setFontSize(7);
                doc.setTextColor(148, 163, 184);
                doc.text('Page ' + doc.internal.getCurrentPageInfo().pageNumber + ' of ' + doc.internal.getNumberOfPages(),
                    pageW - 14, doc.internal.pageSize.getHeight() - 8, { align: 'right' });
            }
        });

        doc.save(fileName('pdf'));
        showSuccess('PDF downloaded successfully!', 'Success');
    }

    window.e7MaintExport = function (format, criteria, period, url) {
        $ = window.jQuery;
        $('#loader').show();
        $.ajax({
            url: url || '/FRS25/MaintenanceMode/DownloadMaintenanceMode',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({ SearchCriteria: criteria }),
            success: function (data) {
                $('#loader').hide();
                if (!data || !data.length) {
                    showError('No data available for specified date to download', 'Download Error');
                    return;
                }
                try {
                    if (format === 'excel') buildExcel(data, period);
                    else if (format === 'csv') buildCsv(data, period);
                    else buildPdf(data, period);
                } catch (error) {
                    $('#loader').hide();
                    console.error('Maintenance export error:', error);
                    showError('Error generating ' + format.toUpperCase(), 'Download Error');
                }
            },
            error: function () {
                $('#loader').hide();
                showError('Something went wrong!', 'Error');
            }
        });
    };
})(window);
