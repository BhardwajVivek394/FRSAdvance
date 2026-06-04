

$(document).ready(function () {

    $('#drpPageSize').change(function () {
        $("#hdnSkip").val('0');
        $("#frmGetNetworkList").submit();
    });

    // ZONE ? DIVISION
    $("#zoneAdd1").change(function () {
        GetDivisionByZone($(this).val());
        $("#frmGetNetworkList").submit();
    });

    // On page load ? fill divisions
    GetDivisionByZone($("#zoneAdd1").val());

    // DIVISION ? SITE
    $("#divisions").change(function () {
        GetSiteByDivisionId($(this).val());
        $("#frmGetNetworkList").submit();
    });

    // On page load ? fill sites if division preselected
    GetSiteByDivisionId($("#divisions").val());
    
    

});



function PageSize() {
    $("#hdnSkip").val('0');
    $("#frmGetNetworkList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetNetworkList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $("#zoneAdd").val('0');
    $("#divisions").val('0');
    $("#txtSearchName").val("");
    $("#frmGetNetworkList").submit();
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
    $("#frmGetNetworkList").submit();
}

function GetSiteById(id) {
    $("#loader").show();
    $.ajax({
        url: '/Site/GetSiteById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveSite").empty().append(data);
            $("#modal-add-site").modal('show');
            $("#zoneAdd").trigger("change");
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });


}

function GetDivisionByZone(zoneId) {
    if (zoneId != null && zoneId != undefined && zoneId != '') {
        $("#loader").show();
        $.ajax({
            url: '/Network/GetDivisionByZoneId',
            type: 'POST',
            data: '{zoneId:' + zoneId + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                $("#divisions").empty();
                $("#divisions").append($("<option></option>").val('0').html('Select Divison'));
                $.each(data, function (key, value) {
                    if ($('#hdnSearchDivisionId').val() != '0' && $('#hdnSearchDivisionId').val() == value.Id) {
                        $("#divisions").append($("<option selected></option>").val(value.Id).html(value.Name));
                    } else {
                        $("#divisions").append($("<option></option>").val(value.Id).html(value.Name));
                    }
                });
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }

}


function GetSiteByDivisionId(divisionId) {

    if (!divisionId || divisionId == 0) {
        $("#drpSiteId").html('<option value="0">Select Site</option>');
        return;
    }

    $.ajax({
        url: '/Network/GetSiteByDivisionId',
        type: 'GET',
        data: { divisionId: divisionId },
        success: function (data) {

            $("#drpSiteId").empty();
            $("#drpSiteId").append('<option value="0">Select Site</option>');

            $.each(data, function (i, item) {
                $("#drpSiteId").append('<option value="' + item.Id + '">' + item.Name + '</option>');
            });
        }
    });
}


