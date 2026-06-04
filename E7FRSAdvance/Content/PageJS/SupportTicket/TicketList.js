$(document).ready(function () {
    $('#drpPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetTicketList").submit();
    });

    $("#btnAddTicket").click(function () {
        ClearAddTicketControls();
    });

    $("#txtSearchName").keyup(function (e) {
        e.preventDefault();
        if (e.keyCode === 13) {
            if ($.trim($("#txtSearchTitle").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetTicketList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetTicketList").submit();
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
    $("#frmGetTicketList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetTicketList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $("#txtSearchName").val("");
    $("#frmGetTicketList").submit();
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
    $("#frmGetTicketList").submit();
}

function ClearAddTicketControls() {
    $("#hdnTicketId").val("0");
    $("#Title").val("");
    $("#Title").next("span").html("");
    $("#Description").val("");
    $("#Description").next("span").html("");
    $("#UploadImg").val("");
    $("#UploadImg").next("span").html("");
    $("#spHeader").empty().text("Add Ticket");
}