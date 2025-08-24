using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MajdataEdit
{
    public enum ErrorType
    {
        Info,
        MuriDXS,
        MuriDXD,
        Syntax,
        Other
    }
    public class Position
    {
        public int x; //column
        public int y; //Line
        public Position(int _x, int _y)
        {
            x = _x;
            y = _y;
        }

        public override string ToString()
        {
            return $"L{y},C{x}";
        }
    }
    public class Error
    {
        public ErrorType Type { get; set; }
        public Position Position { get; set; }
        public string Message { get; set; }
        public string? Detail { get; set; }
        public Error(ErrorType _type, Position _position, string _message, string? _detail)
        {
            Type = _type;
            Position = _position;
            Message = _message;
            Detail = _detail;
        }
    }

    /// <summary>
    /// ErrorList.xaml 的交互逻辑
    /// </summary>
    public partial class ErrorList : Window
    {
        public ErrorList()
        {
            InitializeComponent();
        }

        private void ErrorListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            txtDetail.Text = (ErrorListView.SelectedItem as Error)?.Detail ?? "";
        }

        private void ErrorListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            Error error = (ErrorListView.SelectedItem as Error)!;
            ((MainWindow)Owner).ScrollToFumenContentSelection(error.Position.x, error.Position.y - 1);
            ((MainWindow)Owner).Activate();
        }
        protected override void OnClosing(CancelEventArgs e)
        {
            this.Hide();
            e.Cancel = true;
        }
    }
}
