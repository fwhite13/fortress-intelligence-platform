import {
  Chart,
  CategoryScale,
  LinearScale,
  BarController,
  BarElement,
  LineController,
  LineElement,
  PointElement,
  PieController,
  ArcElement,
  Title,
  Tooltip,
  Legend,
} from 'chart.js';

Chart.register(
  CategoryScale,
  LinearScale,
  BarController,
  BarElement,
  LineController,
  LineElement,
  PointElement,
  PieController,
  ArcElement,
  Title,
  Tooltip,
  Legend
);

export interface PptChartSpec {
  type: 'bar' | 'line' | 'pie' | 'doughnut' | 'scatter';
  title: string;
  width: number;
  height: number;
  labels: string[];
  datasets: any[];
  xAxis?: { title: string };
  yAxis?: { title: string };
}

/**
 * Render a Chart.js chart to a base64 PNG data URL.
 * Creates a hidden off-screen canvas, renders, captures, destroys.
 * Must run in a browser context with a real DOM (works in Office Add-in taskpane).
 */
export async function renderChartToBase64(spec: PptChartSpec): Promise<string> {
  const canvas = document.createElement('canvas');
  canvas.width = spec.width || 600;
  canvas.height = spec.height || 400;
  canvas.style.position = 'absolute';
  canvas.style.left = '-9999px';
  canvas.style.top = '-9999px';
  document.body.appendChild(canvas);

  return new Promise((resolve, reject) => {
    try {
      const chart = new Chart(canvas, {
        type: spec.type,
        data: {
          labels: spec.labels,
          datasets: spec.datasets,
        },
        options: {
          responsive: false,  // CRITICAL: responsive:true breaks off-screen canvas render
          animation: false,   // No animation needed — capture immediately
          plugins: {
            title: spec.title
              ? { display: true, text: spec.title, font: { size: 16 } }
              : { display: false },
            legend: { display: spec.type !== 'bar' || spec.datasets.length > 1 },
          },
          scales: spec.type === 'pie' || spec.type === 'doughnut'
            ? undefined
            : {
                x: spec.xAxis?.title
                  ? { title: { display: true, text: spec.xAxis.title } }
                  : undefined,
                y: spec.yAxis?.title
                  ? { title: { display: true, text: spec.yAxis.title } }
                  : undefined,
              },
        },
      });

      // Small timeout to allow paint cycle to complete
      setTimeout(() => {
        try {
          const base64 = canvas.toDataURL('image/png');
          chart.destroy();
          document.body.removeChild(canvas);
          resolve(base64);
        } catch (err) {
          chart.destroy();
          document.body.removeChild(canvas);
          reject(err);
        }
      }, 50);
    } catch (e) {
      if (document.body.contains(canvas)) {
        document.body.removeChild(canvas);
      }
      reject(e);
    }
  });
}
