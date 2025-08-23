using MajdataEdit.MaiMuriDX;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Python.Runtime;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MajdataEdit;


public partial class LaunchMaiMuriDX : Window
{
    RunArg RunArg { get; set; }
    public LaunchMaiMuriDX(RunArg runArg)
    {
        InitializeComponent();
        RunArg = runArg;
    }

    private void StartCheck_Button_Click(object sender, RoutedEventArgs e)
    {
        RunArg.render = (bool)RenderEnable_Checkbox.IsChecked!;

        string py_home = "C:\\Users\\BUCYU\\source\\repos\\MajdataEditYours\\MaiMuriDX\\python312-embed";

        Runtime.PythonDLL = $"{py_home}\\python312.dll";
        PythonEngine.PythonHome = py_home;
        PythonEngine.ProgramName = "MaiMuriDX";
        PythonEngine.Initialize();

        dynamic s, t; //s:静态检查结果，t:动态检查结果

        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.insert(0, @"C:\Users\BUCYU\source\repos\MajdataEditYours\MaiMuriDX");
            dynamic main = Py.Import("main");
            PyObject pyArg = RunArg.ToPython();
            dynamic result = main.c_run(pyArg); 
            s = result[0];
            t = result[1];
        }
        PythonEngine.Shutdown();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
    }

    private void Window_Initialized(object sender, EventArgs e)
    {
    }
}