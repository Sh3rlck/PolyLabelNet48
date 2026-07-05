using ScottPlot;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace WinApp
{
    public partial class Form1 : Form
    {
        private ScottPlot.WinForms.FormsPlot _formsPlot;

        public Form1()
        {
            InitializeComponent();

            btnRun.Click += Run_Click;

            InitPlot();
        }

        private void InitPlot()
        {
            // 在 Form1 的构造函数或 Load 事件中
            _formsPlot = new ScottPlot.WinForms.FormsPlot() { Dock = DockStyle.Fill };
            panelView.Controls.Add(_formsPlot); // panel1 为界面上的占位符

            // create floating X and Y axes using one of the existing axes for reference
            ScottPlot.Plottables.FloatingAxis floatingX = new ScottPlot.Plottables.FloatingAxis(_formsPlot.Plot.Axes.Bottom);
            ScottPlot.Plottables.FloatingAxis floatingY = new ScottPlot.Plottables.FloatingAxis(_formsPlot.Plot.Axes.Left);

            // hide the default axes and add the custom ones to the plot
            _formsPlot.Plot.Axes.Frameless();
            // _formsPlot.Plot.HideGrid();
            _formsPlot.Plot.Add.Plottable(floatingX);
            _formsPlot.Plot.Add.Plottable(floatingY);


            // var rect1 = _formsPlot.Plot.Add.Rectangle(0, 1, 0, 1);
            // var rect2 = _formsPlot.Plot.Add.Rectangle(1, 2, 0, 1);


            var rect1 = CoordinateRect.UnitSquare.WithTranslation(0, 0);
            var rect2 = CoordinateRect.UnitSquare.WithTranslation(1, 0);
            _formsPlot.Plot.Add.Rectangle(rect1);
            _formsPlot.Plot.Add.Rectangle(rect2);


            // // 绘制示例
            // _formsPlot.Plot.Add.Signal(new double[] { 1, 2, 8, 4, 5 });

            // //==================================================
            // _crosshair = _formsPlot.Plot.Add.Crosshair(0, 0);
            // _crosshair.IsVisible = false;
            // _crosshair.MarkerShape = MarkerShape.OpenCircle;
            // _crosshair.MarkerSize = 10;
            // 
            // _scatter = null;

            _formsPlot.Plot.Axes.SquareUnits();
            _formsPlot.Plot.Axes.AutoScale();
            _formsPlot.Refresh();

            // _formsPlot.MouseMove += _formsPlot_MouseMove;
        }

        private void Run_Click(object sender, EventArgs e)
        {

        }
    }
}
