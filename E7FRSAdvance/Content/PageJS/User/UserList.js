$(document).ready(function () {
    $("[data-bs-toggle='popover']").popover();
   // $('#drpPageSize').select2({ minimumResultsForSearch: Infinity });
  
    $('#drpUserPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetUserList").submit();
    });

    $('#drpUserSiteId').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetUserList").submit();
    });    

    $("#btnAddUser").click(function () {
        ClearAddUserControls();
        //$("#modal-add-campaign .form-horizontal input").val('');
        //$("#modal-add-campaign .form-horizontal textarea").val('');
        //$("#modal-add-campaign .form-control").next('span').html('');
        //$("#Id").val('0');
    });

    //$('td').tooltip({
    //    disabled: true,
    //    close: function (event, ui) { $(this).tooltip('disable'); }
    //});

    $("#txtSearchFirstName").keyup(function (e) {
        e.preventDefault();
        if (e.keyCode === 13) {
            if ($.trim($("#txtSearchFirstName").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetUserList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetUserList").submit();
            }
        }
    });

    $("#txtSearchLastName").keyup(function (e) {
        e.preventDefault();
        if (e.keyCode === 13) {
            if ($.trim($("#txtSearchLastName").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetUserList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetUserList").submit();
            }
        }
    });

    $("#txtEmailAddress").keyup(function (e) {
        e.preventDefault();
        if (e.keyCode === 13) {
            if ($.trim($("#txtUserName").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetUserList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetUserList").submit();
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

function fnBranchId(id) {
    $("#hdnBranchId").val(id);
    $("#hdnSkip").val('0');
    $("#spBranchmainPage").text($("#atagBranchId" + id).attr('data-value'));
    $("#frmGetUserList").submit();
}

function PageSize() {
    $("#hdnSkip").val('0');
    $("#frmGetUserList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetUserList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $("#txtSearchFirstName").val("");
    $("#txtSearchLastName").val("");
    $("#txtEmailAddress").val("");
    $("#drpUserSiteId").val($("#drpUserSiteId option:first").val());
    $("#frmGetUserList").submit();
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
    $("#frmGetUserList").submit();
}

function GetUserById(id) {
    $("#loader").show();
    $.ajax({
        url: '/User/GetUserById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveUser").empty().append(data);
            $("#modal-add-user").modal('show');
            //$("#Password").attr("disabled", "disabled");
            $("#EmailAddress").attr("disabled", "disabled");
            $("#btnSave").attr("disabled", false);
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function DeleteUserById(id) {
    if (confirm('Are you sure you want to delete this record?')) {
        $("#loader").show();
        $.ajax({
            url: '/User/DeleteUserById',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data.IsSuccess === true) {
                    CommonNotification("SUCCESS", "Site has been deleted.", "success");
                    $("#frmGetUserList").submit();
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

var userId;

function GetZoneDivision(id, $this) {
    userId = id;

    if ($($this).hasClass("fa-plus")) {
        $($this).removeClass("fa-plus").addClass("fa-minus");
        var html = "";
        html += "<td colspan='14'>";
        html += "<div class='card card-inverse' style='padding:10px;'>"
        html += "<div class='card-heading'>"
        html += "<h3 class='card-title'>Sites</h3>"
        html += "</div>"
        html += "<div class='card-body'>"
        html += "<div class='row'>"
        html += "<div class='col-lg-4 col-md-2' style='padding:5px'>"
        html += "</div >"
        html += "<div class='col-lg-2 col-md-2 col-sm-2' style='padding:8px'>"
        html += "<label class='control-label' style='font-size:15px'>Filter by:</label>"
        html += "</div >"
        html += "<div class='col-lg-3 col-md-4 col-sm-4' style='margin-bottom:14px'>"
        html += "<div class='row'>"
        html += "<div class='col-sm-4' style='padding:8px'>"
        html += "<label class='control-label' style='font-size:15px'>Zone:</label>"
        html += "</div >"
        html += "<div class='col-sm-8' style='margin-bottom:14px'>"
        html += "<select name='ZoneId' id='drpZonePanel" + id + "' class='form-control' data-Id=" + id + " style='width:100%' onchange='GetDivisionByZoneId(this)'>"
        html += "</select>"
        html += "</div >"
        html += "</div >"
        html += "</div >"
        html += "<div class='col-lg-3 col-md-4 col-sm-4' style='margin-bottom:14px'>"
        html += "<div class='row'>"
        html += "<div class='col-sm-4' style='padding:8px'>"
        html += "<label class='control-label' style='font-size:15px'>Division:</label>"
        html += "</div >"
        html += "<div class='col-sm-8' style='margin-bottom:14px'>"
        html += "<select name='DivisionId' id='drpDivisionPanel" + id + "' data-Id=" + id + " class='form-control' style='width:100%' onchange='fnGetSites(this)'>"
        html += "</select>"
        html += "</div >"
        html += "</div >"
        html += "</div >"
        html += "</div >"
        html += "<div class='row' id='sites" + id + "'>"
        html += "</div>";
        html += "</div>";
        html += "</td>";
        $("#trSites" + id).empty().append(html);
        $("#trSites" + id).show();
        GetAllZones();
    } else {
        $($this).removeClass("fa-minus").addClass("fa-plus");
        $("#trSites" + id).hide();
        $("#trSites" + id).empty();
    }
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

function GetDivisionByZoneId($this) {
    var zoneId = $($this).val();
    var divisionId = $($this).attr("data-Id");
    if (zoneId != null) {
        $("#loader").show();
        $.ajax({
            type: 'POST',
            url: '/Site/GetDivisionByZoneId',
            contentType: "application/json; charset=utf-8",
            dataType: "json",
            data: '{zoneId:' + zoneId + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                var temp = '';
                if (data != null) {
                    $.each(data, function (key, value) {
                        temp += '<option value="' + value.Id + '">' + value.Name + '</option>';
                    });
                }
                $("#drpDivisionPanel" + divisionId + "").empty().append(temp);
                $("#drpDivisionPanel" + divisionId + "").val($("#drpDivisionPanel" + divisionId + " option:first").val());
               // $("#drpDivisionPanel" + divisionId + "").select2({ minimumResultsForSearch: Infinity });
                $("#loader").hide();
                fnGetSites($("#drpDivisionPanel" + divisionId + ""));
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Internal server error.", "error");
            }
        });
    } else {
        var temp = '';
        $("#drpDivisionPanel" + divisionId + "").empty().append(temp);
       // $("#drpDivisionPanel" + divisionId + "").select2({ minimumResultsForSearch: Infinity });
    }
}

function fnGetSites($this) {
    var uId = $($this).attr("data-Id");
    var zoneId = $("#drpZonePanel" + uId + "").val();
    var divisionId = $("#drpDivisionPanel" + uId + "").val();

    $("#loader").show();
    $.ajax({
        url: '/User/GetSites',
        type: 'Post',
        data: '{zoneId:' + zoneId + ',divisionId:' + divisionId + ',userId:' + uId + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            var temp = "";
            if (data != null && data.length > 0) {
                $.each(data, function (id, val) {
                    if (val.IsAssignSite) {
                        temp += "<div class='col-sm-3' style='margin-top:14px'>";
                        temp += "<label class='checkbox-inline' style='font-size:15px'><input style='margin-top:4px' checked type='checkbox' data-userId=" + uId + "   data-siteId=" + val.Id + " onclick='fnAssignSite(" + userId + "," + val.Id + ",this)'  />" + val.Name + "</label>";
                        temp += "</div>";
                    }
                    else {
                        temp += "<div class='col-sm-3' style='margin-top:14px'>";
                        temp += "<label class='checkbox-inline' style='font-size:15px'><input style='margin-top:4px' type='checkbox' data-userId=" + uId + "   data-siteId=" + val.Id + " onclick='fnAssignSite(" + userId + "," + val.Id + ",this)'  />" + val.Name + "</label>";
                        temp += "</div>";
                    }
                });
                $("#sites" + uId).empty().append(temp);
            } else {
                temp += "<label class='text-center' style='font-size:15px;margin:10px'>No sites found!</label>";
                $("#sites" + uId).empty().append(temp);
            }
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnAssignSite(id, siteId, $this) {
    var isAssigned = $($this).is(":checked");
    $.ajax({
        url: '/User/AssignSiteByUserId',
        type: 'Post',
        data: '{id:' + id + ',siteId:' + siteId + ',isAssigned:' + isAssigned + '}',
        contentType: 'application/json',
        success: function (data) {
            if (data == false) {
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function ClearAddUserControls() {
    $("#drpBranch").val('0').trigger('change');
    $('#roleAdd').val($('#roleAdd option:first').val()).trigger('change');
    $('#levelAdd').val($('#roleAdd option:first').val()).trigger('change');
    $('#spdrpBranch').html("");
    $("#hdnUserId").val("0");
    $("#FirstName").val("");
    $("#FirstName").next("span").html("");
    $("#PhoneNumber").val("");
    $("#PhoneNumber").next("span").html("");
    $("#LastName").val("");
    $("#LastName").next("span").html("");
    $("#EmailAddress").val("");
    $("#EmailAddress").next("span").html("");
    $("#myModalLabel").text("Add User");
    $("#Password").val("");
    $("#Password").next("span").html("");
    $("#IsActive").attr("Checked", "Checked");
    $("#Password").removeAttr("disabled", "disabled");
    $("#EmailAddress").removeAttr("disabled", "disabled");
    $("#spHeader").empty().text("Add User");
    $("#btnSave").attr("disabled", true);
}

function ViewPassword($this) {

    if ($($this).hasClass("fa-eye-slash")) {
        $($this).removeClass("fa-eye-slash").addClass("fa-eye");
    }
    else {
        $($this).removeClass("fa-eye").addClass("fa-eye-slash");
    }
}

function parseDate(date) {
    return new Date(parseInt(/-?\d+/.exec(date)[0]))
}

function fnConvertDate(currentdate) {

    return "" + currentdate.getDate() + "/"
        + (currentdate.getMonth() + 1) + "/"
        + currentdate.getFullYear() + " "
        + currentdate.getHours() + ":"
        + currentdate.getMinutes() + ":"
        + currentdate.getSeconds();
}

function fnGetAppAccessByUser(id, startDate, endDate) { 
    $('#hdnAppAccessUserId').val(id);
    $('#btnAppAccessDownload').hide();

    var mAppAccessLister = {};
    mAppAccessLister.SearchCriteria = {};
    mAppAccessLister.SearchCriteria.UserId = $('#hdnAppAccessUserId').val();

    if (startDate != null && startDate != undefined && startDate != '') {
        mAppAccessLister.SearchCriteria.StartTime = startDate;
    }
    if (endDate != null && endDate != undefined && endDate != '') {
        mAppAccessLister.SearchCriteria.EndTime = endDate;
    }

    $("#loader").show();
    $.ajax({
        url: '/User/GetAllAppAccess',
        type: 'Post',
        data: JSON.stringify(mAppAccessLister),
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            var tbody = '';
            $("#tblAppAccessBySites").hide();
            $("#tblAppAccessByUser").show();
            $("#tbodyAppAccessRangeByUser").empty();
            if (data != null && data != undefined && data.mAppAccess != null && data.mAppAccess != undefined) {
                $.each(data.mAppAccess, function (data, val) {
                    var dStartTime = parseDate(val.StartTime);
                    var eStartTime = parseDate(val.EndTime);
                    tbody += '<tr>';
                    tbody += '<td>' + fnConvertDate(dStartTime) + '</td>';
                    tbody += '<td>' + fnConvertDate(eStartTime) + '</td>';
                    tbody += '<td>' + val.Duration + '</td>';
                    tbody += '</tr>';

                });
            }
            $("#tbodyAppAccessRangeByUser").append(tbody);
            $("#modal-AppAccess").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnGetAppAccessBySite(startDate, endDate) {  
    $('#hdnAppAccessUserId').val('');
    $('#btnAppAccessDownload').show();

    var mAppAccessLister = {};
    mAppAccessLister.SearchCriteria = {};
    mAppAccessLister.SearchCriteria.SiteId = $('#drpUserSiteId').val();
    mAppAccessLister.SearchCriteria.DivisionId = $('#drpUserDivisionId').val();

    if (startDate != null && startDate != undefined && startDate != '') {
        mAppAccessLister.SearchCriteria.StartTime = startDate;
    }
    if (endDate != null && endDate != undefined && endDate != '') {
        mAppAccessLister.SearchCriteria.EndTime = endDate;
    }

    if (($('#drpUserSiteId').val() != null && $('#drpUserSiteId').val() != undefined && $('#drpUserSiteId').val() != '' && $('#drpUserSiteId').val() != '0') || ($('#drpUserDivisionId').val() != null && $('#drpUserDivisionId').val() != undefined && $('#drpUserDivisionId').val() != '' && $('#drpUserDivisionId').val() != '0')) {
        $("#loader").show();
        $.ajax({
            url: '/User/GetAllAppAccess',
            type: 'Post',
            data: JSON.stringify(mAppAccessLister),
            contentType: 'application/json',
            success: function (data) {
                $("#tblAppAccessBySites").hide();

                $("#loader").hide();
                var tbody = '';

                $("#tblAppAccessBySites").show();
                $("#tblAppAccessByUser").hide();

                $("#tbodyAppAccessRangeBySite").empty();
                if (data != null && data != undefined && data.mAppAccess != null && data.mAppAccess != undefined) {

                    $.each(data.mUser, function (value, userVal) {
                        tbody += '<tr>';
                        tbody += '<td><a href="#" data-bs-original-title="" title=""><i class="fa text-vision fa-plus" data-id="25" onclick="fnShowAppAccess(' + userVal.Id +',this)" data-alert="False"></i></a></td>';
                        tbody += '<td>' + userVal.Name + '</td>';
                        tbody += '<td>' + userVal.EmailAddress + '</td>';
                        tbody += '<td>' + userVal.Duration + '</td>';
                        tbody += '</tr>';

                        tbody += '<tr style="display:none" id="trAppAccess_' + userVal.Id + '">';
                        tbody += '<td colspan="4">';
                        tbody += '<table class="table">';
                        tbody += '<tr><th>Start Time</th><th>End Time</th><th>Duration(Min)</th></tr>';
                        $.each(data.mAppAccess, function (test, val) {
                            if (val.Name == userVal.Name) {
                                var dStartTime = parseDate(val.StartTime);
                                var eStartTime = parseDate(val.EndTime);
                                tbody += '<tr>';
                                tbody += '<td>' + fnConvertDate(dStartTime) + '</td>';
                                tbody += '<td>' + fnConvertDate(eStartTime) + '</td>';
                                tbody += '<td>' + val.Duration + '</td>';
                                tbody += '</tr>';
                            }
                            

                        });
                        tbody += '</table>';
                        tbody += '</td>';
                        tbody += '</tr>';
                    });
                  
                }
                $("#tbodyAppAccessRangeBySite").append(tbody);
                // $("#divAppAccess").empty().append(data);
                $("#modal-AppAccess").modal('show');
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }
    else {
        CommonNotification("ERROR", "Please select site or division", "Error");
    }
}

function fnShowAppAccess(userId, $this) {
    if ($($this).hasClass("fa-plus")) {
        $('#trAppAccess_' + userId).show();
        $($this).removeClass("fa-plus").addClass("fa-minus");
    }
    else {
        $($this).removeClass("fa-minus").addClass("fa-plus");
        $('#trAppAccess_' + userId).hide();
    }
   /* $('#trAppAccess_' + userId).show();*/
}

function fnAppAccessDownload() {
    var mAppAccessLister = {};
    mAppAccessLister.SearchCriteria = {};
    mAppAccessLister.SearchCriteria.SiteId = $('#drpUserSiteId').val();
    mAppAccessLister.SearchCriteria.DivisionId = $('#drpUserDivisionId').val();
    
    if (($('#drpUserSiteId').val() != null && $('#drpUserSiteId').val() != undefined && $('#drpUserSiteId').val() != '' && $('#drpUserSiteId').val() != '0') || ($('#drpUserDivisionId').val() != null && $('#drpUserDivisionId').val() != undefined && $('#drpUserDivisionId').val() != '' && $('#drpUserDivisionId').val() != '0')) {
        $("#loader").show();
        $.ajax({
            url: '/User/DownloadAppAccess',
            type: 'Post',
            data: JSON.stringify(mAppAccessLister),
            contentType: 'application/json',
            success: function (filePath) {
                $("#loader").hide();

                const link = document.createElement('a')
                link.setAttribute('download', 'test')
                link.setAttribute('href', filePath)
                link.click()
                $('.modal-backdrop').remove();
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }
    else {
        CommonNotification("ERROR", "Please select site or division", "Error");
    }
}


function fnAssignUserDivision(userId) {
    $("#loader").show();
    $.ajax({
        url: '/User/_AssignUserDivision',
        type: 'Post',
        data: '{userId:' + userId + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divAssignUserDivision").empty().append(data);
            $("#modal-assign-user-division").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnSaveUserDivision(id, divisionId, $this) {
    var isAssigned = $($this).is(":checked");
    if (isAssigned) {
        $("#loader").show();
        var mUserDivision = {};
        mUserDivision.UserId = id;
        mUserDivision.DivisionId = divisionId;
        $.ajax({
            url: '/User/AssignUserDivision',
            type: 'Post',
            data: '{mUserDivision:' + JSON.stringify(mUserDivision) + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data == false) {
                    CommonNotification("ERROR", "Something went wrong!", "Error");
                }
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }
    else {
        $.ajax({
            url: '/User/DeleteUserDivision',
            type: 'Post',
            data: '{userId:' + id + ',divisionId:' + divisionId + '}',
            contentType: 'application/json',
            success: function (data) {
                if (data == false) {
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