
  var pieChart = echarts.init(document.getElementById('HealthLivePie'));

  var datas = [
    [
      { name: 'Failure', value: 1 },
      { name: 'Healthy', value: 90 },
      { name: 'Predictive', value: 10 }
    ]
  ];

  option = {
    color: ['#ff928a', '#98d8aa', '#f7d060'],

    title: {
      text: '508.22',
      left: 'center',
      top: '46%',
      textStyle: {
        color: '#000',
        fontWeight: '800',
        fontSize: 15
      }
    },
    series: datas.map(function (data, idx) {
      var top = idx * 100;
      return {
        type: 'pie',
        radius: ['25%', '60%'],
        top: top + '%',
        height: '99%',
        left: 'center',
        width: '100%',
        itemStyle: {
          borderColor: '#fff',
          borderWidth: 1
        },
        label: {
          alignTo: 'edge',
          formatter: function (params) {
            // Assign class name based on name
            let className = 'default';
            if (params.name === 'Failure') className = 'failure';
            else if (params.name === 'Healthy') className = 'healthy';
            else if (params.name === 'Predictive') className = 'predictive';

            return `{${className}|${params.name}}\n{${className}Value|${params.value}}`;
          },
          rich: {
            failure: {
              fontWeight: 600,
              fontSize: 12,
              color: '#ff928a'
            },
            failureValue: {
              fontWeight: 600,
              fontSize: 10,
              color: '#ff928a'
            },
            healthy: {
              fontWeight: 600,
              fontSize: 12,
              color: '#98d8aa'
            },
            healthyValue: {
              fontWeight: 600,
              fontSize: 10,
              color: '#98d8aa'
            },
            predictive: {
              fontWeight: 600,
              fontSize: 12,
              color: '#f7d060'
            },
            predictiveValue: {
              fontWeight: 600,
              fontSize: 10,
              color: '#f7d060'
            },
            default: {
              fontWeight: 600,
              fontSize: 12,
              color: '#000'
            },
            defaultValue: {
              fontWeight: 600,
              fontSize: 10,
              color: '#000'
            }
          },
          minMargin: 5,
          edgeDistance: 10,
          lineHeight: 15
        },
        labelLine: {
          length: 15,
          length2: 0,
          maxSurfaceAngle: 80
        },
        labelLayout: function (params) {
          const isLeft = params.labelRect.x < pieChart.getWidth() / 2;
          const points = params.labelLinePoints;
          points[2][0] = isLeft
            ? params.labelRect.x
            : params.labelRect.x + params.labelRect.width;
          return {
            labelLinePoints: points
          };
        },
        data: data.map(item => {
          // Assign class name for label rich text formatter
          let className = 'default';
          if (item.name === 'Failure') className = 'failure';
          else if (item.name === 'Healthy') className = 'healthy';
          else if (item.name === 'Predictive') className = 'predictive';

          return {
            ...item,
            label: {
              formatter: `{${className}|${item.name}}\n{${className}Value|${item.value}}`,
              rich: {
                failure: {
                  fontWeight: 600,
                  fontSize: 14,
                  color: '#ff928a'
                },
                failureValue: {
                  fontWeight: 600,
                  fontSize: 10,
                  color: '#ff928a'
                },
                healthy: {
                  fontWeight: 600,
                  fontSize: 14,
                  color: '#98d8aa'
                },
                healthyValue: {
                  fontWeight: 600,
                  fontSize: 10,
                  color: '#98d8aa'
                },
                predictive: {
                  fontWeight: 600,
                  fontSize: 14,
                  color: '#f7d060'
                },
                predictiveValue: {
                  fontWeight: 600,
                  fontSize: 10,
                  color: '#f7d060'
                },
                default: {
                  fontWeight: 600,
                  fontSize: 14,
                  color: '#606060'
                },
                defaultValue: {
                  fontWeight: 600,
                  fontSize: 10,
                  color: '#606060'
                }
              }
            }
          };
        })
      };
    })
  };

  pieChart.setOption(option);

  // Resize listener for pie chart
  window.addEventListener('resize', function() {
    pieChart.resize();
  });










    // ✅ Get colors from CSS
    const yAxisColor = getComputedStyle(document.getElementById('yAxisColor')).color;
    const legendHealthyColorTotalAssets = getComputedStyle(document.getElementById('legendHealthy')).color;
    const legendPredictiveColorTotalAssets = getComputedStyle(document.getElementById('legendPredictive')).color;
    const legendFailureColorTotalAssets = getComputedStyle(document.getElementById('legendFailure')).color;

    var chartDom = document.getElementById('HealthLiveGraph');
    var lineChart = echarts.init(chartDom);

    var option = {
      tooltip: {
        trigger: 'axis',
        axisPointer: {
          type: 'cross',
          label: {
            backgroundColor: '#6a7985'
          }
        }
      },
      legend: {
        data: [
          { name: 'Healthy', textStyle: { color: legendHealthyColorTotalAssets } },
          { name: 'Predictive', textStyle: { color: legendPredictiveColorTotalAssets } },
          { name: 'Failure', textStyle: { color: legendFailureColorTotalAssets } }
        ]
      },
      grid: {
        left: 0,
        right: 0,
        bottom: 60,
        containLabel: true
      },
      xAxis: [
        {
          type: 'category',
          boundaryGap: false,
          data: ['', '', '', '', '', '', ''],
          axisLabel: {
            color: yAxisColor // ✅ Using same color for X-axis
          }
        }
      ],
      yAxis: [
        {
          type: 'value',
          axisLabel: {
            color: yAxisColor // ✅ CSS-based color
          }
        }
      ],
      series: [
        {
          name: 'Failure',
          type: 'line',
          stack: 'Total',
          smooth: true,
          areaStyle: {
            color: {
              type: 'linear',
              x: 0, y: 0, x2: 0, y2: 1,
              colorStops: [
                { offset: 0, color: '#ff928a' },
                { offset: 1, color: 'rgba(255, 146, 138, 0)' }
              ]
            }
          },
          lineStyle: { color: '#ff928a' },
          itemStyle: { color: '#ff928a' },
          emphasis: { focus: 'series' },
          data: [500, 750, 201, 154, 350, 330, 600]
        },
        {
          name: 'Predictive',
          type: 'line',
          stack: 'Total',
          smooth: true,
          areaStyle: {
            color: {
              type: 'linear',
              x: 0, y: 0, x2: 0, y2: 1,
              colorStops: [
                { offset: 0, color: '#f7d060' },
                { offset: 1, color: 'rgba(247, 208, 96, 0)' }
              ]
            }
          },
          lineStyle: { color: '#f7d060' },
          itemStyle: { color: '#f7d060' },
          emphasis: { focus: 'series' },
          data: [700, 750, 800, 600, 800, 630, 800]
        },
        {
          name: 'Healthy',
          type: 'line',
          stack: 'Total',
          smooth: true,
          areaStyle: {
            color: {
              type: 'linear',
              x: 0, y: 0, x2: 0, y2: 1,
              colorStops: [
                { offset: 0, color: '#98d8aa' },
                { offset: 1, color: 'rgba(152, 216, 170, 0)' }
              ]
            }
          },
          lineStyle: { color: '#98d8aa' },
          itemStyle: { color: '#98d8aa' },
          emphasis: { focus: 'series' },
          data: [1200, 850, 900, 800, 1000, 800, 1100]
        }
      ]
    };

    option && lineChart.setOption(option);

    // Resize listener for line chart
    window.addEventListener('resize', function() {
      lineChart.resize();
    });








    // Read color values from hidden CSS-based elements
    const axisLabelColor = getComputedStyle(document.getElementById('axisColorRef')).color;
    const legendHealthyColor = getComputedStyle(document.getElementById('legendHealthyColor')).color;
    const legendPredictiveColor = getComputedStyle(document.getElementById('legendPredictiveColor')).color;
    const legendFailureColor = getComputedStyle(document.getElementById('legendFailureColor')).color;

    // Initialize chart
    var chartDom = document.getElementById('HealthLiveBar');
    var barChart = echarts.init(chartDom);

    var option = {
      color: ['#98d8aa', '#f7d060', '#ff928a'], // Bar colors
      legend: {
        data: [
          { name: 'Healthy', textStyle: { color: legendHealthyColor } },
          { name: 'Predictive', textStyle: { color: legendPredictiveColor } },
          { name: 'Failure', textStyle: { color: legendFailureColor } }
        ]
      },
      tooltip: {},
      grid: {
        left: 0,
        right: 0,
        bottom: 60,
        containLabel: true
      },
      dataset: {
        source: [
          ['product', 'Healthy', 'Predictive', 'Failure'],
          ['DC Track Circuit', 60, 45, 20],
          ['Main Signal', 30, 45, 55],
          ['Axle Counter', 45, 25, 14],
          ['LC Gate', 20, 15, 10],
          ['Point Machine', 25, 18, 20]
        ]
      },
      xAxis: {
        type: 'category',
        axisLabel: {
          color: axisLabelColor
        }
      },
      yAxis: {
        axisLabel: {
          color: axisLabelColor
        }
      },
      series: [
        { type: 'bar', barWidth: 15 },
        { type: 'bar', barWidth: 15 },
        { type: 'bar', barWidth: 15 }
      ]
    };

    barChart.setOption(option);

    // Resize listener for bar chart
    window.addEventListener('resize', function() {
      barChart.resize();
    });
