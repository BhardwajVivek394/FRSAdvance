$(document).ready(function () {
    //$('#drpPageSize').select2({ minimumResultsForSearch: Infinity });

    $('#drpPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetRoleList").submit();
    });

    $("#btnAddRole").click(function () {
        ClearAddRoleControls();
        //$("#modal-add-campaign .form-horizontal input").val('');
        //$("#modal-add-campaign .form-horizontal textarea").val('');
        //$("#modal-add-campaign .form-control").next('span').html('');
        //$("#Id").val('0');
    });

    $("#txtSearchTitle").keyup(function (e) {
        e.preventDefault();
        if (e.keyCode === 13) {
            if ($.trim($("#txtSearchTitle").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetRoleList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetRoleList").submit();
            }
        }
    });


    if ($('#hdnAreaType').val() == 'Success') {
        CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnAreaMessage").val(), $("#hdnSaveType").val());
    }
    else if ($('#hdnAreaType').val() == 'Error') {
        if ($('#hdnCatListMessage').val() == 'Forbidden!') {
            window.location.href = "/Login/Index";
        } else {
            CommonNotification($("#hdnSaveType").val().toUpperCase(), $("#hdnCatListMessage").val(), $("#hdnSaveType").val());
        }
    }
});

function PageSize() {
    $("#hdnSkip").val('0');
    $("#frmGetRoleList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetRoleList").submit();
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
    $("#frmGetRoleList").submit();
}

function GetRoleById(id) {
    $("#loader").show();
    $.ajax({
        url: '/User/GetRoleById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveRole").empty().append(data);
            $("#modal-add-role").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function DeleteRoleById(id) {
    if (confirm('Are you sure you want to delete this record?')) {
        $("#loader").show();
        $.ajax({
            url: '/User/DeleteRoleById',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data === "true") {
                    CommonNotification("SUCCESS", "Role has been deleted.", "success");
                    $("#frmGetRoleList").submit();
                }
                else if (data === "false") {
                    CommonNotification("ERROR", "Error occured while deleting Area!", "error");
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

function ClearAddRoleControls() {
    $("#Id").val('0');
    $("#Title").val("");
    $("#Title").next("span").html("");
    $("#Description").val("");
    $("#Description").next("span").html("");
    $("#myModalLabel").text("Add Role");
    $("#spHeader").empty().text("Add Role");
}