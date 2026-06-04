$(document).ready(function () {
    //$('#drpPageSize').select2({ minimumResultsForSearch: Infinity });

    $('#drpPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetUserClassList").submit();
    });

    $("#btnAddUserClass").click(function () {
        ClearAddUserClassControls();
    });

    $("#txtSearchClassName").keyup(function (e) {
        e.preventDefault();
        if (e.keyCode === 13) {
            if ($.trim($("#txtSearchClassName").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetUserClassList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetUserClassList").submit();
            }
        }
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
});

function PageSize() {
    $("#hdnSkip").val('0');
    $("#frmGetUserClassList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetUserClassList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $("#txtSearchClassName").val("");
    $("#frmGetUserClassList").submit();
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
    $("#frmGetUserClassList").submit();
}

function GetUserClassById(id) {
    $("#loader").show();
    $.ajax({
        url: '/UserClass/GetUserClassById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveUserClass").empty().append(data);
            $("#modal-add-userClass").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function DeleteUserClassById(id) {
    if (confirm('Are you sure you want to delete this record?')) {
        $("#loader").show();
        $.ajax({
            url: '/UserClass/DeleteUserClassById',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data.IsSuccess === true) {
                    CommonNotification("SUCCESS", "User class has been deleted.", "success");
                    $("#frmGetUserClassList").submit();
                } else if (data.IsSuccess === false) {
                    if (data.Message.includes("Could not delete this record")) {
                        CommonNotification("WARNING", data.Message, "warning");
                    } else if (data.Message == "Internal server error.") {
                        CommonNotification("ERROR", "Error occured while deleting user class!", "error");
                    }
                } else {
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

function ClearAddUserClassControls() {
    $("#hdnUserClassId").val("0");
    $("#ClassName").val("");
    $("#ClassName").next("span").html("");
    $("#DelayInMinutes").val("");
    $("#DelayInMinutes").next("span").html("");
    $("#Description").val("");

    $("#spHeader").empty().text("Add User class");
}