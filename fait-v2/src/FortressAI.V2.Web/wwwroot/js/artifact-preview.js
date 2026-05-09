window.artifactPreview = {
    initPrism: function (elementId) {
        const el = document.getElementById(elementId);
        if (el && window.Prism) {
            Prism.highlightElement(el);
        }
    },
    initChart: function (elementId, chartConfigJson) {
        const canvas = document.getElementById(elementId);
        if (!canvas || !window.Chart) return;
        try {
            const config = typeof chartConfigJson === 'string'
                ? JSON.parse(chartConfigJson)
                : chartConfigJson;
            const existing = Chart.getChart(canvas);
            if (existing) existing.destroy();
            new Chart(canvas, config);
        } catch (e) {
            console.error('artifactPreview.initChart error:', e);
        }
    },
    initPlotly: function (elementId, plotlyData) {
        const el = document.getElementById(elementId);
        if (!el || !window.Plotly) return;
        try {
            const data = typeof plotlyData === 'string'
                ? JSON.parse(plotlyData)
                : plotlyData;
            Plotly.newPlot(el, data.data || data, data.layout || {}, { responsive: true });
        } catch (e) {
            console.error('artifactPreview.initPlotly error:', e);
        }
    }
};
