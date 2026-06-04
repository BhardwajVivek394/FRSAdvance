$(document).ready(function () {
    //$('#Pager').select2({ minimumResultsForSearch: Infinity });
    $('#alert-to-date').datepicker({
        dateFormat: 'mm/dd/yy',
        autoClose: true,
    }).datepicker("setDate", new Date());


    $('#alert-from-date').datepicker({
        dateFormat: 'mm/dd/yy',
        autoClose: true,
    }).datepicker("setDate", new Date());


    GetAllSite();
    $('#tblAnalytics').on('scroll', function () {
        $("#" + this.id + " > *").width($(this).width() + $(this).scrollLeft());
    });

});

function FullScreen($this) {

    if ($("#full-screen").hasClass("card-fullscreen")) {
        $("#full-screen").removeClass('card-fullscreen');
        $($this).text("Full Screen");

    }
    else {
        $("#full-screen").addClass('card-fullscreen');
        $($this).text("Reset");
    }
}

function ReportingRefreshed() {
    SetSortDirectionOnLoad();
}

function SetSortDirectionOnLoad() {
    var columnName = $("#hdnColumnName").val();
    var sortDirection = $("#hdnSortDirection").val();
    var sortDirectionClass;
    if (sortDirection === "ASC") {
        sortDirectionClass = 'sorting_desc';
    }
    else if (sortDirection === "DESC") {
        sortDirectionClass = 'sorting_asc';
    }
    var thSortingClass = document.querySelectorAll('.sorting');
    for (var i = 0; i < thSortingClass.length; i++) {
        var sortElement = thSortingClass[i].id;
        if (sortElement === "th" + columnName) {
            $("#th" + columnName).removeClass('sorting');
            $("#th" + columnName).addClass(sortDirectionClass);
        }
    }

}

function GetAllSite() {
    $("#selSites").empty();
    $.ajax({
        url: '/Reporting/GetAllSite',
        type: 'Post',
        contentType: 'application/json',
        success: function (data) {
            if (data != null && data.length > 0) {
                var html = "";
                var html1 = "";
                $.each(data, function (id, val) {
                    html += "<option value=" + val.Id + ">" + val.Name + "</option>";
                });
                $("#selSites").append(html);

                html1 += "<option value='0'>Select Site</option>";
                $.each(data, function (id, val) {
                    html1 += "<option data-path=" + val.MQTTBasePath + " data-IsHttpPost=" + val.IsHttpPost + " data-issetuplocalsite=" + val.IsSetupLocalSite + " value=" + val.Id + ">" + val.Name + "</option>";
                });
                $("#selRoosterSites").empty().append(html1);
                $("#selHistorySite").empty().append(html1);
                $("#selGraphSite").empty().append(html1);
                $("#selMaintenaceSite").empty().append(html1);
                $("#selGludeSite").empty().append(html1);
            }
            //GetSiteMaintenance(data);
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function GetSiteMaintenance(data) {
    var html = "";
    $.each(data, function (id, val) {
        html += "<tr>";
        html += "<td class='text-center width60'>";
        html += '<label class="ckbox ckbox-primary">';
        html += '<input style="cursor:pointer;margin:5px" class="main-site" type="checkbox" data-id=' + val.Id + ' id="chkMainSite' + val.Id + '" onclick="GetAllMainteneceAsset()">';
        html += '</label>';
        html += '</td>';
        html += '<td>' + val.Name + '</td>';
        if (val.IsActive) {
            html += '<td class=""><span class="span badge rounded-pill pill-badge-success"  >Active</span ></td >';
        }
        else {
            html += '<td class=""> <span class="span badge rounded-pill pill-badge-yellow">In Active</span></td >';
        }
        html += "</tr>";
    });
    $("#maintenanceTbl").empty().append(html);
}

function GetAllMainteneceAsset() {

    var idList = [];

    //$(".main-site").each(function () {
    //    if ($(this).is(":checked")) {
    //        var id = $(this).attr('data-id');
    //        idList.push(id);
    //    }
    //});
    idList.push($("#selMaintenaceSite").val())
    $("#loader").show();
    if (idList.length > 0) {
        $.ajax({
            url: '/Reporting/GetAllMaintenanceAssetTypeBySiteId',
            type: 'Post',
            data: '{siteIds:' + JSON.stringify(idList) + '}',
            dataType: 'html',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                //$("#divAssetDetails").show();
                //$("#divAssetDetails").empty().append(data);

                $("#divMainTypeDetails").show();
                $("#divMainTypeDetails").empty().append(data);
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    } else {
        $("#loader").hide();
        $("#divMainTypeDetails").empty().hide();
    }
}

function MaintenanceSearchSite(assetTypeId) {

    var idList = [];

    //$(".main-site").each(function () {
    //    if ($(this).is(":checked")) {
    //        var id = $(this).attr('data-id');
    //        idList.push(id);
    //    }
    //});
    idList.push($("#selMaintenaceSite").val())
    $("#loader").show();
    if (idList.length > 0) {
        $.ajax({
            url: '/Reporting/GetAllMaintenanceAssetBySiteId',
            type: 'Post',
            data: '{siteIds:' + JSON.stringify(idList) + ',assetTypeId:' + assetTypeId + '}',
            dataType: 'html',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                $("#divMaintenanceAssetDetails").show();
                $("#divMaintenanceAssetDetails").empty().append(data);
                //$("#modal-add-division").modal('show');
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    } else {
        $("#loader").hide();
        $("#divMaintenanceAssetDetails").empty().hide();
    }
}

function SaveMaintencesInput() {
    var maintenanceList = [];

    $("#tblMaintenceInput tr").each(function (id, ele) {

        var maintenance = {};
        maintenance.SiteId = $(this).attr("data-siteId");
        maintenance.AssetId = $(this).attr("data-id");
        maintenance.AttributeId = $(this).attr("data-attributeid");
        maintenance.IndexScore = $("#txtIndexScore" + maintenance.AssetId).val();
        maintenance.MaintenenceInput = $("#txtInput" + maintenance.AssetId).val();
        maintenanceList.push(maintenance);
    });
    if (maintenanceList.length > 0) {
        $("#loader").show();
        $.ajax({
            url: '/Reporting/SaveMaintencesInput',
            type: 'Post',
            data: '{maintenances:' + JSON.stringify(maintenanceList) + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data.IsSuccess && data.Value > 0) {
                    CommonNotification("SUCCESS", "Maintenance input save successfully", "Success");
                }
                else {
                    CommonNotification("ERROR", "Something went wrong!", "Error");
                }
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }
}

function fnSearchAssetTypeCount() {

    var mAlertAnalyticsSearch = {};
    mAlertAnalyticsSearch.SiteId = $("#selSites").val();
    mAlertAnalyticsSearch.FromDate = $("#alert-from-date").val();
    mAlertAnalyticsSearch.ToDate = $("#alert-to-date").val();

    $("#loader").show();
    $.ajax({
        url: '/Reporting/GetAssetTypeSmsLogCount',
        type: 'Post',
        contentType: 'application/json',
        dataType: 'html',
        data: '{mAlertAnalyticsSearch:' + JSON.stringify(mAlertAnalyticsSearch) + '}',
        success: function (data) {
            $("#loader").hide();
            $("#assetCount").empty().append(data)
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnGetAlertCountByAsset(assetTypeId, name) {
    var mAlertAnalyticsSearch = {};
    mAlertAnalyticsSearch.SiteId = $("#selSites").val();
    mAlertAnalyticsSearch.FromDate = $("#alert-from-date").val();
    mAlertAnalyticsSearch.ToDate = $("#alert-to-date").val();
    mAlertAnalyticsSearch.AssetTypeId = assetTypeId;
    $("#hdnAssetType").val(name)
    $("#loader").show();
    $.ajax({
        url: '/Reporting/GetAlertCountByAsset',
        type: 'Post',
        contentType: 'application/json',
        dataType: 'html',
        data: '{mAlertAnalyticsSearch:' + JSON.stringify(mAlertAnalyticsSearch) + '}',
        success: function (data) {
            $("#loader").hide();
            $("#analyticsreport").empty().append(data)
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function DownloadCsvFile() {
    var titles = [];
    var data = [];

    $('#tblAnalytics > thead > tr > th').each(function () {
        titles.push($.trim($(this).text()));
    });

    //$('#tbodyExportOrder > tr > td').each(function () {
    //       data.push($.trim($(this).text()));
    //});

    $('#tblAnalytics > tbody >  tr').each(function (id, ele) {
        $(this).find('td').each(function (id, ele) {

            if ($("#hdnAssetType").val().toLocaleLowerCase() == "TRACK".toLocaleLowerCase()) {
                if (id == 0) {
                    var val = $.trim($(ele).text()) + "-t"
                    data.push(val);
                }
                else {
                    data.push($.trim($(ele).text()));
                }
            }
            else {
                data.push($.trim($(ele).text()));
            }
        });
    });

    var csvString = prepCSVRow(titles, titles.length, '');
    csvString = prepCSVRow(data, titles.length, csvString);

    var downloadLink = document.createElement("a");
    var blob = new Blob(["\ufeff", csvString]);
    var url = URL.createObjectURL(blob);
    downloadLink.href = url;
    downloadLink.download = "Analytics" + GenerateUniqueCode() + ".csv";

    document.body.appendChild(downloadLink);
    downloadLink.click();
    document.body.removeChild(downloadLink);
}

function prepCSVRow(arr, columnCount, initial) {
    var row = '';
    var delimeter = ',';
    var newLine = '\r\n';

    function splitArray(_arr, _count) {
        var splitted = [];
        var result = [];
        _arr.forEach(function (item, idx) {
            if ((idx + 1) % _count === 0) {
                splitted.push(item);
                result.push(splitted);
                splitted = [];
            } else {
                splitted.push(item);
            }
        });
        return result;
    }
    var plainArr = splitArray(arr, columnCount);
    plainArr.forEach(function (arrItem) {
        arrItem.forEach(function (item, idx) {
            row += item + ((idx + 1) === arrItem.length ? '' : delimeter);
        });
        row += newLine;
    });
    return initial + row;
}

function GenerateUniqueCode() {
    var currentdate = new Date();
    return ((currentdate.getTime() * 10000) + 621355968000000000);
}

function DownloadChildCsvFile() {
    var titles = [];
    var data = [];

    $('#tblsmslogs > thead > tr > th').each(function () {
        titles.push($.trim($(this).text()));
    });

    //$('#tbodyExportOrder > tr > td').each(function () {
    //       data.push($.trim($(this).text()));
    //});

    $('#tblsmslogs > tbody >  tr').each(function (id, ele) {
        $(this).find('td').each(function (id, ele) {
            var val = $.trim($(ele).text()).replace(",", " ").replace("\n", " ").replace("\r", " ");
            data.push(val);
        });
    });

    var csvString = prepCSVRow(titles, titles.length, '');
    csvString = prepCSVRow(data, titles.length, csvString);

    var downloadLink = document.createElement("a");
    var blob = new Blob(["\ufeff", csvString]);
    var url = URL.createObjectURL(blob);
    downloadLink.href = url;
    downloadLink.download = "Analytics" + GenerateUniqueCode() + ".csv";

    document.body.appendChild(downloadLink);
    downloadLink.click();
    document.body.removeChild(downloadLink);
}

function fnGetAlertWiseCount(alertId, assetId) {
    if (alertId > 0) {
        var mAlertAnalyticsSearch = {};
        mAlertAnalyticsSearch.SiteId = $("#selSites").val();
        mAlertAnalyticsSearch.FromDate = $("#alert-from-date").val();
        mAlertAnalyticsSearch.ToDate = $("#alert-to-date").val();
        mAlertAnalyticsSearch.AssetId = assetId;
        mAlertAnalyticsSearch.AlertId = alertId;

        $("#loader").show();
        $.ajax({
            url: '/Reporting/GetSmsLogById',
            type: 'Post',
            contentType: 'application/json',
            dataType: 'html',
            data: '{mAlertAnalyticsSearch:' + JSON.stringify(mAlertAnalyticsSearch) + '}',
            success: function (data) {
                $("#loader").hide();
                $("#model-div").empty().append(data);
                $("#modalSmslogs").modal("show");
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }

}

function DeleteSMSLofById(id) {
    $("#loader").show();
    $.ajax({
        url: '/SMSLog/DeleteSMSLogById',
        type: 'Post',
        data: '{id:' + id + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();

            if (data == true) {
                $("#tr" + id).remove();
                CommonNotification("SUCCESS", "Alert Log has been deleted.", "success");

            }
            else {
                CommonNotification("ERROR", "Something went wrong!", "error");
            }
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "error");
        }
    });
}

function fnDayMaintenance(day) {
    if ($("#selRoosterSites").val() != "0") {
        $("#tblRooster").empty();
        $.ajax({
            url: '/Reporting/GetDayMaintenace',
            type: 'Post',
            contentType: 'application/json',
            data: '{day:' + day + '}',
            dataType: 'html',
            success: function (data) {
                $("#divDaysReport").empty().append(data)
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }

}

function fnScheduleMaintenenace() {
    var dateList = [];

    $(".schedule").each(function () {
        if (!$(this).is(":checked")) {
            var date = $(this).val();
            dateList.push(date);
        }
    });

    $("#loader").show();
    if (dateList.length > 0) {
        $.ajax({
            url: '/Reporting/ScheduleMaintenenace',
            type: 'Post',
            data: '{dateList:' + JSON.stringify(dateList) + ',siteId:' + $("#selRoosterSites").val() + '}',
            dataType: 'html',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                $("#divDaysReportMain").empty().append(data);
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    } else {
        $("#loader").hide();
        $("#divMainTypeDetails").empty().hide();
    }
}

function fnCreateRoster() {
    var roasterList = [];
    $("#tbodyHistoryList tr").each(function () {

        var roaster = {};
        roaster.Id = $(this).attr("data-id");
        roaster.RosterId = $(this).attr("data-rosterId");
        roaster.Remark = $(this).find("td:eq(4)").children("input").val();
        if ($(this).find("td:eq(3)").children("input").is(":checked")) {
            roaster.Status = "Closed";
        }
        else {
            roaster.Status = "Open";
        }
        roasterList.push(roaster);
    });
    if (roasterList.length > 0) {
        $("#loader").show();
        $.ajax({
            url: '/Reporting/UpdateRosterHistory',
            type: 'Post',
            data: '{mRoasterList:' + JSON.stringify(roasterList) + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data == true) {
                    CommonNotification("SUCCESS", "Roster save successfully", "Success");
                }
                else {
                    CommonNotification("ERROR", "Something went wrong!", "Error");
                }
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }
}

function fnGetAllHistory() {
    $("#divDaysRosterDetails").empty();
    $("#divDaysReportHistory").empty();
    $("#loader").show();
    $.ajax({
        // url: '/Reporting/GetAllRosterHistory',
        url: '/Reporting/GetAllRoster',
        type: 'Post',
        data: '{siteId:' + $("#selHistorySite").val() + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divDaysReportHistory").empty().append(data);
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnGetHistory(rosterId) {
    $("#loader").show();
    $("#rosterId").val(rosterId);
    $.ajax({
        url: '/Reporting/GetAllRosterHistory',
        type: 'Post',
        data: '{siteId:' + $("#selHistorySite").val() + ',rosterId:' + rosterId + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divDaysRosterDetails").empty().append(data);
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });

    //$("#divDaysRosterDetails").append("<h4>History: " + $($this).children('span').text() + "</h4>")
    //if (res != null && res.length > 0) {

    //    var html = '<div class="table-responsive" style="max-height:700px;">';
    //    html += '<table class="table table-bordered">';
    //    html += '<thead>';
    //    html += '<tr>';
    //    html += '<th style="width:200px;">Day</th>';
    //    html += '<th style="width:180px; text-align:left">Track</th>';
    //    html += '<th>Condition</th>';
    //    html += '<th style="width:100px;">Status </th>';
    //    html += '<th style="width:200px;">Remark</th>';
    //    html += '<th colspan="2" class="text-center" style="width:60px;">Action</th>';
    //    html += '</tr>';
    //    html += '</thead>';
    //    html += ' <tbody id="tblhistorydata">';
    //    $.each(res, function (id, val) {
    //        html += '<tr>';
    //        html += '<td>' + ConvertToDate(val.AssignDate) + '</td>';
    //        html += '<td>' + val.AssetTypeName + ' > ' + val.Name + ' </td>';
    //        html += '<td>' + $.trim( val.MaintenanceInput) + '</td>';
    //        html += '<td>' + $.trim(val.Status )+ '</td>';
    //        html += '<td>' + $.trim(val.Remark) + '</td>';
    //        html += '<td class="text-center"> <button class="btn btn-sm btn-success" onclick="fnEditRoster(' + val.Id + ',\'' + val.Status + '\',\'' + val.Remark + '\')"><i class="fa fa-pencil"></i></button> </td>';
    //        html += '<td class="text-center"> <button class="btn btn-sm btn-danger" onclick="fnDeleteRoster(' + val.Id + ')"><i class="fa fa-trash"></i></button> </td>';
    //        html += '</tr>';
    //    });
    //    html += "</tbody>";
    //    html += "</table>";
    //    html += "</div>";

    //    $("#divDaysRosterDetails").append(html);
    //    $("#loader").hide();
    //}
}

function ConvertToDate(dateValue) {
    var convertedDate = new Date(parseInt(dateValue.substr(6)));
    var month = convertedDate.getMonth() + 1;
    var cdate = convertedDate.getDate();
    var year = convertedDate.getFullYear();
    month = month >= 10 ? month : "0" + month;
    cdate = cdate >= 10 ? cdate : "0" + cdate;
    var temp = cdate + '/' + month + '/' + year;
    return temp;

}

function fnEditRoster(id, status, rewmark, rosterId) {
    $("#hdnHistoryId").val(id);
    $("#hdnRosterId").val(rosterId);
    $("#txtRemark").val(rewmark);
    if (status.toLocaleLowerCase() == "Open".toLocaleLowerCase()) {
        $("#chkroster").prop("checked", false)
    }
    else {
        $("#chkroster").prop("checked", true)
    }
    $("#modal-roster").modal("show");
}

function fnUpdateRoster() {

    var roster = {}
    roster.Id = $("#hdnHistoryId").val();
    roster.Remark = $("#txtRemark").val();
    roster.RosterId = $("#hdnRosterId").val();
    if ($("#chkroster").is(":checked")) {
        roster.Status = "Closed";
    }
    else {
        roster.Status = "Open";
    }
    $("#loader").show();
    $.ajax({
        url: '/Reporting/UpdateRosterHistory',
        type: 'Post',
        data: '{roster:' + JSON.stringify(roster) + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            if (data == true) {
                CommonNotification("SUCCESS", "Record has been updated!", "Success");
                $("#modal-roster").modal("hide");
                fnGetHistory($("#rosterId").val());
            }
            else {
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnDeleteRoster(id) {
    $("#loader").show();
    $.ajax({
        url: '/Reporting/DeleteRosterHistory',
        type: 'Post',
        data: '{id:' + id + ',siteId:' + $("#selHistorySite").val() + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            if (data == true) {
                CommonNotification("SUCCESS", "Record has been deleted!", "Success");
                // fnGetHistory($("#rosterId").val());
                fnGetAllHistory();
            }
            else {
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnDownlaodPdf(rosterId) {
    var siteId = $("#selHistorySite").val()
    window.location.href = "/Reporting/GeneratePdf?siteId=" + siteId + "&rosterId=" + rosterId;
}

function fnGetAllTrack() {
    $("#loader").show();
    $.ajax({
        url: '/Site/GetAllAssest',
        type: 'Post',
        data: '{siteId:' + $('#selGludeSite').val() + ',assetTypeId:1}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            var html = "";
            html += "<option value=0>Select Asset</option>";
            if (data != null && data != undefined) {
                $.each(data, function (id, val) {
                    var trackFamilyIds = '';
                    $.each(val.mFamilyTracks, function (i, j) {
                        trackFamilyIds += j.TrackFamilyId + ',';
                    });

                    html += "<option data-familyTrack=" + trackFamilyIds + " value=" + val.Id + ">" + val.Name + "</option>";
                });
            }
            //if (trackFamilyIds != null && trackFamilyIds != undefined && trackFamilyIds != '') {
            //    $('#hdnFamilyTrackIds').val(trackFamilyIds);
            //}
            
            $('#selAllTrack').empty().append(html);
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

var chart;
function fnChangeGludeLeakgeGraph(sd, ed, isSearch) {
    var familyTrackIds = [];
    var familyTrack = $("#selAllTrack").find(':selected').attr('data-familyTrack');
    if (familyTrack != null && familyTrack != undefined && familyTrack != '') {
        familyTrack = familyTrack.substring(0, familyTrack.length - 1);
        familyTrackIds = familyTrack.split(",");
    }
    var mAsset = {};
    mAsset.Id = $("#selAllTrack").val();
    mAsset.SiteId = $("#selGludeSite").val();
    mAsset.StartDate = sd;
    mAsset.EndDate = ed;

    mAsset.StartTime = $("#tpFrom").val();
    mAsset.EndTime = $("#tpTo").val();

    if (chart != null && chart != "" && chart != undefined) {
        chart.dispose();
    }
    mAsset.IsReadHttpPost = false;
    if ($("#selDatabase").val() == "HTTP") {
        mAsset.IsReadHttpPost = true;
    }

    if (isSearch == null || isSearch == undefined || isSearch == '') {
        mAsset.IValue = $("#sel option:selected").text();
    }

    familyTrackIds.push(mAsset.Id);

    $("#loader").show();
    $.ajax({
        url: '/Site/GetLeakgeReportData',
        type: 'Post',
        data: '{asset:' + JSON.stringify(mAsset) + ',familyTrack:' + JSON.stringify(familyTrackIds) + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            if (data != null) {
                DemoLeakgechart(data)
            }
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });

}

function DemoLeakgechart(data) {

    if (chart != null && chart != "" && chart != undefined) {
        chart.dispose();
    }
    var colorB = ["rgba(75, 172, 198, 0.1)", "rgba(33, 89, 104, 0.1)", "rgba(64, 64, 64, 0.1)", "rgba(115, 102, 255, 0.1)", "rgba(247, 49, 100, 0.1)", "rgba(144, 212, 116, 0.1)", "rgba(252, 237, 162, 0.1)", "rgba(0, 227, 150, 0.1)", "rgba(0, 206, 186, 0.1)", "rgba(243, 121, 149, 0.1)", "rgba(150, 188, 715,0.1)", "rgba(150, 188, 715,0.1)", "rgba(150, 188, 715,0.1)", "rgba(150, 188, 715,0.1)"];

    var color = ["rgba(75, 172, 198, 0.1)", "rgba(33, 89, 104, 0.1)", "rgba(64, 64, 64, 0.1)", "rgba(115, 102, 255, 0.1)", "rgba(247, 49, 100, 0.1)", "rgba(144, 212, 116, 0.1)", "rgba(252, 237, 162, 0.1)", "rgba(0, 227, 150, 0.1)", "rgba(0, 206, 186, 0.1)", "rgba(243, 121, 149, 0.1)", "rgba(150, 188, 715,0.1)", "rgba(150, 188, 715,0.1)", "rgba(150, 188, 715,0.1)", "rgba(150, 188, 715,0.1)"];

    var DataSeriaes = [];
    var maxV = 0;
    var legd = [];
    var macLeft = 0;
    var yAxisArray = [];
    var lary = [];
    var rary = [];

    if (data.assetAttributes != null && data.assetAttributes.length > 0) {
        $.each(data.assetAttributes, function (ids, val) {
            $("#lblType").text(val.AssetTypeName + " :");
            $("#lblName").text(val.AssetName)

            var xA = [];
            $.each(val.Data, function (i, j) {
                xA.push(j);
            });

            legd.push(val.Title);
            var attr = {};
            attr.name = val.Title;
            attr.type = 'line';
            attr.notShowSymbol = true;
            attr.sampling = sampling;
            attr.hoverAnimation = false;
            attr.clip = true;
            // attr.yAxisIndex = ids;
            if (val.Title.indexOf('V') > -1) {
                var yaxis = {
                    value: val.Title,
                };
                rary.push(yaxis)
                attr.yAxisIndex = 1;
            }
            else {
                var yaxis = {
                    value: val.Title,
                };
                lary.push(yaxis)
                attr.yAxisIndex = 0;
            }

            attr.data = [];
            var values = [];
            $.each(val.Data, function (i, j) {
                values.push(j);
            });

            if (val.Title != "TPR V" || val.Title != "DG V" || val.Title != "RG V" || val.Title != "HG V" || val.Title != "HHG V" || val.Title != "Tx1 V" || val.Title != "Tx2 V" || val.Title != "Choke V" || val.Title != "Vr" || val.Title != "Voltage") {
                var yLeft = Math.max.apply(Math, values); // 3
                if (yLeft >= macLeft) {
                    macLeft = yLeft;
                }
            }


            attr.connectNulls = true;
            attr.data = values;
            attr.datasetIndex = ids;
            DataSeriaes.push(attr);

        });
    }

    var xValue = [];
    $.each(data.DateList, function (ids, val) {
        xValue.push(val);
    });

    chart = echarts.init(document.getElementById('area-echart'));

    var sampling = 'none';

    chart.setOption({
        tooltip: {
            trigger: 'axis',
            axisPointer: {
                type: 'cross',
                label: {
                    backgroundColor: '#6a7985'
                }
            }
        },
        legend: {
            data: legd,
            x: 'left'

        },
        toolbox: {
            show: true,
            feature: {
                mark: { show: true },
                dataView: { show: true, readOnly: false },
                magicType: { show: true, type: ['line', 'bar'] },
                restore: { show: true },
                saveAsImage: { show: true }
            }
        },
        grid: {
            left: '3%',
            right: '4%',
            bottom: '3%',
            containLabel: true
        },
        xAxis: [
            {
                type: 'category',
                boundaryGap: false,
                axisLine: { onZero: false },
                interval: 50,
                //minInterval:1,
                //  maxInterval: 3600 * 1000 * 24,
                data: xValue,
                // min: "00:00",
                //  max:"11.59"
            }
        ],
        //yAxis: yAxisArray,
        yAxis: [
            {
                type: 'value',
                min: 0,
                max: macLeft,
                data: lary
            }
        ],
        color: ["#4bacc6", "#215968", "#717171", "#7366ff", "#cc6600", "#90d474", "#4d4d00", "#00e396", "#00ceba", "#f37995"],

        series: DataSeriaes
    });

    chart.on('legendselectchanged', function (params) {
        legendSelection(chart, params);
    });

    //window.onresize = chart.resize;
}

