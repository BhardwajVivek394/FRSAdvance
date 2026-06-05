$(document).ready(function () {
    //$('#drpPageSize').select2({ minimumResultsForSearch: Infinity });

    $('#drpPageSize').change(function (e) {
        $("#hdnSkip").val('0');
        $("#frmGetAssetTypeList").submit();
    });

    $("#btnAddAssetType").click(function () {
        ClearAddAssetTypeControls();
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
                $("#frmGetAssetTypeList").submit();
            }
            else {
                $("#hdnTake").val(parseInt($("#drpPageSize").val()));
                $("#frmGetAssetTypeList").submit();
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
    $("#frmGetAssetTypeList").submit();
}

function Search() {
    $("#hdnSkip").val('0');
    $("#frmGetAssetTypeList").submit();
}

function Clear() {
    $("#hdnSkip").val('0');
    $("#txtSearchName").val("");
    $("#frmGetAssetTypeList").submit();
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
    $("#frmGetAssetTypeList").submit();
}

function GetAssetTypeById(id) {
    $("#loader").show();
    $.ajax({
        url: '/AssetType/GetAssetTypeById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveAssetType").empty().append(data);
            $("#modal-add-assetType").modal('show');
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function DeleteAssetTypeById(id) {
    if (confirm('Are you sure you want to delete this record?')) {
        $("#loader").show();
        $.ajax({
            url: '/AssetType/DeleteAssetTypeById',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data.IsSuccess === true) {
                    CommonNotification("SUCCESS", "Asset has been deleted.", "success");
                    $("#frmGetAssetTypeList").submit();
                } else if (data.IsSuccess === false) {
                    if (data.Message.includes("Could not delete this record")) {
                        CommonNotification("WARNING", data.Message, "warning");
                    } else if (data.Message == "Internal server error.") {
                        CommonNotification("ERROR", "Error occured while deleting asset!", "error");
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

function GetAssestAttributes(id, $this) {
    var isAlert = $($this).attr("data-alert");
    if ($($this).hasClass("fa-plus")) {
        $($this).removeClass("fa-plus").addClass("fa-minus");
        var html = "";
        html += "<td colspan='10'>";
        html += "<div class='card panel-inverse' style='padding:10px;'>"
        html += "<div class='card-heading'>"
        html += "<h3 class='card-title'>Assest Attributes / Info</h3>"
        html += "</div>"
        html += "<div class='card-body'>"
        html += "<div class='row'>"
        html += "<div class='col-lg-3 col-sm-6 col-xs-6 pull-right' style='margin-bottom:14px'>"
        html += "<a class='btn btn-primary' id='btnAddAttributes" + id + "' data-Id=" + id + " onclick='AddAttributes(" + id + ",\"" + isAlert + "\")' style='width:100%'>"
        html += "Add Attributes / Info"
        html += "</a>"
        html += "</div>"
        html += "</div>"
        html += "<div class='table-responsive'>"
        html += "<table class='table table-bordered'>"
        html += "<thead class='table-dark'>"
        html += "<tr>"
        html += "<th colspan='2' class='text-center'>Action</th>"
        html += "<th>Name</th>"
        html += "<th>Alias Name</th>"
        html += "<th>Min</th>"
        html += "<th>Max</th>"
        html += "<th class='text-center'>Is Digital</th>"
        if (isAlert == "True") {
            html += "<th>Min Threshold</th>";
            html += "<th>Max Threshold</th>";
            html += "<th>ZeroOffset</th>";
        }
        html += "</tr>"
        html += "</thead>"
        html += "<tbody style='background:#f0f1f4' id='attributes" + id + "'>"
        html += "</tbody>"
        html += "</table>";
        html += "</div>";
        html += "</div>";
        html += "</td>";
        $("#trAssestTypes" + id).empty().append(html);
        $("#trAssestTypes" + id).show();
        fnGetAttributesById(id, isAlert);
    } else {
        $($this).removeClass("fa-minus").addClass("fa-plus");
        $("#trAssestTypes" + id).hide();
        $("#trAssestTypes" + id).empty();
    }
}

function fnGetAttributesById(id, isAlert) {
    var assestId = id;
    $("#loader").show();
    $.ajax({
        url: '/AssetType/GetAttributesByAssestId',
        type: 'Post',
        data: '{id:' + id + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            var temp = "";
            if (data != null && data.length > 0) {
                $.each(data, function (id, val) {
                    temp += "<tr>";
                    temp += "<td class='text-center width60'>";
                    temp += "<a href='javascript:void(0)' data-placement='top' data-toggle='tooltip' class='tooltips' data-original-title='Edit'><i class='fa fa-edit text-vision' onclick='GetAttributesById(" + val.Id + ",\"" + isAlert + "\")'></i></a>";
                    temp += "</td>";
                    temp += "<td class='text-center width60'>";
                    temp += "<a href='javascript:void(0)' data-placement='top' data-toggle='tooltip' class='tooltips' data-original-title='Delete'><i class='fa fa-trash text-danger' id='DeleteAttributes" + val.Id + "' data-Id=" + assestId + " onclick='DeleteAttributesById(" + val.Id + ")'></i></a>";
                    temp += "</td>";
                    temp += "<td>";
                    temp += "" + val.Title + "";
                    temp += "</td>";
                    temp += "<td>";
                    if (val.AliasName != null && val.AliasName != undefined && val.AliasName != '') {
                        temp += "" + val.AliasName + "";
                    }
                    temp += "</td>";
                    temp += "<td>";
                    temp += "" + $.trim( val.MinValue) + "";
                    temp += "</td>";
                    temp += "<td>";
                    temp += "" + $.trim(val.MaxValue )+ "";
                    temp += "</td>";
                    temp += "<td>";
                    if (val.IsDigital != null && val.IsDigital != undefined && val.IsDigital) {
                        temp += "<input type='checkbox' value='checkbox' id='CheckboxGroup1_1' checked>";
                    }
                    temp += "</td>";

                  
                    if (isAlert == "True") {
                        temp += "<td>";
                        temp += "" + $.trim(val.MinThreshold) + "";
                        temp += "</td>";
                        temp += "<td>";
                        temp += "" + $.trim(val.MaxThreshold) + "";
                        temp += "</td>";
                        temp += "<td>";
                        temp += "" + $.trim(val.ZeroOffset) + "";
                        temp += "</td>";
                    }
                    temp += "</tr>";
                });
                $("#attributes" + id).empty().append(temp);
            } else {
                temp += "<tr>";
                temp += "<td colspan='9' class='text-center'>";
                temp += "No record found!";
                temp += "</td>";
                temp += "</tr>";
                $("#attributes" + id).empty().append(temp);
            }
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function AddAttributes(id, isAlert) {
    $("#loader").show();
    $.ajax({
        url: '/AssetType/AddAttributes',
        type: 'Post',
        data: '{id:' + id + '}',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveAttributes").empty().append(data);
            $("#modal-add-attribute").modal('show');
            $("#hdnAssetAttributeId").val("0");
            $("#hdnalert").val(isAlert);
            if (isAlert == "True") {
                $(".min-th,.max-th,.zero-th").show();
            } else {
                $(".min-th,.max-th,.zero-th").hide();
            }
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function fnSaveAttributes() {
    if (ValidationAttributes()) {
        var id = $("#hdnAssetTypeId1").val();
        var AssetAttribute = {};
        AssetAttribute.Id = $("#hdnAssetAttributeId").val();
        AssetAttribute.AssetTypeId = $("#hdnAssetTypeId1").val();
        AssetAttribute.Title = $("#Title").val();
        AssetAttribute.IsInfo = $("#IsInfo").is(':checked');
        AssetAttribute.MinValue = $("#MinValue").val();
        AssetAttribute.MaxValue = $("#MaxValue").val();
        AssetAttribute.MinThreshold = $("#MinThreshold").val();
        AssetAttribute.MaxThreshold = $("#MaxThreshold").val();
        AssetAttribute.ZeroOffset = $("#ZeroOffset").val();
        AssetAttribute.DefaultMultiplication = $("#DefaultMultiplication").val();
        AssetAttribute.PannelTestValue = $("#PannelTestValue").val();
        AssetAttribute.AliasName = $("#AliasName").val();
        AssetAttribute.Sequence = $("#Sequence").val();
        AssetAttribute.ValueType = $("#drpValueType").val();
        AssetAttribute.SensorType = $("#drpSensorType").val();
        AssetAttribute.RepresentationCode = $("#drpRepresentationCode").val();
        AssetAttribute.Type = $("#drpAttributeType").val();
        AssetAttribute.ParameterRepresentationCode = $("#drpParameterRepresentationCode").val();

        AssetAttribute.Mode = $("#txtMode").val();
        AssetAttribute.ThresholdValue = $("#txtThresholdValue").val();
        AssetAttribute.Absolute = $("#txtAbsolute").val();
        if ($("#IsDigital").prop('checked') == true) {
            AssetAttribute.IsDigital = true;
        } else {
            AssetAttribute.IsDigital = false;
        }
        if ($("#IsDerived").prop('checked') == true) {
            AssetAttribute.IsDerived = true;
        } else {
            AssetAttribute.IsDerived = false;
        }
        $("#loader").show();
        $.ajax({
            url: '/AssetType/SaveAttributes',
            type: 'Post',
            data: '{mAssetAttribute:' + JSON.stringify(AssetAttribute) + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data != null && data.IsSuccess == true) {
                    CommonNotification("SUCCESS", data.Message, "success");
                    $("#modal-add-attribute").modal('hide');
                    //$("[class*='modal-backdrop in']").remove();
                    fnGetAttributesById(id, $("#hdnalert").val());
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

function ValidationAttributes() {
    var result = true;

    if ($.trim($("#Title").val()).length == 0) {
        $("#Title").next("span").html("Title is required");
        result = false;
    }
    else {
        $("#Title").next("span").html("");
    }

    return result;
}

function GetAttributesById(id, isAlert) {
    $("#loader").show();
    $.ajax({
        url: '/AssetType/GetAttributesById',
        type: 'Post',
        data: '{id:' + id + '}',
        dataType: 'html',
        contentType: 'application/json',
        success: function (data) {
            $("#loader").hide();
            $("#divSaveAttributes").empty().append(data);
            $("#modal-add-attribute").modal('show');
            $("#hdnalert").val(isAlert);
            if (isAlert == "True") {
                $(".min-th,.max-th,.zero-th").show();
            } else {
                $(".min-th,.max-th,.zero-th").hide();
            }
        },
        error: function (response) {
            $("#loader").hide();
            CommonNotification("ERROR", "Something went wrong!", "Error");
        }
    });
}

function DeleteAttributesById(id) {
    if (confirm('Are you sure you want to delete this record?')) {
        var assetId = $("#DeleteAttributes" + id).attr("data-id");
        $("#loader").show();
        $.ajax({
            url: '/AssetType/DeleteAttributesById',
            type: 'Post',
            data: '{id:' + id + '}',
            contentType: 'application/json',
            success: function (data) {
                $("#loader").hide();
                if (data.IsSuccess === true) {
                    CommonNotification("SUCCESS", "Asset has been deleted.", "success");
                    fnGetAttributesById(assetId);
                } else if (data.IsSuccess === false) {
                    if (data.Message == "Could not delete this record because it's reference to SiteAttributeData table.") {
                        CommonNotification("WARNING", data.Message, "warning");
                    } else if (data.Message == "Internal server error.") {
                        CommonNotification("ERROR", "Error occured while deleting asset!", "error");
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

function ClearAddAssetTypeControls() {
    $("#hdnAssetTypeId").val("0");
    $("#Name").val("");
    $("#Name").next("span").html("");
    $("#IsActive").attr("Checked", "Checked");
    $("#IsListView").prop("checked",false);
    $("#IsDualGPS").attr("Checked", false);
    $("#spHeader").empty().text("Add Asset Type");
}

function GetSensorTypeById($this, id) {
    if ($this != null && $this != undefined) {
        var sensorType = $($this).val();
        if (sensorType != null && sensorType != undefined && sensorType != '') {
            $("#loader").show();
            $.ajax({
                url: '/AssetType/GetSensorType',
                type: 'Post',
                data: '{sensorType:' + sensorType + '}',
                contentType: 'application/json',
                success: function (data) {
                    $("#loader").hide();
                    if (data != null && data.length > 0) {
                        $("#loader").hide();
                        $("#drpSensorType").empty();
                        $("#drpSensorType").append($("<option></option>").val('0').html('Select'));
                        $.each(data, function (key, value) {
                            var option = $("<option></option>")
                                .val(value.Id)
                                .html(value.Name);

                            if (id != null && id !== undefined && id !== '' && value.Id == id) {
                                option.attr("selected", "selected");
                            }

                            $("#drpSensorType").append(option);
                            
                        });
                    }
                },
                error: function (response) {
                    $("#loader").hide();
                    CommonNotification("ERROR", "Something went wrong!", "Error");
                }
            });
        }
       
    }

}