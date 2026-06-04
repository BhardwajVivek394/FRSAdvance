function SectionRefreshed() {
    SetSortDirectionOnLoad();
}

function SetSortDirectionOnLoad() {
    var columnName = $("#hdnColumnName").val();
    var sortDirection = $("#hdnSortDirection").val();
    var sortDirectionClass;
    if (sortDirection === "ASC") {
        sortDirectionClass = 'sorting_desc';
    }
    else if (sortDirection === "DESC") {
        sortDirectionClass = 'sorting_asc';
    }
    var thSortingClass = document.querySelectorAll('.sorting');
    for (var i = 0; i < thSortingClass.length; i++) {
        var sortElement = thSortingClass[i].id;
        if (sortElement === "th" + columnName) {
            $("#th" + columnName).removeClass('sorting');
            $("#th" + columnName).addClass(sortDirectionClass);
        }
    }
}