$(document).ready(function () {

    $('#drpPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetSectionList").submit();
    });

    $("#btnAddSection").click(function () {
        ClearAddSectionControls();
        //$("#modal-add-campaign .form-horizontal input").val('');
        //$("#modal-add-campaign .form-horizontal textarea").val('');
        //$("#modal-add-campaign .form-control").next('span').html('');
        //$("#Id").val('0');
    });

    $("#txtSearchName").keyup(function (e) {
        e.preventDefault();
        if (e.keyCode === 13) {
            if ($.trim($("#txtSearchTitle").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetSectionList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetSectionList").submit();
            }
        }
    });

    if ($('#hdnCatListType').val() == 'Success') {
        CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnCatListMessage").val());
    }
    else if ($('#hdnCatListType').val() == 'Error') {
        if ($('#hdnCatListMessage').val() == 'Forbidden!') {
            window.location.href = "/Login/Index";
        } else {
            CommonNotification($("#hdnSaveType").val().toLowerCase(), $("#hdnCatListMessage").val());
        }
    }
});

function PageSize() {
    $("#hdnSkip").val('0');
    $("#frmGetSectionList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetSectionList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $("#txtSearchName").val("");
    $("#frmGetSectionList").submit();
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
    $("#frmGetSectionList").submit();
}

function GetSectionById(id) {
    $("#loader").show();
    $.ajax({
        url: '/Section/GetSectionById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveSection").empty().append(data);
            $("#modal-add-section").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function DeleteSectionById(id) {
    if (confirm('Are you sure you want to delete this record?')) {
        $("#loader").show();
        $.ajax({
            url: '/Section/DeleteSectionById',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data.type == "success") {
                    CommonNotification("SUCCESS", "Site has been deleted.", "success");
                    $("#frmGetSectionList").submit();
                } 
                 else {
                    CommonNotification("ERROR", "Internal server error.", "error");
                }
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Internal server error.", "error");
            }
        });
    }
}
function ClearAddSectionControls() {
    $("#hdnSectionId").val("0");
    $('#divisionAdd').val($('#divisionAdd option:first').val()).trigger('change');
    $("#Name").val("");
    $("#Name").next("span").html("");
    $("#Description").val("");
    $("#Description").next("span").html("");
    $("#IsActive").attr("Checked", "Checked");

    $("#spHeader").empty().text("Add Section");
}

function GetAllZones() {
    $("#loader").show();
    $.ajax({
        type: 'POST',
        url: '/User/GetAllZones',
        contentType: "application/json; charset=utf-8",
        dataType: "json",
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            var temp = '';
            if (data != null) {
                $.each(data, function (key, value) {
                    temp += '<option value="' + value.Id + '">' + value.Name + '</option>';
                });
            }
            $("#drpZonePanel" + userId + "").empty().append(temp);
            $("#drpZonePanel" + userId + "").val($("#drpZonePanel" + userId + " option:first").val());
            //  $("#drpZonePanel" + userId + "").select2({ minimumResultsForSearch: Infinity });
            GetDivisionByZoneId($("#drpZonePanel" + userId + ""));
            $("#loader").hide();
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Internal server error.", "error");
        }
    });
}