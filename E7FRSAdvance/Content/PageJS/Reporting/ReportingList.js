$(document).ready(function () {

    if ($("#radioinline1").is(":checked")) {
        $("#byasset").addClass("col-sm-12");
        $("#byasset").removeClass("col-sm-8");
        $(".attribute-check").prop("checked", false);
        $("#byAttribute").hide();
    }

    if ($("#radioinline2").is(":checked")) {
        $("#byasset").removeClass("col-sm-12");
        $("#byasset").addClass("col-sm-8");
        $("#byAttribute").show();
    }

    $("#radioinline1").click(function () {
        $("#byasset").addClass("col-sm-12");
        $("#byasset").removeClass("col-sm-8");
        $("#byAttribute").hide();
        $(".attribute-check").prop("checked", false);
    });

    $("#radioinline2").click(function () {
        $("#byasset").removeClass("col-sm-12");
        $("#byasset").addClass("col-sm-8");
        $("#byAttribute").show();
    });

    $('#datepickerStart').datepicker({
        dateFormat: 'mm/dd/yy',
        autoclose: true,
        onSelect: function (newText) {
            if (newText == $('#datepickerEnd').val()) {
                $("#fTime,#tTime").show();
            }
            else {
                $("#fTime,#tTime").hide();
                $("#tpFrom").val(''); $("#tpTo").val('');
            }
        },
    }).datepicker("setDate", new Date());
    $('#datepickerEnd').datepicker({
        dateFormat: 'mm/dd/yy',
        autoclose: true,
        onSelect: function (newText) {
            if (newText == $('#datepickerStart').val()) {
                $("#fTime,#tTime").show();
            }
            else {
                $("#fTime,#tTime").hide();
                $("#tpTo").val(''); $("#tpFrom").val('');
            }
        },

    }).datepicker("setDate", new Date());

    //$("#tpFrom").clockpicker({ 'timeFormat': 'H:i'});
   // $("#tpTo").clockpicker({ 'timeFormat': 'H:i' });

  
   
    $('#tpFrom').clockpicker({
        placement: 'bottom',
        align: 'right',
        autoclose: true,
        'default': '00:00'
    });

    $('#tpTo').clockpicker({
        placement: 'bottom',
        align: 'right',
        autoclose: true,
        'default': '00:00'
    });
    

    $('[data-toggle="tooltip"]').tooltip();

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

    $(".sites").click(function () {
        if ($(this).is(":checked")) {
            var isAllChecked = 0;

            $(".sites").each(function () {
                if (!this.checked)
                    isAllChecked = 1;
            });

            if (isAllChecked == 0) {
                $("#checkAllSite").prop("checked", true);
            }
        }
        else {
            $("#checkAllSite").prop("checked", false);
        }
    });

    $(".assets").click(function () {

        if ($(this).is(":checked")) {
            var isAllChecked = 0;

            $(".assets").each(function () {
                if (!this.checked)
                    isAllChecked = 1;
            });

            if (isAllChecked == 0) {
                $("#checkAllAsset").prop("checked", true);
            }
        }
        else {
            $("#checkAllAsset").prop("checked", false);
        }

        fnGetAssetAttributesByAssetId();

    });

    $("#checkAllAsset").click(function () {
        $('.assets').not(this).prop('checked', this.checked);
        fnGetAssetAttributesByAssetId();
    });

});

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

function GetAllAsset(id) {

    var idList = [];

    //$(".sites").each(function () {
    //    if ($(this).is(":checked")) {
    //        var id = $(this).attr('data-id');
    //        idList.push(id);
    //    }
    //});
    idList.push($(id).val());
    $("#loader").show();
    if (idList.length > 0) {
        $.ajax({
            url: '/Reporting/GetAllAssetTypeBySiteId',
            type: 'Post',
            data: '{siteIds:' + JSON.stringify(idList) + '}',
            dataType: 'html',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                //$("#divAssetDetails").show();
                //$("#divAssetDetails").empty().append(data);

                $("#divAssetTypeDetails").show();
                $("#divAssetTypeDetails").empty().append(data);
                $("#divAssetDetails").empty().hide();

                //$("#modal-add-division").modal('show');
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    } else {
        $("#loader").hide();
        $("#divAssetTypeDetails").empty().hide();
        $("#divAssetDetails").empty().hide();
    }
}

function GetAllAssetAll($this) {

    if ($($this).is(":checked")) {
        $('.sites').prop('checked', true);
    } else {
        $('.sites').prop('checked', false);
    }

    var idList = [];

    $(".sites").each(function () {
        if ($(this).is(":checked")) {
            var id = $(this).attr('data-id');
            idList.push(id);
        }
    });
    $("#loader").show();
    if (idList.length > 0) {
        $.ajax({
            url: '/Reporting/GetAllAssetTypeBySiteId',
            type: 'Post',
            data: '{siteIds:' + JSON.stringify(idList) + '}',
            dataType: 'html',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                //$("#divAssetDetails").show();
                //$("#divAssetDetails").empty().append(data);

                $("#divAssetTypeDetails").show();
                $("#divAssetTypeDetails").empty().append(data);
                $("#divAssetDetails").empty().hide();

                //$("#modal-add-division").modal('show');
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    } else {
        $("#loader").hide();
        $("#divAssetTypeDetails").empty().hide();
        $("#divAssetDetails").empty().hide();
    }
}

function SearchSite(assetTypeId) {

    var idList = [];

    //$(".sites").each(function () {
    //    if ($(this).is(":checked")) {
    //        var id = $(this).attr('data-id');
    //        idList.push(id);
    //    }
    //});
    idList.push($("#siteLog").val());
    $("#loader").show();
    if (idList.length > 0) {
        $.ajax({
            url: '/Reporting/GetAllAssetBySiteId',
            type: 'Post',
            data: '{siteIds:' + JSON.stringify(idList) + ',assetTypeId:' + assetTypeId + '}',
            dataType: 'html',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                $("#divAssetDetails").show();
                $("#divAssetDetails").empty().append(data);
                //$("#modal-add-division").modal('show');
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    } else {
        $("#loader").hide();
        $("#divAssetDetails").empty().hide();
    }
}

function GenerateReport(btnValue) {

    if (DateValidation()) {
        var idList = [];
        $(".assets").each(function (index) {
            if ($(this).is(":checked")) {
                var assets = {};
                assets.StartDate = $("#datepickerStart").val();
                assets.EndDate = $("#datepickerEnd").val();
                assets.Id = $(this).attr('data-id');
                assets.SiteId = $(this).attr('data-siteid');
                assets.AttributeId = $(this).attr('data-attributeid');
                assets.StartTime = $("#tpFrom").val();
                assets.EndTime = $("#tpTo").val();
                assets.IsReadHttpPost = false;
                assets.IsLocal = false;
                assets.DataBaseName = $("#selDatabase").val();
                if ($("#selDatabase").val() == "HTTP") {
                    assets.IsReadHttpPost = true;
                }
                if ($("#selDatabase").val() == "Local") {
                    assets.IsLocal = true;
                }
                //if ($("#chkSite" + assets.SiteId).attr("data-httpPost") == "True") {
                //    assets.IsReadHttpPost = true;
                //}

                //if (index == 0) {
                assets.assetAttributes = [];
                $(".attribute-check").each(function (ix, str) {
                    var checked = $(str).is(":checked");
                    if (checked) {
                        var attr = {};
                        attr.Id = $(str).attr("data-attId");
                        assets.assetAttributes.push(attr);
                    }
                });
               // }
                idList.push(assets);
            }

        });
        
        $("#loader").show();
        if (idList.length > 0) {
            $.ajax({
                url: '/Reporting/Report',
                type: 'Post',
                data: '{mAssets:' + JSON.stringify(idList) + ',btnValue:"' + btnValue + '"}',
                dataType: 'html',
                contentType: 'application/json',
                success: function (data) {
                    if (btnValue == "CSV") {
                        window.location.href = "/Reporting/DownloadReportCsv";
                    } else if (data != null && data != undefined && data != "" && data == "true") {
                        window.open('/Reporting/SiteReportDetails', '_blank');
                    }
                    $("#loader").hide();
                },
                error: function (response) {
                    $("#loader").hide();
                    CommonNotification("ERROR", "Something went wrong!", "Error");
                }
            });
        } else {
            $("#loader").hide();
            $("#dateValidate").html("Please select attributes!");
        }
    }
}

function DateValidation() {
    var result = true;
   
    var startDate = new Date($("#datepickerStart").val());
    var endDate = new Date($("#datepickerEnd").val());

    if ($.trim(startDate).length == 0) {
        $("#dateValidate").html("Please select start date!");
        result = false;
    } else if ($.trim(endDate).length == 0) {
        $("#dateValidate").html("Please select end date!");
        result = false;
    } else if (FindDiffBetweenTwoDates(startDate, endDate) < 0) {
        $("#dateValidate").html("Please select valid date!");
        result = false;
    }
    else {
        $("#dateValidate").html("");
    }
    return result;
}

function FindDiffBetweenTwoDates(firstDate, secondDate) {
    days = (secondDate - firstDate) / (1000 * 60 * 60 * 24);
    var result = (Math.round(days));
    return result;
}
function fnLogDataBase($this, db) {
    $("#selDatabase").val(db);
    $(".db1").removeClass("active");
    $($this).addClass("active");

}

function fnGetAssetAttributesByAssetId() {
    var assetId = [];
    var assetTypeId = 0;
    $(".assets").each(function () {

        if ($(this).is(":checked")) {
            assetId.push($(this).attr("data-id"));
            assetTypeId = $(this).attr("data-assetTypeId");
        }
    });


    var idList = [];
    idList.push($("#siteLog").val());
    $("#loader").show();
    if (idList.length > 0 && assetId.length>0) {
        $.ajax({
            url: '/Reporting/GetAssetAttributesByAssetId',
            type: 'Post',
            data: '{siteIds:' + JSON.stringify(idList) + ',assetTypeId:' + assetTypeId + ',assetIds:' + JSON.stringify(assetId)+'}',
            dataType: 'html',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                $("#divAttributes").empty().append(data);
            },
            error: function (response) {
                $("#loader").hide();
                CommonNotification("ERROR", "Something went wrong!", "Error");
            }
        });
    } else {
        $("#loader").hide();
        $("#divAttributes").empty();
    }

}