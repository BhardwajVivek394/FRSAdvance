
$(document).ready(function () {
    $("[data-toggle='popover']").popover();
   // $('#drpPageSize').select2({ minimumResultsForSearch: Infinity });
  //  $("#hdnIsSmsLogActive").val(1);
    $('#drpPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetSMSLogList").submit();
    });

    GetAllSite($('#SearchCriteria_DivisionId').val());
    $('#SearchCriteria_DivisionId').change(function () {
        GetAllSite($(this).val());
    });
  
    $("#txtSearchSiteName").keyup(function (e) {
        e.preventDefault();
        if (e.keyCode === 13) {
            if ($.trim($("#txtSearchSiteName").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetSMSLogList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetSMSLogList").submit();
            }
        }
    });
    $(".txtSearch").keyup(function (e) {
        e.preventDefault();
        setTimeout(function () {
            $("#hdnSkip").val('0');
            $("#frmGetSMSLogList").submit();
        }, 3000)
    });

    if ($('#hdnCatListType').val() == 'Success') {
        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnCatListMessage").val(), $("#hdnSaveType").val());
    }
    else if ($('#hdnCatListType').val() == 'Error') {
        if ($('#hdnCatListMessage').val() == 'Forbidden!') {
            window.location.href = "/Login/Index";
        } else {
            CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnCatListMessage").val(), $("#hdnSaveType").val());
        }
    }
    //$("#rdActive").click(function () {
    //    $("#hdnIsSmsLogActive").val(1);
    //    $("#hdnSkip").val('0');
    //    $("#frmGetSMSLogList").submit();
    //});
    //$("#rdInActive").click(function () {
    //    $("#hdnIsSmsLogActive").val(0);
    //    $("#hdnSkip").val('0');
    //    $("#frmGetSMSLogList").submit();
    //});
});
//function fnIsActive($this) {
//    alert()
//    $("#hdnIsSmsLogActive").val($($this).val())
//}

function fnSearchSMSLog()
{
    $("#hdnSiteId").val($("#selSearchSiteName").val())

    $("#hdnAssetTypeId").val($("#selGear").val());

    var filterType = $('input[name="filter"]:checked').val();
    if (filterType == "0") {
        $("#hdnIsSmsLogActive").val(0);
    } else if (filterType == "1") {
        $("#hdnIsSmsLogActive").val(1);
    }

    $("#hdnSkip").val('0');
    $("#frmGetSMSLogList").submit();
}

function PageSize() {
    $("#hdnSkip").val('0');
    $("#frmGetSMSLogList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetSMSLogList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $(".gear").prop("checked", false);
    $("#hdnAssetTypeId").val(0);
    $("#txtSearchSiteName").val("");
    $("#frmGetSMSLogList").submit();
}

function SortList(columnName) {
    $("#hdnColumnName").val(columnName);
    if ($("#th" + columnName).hasClass("sorting")) {
        $("#hdnSortDirection").val("ASC");
        var upClass = document.querySelectorAll('.sorting_asc');
        for (var i = 0; i < upClass.length; i++) {
            var upElement = upClass[i].id;
            $("#" + upElement).removeClass('sorting_asc');
            $("#" + upElement).addClass('sorting');
        }
        var downClass = document.querySelectorAll('.sorting_desc');
        for (var i = 0; i < downClass.length; i++) {
            var downElement = downClass[i].id;
            $("#" + downElement).removeClass('sorting_desc');
            $("#" + downElement).addClass('sorting');
        }
        $("#th" + columnName).removeClass('sorting');
        $("#th" + columnName).addClass('sorting_desc');
    }
    else if ($("#th" + columnName).hasClass("sorting_desc")) {
        $("#hdnSortDirection").val("DESC");
        var upClass = document.querySelectorAll('.sorting_asc');
        for (var i = 0; i < upClass.length; i++) {
            var upElement = upClass[i].id;
            $("#" + upElement).removeClass('sorting_asc');
            $("#" + upElement).addClass('sorting');
        }
        var downClass = document.querySelectorAll('.sorting_desc');
        for (var i = 0; i < downClass.length; i++) {
            var downElement = downClass[i].id;
            $("#" + downElement).removeClass('sorting_desc');
            $("#" + downElement).addClass('sorting');
        }
        $("#th" + columnName).removeClass('sorting');
        $("#th" + columnName).addClass('sorting_asc');
    }
    else if ($("#th" + columnName).hasClass("sorting_asc")) {
        $("#hdnSortDirection").val("ASC");
        var upClass = document.querySelectorAll('.sorting_asc');
        for (var i = 0; i < upClass.length; i++) {
            var upElement = upClass[i].id;
            $("#" + upElement).removeClass('sorting_asc');
            $("#" + upElement).addClass('sorting');
        }
        var downClass = document.querySelectorAll('.sorting_desc');
        for (var i = 0; i < downClass.length; i++) {
            var downElement = downClass[i].id;
            $("#" + downElement).removeClass('sorting_desc');
            $("#" + downElement).addClass('sorting');
        }
        $("#th" + columnName).removeClass('sorting');
        $("#th" + columnName).addClass('sorting_desc');
    }
    $("#frmGetSMSLogList").submit();
}

function GetAllSite(divisionId) {
    $("#selSearchSiteName").empty();
    if (divisionId != null && divisionId != undefined && divisionId != '' && parseInt(divisionId) > 0) {
        $.ajax({
            url: '/SMSLog/GetSitesBy',
            type: 'Post',
            data: '{divisionId:' + divisionId + '}',
            contentType: 'application/json',
            success: function (data) {
                if (data != null && data.length > 0) {
                    var html = "";
                    html += "<option value='0'>All</option>";
                    $.each(data, function (id, val) {
                        if ($("#hdnSiteId").val() == val.Id) {
                            html += "<option value=" + val.Id + " selected>" + val.Name + "</option>";
                        }
                        else {
                            html += "<option value=" + val.Id + ">" + val.Name + "</option>";
                        }

                    });
                    $("#selSearchSiteName").append(html);
                }
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }
    else {
        GetSite();
    }

}

function GetSite() {
    $("#selSearchSiteName").empty();
    $.ajax({
        url: '/SMSLog/GetAllSites',
        type: 'Post',
        contentType: 'application/json',
        success: function (data) {
            if (data != null && data.length > 0) {
                var html = "";
                html += "<option value='0'>All</option>";
                $.each(data, function (id, val) {
                    if ($("#hdnSiteId").val() == val.Id) {
                        html += "<option value=" + val.Id + " selected>" + val.Name + "</option>";
                    }
                    else {
                        html += "<option value=" + val.Id + ">" + val.Name + "</option>";
                    }

                });
                $("#selSearchSiteName").append(html);
            }
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

//function fnSearchSite() {
//    $("#hdnSkip").val('0');
//    $("#hdnSiteId").val($("#selSearchSiteName").val())
//    $("#frmGetSMSLogList").submit();
//}

function DeleteSMSLogById(id) {
    $("#loader").show();
    $.ajax({
        url: '/SMSLog/DeleteSMSLogById',
        type: 'Post',
        data:'{id:'+id+'}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            if (data == true) {
                $("#frmGetSMSLogList").submit();
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

function DeleteSMSLog(id) {
    $("#loader").show();
    $.ajax({
        url: '/SMSLog/DeleteSMSLogById',
        type: 'Post',
        data: '{id:' + id + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            if (data == true) {
                $("#frmGetSMSLogList").submit();
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