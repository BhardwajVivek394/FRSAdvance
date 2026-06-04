"use strict";


// DropzoneJS
if (window.Dropzone) {
  Dropzone.autoDiscover = false;
}

// Basic confirm box
$('[data-confirm]').each(function () {
  var me = $(this),
    me_data = me.data('confirm');

  me_data = me_data.split("|");
  me.fireModal({
    title: me_data[0],
    body: me_data[1],
    buttons: [
      {
        text: me.data('confirm-text-yes') || 'Yes',
        class: 'btn btn-danger btn-shadow',
        handler: function () {
          eval(me.data('confirm-yes'));
        }
      },
      {
        text: me.data('confirm-text-cancel') || 'Cancel',
        class: 'btn btn-secondary',
        handler: function (modal) {
          $.destroyModal(modal);
          eval(me.data('confirm-no'));
        }
      }
    ]
  })
});

// Global
$(function () {
  let sidebar_nicescroll_opts = {
    cursoropacitymin: 0,
    cursoropacitymax: .8,
    zindex: 892
  }, now_layout_class = null;

  var sidebar_sticky = function () {
    if ($("body").hasClass('layout-2')) {
      $("body.layout-2 #sidebar-wrapper").stick_in_parent({
        parent: $('body')
      });
      $("body.layout-2 #sidebar-wrapper").stick_in_parent({ recalc_every: 1 });
    }
  }
  sidebar_sticky();

  var sidebar_nicescroll;
  var update_sidebar_nicescroll = function () {
    let a = setInterval(function () {
      if (sidebar_nicescroll != null)
        sidebar_nicescroll.resize();
    }, 10);

    setTimeout(function () {
      clearInterval(a);
    }, 600);
  }

  var sidebar_dropdown = function () {
    if ($(".main-sidebar").length) {
      $(".main-sidebar").niceScroll(sidebar_nicescroll_opts);
      sidebar_nicescroll = $(".main-sidebar").getNiceScroll();

      $(".main-sidebar .sidebar-menu li a.has-dropdown").off('click').on('click', function () {
        var me = $(this);

        me.parent().find('> .dropdown-menu').slideToggle(500, function () {
          update_sidebar_nicescroll();
          return false;
        });
        return false;
      });
    }
  }
  sidebar_dropdown();

  if ($("#top-5-scroll").length) {
    $("#top-5-scroll").css({
      height: 315
    }).niceScroll();
  }

  $(".main-content").css({
    minHeight: $(window).outerHeight() - 95
  })

  $(".nav-collapse-toggle").click(function () {
    $(this).parent().find('.navbar-nav').toggleClass('show');
    return false;
  });

  $(document).on('click', function (e) {
    $(".nav-collapse .navbar-nav").removeClass('show');
  });

  var toggle_sidebar_mini = function (mini) {
    let body = $('body');

    if (!mini) {
      body.removeClass('sidebar-mini');
      $(".main-sidebar").css({
        overflow: 'hidden'
      });
      setTimeout(function () {
        $(".main-sidebar").niceScroll(sidebar_nicescroll_opts);
        sidebar_nicescroll = $(".main-sidebar").getNiceScroll();
      }, 500);
      $(".main-sidebar .sidebar-menu > li > ul .dropdown-title").remove();
      $(".main-sidebar .sidebar-menu > li > a").removeAttr('data-toggle');
      $(".main-sidebar .sidebar-menu > li > a").removeAttr('data-original-title');
      $(".main-sidebar .sidebar-menu > li > a").removeAttr('title');
    } else {
      body.addClass('sidebar-mini');
      body.removeClass('sidebar-show');
      sidebar_nicescroll.remove();
      sidebar_nicescroll = null;
      $(".main-sidebar .sidebar-menu > li").each(function () {
        let me = $(this);

        if (me.find('> .dropdown-menu').length) {
          me.find('> .dropdown-menu').hide();
          me.find('> .dropdown-menu').prepend('<li class="dropdown-title pt-3">' + me.find('> a').text() + '</li>');
        } else {
          me.find('> a').attr('data-toggle', 'tooltip');
          me.find('> a').attr('data-original-title', me.find('> a').text());
          $("[data-toggle='tooltip']").tooltip({
            placement: 'right'
          });
        }
      });
    }
  }

  $("[data-toggle='sidebar']").click(function () {
    var body = $("body"),
      w = $(window);

    if (w.outerWidth() <= 1024) {
      body.removeClass('search-show search-gone');
      if (body.hasClass('sidebar-gone')) {
        body.removeClass('sidebar-gone');
        body.addClass('sidebar-show');
      } else {
        body.addClass('sidebar-gone');
        body.removeClass('sidebar-show');
      }

      update_sidebar_nicescroll();
    } else {
      body.removeClass('search-show search-gone');
      if (body.hasClass('sidebar-mini')) {
        toggle_sidebar_mini(false);
      } else {
        toggle_sidebar_mini(true);
      }
    }

    return false;
  });

  var toggleLayout = function () {
    var w = $(window),
      layout_class = $('body').attr('class') || '',
      layout_classes = (layout_class.trim().length > 0 ? layout_class.split(' ') : '');

    if (layout_classes.length > 0) {
      layout_classes.forEach(function (item) {
        if (item.indexOf('layout-') != -1) {
          now_layout_class = item;
        }
      });
    }

    if (w.outerWidth() <= 1024) {
      if ($('body').hasClass('sidebar-mini')) {
        toggle_sidebar_mini(false);
        $('.main-sidebar').niceScroll(sidebar_nicescroll_opts);
        sidebar_nicescroll = $(".main-sidebar").getNiceScroll();
      }

      $("body").addClass("sidebar-gone");
      $("body").removeClass("layout-2 layout-3 sidebar-mini sidebar-show");
      $("body").off('click').on('click', function (e) {
        if ($(e.target).hasClass('sidebar-show') || $(e.target).hasClass('search-show')) {
          $("body").removeClass("sidebar-show");
          $("body").addClass("sidebar-gone");
          $("body").removeClass("search-show");

          update_sidebar_nicescroll();
        }
      });

      update_sidebar_nicescroll();

      if (now_layout_class == 'layout-3') {
        let nav_second_classes = $(".navbar-secondary").attr('class'),
          nav_second = $(".navbar-secondary");

        nav_second.attr('data-nav-classes', nav_second_classes);
        nav_second.removeAttr('class');
        nav_second.addClass('main-sidebar');

        let main_sidebar = $(".main-sidebar");
        main_sidebar.find('.container').addClass('sidebar-wrapper').removeClass('container');
        main_sidebar.find('.navbar-nav').addClass('sidebar-menu').removeClass('navbar-nav');
        main_sidebar.find('.sidebar-menu .nav-item.dropdown.show a').click();
        main_sidebar.find('.sidebar-brand').remove();
        main_sidebar.find('.sidebar-menu').before($('<div>', {
          class: 'sidebar-brand'
        }).append(
          $('<a>', {
            href: $('.navbar-brand').attr('href'),
          }).html($('.navbar-brand').html())
        ));
        setTimeout(function () {
          sidebar_nicescroll = main_sidebar.niceScroll(sidebar_nicescroll_opts);
          sidebar_nicescroll = main_sidebar.getNiceScroll();
        }, 700);

        sidebar_dropdown();
        $(".main-wrapper").removeClass("container");
      }
    } else {
      $("body").removeClass("sidebar-gone sidebar-show");
      if (now_layout_class)
        $("body").addClass(now_layout_class);

      let nav_second_classes = $(".main-sidebar").attr('data-nav-classes'),
        nav_second = $(".main-sidebar");

      if (now_layout_class == 'layout-3' && nav_second.hasClass('main-sidebar')) {
        nav_second.find(".sidebar-menu li a.has-dropdown").off('click');
        nav_second.find('.sidebar-brand').remove();
        nav_second.removeAttr('class');
        nav_second.addClass(nav_second_classes);

        let main_sidebar = $(".navbar-secondary");
        main_sidebar.find('.sidebar-wrapper').addClass('container').removeClass('sidebar-wrapper');
        main_sidebar.find('.sidebar-menu').addClass('navbar-nav').removeClass('sidebar-menu');
        main_sidebar.find('.dropdown-menu').hide();
        main_sidebar.removeAttr('style');
        main_sidebar.removeAttr('tabindex');
        main_sidebar.removeAttr('data-nav-classes');
        $(".main-wrapper").addClass("container");
        // if(sidebar_nicescroll != null)
        //   sidebar_nicescroll.remove();
      } else if (now_layout_class == 'layout-2') {
        $("body").addClass("layout-2");
      } else {
        update_sidebar_nicescroll();
      }
    }
  }
  toggleLayout();
  $(window).resize(toggleLayout);



  // tooltip
  $("[data-toggle='tooltip']").tooltip();




});




// -------------------------------------------------------



// Fullscreen Toggle
document.getElementById("fullscreenToggle").addEventListener("click", function () {
  if (!document.fullscreenElement) {
    document.documentElement.requestFullscreen();
     this.innerHTML = `<svg width="28" height="28" fill="none" stroke="currentColor" stroke-width="1.5" viewBox="0 0 24 24" stroke-linecap="round" stroke-linejoin="round" xmlns="http://www.w3.org/2000/svg"> <path d='M9.75 3.01c-.04 2.79-.247 4.37-1.308 5.432C7.38 9.502 5.799 9.71 3.01 9.75M9.75 21c-.04-2.79-.247-4.371-1.308-5.432S5.799 14.3 3.01 14.26M14.26 3.01c.04 2.79.247 4.37 1.308 5.432C16.629 9.502 18.211 9.71 21 9.75M14.26 21c.04-2.79.247-4.371 1.308-5.432S18.211 14.3 21 14.26'/></svg>`;
  } else {
    if (document.exitFullscreen) {
      document.exitFullscreen();
      this.innerHTML = `<svg width="28" height="28" fill="none" stroke="currentColor" stroke-width="1.5" viewBox="0 0 24 24" stroke-linecap="round" stroke-linejoin="round" xmlns="http://www.w3.org/2000/svg"> <path d='M9.4 21c-2.24 0-3.36 0-4.216-.436a4 4 0 0 1-1.748-1.748C3 17.96 3 16.84 3 14.6m18 0c0 2.24 0 3.36-.436 4.216a4 4 0 0 1-1.748 1.748C17.96 21 16.84 21 14.6 21m0-18c2.24 0 3.36 0 4.216.436a4 4 0 0 1 1.748 1.748C21 6.04 21 7.16 21 9.4M9.4 3c-2.24 0-3.36 0-4.216.436a4 4 0 0 0-1.748 1.748C3 6.04 3 7.16 3 9.4'/></svg>`;
    }
  }
});


// ---------------------------- date and time---------------------------
 function pad(n) {
            return n < 10 ? '0' + n : n;
        }

        function updateClock() {
            const now = new Date();

            const day = pad(now.getDate());
            const month = pad(now.getMonth() + 1); // Months are zero-indexed
            const year = now.getFullYear();

            const hours = pad(now.getHours());
            const minutes = pad(now.getMinutes());
            const seconds = pad(now.getSeconds());

            const formatted = `${day}/${month}/${year} ${hours}:${minutes}:${seconds}`;

            document.getElementById('clock').textContent = formatted;
        }

        setInterval(updateClock, 1000);
        updateClock();


