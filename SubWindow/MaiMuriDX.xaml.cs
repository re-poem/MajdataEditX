using MajdataEdit.MaiMuriDX;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Python.Runtime;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

namespace MajdataEdit;


public partial class LaunchMaiMuriDX : Window
{
    RunArg RunArg { get; set; }
    public List<Error> SErrorList { get; set; } = new();
    public List<Error> TErrorList { get; set; } = new();
    public LaunchMaiMuriDX(RunArg runArg)
    {
        InitializeComponent();
        RunArg = runArg;
    }

    private void StartCheck_Button_Click(object sender, RoutedEventArgs e)
    {
        RunArg.render = (bool)RenderEnable_Checkbox.IsChecked!;

        string home = Path.Combine(Directory.GetCurrentDirectory(), "MaiMuriDX");
        string py_home = Path.Combine(home, "python312-embed");

        Runtime.PythonDLL = $"{py_home}\\python312.dll";
        PythonEngine.PythonHome = py_home;
        PythonEngine.ProgramName = "MaiMuriDX";
        PythonEngine.Initialize();

        dynamic s, t; //s:静态检查结果，t:动态检查结果

        using (Py.GIL())
        {
            dynamic sys = Py.Import("sys");
            sys.path.insert(0, home);
            dynamic main = Py.Import("main");
            PyObject pyArg = RunArg.ToPython();
            dynamic result = main.c_run(pyArg); 
            s = result[0];
            t = result[1];

            foreach (var item in s)
            {
                Error error;
                if (item["type"] == "Overlap")
                {
                    error = new Error(ErrorType.MuriDXS,
                        new Position((int)item["affected"]["col"], (int)item["affected"]["line"]),
                        $"[叠键无理] “{item["affected"]["note"]}” 与 “{item["cause"]["note"]}” 重叠",

                        string.Format(
                            "[叠键无理] {0}cb处\"{1}\"(L{2},C{3}) 与 ",
                            item["affected"]["combo"],
                            item["affected"]["note"],
                            item["affected"]["line"],
                            item["affected"]["col"]
                        ) +
                        string.Format(
                            "{0}cb处\"{1}\"(L{2},C{3}) 重叠 ",
                            item["cause"]["combo"],
                            item["cause"]["note"],
                            item["cause"]["line"],
                            item["cause"]["col"]
                        ));
                    SErrorList.Add(error);
                }
                else if (item["type"] == "SlideHeadTap")
                {
                    error = new Error(ErrorType.MuriDXS,
                        new Position((int)item["affected"]["col"], (int)item["affected"]["line"]),
                        $"[外键无理] “{item["affected"]["note"]}” 可能被 “{item["cause"]["note"]}” 蹭到",

                        string.Format(
                            "[外键无理] {0}cb处\"{1}\"(L{2},C{3}) 可能被 ",
                            item["affected"]["combo"],
                            item["affected"]["note"],
                            item["affected"]["line"],
                            item["affected"]["col"]
                        ) +
                        string.Format(
                            "{0}cb处\"{1}\"(L{2},C{3}) 蹭到\n",
                            item["cause"]["combo"],
                            item["cause"]["note"],
                            item["cause"]["line"],
                            item["cause"]["col"]
                        ) +
                        string.Format("({0:+0;-0} ms)", item["delta"] * 1000 / 180));
                    SErrorList.Add(error);
                }
                else if (item["type"] == "TapOnSlide")
                {
                    error = new Error(ErrorType.MuriDXS,
                        new Position((int)item["affected"]["col"], (int)item["affected"]["line"]),
                        $"[撞尾无理] “{item["affected"]["note"]}” 可能被 “{item["cause"]["note"]}” 蹭到",

                        string.Format(
                            "[撞尾无理] {0}cb处\"{1}\"(L{2},C{3}) 可能被 ",
                            item["affected"]["combo"],
                            item["affected"]["note"],
                            item["affected"]["line"],
                            item["affected"]["col"]
                        ) +
                        string.Format(
                            "{0}cb处\"{1}\"(L{2},C{3}) 蹭到\n",
                            item["cause"]["combo"],
                            item["cause"]["note"],
                            item["cause"]["line"],
                            item["cause"]["col"]
                        ) +
                        string.Format("({0:+0;-0} ms)", item["delta"] * 1000 / 180));
                    SErrorList.Add(error);
                }
            }



            foreach (var item in t)
            {
                Error error;
                float otime = item["time"];
                int frame, sec = Math.DivRem((int)(otime * 60), 60, out frame);
                int min = Math.DivRem(sec, 60, out sec);
                string time = string.Format("[{0:D2}:{1:D2}F{2:00.00}]\n", min, sec, frame);
                if (item["type"] == "MultiTouch")
                {
                    string msg_notes = "";
                    foreach (var note in item["cause"])
                    {
                        msg_notes += string.Format(
                            "\"{2}\"(L{0},C{1})",
                            note["line"],
                            note["col"],
                            note["note"]) + "  ";
                    }
                    error = new Error(ErrorType.MuriDXD,
                        new Position((int)item["cause"][0]["col"], (int)item["cause"][0]["line"]),
                        $"[多押无理] 可能形成{item["hand_count"]}押",

                        time + 
                        $"[多押无理] 下列note可能形成{item["hand_count"]}押\n    " + 
                        msg_notes
                        );
                    SErrorList.Add(error);
                }
                else if (item["type"] == "SlideTooFast")
                {
                    error = new Error(ErrorType.MuriDXD,
                        new Position((int)item["affected"]["col"], (int)item["affected"]["line"]),
                        $"[内屏无理] “{item["affected"]["note"]}” 被提前蹭掉",

                        time + 
                        string.Format(
                            "[内屏无理] {0}cb处\"{1}\"(L{2},C{3}) 被提前蹭掉\n",
                            item["affected"]["combo"],
                            item["affected"]["note"],
                            item["affected"]["line"],
                            item["affected"]["col"]
                        ) +
                        string.Format("CP区间±{0:0.0} ms，相关判定区如下:\n", 
                            item["--critical_delta"] * 1000.0 / 180
                        ) +
                        item["--msg_areas"]
                        );
                    SErrorList.Add(error);
                }
                else if (item["type"] == "Overlap")
                {
                    error = new Error(ErrorType.MuriDXD,
                        new Position((int)item["affected"]["col"], (int)item["affected"]["line"]),
                        $"[叠键无理] “{item["affected"]["note"]}” 似乎与另一个note重叠",

                        time +
                        string.Format(
                            "[叠键无理] {0}cb处\"{1}\"(L{2},C{3}) 似乎与另一个note重叠\n",
                            item["affected"]["combo"],
                            item["affected"]["note"],
                            item["affected"]["line"],
                            item["affected"]["col"]
                        ) +
                        string.Format("({0:+0;-0} ms)", item["delta"] * 1000 / 180));
                    SErrorList.Add(error);
                }
                else if (item["type"] == "SlideHeadTap")
                {
                    error = new Error(ErrorType.MuriDXD,
                        new Position((int)item["affected"]["col"], (int)item["affected"]["line"]),
                        $"[外键无理] “{item["affected"]["note"]}” 被 “{item["cause"]["note"]}” 蹭到",

                        time + 
                        string.Format(
                            "[外键无理] {0}cb处\"{1}\"(L{2},C{3}) 被 ",
                            item["affected"]["combo"],
                            item["affected"]["note"],
                            item["affected"]["line"],
                            item["affected"]["col"]
                        ) +
                        string.Format(
                            "{0}cb处\"{1}\"(L{2},C{3}) 蹭到\n",
                            item["cause"]["combo"],
                            item["cause"]["note"],
                            item["cause"]["line"],
                            item["cause"]["col"]
                        ) +
                        string.Format("({0:+0;-0} ms)", item["delta"] * 1000 / 180));
                    SErrorList.Add(error);
                }
                else if (item["type"] == "TapOnSlide")
                {
                    error = new Error(ErrorType.MuriDXD,
                        new Position((int)item["affected"]["col"], (int)item["affected"]["line"]),
                        $"[撞尾无理] “{item["affected"]["note"]}” 被 “{item["cause"]["note"]}” 蹭到",

                        time + 
                        string.Format(
                            "[撞尾无理] {0}cb处\"{1}\"(L{2},C{3}) 可能被 ",
                            item["affected"]["combo"],
                            item["affected"]["note"],
                            item["affected"]["line"],
                            item["affected"]["col"]
                        ) +
                        string.Format(
                            "{0}cb处\"{1}\"(L{2},C{3}) 蹭到\n",
                            item["cause"]["combo"],
                            item["cause"]["note"],
                            item["cause"]["line"],
                            item["cause"]["col"]
                        ) +
                        string.Format("({0:+0;-0} ms)", item["delta"] * 1000 / 180));
                    SErrorList.Add(error);
                }
            }
        }
        PythonEngine.Shutdown();
        ((MainWindow)Owner).ShowMuriDXError(this);
        Close();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
    }

    private void Window_Initialized(object sender, EventArgs e)
    {
    }
}