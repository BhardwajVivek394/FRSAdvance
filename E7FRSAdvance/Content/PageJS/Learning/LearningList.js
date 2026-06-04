$(document).ready(function () {


    $('#drpPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetLearningList").submit();
    });

    $("#btnAddLearning").click(function () {
        ClearAddLearningControls();
    });

    $("#txtSearchName").keyup(function (e) {
        e.preventDefault();
        if (e.keyCode === 13) {
            if ($.trim($("#txtSearchTitle").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetLearningList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetLearningList").submit();
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
    $("#frmGetLearningList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetLearningList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $("#txtSearchName").val("");
    $("#frmGetLearningList").submit();
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
    $("#frmGetLearningList").submit();
}


function ClearAddLearningControls() {
    $("#hdnLearningId").val("0");
    $("#Subject").val("");
    $("#Subject").next("span").html("");
    $("#VideoURL").val("");
    $("#VideoURL").next("span").html("");
    $("#Description").val("");
    $("#Description").next("span").html("");


    $("#spHeader").empty().text("Add Learning");
}