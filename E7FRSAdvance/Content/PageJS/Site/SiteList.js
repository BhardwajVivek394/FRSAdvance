$(document).ready(function () {
    // $('#drpPageSize').select2({ minimumResultsForSearch: Infinity });
    $('#drpPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetSiteList").submit();
    });

    $("#zoneAdd").change(function () {
        $("#frmGetSiteList").submit();
    });

    $("#divisions").change(function () {
        $("#frmGetSiteList").submit();
    });

    $("#btnAddSite").click(function () {
        ClearAddSiteControls();
        //$("#modal-add-campaign .form-horizontal input").val('');
        //$("#modal-add-campaign .form-horizontal textarea").val('');
        //$("#modal-add-campaign .form-control").next('span').html('');
        //$("#Id").val('0');
    });

    GetDivisionByZone($("#zoneAdd1").val());

    $("#zoneAdd1").change(function () {
        GetDivisionByZone($(this).val());

        $("#frmGetSiteList").submit();
    });

    $("#txtSearchName").keyup(function (e) {
        e.preventDefault();


        if (e.keyCode === 13) {
            if ($.trim($("#txtSearchTitle").val()) !== "") {
                $("#hdnSkip").val('0');
                $("#frmGetSiteList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetSiteList").submit();
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

function fnSeriesAlert($this) {
    if ($($this).is(":checked")) {
        $("[href='#AlertsSeries']").parent().show();
        $("[href='#AlertsSeries']").click();
        $("#chkAlertParrallel").prop("checked", false);
        $("[href='#Alerts']").parent().hide();
    }
    else {

        $("[href='#AlertsSeries']").parent().hide();
        $("#chkAlertParrallel").prop("checked", true);
        $("[href='#Alerts']").parent().show();
        $("[href='#Alerts']").click();
    }
}

function fnParrallelAlert($this) {
    if ($($this).is(":checked")) {
        $("[href='#Alerts']").parent().show();
        $("[href='#Alerts']").click();

        $("[href='#AlertsSeries']").parent().hide();
        $("#chkAlertSeries").prop("checked", false);
    }
    else {

        $("[href='#Alerts']").parent().hide();
        $("#chkAlertSeries").prop("checked", true);
        $("[href='#AlertsSeries']").parent().show();
        $("[href='#AlertsSeries']").click();
    }
}

function fnChkErrorCode($this) {

    if ($($this).is(":checked")) {
        $("[href='#errorCode']").parent().show();
    }
    else {
        $("[href='#errorCode']").parent().hide();
        $("[href ='#Config']").click();
        $("#txtIdentifier").val("");
    }
}

function fnErrCodeSaveAsset() {
    if ($.trim($("#txtIdentifier").val()).length > 0) {
        var counter = 1;
        var teLength = $("#tblErrorcode > tr").length;

        if (teLength > 0) {
            var i = 0;
            $("#tblErrorcode > tr").each(function () {
                if (parseInt($(this).attr("data-id")) > i) {
                    counter = parseInt($(this).attr("data-id")) + 1;
                }
            })
        }
        else
            counter = 1;

        if ($.trim($("#txtErrocode").val()).length > 0) {

            if ($.trim($("#action").val()) == "add") {
                var html = "";
                html += "<tr data-id=" + counter + " id='tre" + counter + "'>";
                html += "<td id='tde" + counter + "'>" + $("#txtErrocode").val() + "</td>";
                html += "<td><a onclick='EditErrorCode(this)'><i class='glyphicon glyphicon-edit text-vision'></i></a><a onclick='DeleteErrorCode(this)' style='margin-left:10px;'><i class='glyphicon glyphicon-trash text-danger'></i></a></td>";
                html += "</tr>";
                $("#tblErrorcode").append(html);
            }
            else {
                var id = $("#errId").val();
                $("#tde" + id).html($("#txtErrocode").val());
            }
        }
        else {
            CommonNotification("ERROR", "Word required!", "Error");
        }

    }
    else {
        CommonNotification("ERROR", "Identifier required!", "Error");
    }
    $("#modal-add-errocode").modal("hide");
}

function AddErrorCode() {
    $("#action").val("add")
    if ($.trim($("#txtIdentifier").val()).length > 0) {
        $("#modal-add-errocode").modal("show");
        $("#errId").val(0);
        $("#txtErrocode").val("");
    }
    else {
        CommonNotification("ERROR", "Word required!", "Error");
    }

}

function EditErrorCode($this) {
    $("#action").val("edit")
    var id = $($this).parent().parent().attr("data-id");
    $("#modal-add-errocode").modal("show");
    $("#errId").val(id);
    var val = $("#tde" + id).html();

    $("#txtErrocode").val($.trim(val));
}

function DeleteErrorCode(sefl) {
    var id = $(sefl).parent().parent().attr("data-id");
    $("#tre" + id).remove();

}

function PageSize() {
    $("#hdnSkip").val('0');
    $("#frmGetSiteList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetSiteList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $("#zoneAdd").val('0');
    $("#divisions").val('0');
    $("#txtSearchName").val("");
    $("#frmGetSiteList").submit();
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
    $("#frmGetSiteList").submit();
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

function DeleteSiteById(id) {
    if (confirm('Are you sure you want to delete this record?')) {
        $("#loader").show();
        $.ajax({
            url: '/Site/DeleteSiteById',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data.IsSuccess === true) {
                    CommonNotification("SUCCESS", "Site has been deleted.", "success");
                    $("#frmGetSiteList").submit();
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

function GetAssestTypesById(id, $this) {
    siteId = id;
    if ($($this).hasClass("fa-plus")) {
        $($this).removeClass("fa-plus").addClass("fa-minus");

        $.ajax({
            url: '/Site/GetAllAssestTypes',
            type: 'Post',
            //data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data != null && data.length > 0) {

                    var html = "";
                    html += "<td colspan='13'>";
                    html += "<div class='panel panel-inverse' style='padding:10px;'>"
                    html += "<div class='panel-heading'>"
                    html += "<h3 class='panel-title'>Asset</h3>"
                    html += "</div>"
                    html += "<div class='panel-body'>"
                    html += "<div class='row'>"
                    //html += "<div class='col-sm-2 text-right' style='padding:8px'>"
                    //html += "</div >"
                    html += "<div class='col-sm-12 text-end' style='margin-bottom:14px'>"
                    html += "<label class='control-label' style='font-size:15px;margin-right:10px'>Asset type by:</label>"

                    //html += "<div class='btn-group mr5'>"
                    //html += "<button type='button' class='btn btn-primary'>Action</button>"
                    //html += "<button type='button' class='btn btn-primary'>"
                    //html += "<span class='fa fa-plus'></span>"
                    //html += "</button>"
                    //html += "</div >"

                    $.each(data, function (ids, val) {
                        html += "<div class='btn-group mr5' style='margin:5px'>"
                        html += "<button type='button' onclick='fnGetSearchAsset(" + id + ",\"" + val.Name + "\")' class='btn btn-dark'>" + val.Name + "</button>"
                        html += "<button type='button' class='btn btn-primary' onclick='AddAsset(" + val.Id + "," + siteId + ",\"" + val.Name + "\")'>"
                        html += "<span class='fa fa-plus'></span>"
                        html += "</button>";
                        html += "</div>";
                    });

                    html += "</div >"
                    html += "</div >"
                    html += "<div class='row' id='Asset" + id + "'>"
                    html += "</div>";
                    html += "</div>";
                    html += "</td>";
                    $("#trAssetType" + id).empty().append(html);
                    $("#trAssetType" + id).show();
                    fnGetAsset(id);
                } else {
                    var html = "";
                    html += "<td colspan='10'>";
                    html += "<div class='panel panel-inverse' style='padding:10px;'>"
                    html += "<div class='panel-heading'>"
                    html += "<h3 class='panel-title'>Asset</h3>"
                    html += "</div>"
                    html += "<div class='panel-body'>"
                    html += "<div class='row'>"
                    html += "<p style='font-size:15px'>Asset type not found! Please add Asset Type first.</p>"
                    html += "</div >"
                    html += "</div >"
                    html += "<div class='row' id='Asset" + id + "'>"
                    html += "</div>";
                    html += "</div>";
                    html += "</td>";
                    $("#trAssetType" + id).empty().append(html);
                    $("#trAssetType" + id).show();
                }
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    } else {
        $($this).removeClass("fa-minus").addClass("fa-plus");
        $("#trAssetType" + id).hide();
        $("#trAssetType" + id).empty();
    }
}

function fnGetAsset(id) {
    var siteId = id;
    $("#loader").show();
    $.ajax({
        url: '/Site/GetAllAssetById',
        type: 'Post',
        data: '{siteId:' + siteId + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            var temp = "";
            if (data != null && data.length > 0) {
                $.each(data, function (id, val) {
                    if (val.IsActive) {
                        temp += "<div class='col-lg-3 draggable' id='" + val.Id + "' >";
                        temp += "<div class='card shadow-lg'>";
                        temp += "<div class='card-header bg-primary'>";
                        temp += "<h6 class=''>" + val.AssetTypeName + "</h3>";
                        temp += "<div class='edit tooltips' data-placement='top' data-toggle='tooltip' data-original-title='Edit' data-Id=" + siteId + " onclick='GetAssetById(" + val.Id + ")'>";
                        temp += "<span class='fa fa-pencil'></span>";
                        temp += "</div>";
                        temp += "<div class='delete tooltips' data-placement='top' data-toggle='tooltip' data-original-title='Delete' data-assetname='" + val.Name + "' data-Id=" + siteId + " onclick='DeleteAssetById(" + val.Id + ",this)'>";
                        temp += "<span class='fa fa-times'></span>";
                        temp += "</div>";

                        temp += "</div>";

                        temp += "<div class='card-body' style='padding:15px;'>";
                        if (val.Name.length > 15)
                            temp += "<label class='tooltips' style='font-size:15px;padding:2px' data-placement='top' data-toggle='tooltip' data-original-title='" + val.Name + "'>" + val.Name.substring(0, 15) + '...' + "</label>";
                        else
                            temp += "<label style='font-size:15px;padding:2px'>" + val.Name + "</label>";

                        temp += "<span class='label label-success' style='font-size:11px;padding:5px;border-radius:10px;float:right'>Active</span>";
                        temp += "</div>";

                        temp += "</div>";
                        temp += "</div>";
                    } else {
                        temp += "<div class='col-lg-3 col-md-6' id='" + val.Id + "' >";
                        temp += "<div class='panel panel-danger' style='background-color: #f0f1f4;'>";
                        temp += "<div class='panel-heading'>";
                        temp += "<div class='edit tooltips' data-placement='top' data-toggle='tooltip' data-original-title='Edit' data-Id=" + siteId + " onclick='GetAssetById(" + val.Id + ")'>";
                        temp += "<span class='fa fa-pencil'></span>";
                        temp += "</div>";
                        temp += "<div class='delete tooltips' data-placement='top' data-toggle='tooltip' data-original-title='Delete' data-Id=" + siteId + " onclick='DeleteAssetById(" + val.Id + ",this)'>";
                        temp += "<span class='fa fa-times'></span>";
                        temp += "</div>";
                        temp += "<h3 class='panel-title'>" + val.AssetTypeName + "</h3>";
                        temp += "</div>";

                        temp += "<div class='panel-body'>";
                        if (val.Name.length > 15)
                            temp += "<label class='tooltips' style='font-size:15px;padding:2px' data-placement='top' data-toggle='tooltip' data-original-title='" + val.Name + "'>" + val.Name.substring(0, 15) + '...' + "</label>";
                        else
                            temp += "<label style='font-size:15px;padding:2px'>" + val.Name + "</label>";

                        temp += "<span class='label label-danger' style='font-size:11px;padding:5px;border-radius:10px;float:right'>Inactive</span>";
                        temp += "</div>";

                        temp += "</div>";
                        temp += "</div>";
                    }
                });
                $("#Asset" + siteId).empty().append(temp);
            } else {
                temp += "<label class='text-center' style='font-size:15px;margin:10px'>No asset found!</label>";
                $("#Asset" + siteId).empty().append(temp);
            }
            $('[data-toggle="tooltip"]').tooltip();
            fnDraggableAsset(siteId);
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnGetSearchAsset(id, search) {
    var siteId = id;
    $("#loader").show();
    $.ajax({
        url: '/Site/GetAllAssetById',
        type: 'Post',
        data: '{siteId:' + siteId + ',search:"' + search + '"}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            var temp = "";
            if (data != null && data.length > 0) {
                $.each(data, function (id, val) {
                    if (val.IsActive) {
                        temp += "<div class='col-lg-3 draggable' id='" + val.Id + "' >";
                        temp += "<div class='card shadow-lg'>";
                        temp += "<div class='card-header bg-primary'>";
                        temp += "<h6 class=''>" + val.AssetTypeName + "</h3>";
                        temp += "<div class='edit tooltips' data-placement='top' data-toggle='tooltip' data-original-title='Edit' data-Id=" + siteId + " onclick='GetAssetById(" + val.Id + ")'>";
                        temp += "<span class='fa fa-pencil'></span>";
                        temp += "</div>";
                        temp += "<div class='delete tooltips' data-placement='top' data-toggle='tooltip' data-original-title='Delete' data-assetname='" + val.Name + "' data-Id=" + siteId + " onclick='DeleteAssetById(" + val.Id + ",this)'>";
                        temp += "<span class='fa fa-times'></span>";
                        temp += "</div>";

                        temp += "</div>";

                        temp += "<div class='card-body' style='padding:15px;'>";
                        if (val.Name.length > 15)
                            temp += "<label class='tooltips' style='font-size:15px;padding:2px' data-placement='top' data-toggle='tooltip' data-original-title='" + val.Name + "'>" + val.Name.substring(0, 15) + '...' + "</label>";
                        else
                            temp += "<label style='font-size:15px;padding:2px'>" + val.Name + "</label>";

                        temp += "<span class='label label-success' style='font-size:11px;padding:5px;border-radius:10px;float:right'>Active</span>";
                        temp += "</div>";

                        temp += "</div>";
                        temp += "</div>";
                    } else {
                        temp += "<div class='col-lg-3 col-md-6' id='" + val.Id + "' >";
                        temp += "<div class='panel panel-danger' style='background-color: #f0f1f4;'>";
                        temp += "<div class='panel-heading'>";
                        temp += "<div class='edit tooltips' data-placement='top' data-toggle='tooltip' data-original-title='Edit' data-Id=" + siteId + " onclick='GetAssetById(" + val.Id + ")'>";
                        temp += "<span class='fa fa-pencil'></span>";
                        temp += "</div>";
                        temp += "<div class='delete tooltips' data-placement='top' data-toggle='tooltip' data-original-title='Delete' data-Id=" + siteId + " onclick='DeleteAssetById(" + val.Id + ",this)'>";
                        temp += "<span class='fa fa-times'></span>";
                        temp += "</div>";
                        temp += "<h3 class='panel-title'>" + val.AssetTypeName + "</h3>";
                        temp += "</div>";

                        temp += "<div class='panel-body'>";
                        if (val.Name.length > 15)
                            temp += "<label class='tooltips' style='font-size:15px;padding:2px' data-placement='top' data-toggle='tooltip' data-original-title='" + val.Name + "'>" + val.Name.substring(0, 15) + '...' + "</label>";
                        else
                            temp += "<label style='font-size:15px;padding:2px'>" + val.Name + "</label>";

                        temp += "<span class='label label-danger' style='font-size:11px;padding:5px;border-radius:10px;float:right'>Inactive</span>";
                        temp += "</div>";

                        temp += "</div>";
                        temp += "</div>";
                    }
                });
                $("#Asset" + siteId).empty().append(temp);
            } else {
                temp += "<label class='text-center' style='font-size:15px;margin:10px'>No asset found!</label>";
                $("#Asset" + siteId).empty().append(temp);
            }
            $('[data-toggle="tooltip"]').tooltip();
            fnDraggableAsset(siteId);
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnDraggableAsset(siteId) {
    $(".draggable")
        .draggable({
            revert: true,
            helper: "clone",
            revertDuration: 0,
            scroll: true
        })
        .droppable({
            drop: function (event, ui) {
                // Get drag & drop elements
                var oldId = $(this).attr('id');
                var newId = ui.draggable.attr("id");
                $.ajax({
                    type: "POST",
                    url: '/Site/UpdateAssetSequence',
                    contentType: "application/json; charset=utf-8",
                    data: '{oldAssetId:' + oldId + ',newAssetId:' + newId + '}',
                    success: function (response) {
                        fnGetAsset(siteId);
                    },
                    error: function (xhr, status, error) {
                        $("#imgLoading").hide();
                        $.gritter.add({
                            title: xhr.statusText,
                            text: 'Error occured.',
                            class_name: 'with-icon times-circle danger'
                        });
                    }
                });
            },
        });
}

function AddAsset(assetTypeId, siteId, name) {
    $("#loader").show();

    $.ajax({
        url: '/Site/AddAsset',
        type: 'Post',
        data: '{assetTypeId:' + assetTypeId + ',siteId:' + siteId + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveAsset").empty().append(data);
            $("#modal-add-asset").modal('show');
            $("#chkIsActive").attr("checked", "checked");
            $("#hdnName").val(name);
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnSaveAsset() {
    if (ValidationAsset()) {
        var siteId = $("#hdnSiteId1").val();
        var Asset = {};
        Asset.Id = $("#hdnAssetId").val();
        Asset.AssetTypeId = $("#hdnAssetTypeId1").val();
        Asset.SiteId = $("#hdnSiteId1").val();
        Asset.Name = $("#txtName").val();
        Asset.HttpMisLink = $("#txtHttpLink").val();
        if ($('#refVal').length) {
            Asset.ReferenceValue = $("#txtReferenceValue").val();
        }
        if ($('#refVal').length) {
            Asset.ReferenceValue = $("#txtReferenceValue").val();
        }

        if ($('#txtNumberOfCell').length) {
            Asset.NumberOfCell = $('#txtNumberOfCell').val();
        }
        if ($('#drpIPSTypeId').length) {
            Asset.IPSTypeId = $('#drpIPSTypeId').val();
        }

        if ($('#chkIsActive').is(":checked")) {
            Asset.IsActive = true;
        } else {
            Asset.IsActive = false;
        }

       

        if ($("#hdnName").val().toLowerCase().trim() == "signal".toLowerCase().trim()) {
            if ($('#chkIsTemperature').is(":checked")) {
                Asset.IsTemperature = true;
            } else {
                Asset.IsTemperature = false;
            }

            var AssetInfos = [];
            $("#tbodySignalAttr tr").each(function () {
                var assetInfos = {};
                assetInfos.Value = $(this).find("td").eq(2).children("input").val();
                assetInfos.Channels = $(this).find("td").eq(1).children("select").children("option:selected").val();
                assetInfos.AssetAttributeId = $(this).find("td").eq(2).children("input").attr("data-id");
                assetInfos.Multiplication = $(this).find("td").eq(3).children("input").val();
                assetInfos.MaxSafeValue = $(this).find("td").eq(4).children("input").val();
                assetInfos.AverageValue = $(this).find("td").eq(5).children("input").val();
                assetInfos.MinSafeValue = $(this).find("td").eq(6).children("input").val();
                assetInfos.MinFailValue = $(this).find("td").eq(7).children("input").val();
                assetInfos.ThresHold = $(this).find("td").eq(8).children("input").val();
                assetInfos.HttpPostName = $(this).find("td").eq(0).children("input").val();

                assetInfos.GateContectTypeId = $(this).find("td").eq(0).children("input[type=radio]:checked").val();
                assetInfos.AssetId = $("#hdnAssetId").val();
                AssetInfos.push(assetInfos);
            });

            var Alerts = [];
            $("#tbodyOperators tr").each(function () {
                var alert = {};
                alert.IsActive = $(this).find("td").eq(0).children("input").prop("checked");
                alert.AlertName = $(this).find("td").eq(1).children("input").val();
                alert.Id = $(this).find("td").eq(1).children("input").attr("data-id");
                alert.OperatorId = $(this).find("td").eq(2).children("select").children("option:selected").val();
                alert.AlertValue = $(this).find("td").eq(3).children("input").val();
                Alerts.push(alert);
            });
            Asset.mAlerts = [];
            Asset.mAlerts = Alerts;

            Asset.mAssetInfos = [];
            Asset.mAssetInfos = AssetInfos;

            var mAssetInfoDataloggers = [];
            $("#tbodyDataloggerSignalAttributes tr").each(function () {
                var assetInfoDatalogger = {};
                assetInfoDatalogger.Value = $(this).find("td").eq(2).children("input").val();
                assetInfoDatalogger.ContactType = $(this).find("td").eq(1).children("select").children("option:selected").val();
                assetInfoDatalogger.DataloggerAttributeId = $(this).find("td").eq(2).children("input").attr("data-id");
                assetInfoDatalogger.AssetId = $("#hdnAssetId").val();
                mAssetInfoDataloggers.push(assetInfoDatalogger);
            });

            Asset.mAssetInfoDataloggers = [];
            Asset.mAssetInfoDataloggers = mAssetInfoDataloggers;

            var mFRSAttributeRanges = [];

            $(".tbodyFRSAlert tr").each(function () {

                var mFRSAttributeRange = {};
                mFRSAttributeRange.AssetAttributeId = $(this).find("td").eq(0).children("input").val();
                mFRSAttributeRange.MaxSafeValue = $(this).find("td").eq(1).children("input").val();
                mFRSAttributeRange.AverageValue = $(this).find("td").eq(2).children("input").val();
                mFRSAttributeRange.MinSafeValue = $(this).find("td").eq(3).children("input").val();
                mFRSAttributeRange.MinFailValue = $(this).find("td").eq(4).children("input").val();

                if (mFRSAttributeRange.MaxSafeValue == '' && mFRSAttributeRange.AverageValue == '' && mFRSAttributeRange.MinSafeValue == '' && mFRSAttributeRange.MinFailValue == '') {

                } else {
                    mFRSAttributeRanges.push(mFRSAttributeRange);
                }

               
            });
            Asset.mFRSAttributeRanges = [];
            Asset.mFRSAttributeRanges = mFRSAttributeRanges;

            var Defaultconfig = [];
            $(".globalConfig").each(function () {
                var LastUpdateValue = {};
                LastUpdateValue.AssetId = $("#hdnAssetId").val();
                LastUpdateValue.AssetAttributeId = $(this).attr("data-attrId");
                LastUpdateValue.Value = $("#default" + LastUpdateValue.AssetAttributeId).val();
                Defaultconfig.push(LastUpdateValue)
            });

            Asset.mGlobalConfigs = [];
            Asset.mGlobalConfigs = Defaultconfig;


            var mAlertInfoActivations = [];
            $(".tbodyAlertInfoActivation tr").each(function () {
                var mAlertInfoActivation = {};
                mAlertInfoActivation.AlertInfoId = $(this).find("td").eq(1).children("input").val();

                if ($(this).find("td").eq(0).children("input").prop('checked') == true) {
                    mAlertInfoActivation.IsActive = true;

                } else {
                    mAlertInfoActivation.IsActive = false;
                }

                mAlertInfoActivations.push(mAlertInfoActivation);
            });
            Asset.mAlertInfoActivations = mAlertInfoActivations;

        } else if ($("#hdnName").val().toLowerCase().trim() == "point machine".toLowerCase().trim()) {
            var AssetInfos = [];
            $("#tbodyAttr tr").each(function () {
                var assetInfos = {};
                assetInfos.Value = $(this).find("td").eq(2).children("input").val();
                assetInfos.Channels = $(this).find("td").eq(1).children("select").children("option:selected").val();
                assetInfos.AssetAttributeId = $(this).find("td").eq(2).children("input").attr("data-id");
                assetInfos.HttpPostName = $(this).find("td").eq(0).children("input").val();
                assetInfos.Multiplication = $(this).find("td").eq(3).children("input").val();
                assetInfos.AssetId = $("#hdnAssetId").val();
                AssetInfos.push(assetInfos);
            });

            Asset.mAssetInfos = [];
            Asset.mAssetInfos = AssetInfos;

            var mAssetInfoDataloggers = [];
            $("#tbodyDataloggerPointAttributes tr").each(function () {
                var assetInfoDatalogger = {};
                assetInfoDatalogger.Value = $(this).find("td").eq(2).children("input").val();
                assetInfoDatalogger.ContactType = $(this).find("td").eq(1).children("select").children("option:selected").val();
                assetInfoDatalogger.DataloggerAttributeId = $(this).find("td").eq(2).children("input").attr("data-id");
                assetInfoDatalogger.AssetId = $("#hdnAssetId").val();
                mAssetInfoDataloggers.push(assetInfoDatalogger);
            });

            Asset.mAssetInfoDataloggers = [];
            Asset.mAssetInfoDataloggers = mAssetInfoDataloggers;

            var Alerts = [];
            $("#tbodyOperators tr").each(function () {
                var alert = {};
                alert.IsActive = $(this).find("td").eq(0).children("input").prop("checked");
                alert.AlertName = $(this).find("td").eq(1).children("input").val();
                alert.Id = $(this).find("td").eq(1).children("input").attr("data-id");
                alert.OperatorId = $(this).find("td").eq(2).children("select").children("option:selected").val();
                alert.AlertValue = $(this).find("td").eq(3).children("input").val();
                alert.AlertValueB = $(this).find("td").eq(4).children("input").val();

                Alerts.push(alert);
            });

            $("#tbodyVibrationOperators tr").each(function () {
                var alert = {};
                alert.IsActive = $(this).find("td").eq(0).children("input").prop("checked");
                alert.AlertName = $(this).find("td").eq(1).children("input").val();
                alert.Id = $(this).find("td").eq(1).children("input").attr("data-id");
                alert.OperatorId = $(this).find("td").eq(2).children("select").children("option:selected").val();
                alert.AlertValue = $(this).find("td").eq(3).children("input").val();
                alert.MaxValueA = $(this).find("td").eq(4).children("input").val();
                alert.AlertValueB = $(this).find("td").eq(5).children("input").val();
                alert.MaxValueB = $(this).find("td").eq(6).children("input").val();
                Alerts.push(alert);
            });

            Asset.mAlerts = [];
            Asset.mAlerts = Alerts;


            var mFRSAttributeRanges = [];
            $(".tbodyFRSAlert tr").each(function () {
                var mFRSAttributeRange = {};
                mFRSAttributeRange.AssetAttributeId = $(this).find("td").eq(0).children("input").val();
                mFRSAttributeRange.MaxSafeValue = $(this).find("td").eq(1).children("input").val();
                mFRSAttributeRange.AverageValue = $(this).find("td").eq(2).children("input").val();
                mFRSAttributeRange.MinSafeValue = $(this).find("td").eq(3).children("input").val();
                mFRSAttributeRange.MinFailValue = $(this).find("td").eq(4).children("input").val();
                //mFRSAttributeRanges.push(mFRSAttributeRange);

                if (mFRSAttributeRange.MaxSafeValue == '' && mFRSAttributeRange.AverageValue == '' && mFRSAttributeRange.MinSafeValue == '' && mFRSAttributeRange.MinFailValue == '') {

                } else {
                    mFRSAttributeRanges.push(mFRSAttributeRange);
                }
            });
            Asset.mFRSAttributeRanges = [];
            Asset.mFRSAttributeRanges = mFRSAttributeRanges;

            var mAlertInfoActivations = [];
            $(".tbodyAlertInfoActivation tr").each(function () {
                var mAlertInfoActivation = {};
                mAlertInfoActivation.AlertInfoId = $(this).find("td").eq(0).children("input").val();

                if ($(this).find("td").eq(1).children("input").prop('checked') == true) {
                    mAlertInfoActivation.IsActive = true;

                } else {
                    mAlertInfoActivation.IsActive = false;
                }
                mAlertInfoActivations.push(mAlertInfoActivation);
            });
            Asset.mAlertInfoActivations = mAlertInfoActivations;

            if ($('#chkHalfPointMachine').is(":checked")) {
                Asset.IsHalfPointMachine = true;
            } else {
                Asset.IsHalfPointMachine = false;
            }

            if ($('#chkIsThickWave').is(":checked")) {
                Asset.IsThickWave = true;
                Asset.ThickWaveTypeId = $('#ThickWaveTypeId').val();
            } else {
                Asset.IsThickWave = false;
                Asset.ThickWaveTypeId = null;
            }
            if ($('#chkIsSeriesOpration').is(":checked")) {
                Asset.IsSeriesOpration = true;
                Asset.SeriesOprationTypeId = $('#SeriesOprationTypeId').val();
                Asset.AliasDirectionA = $('#txtAliasDirectionA').val();
                Asset.AliasDirectionB = $('#txtAliasDirectionB').val();
            } else {
                Asset.IsSeriesOpration = false;
                Asset.SeriesOprationTypeId = null;
                Asset.AliasDirectionA = null;
                Asset.AliasDirectionB = null;
            }

            if ($('#chkISDisplacement').is(":checked")) {
                Asset.ISDisplacement = true;
                Asset.MachineA = $('#MachineA').val();
                Asset.MachineB = $('#MachineB').val();
            } else {
                Asset.ISDisplacement = false;
                Asset.MachineA = null;
                Asset.MachineB = null;
            }

        } else {

            var AssetInfos = [];
            $("#tbodyAttr tr").each(function () {
                var assetInfos = {};
                assetInfos.Value = $(this).find("td").eq(2).children("input").val();
                assetInfos.Channels = $(this).find("td").eq(1).children("select").children("option:selected").val();
                assetInfos.AssetAttributeId = $(this).find("td").eq(2).children("input").attr("data-id");
                assetInfos.Multiplication = $(this).find("td").eq(3).children("input").val();
                assetInfos.ThresHold = $(this).find("td").eq(4).children("input").val();
                assetInfos.HttpPostName = $(this).find("td").eq(0).children("input[type=text]").val();

                assetInfos.GateContectTypeId = $(this).find("td").eq(0).children("input[type=radio]:checked").val();
                assetInfos.AssetId = $("#hdnAssetId").val();
                AssetInfos.push(assetInfos);
            });

            Asset.mAssetInfos = [];
            Asset.mAssetInfos = AssetInfos;

            var mAssetInfoDataloggers = [];
            $("#tbodyDataloggerTrackAttributes tr").each(function () {
                var assetInfoDatalogger = {};
                assetInfoDatalogger.Value = $(this).find("td").eq(2).children("input").val();
                assetInfoDatalogger.ContactType = $(this).find("td").eq(1).children("select").children("option:selected").val();
                assetInfoDatalogger.DataloggerAttributeId = $(this).find("td").eq(2).children("input").attr("data-id");
                assetInfoDatalogger.AssetId = $("#hdnAssetId").val();
                mAssetInfoDataloggers.push(assetInfoDatalogger);
            });

            Asset.mAssetInfoDataloggers = [];
            Asset.mAssetInfoDataloggers = mAssetInfoDataloggers;


            var Alerts = [];
            $("#tbodyOperators tr").each(function () {
                var alert = {};
                alert.IsActive = $(this).find("td").eq(0).children("input").prop("checked");
                alert.AlertName = $(this).find("td").eq(1).children("input").val();
                alert.Id = $(this).find("td").eq(1).children("input").attr("data-id");
                alert.OperatorId = $(this).find("td").eq(2).children("select").children("option:selected").val();
                alert.AlertValue = $(this).find("td").eq(3).children("input").val();
                Alerts.push(alert);
            });

            Asset.mAlerts = [];
            Asset.mAlerts = Alerts;

            var FamilyTracks = [];
            $("#tbodyAssets tr").each(function () {
                var familyTrack = {};
                var val = $(this).data("id");
                familyTrack.Id = $(this).find("td").eq(0).find("div").children("select").children("option:selected").attr("data-familyId");
                familyTrack.TrackFamilyId = $(this).find("td").eq(0).find("div").children("select").children("option:selected").val();

                if ($('#chkIsAdjacent_' + val).prop('checked') && $('#AdjacentTypeId_' + val).val() != null && $('#AdjacentTypeId_' + val).val() != '' && $('#AdjacentTypeId_' + val).val() != '0') {
                    familyTrack.IsAdjacent = $('#chkIsAdjacent_' + val).prop('checked');
                    familyTrack.AdjacentTypeId = $('#AdjacentTypeId_' + val).val();
                }

                if (familyTrack.TrackFamilyId != null && familyTrack.TrackFamilyId != undefined && familyTrack.TrackFamilyId != '') {
                    FamilyTracks.push(familyTrack);
                }
            });

            Asset.mFamilyTracks = [];
            Asset.mFamilyTracks = FamilyTracks;

            var SignalTrackMappings = [];
            $("#tbodySignalTrackMapping tr").each(function () {
                if ($(this).find("td").eq(0).children("input").prop('checked') == true) {
                    var mSignalTrackMapping = {};
                    mSignalTrackMapping.SignalAssetId = $(this).find("td").eq(0).children("input").attr('data-assetId');
                    SignalTrackMappings.push(mSignalTrackMapping);
                } 
            });

            Asset.mSignalTrackMappings = [];
            Asset.mSignalTrackMappings = SignalTrackMappings;

            var mFRSAttributeRanges = [];
            $(".tbodyFRSAlert tr").each(function () {
                var mFRSAttributeRange = {};
                mFRSAttributeRange.AssetAttributeId = $(this).find("td").eq(0).children("input").val();
                mFRSAttributeRange.MaxSafeValue = $(this).find("td").eq(1).children("input").val();
                mFRSAttributeRange.AverageValue = $(this).find("td").eq(2).children("input").val();
                mFRSAttributeRange.MinSafeValue = $(this).find("td").eq(3).children("input").val();
                mFRSAttributeRange.MinFailValue = $(this).find("td").eq(4).children("input").val();

                if (mFRSAttributeRange.MaxSafeValue == '' && mFRSAttributeRange.AverageValue == '' && mFRSAttributeRange.MinSafeValue == '' && mFRSAttributeRange.MinFailValue == '') {

                } else {
                    mFRSAttributeRanges.push(mFRSAttributeRange);
                }
            });
            Asset.mFRSAttributeRanges = [];
            Asset.mFRSAttributeRanges = mFRSAttributeRanges;

            var mAlertInfoActivations = [];
            $(".tbodyAlertInfoActivation tr").each(function () {
                var mAlertInfoActivation = {};
                mAlertInfoActivation.AlertInfoId = $(this).find("td").eq(0).children("input").val();

                if ($(this).find("td").eq(1).children("input").prop('checked') == true) {
                    mAlertInfoActivation.IsActive = true;

                } else {
                    mAlertInfoActivation.IsActive = false;
                }
                mAlertInfoActivations.push(mAlertInfoActivation);
            });
            Asset.mAlertInfoActivations = mAlertInfoActivations;
        }

        if ($("#hdnName").val().toLowerCase().trim() == "AXLE COUNTER".toLowerCase().trim()) {

            var ErrorCodeIdentifier = {};
            ErrorCodeIdentifier.IdentifierName = $("#txtIdentifier").val();

            var errorCodes = [];
            $("#tblErrorcode tr").each(function () {
                var errorCode = {};
                errorCode.ErrorCodeWord = $.trim($(this).find("td").eq(0).html());
                errorCodes.push(errorCode);
            });

            Asset.mErrorCodeIdentifier = ErrorCodeIdentifier;
            Asset.mErrorCodes = [];
            Asset.mErrorCodes = errorCodes;

        }

        if ($("#hdnName").val().toLowerCase().trim() == "Earth Fault".toLowerCase().trim()) {

            var assetMapping = [];
            $('input[name="chkEarthFaultPointMachine"]').each(function () {
                var mAssetMapping = {};
                mAssetMapping.AssetId = $("#hdnAssetId").val();
                mAssetMapping.MapAssetId = $(this).val();
                mAssetMapping.AssetTypeId = 3;
                mAssetMapping.IsChecked = $(this).is(":checked");


                assetMapping.push(mAssetMapping);

            });

            $('input[name="chkEarthFaultTrack"]').each(function () {

                var mAssetMapping = {};
                mAssetMapping.AssetId = $("#hdnAssetId").val();
                mAssetMapping.MapAssetId = $(this).val();
                mAssetMapping.AssetTypeId = 1;
                mAssetMapping.IsChecked = $(this).is(":checked");


                assetMapping.push(mAssetMapping);

            });

            $('input[name="chkEarthFaultSignal"]').each(function () {

                var mAssetMapping = {};
                mAssetMapping.AssetId = $("#hdnAssetId").val();
                mAssetMapping.MapAssetId = $(this).val();
                mAssetMapping.AssetTypeId = 2;
                mAssetMapping.IsChecked = $(this).is(":checked");


                assetMapping.push(mAssetMapping);
            });
            Asset.mAssetMappings = [];

            Asset.mAssetMappings = assetMapping;
        }

        $("#loader").show();
        $.ajax({
            url: '/Site/SaveAsset',
            type: 'Post',
            data: '{mAsset:' + JSON.stringify(Asset) + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data != null && data.IsSuccess == true) {
                    CommonNotification("SUCCESS", data.Message, "success");
                    $("#modal-add-asset").modal('hide');
                    //$("[class*='modal-backdrop in']").remove();
                    fnGetAsset(siteId);
                } else {
                    CommonNotification("ERROR", data.Message, "error");
                }
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    }
}

function GetAssetById(id) {
    $("#loader").show();
    $.ajax({
        url: '/Site/GetAssetById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveAsset").empty().append(data);
            $("#modal-add-asset").modal('show');
            // $(".drpChannel").select2({ minimumResultsForSearch: Infinity, dropdownParent: $('#modal-add-asset'), width: '100%' });
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function DeleteAssetById(id, $this) {
    var siteId = $($this).attr("data-id");
    var assetName = $($this).attr("data-assetname");
    $.confirm({
        title: 'Confirm!',
        content: 'Are you sure to delete "' + assetName + '" asset?<p style="color:red;margin-top:20px">Note: If you delete "' + assetName + '" the related site attributes data deleted itself.</p>',
        buttons: {
            confirm: {
                btnClass: 'btn-blue',
                action: function () {
                    $("#loader").show();
                    $.ajax({
                        url: '/Site/DeleteAssetById',
                        type: 'Post',
                        data: '{id:' + id + ',siteId:' + siteId + '}',
                        contentType: 'application/json',
                        success: function (data) {
                            $("#loader").hide();
                            if (data.IsSuccess === true) {
                                CommonNotification("SUCCESS", "Asset has been deleted.", "success");
                                fnGetAsset(siteId);
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
            },
            cancel: function () {

            }
        }
    });
}

function ValidationAsset() {

    var result = true;
    //var assetNameRegex = /^(?!\d+$)(?![_\s]+$)[A-Za-z0-9\s_]+$/;
    var assetNameRegex = /^[a-zA-Z0-9-/-_ ]+$/;
    if ($.trim($("#txtName").val()).length == 0) {
        $("#txtName").next("span").html("Name is required");
        result = false;
    }
    else {
        if (!assetNameRegex.test($.trim($("#txtName").val()))) {
            $('#txtName').next('span').html('Invalid asset Name!');
            result = false;
        }
        else {
            $("#txtName").next("span").html("");
        }
    }

    return result;
}

function ClearAddSiteControls() {
    $("#hdnSiteId").val("0");
    $("#hdnDivisionId").val("0");
    $('#zoneAdd').val($('#zoneAdd option:first').val()).trigger('change');
    $('#drpDivision').val(0).change().empty();
    $("#devisionId").html("");
    $("#Name").val("");
    $("#Name").next("span").html("");
    $("#MQTTBasePath").val("");
    $("#MQTTBasePath").next("span").html("");
    $("#MQTTNoOfChannels").val("");
    $("#MQTTNoOfChannels").next("span").html("");
    $("#MQTTActiveChannel").val("");
    $("#MQTTActiveChannel").next("span").html("");
    $("#Description").val("");
    $("#Description").next("span").html("");
    $("#MACId").val("");
    $("#MACId").next("span").html("");
    $("#IsActive").attr("Checked", "Checked");

    $("#spHeader").empty().text("Add Site");
}

function GetAttributes() {
    var idList = [];

    $(".chkAttribute").each(function () {
        if ($(this).is(":checked")) {
            var id = $(this).attr('data-id');
            idList.push(id);
        }
    });
    //if (idList != null && idList.length > 0) {
    $("#loader").show();
    $.ajax({
        url: '/Site/GetAttribute',
        type: 'Post',
        data: '{attributes:' + JSON.stringify(idList) + ',assetId:' + $("#hdnAssetId").val() + ',assetTypeId:' + $("#hdnAssetTypeId1").val() + ',siteId:' + $("#hdnSiteId1").val() + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            var temp = "";
            var fRSTemp = "";
            $.each(data.assetAttributes, function (id, val) {
                temp += "<tr>";
                temp += "<td style='vertical-align:middle'>" + val.Title + "";
                if (val.IsDigital != null && val.IsDigital != undefined && val.IsDigital) {
                    temp += "<span> || </span>";
                    if (val.GateContectTypeId != null && val.GateContectTypeId == frontContectId) {
                        temp += "<input type='radio' value='" + frontContectId + "' id='frontContect" + val.Id + "' name='rdiGateContect" + val.Id + "' checked /><label class='m-l-5' for='frontContect" + val.Id + "'>Front</label>";
                    }
                    else {
                        temp += "<input type='radio' value='" + frontContectId + "' id='frontContect" + val.Id + "' name='rdiGateContect" + val.Id + "' /><label class='m-l-5' for='frontContect" + val.Id + "'>Front</label>";
                    }

                    if (val.GateContectTypeId != null && val.GateContectTypeId == backContectId) {
                        temp += "<input type='radio' class='m-l-5' value='" + backContectId + "' id='backContect" + val.Id + "' name='rdiGateContect" + val.Id + "' checked /><label class='m-l-5' for='backContect" + val.Id + "'>Back</label>";
                    }
                    else {
                        temp += "<input type='radio' class='m-l-5' value='" + backContectId + "' id='backContect" + val.Id + "' name='rdiGateContect" + val.Id + "' /><label class='m-l-5' for='backContect" + val.Id + "'>Back</label>";
                    }

                }
                temp += "<input type='text' class='form-control' value='" + $.trim(val.HttpPostName) + "' /></td>";
                temp += "<td style='vertical-align:middle'>";

                temp += "<select class='form-control drpChannel' id='channel" + val.Id + "'>";
                temp += "<option value=''>Select Channel</option>";
                for (var i = 1; i <= data.MQTTNoOfChannels; i++) {
                    var channel = "Channel" + i;
                    if (val.AssetInfoChannel != null && val.AssetInfoChannel == channel) {
                        temp += "<option value='Channel" + i + "' selected>Channel " + i + "</option>";
                    } else {
                        temp += "<option value='Channel" + i + "'>Channel " + i + "</option>";
                    }
                }
                temp += "</select>";

                temp += "</td>";
                if (val.AssetInfoValue != null) {
                    temp += "<td> <input type='text' data-id=" + val.Id + " value=" + $.trim(val.AssetInfoValue) + "  class='form-control signalValue' /></td>";
                }
                else {
                    if (val.Title != "Last Updated") {
                        temp += "<td> <input type='text' data-id=" + val.Id + "   class='form-control signalValue' value='0' /></td>";
                    } else {
                        temp += "<td> <input type='text' data-id=" + val.Id + "   class='form-control signalValue'/></td>";
                    }
                }

                if (val.Multiplication != null) {
                    temp += "<td> <input type='text' data-id=" + val.Id + " value=" + $.trim(val.Multiplication) + "   class='form-control signalmultiplication' /></td>";
                }
                else {
                    temp += "<td> <input type='text' data-id=" + val.Id + "  class='form-control signalmultiplication' /></td>";
                }
                temp += "<td style='display:none;'> </td>";
                temp += "</tr>";

            });

            $("#tbodySignalAttr").empty().append(temp);
            $("#tblSignal").css('display', 'block');
            $(".drpChannel").select2({ minimumResultsForSearch: Infinity, dropdownParent: $('#modal-add-asset'), width: '100%' });
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
    //} else {

    //  $("#tbodySignalAttr").empty();
    //  $("#tblSignal").css('display', 'none');
    // }
}

function GetAttributes1() {
    var idList = [];
    $(".chkAttribute").each(function () {
        if ($(this).is(":checked")) {
            var id = $(this).attr('data-id');
            idList.push(id);
        }
    });
    $("#loader").show();
    $.ajax({
        url: '/Site/GetAttribute',
        type: 'Post',
        data: '{attributes:' + JSON.stringify(idList) + ',assetId:' + $("#hdnAssetId").val() + ',assetTypeId:' + $("#hdnAssetTypeId1").val() + ',siteId:' + $("#hdnSiteId1").val() + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            var temp = "";
            $.each(data.assetAttributes, function (id, val) {
                temp += "<tr>";
                temp += "<td style='vertical-align:middle'>" + val.Title + "<input type='text' class='form-control' value='" + $.trim(val.HttpPostName) + "'  /></td>";
                temp += "<td style='vertical-align:middle'>";
                if (val.Title == 'Name' || val.Title == 'Widget#') {
                    temp += "<select class='form-control drpChannel' id='channel" + val.Id + "' disabled>";
                    temp += "<option value=''>Select Channel</option>";
                    temp += "</select>";
                } else {
                    temp += "<select class='form-control drpChannel' id='channel" + val.Id + "'>";
                    temp += "<option value=''>Select Channel</option>";
                    for (var i = 1; i <= data.MQTTNoOfChannels; i++) {
                        var channel = "Channel" + i;
                        if (val.AssetInfoChannel != null && val.AssetInfoChannel == channel) {
                            temp += "<option value='Channel" + i + "' selected>Channel " + i + "</option>";
                        } else {
                            temp += "<option value='Channel" + i + "'>Channel " + i + "</option>";
                        }
                    }
                    temp += "</select>";
                }
                temp += "</td>";
                if (val.AssetInfoValue != null) {
                    temp += "<td> <input type='text' data-id=" + val.Id + " value=" + $.trim(val.AssetInfoValue) + "  class='form-control signalValue' /></td>";
                }
                else {
                    temp += "<td> <input type='text' data-id=" + val.Id + "   class='form-control signalValue' /></td>";
                }
                if (val.Multiplication != null) {
                    temp += "<td> <input type='text' data-id=" + val.Id + " value=" + $.trim(val.Multiplication) + "  class='form-control multiplicationValue' /></td>";
                }
                else {
                    temp += "<td> <input type='text' data-id=" + val.Id + "   class='form-control multiplicationValue' /></td>";
                }
                temp += "<td style='display:none;'> </td>";
                temp += "</tr>";


            });
            $("#tbodyAttr").empty().append(temp);
            $(".drpChannel").select2({ minimumResultsForSearch: Infinity, dropdownParent: $('#modal-add-asset'), width: '100%' });
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
            url: '/Site/GetDivisionByZoneId',
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

function GetRootAttributes() {

}

function fnAddFrsAlert(alertInfoId, assetTypeId) {

    $("#loader").show();
    $.ajax({
        url: '/Site/GetAllAssetAttributeBy',
        type: 'Post',
        data: '{assetTypeId:' + assetTypeId + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();

            var drpval = '<option value="0">Select</option>';
            $.each(data, function (key, value) {
                drpval += '<option value="' + value.Id + '">' + value.Title + '</option>';

            });
            var tbl = '<tr>';
            tbl += '<td style="display:none">' + alertInfoId + '</td>';
            tbl += '<td><select class="form-control">' + drpval + '</select></td>';
            tbl += '<td><input type="number" class="form-control" /></td>';
            tbl += '<td><input type="number" class="form-control" /></td>';
            tbl += '<td><input type="number" class="form-control" /></td>';
            tbl += '<td><input type="number" class="form-control" /></td>';
            tbl += '<td><input type="number" class="form-control" /></td>';
            $('.clsFRSAlert' + alertInfoId).prepend(tbl);
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });

}

