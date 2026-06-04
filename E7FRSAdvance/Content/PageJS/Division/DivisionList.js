$(document).ready(function () {
    //$('#drpPageSize').select2({ minimumResultsForSearch: Infinity });

    $('#drpPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetDivisionList").submit();
    });

    $("#btnAddDivision").click(function () {
        ClearAddDivisionControls();
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
                $("#frmGetDivisionList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetDivisionList").submit();
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
    $("#frmGetDivisionList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetDivisionList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $("#txtSearchName").val("");
    $("#frmGetDivisionList").submit();
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
    $("#frmGetDivisionList").submit();
}

function GetDivisionById(id) {
    $("#loader").show();
    $.ajax({
        url: '/Division/GetDivisionById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveDivision").empty().append(data);
            $("#modal-add-division").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function AddDivisionLink(id) {
    $("#loader").show();
    $.ajax({
        url: '/Division/_AddDivisionLinkPartial',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divDivisionImportantLink").empty().append(data);
            $("#modal-add-division-important-link").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}


function DeleteDivisionById(id) {
    if (confirm('Are you sure you want to delete this record?')) {
        $("#loader").show();
        $.ajax({
            url: '/Division/DeleteDivisionById',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data.IsSuccess === true) {
                    CommonNotification("SUCCESS", "Site has been deleted.", "success");
                    $("#frmGetDivisionList").submit();
                } else if (data.IsSuccess === false) {
                    if (data.Message.includes("Could not delete this record")) {
                        CommonNotification("WARNING", data.Message, "warning");
                    } else if (data.Message == "Internal server error.") {
                        CommonNotification("ERROR", "Error occured while deleting site!", "error");
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

function ClearAddDivisionControls() {
    $("#hdnDivisionId").val("0");
    $('#zoneAdd').val($('#zoneAdd option:first').val()).trigger('change');
    $("#Name").val("");
    $("#Name").next("span").html("");
    $("#Description").val("");
    $("#Description").next("span").html("");
    $("#IsActive").attr("Checked", "Checked");

    $("#spHeader").empty().text("Add Division");
}

function fnAddDivisionLink() {
    var id = 1;
    $('.txtLinkName').each(function () {
        if ($(this).attr('data-new-Id') != null && $(this).attr('data-new-Id') != '' && $(this).attr('data-new-Id') != undefined) {
            id = parseInt($(this).attr('data-new-Id'));
            id++;
        }

    });

    var remark = '<tr id="tr_' + id + '">';
    remark += '<td><label>Link ' + id + '</label></td>';
    remark += '<td><input type="text" class="form-control txtLinkName" data-new-Id="' + id + '" placeholder="Enter Link Name"/></td>';
    remark += '<td><input type="text" class="form-control txtLink" data-new-Id="' + id + '" placeholder="Enter Link"/></td>';
    remark += '<td></td>';
    remark += '<tr>';
    $('#tbodyDivisionImportantLink').append(remark);
}

function fnSaveImportantLink(divisionId) {
    var mDivisionImportantLinks = [];
    $('#tbodyDivisionImportantLink > tr').each(function (i) {
        var name = $(this).find('input:text.txtLinkName').val();
        var link = $(this).find('input:text.txtLink').val();   
        if (name != null && name != undefined && name != '' && link != null && link != undefined && link != '') {
            var mDivisionImportantLink = {}
            mDivisionImportantLink.Id = $(this).find('input:text.txtLinkName').data('main-id');
            mDivisionImportantLink.DivisionId = divisionId;
            mDivisionImportantLink.Name = name;
            mDivisionImportantLink.Link = link;
            mDivisionImportantLinks.push(mDivisionImportantLink);
        }
       
    });
    if (mDivisionImportantLinks != null && mDivisionImportantLinks.length > 0) {
        $("#loader").show();
        $.ajax({
            url: '/Division/SaveDivisionImportantLink',
            type: 'Post',
            data: '{mDivisionImportantLinks:' + JSON.stringify(mDivisionImportantLinks) + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data != null && data != undefined) {
                    if (data.type == "success") {
                        CommonNotification("SUCCESS", data.result, "success");
                    } else if (data.type == "error") {
                        CommonNotification("ERROR", data.result, "Error");
                    }
                    $('#modal-add-division-important-link').modal('hide');
                } else {
                    CommonNotification("ERROR", data.result, "Error");
                }
            },
            error: function (response) {
                $("#loader").hide();
            }
        });
    }

}

function fnDeleteImportantLink(id) {
    if (confirm('Are you sure you want to delete this record?')) {
        $("#loader").show();
        $.ajax({
            url: '/Division/DeleteImportantLink',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                $('#modal-add-division-important-link').modal('hide');
                if (data.type == 'success') {
                    CommonNotification("SUCCESS", "Important Link has been deleted.", "success");
                    $("#frmGetDivisionList").submit();
                } else if (data.type == 'error') {
                    CommonNotification("ERROR", "Error occured while deleting important link!", "error");
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